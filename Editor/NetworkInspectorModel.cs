using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;

namespace FishNetNetworkTools.Editor
{
    /// <summary>
    /// Which spawned-object view the model is currently reading: the server's full view,
    /// the client's observed-only view, or neither (no NetworkManager, or nothing started).
    /// </summary>
    public enum NetworkView
    {
        None,
        Server,
        Client
    }

    /// <summary>
    /// A single spawned NetworkObject snapshot for the inspector list, rebuilt on every Refresh().
    /// </summary>
    public readonly struct ObjectRow
    {
        public readonly int ObjectId;
        public readonly string Name;

        /// <summary>-1 when unowned.</summary>
        public readonly int OwnerId;

        /// <summary>True when the local connection owns this object; only meaningful as a "(local)" marker.</summary>
        public readonly bool IsOwner;

        /// <summary>-1 in client view: observers are server-side only.</summary>
        public readonly int ObserverCount;

        public ObjectRow(int objectId, string name, int ownerId, bool isOwner, int observerCount)
        {
            ObjectId = objectId;
            Name = name;
            OwnerId = ownerId;
            IsOwner = isOwner;
            ObserverCount = observerCount;
        }
    }

    /// <summary>
    /// A NetworkBehaviour on the selected object, with its SyncType values already read via SyncTypeAdapter.
    /// </summary>
    public readonly struct BehaviourEntry
    {
        public readonly string TypeName;
        public readonly IReadOnlyList<SyncEntry> Syncs;

        public BehaviourEntry(string typeName, IReadOnlyList<SyncEntry> syncs)
        {
            TypeName = typeName;
            Syncs = syncs;
        }
    }

    /// <summary>
    /// Data layer for the network inspector window. Picks a NetworkManager from the active instances,
    /// tracks whichever spawned-object view (server or client) is currently active, and keeps a live
    /// row list via the spawn/despawn events. Holds no UnityEditor UI types, so all layout stays in
    /// the window that owns this model.
    /// </summary>
    public class NetworkInspectorModel : IDisposable
    {
        /// <summary>Raised from the FishNet spawn/despawn events so the window can repaint immediately.</summary>
        public event Action Changed;

        private NetworkManager _selected;
        private ManagedObjects _subscribedObjects;

        private readonly List<NetworkManager> _managers = new List<NetworkManager>();
        private readonly List<ObjectRow> _rows = new List<ObjectRow>();

        // Reused so Refresh() never allocates a fresh list just to iterate the spawned dictionary.
        private readonly List<NetworkObject> _spawnedSnapshot = new List<NetworkObject>();

        private NetworkView _view;
        private bool _serverStarted;

        public IReadOnlyList<NetworkManager> Managers => _managers;
        public NetworkManager Selected => _selected;
        public NetworkView View => _view;
        public bool ServerStarted => _serverStarted;
        public IReadOnlyList<ObjectRow> Rows => _rows;

        /// <summary>
        /// Sets the manager the model reads from. Subscriptions are re-evaluated on the next
        /// Refresh(), not here.
        /// </summary>
        public void Select(NetworkManager nm)
        {
            _selected = nm;
        }

        /// <summary>
        /// Re-resolves the active manager and view, re-subscribes if the view changed, and rebuilds
        /// Rows. Call once per OnGUI while in Play Mode.
        /// </summary>
        public void Refresh()
        {
            _managers.Clear();
            IReadOnlyList<NetworkManager> instances = NetworkManager.Instances;
            for (int i = 0; i < instances.Count; i++)
                _managers.Add(instances[i]);

            if (_selected == null || !ManagersContains(_selected))
                _selected = InstanceFinder.NetworkManager;

            ManagedObjects active = null;
            _serverStarted = false;
            _view = NetworkView.None;

            if (_selected != null)
            {
                _serverStarted = _selected.IsServerStarted;
                if (_serverStarted)
                {
                    active = (ManagedObjects)_selected.ServerManager.Objects;
                    _view = NetworkView.Server;
                }
                else if (_selected.IsClientStarted)
                {
                    active = _selected.ClientManager.Objects;
                    _view = NetworkView.Client;
                }
            }

            if (!ReferenceEquals(active, _subscribedObjects))
            {
                Unsubscribe(_subscribedObjects);
                Subscribe(active);
                _subscribedObjects = active;
            }

            _rows.Clear();
            if (active != null)
            {
                // Snapshot before iterating: a despawn during this loop would mutate the live dictionary.
                _spawnedSnapshot.Clear();
                _spawnedSnapshot.AddRange(active.Spawned.Values);

                for (int i = 0; i < _spawnedSnapshot.Count; i++)
                {
                    NetworkObject no = _spawnedSnapshot[i];
                    if (no == null) continue;

                    int observerCount = _view == NetworkView.Server ? no.Observers.Count : -1;
                    _rows.Add(new ObjectRow(no.ObjectId, no.gameObject.name, no.OwnerId, no.IsOwner, observerCount));
                }

                _rows.Sort((a, b) => a.ObjectId.CompareTo(b.ObjectId));
            }
        }

        /// <summary>
        /// Reads the NetworkBehaviours and live SyncType values for the given spawned object. Returns
        /// false (and clears <paramref name="into"/>) if the object has despawned since it was listed.
        /// </summary>
        public bool GetBehaviourEntries(int objectId, List<BehaviourEntry> into)
        {
            into.Clear();

            if (_subscribedObjects == null) return false;
            if (!_subscribedObjects.Spawned.TryGetValue(objectId, out NetworkObject no)) return false;
            if (no == null) return false;

            List<NetworkBehaviour> behaviours = no.NetworkBehaviours;
            for (int i = 0; i < behaviours.Count; i++)
            {
                NetworkBehaviour nb = behaviours[i];
                if (nb == null) continue;
                into.Add(new BehaviourEntry(nb.GetType().Name, SyncTypeAdapter.GetSyncEntries(nb)));
            }
            return true;
        }

        /// <summary>
        /// Drops the current subscription and all cached state. Call on exiting Play Mode so the model
        /// never holds a reference into a torn-down scene.
        /// </summary>
        public void Reset()
        {
            Unsubscribe(_subscribedObjects);
            _subscribedObjects = null;
            _rows.Clear();
            _managers.Clear();
            _view = NetworkView.None;
            _serverStarted = false;
            _selected = null;
        }

        public void Dispose()
        {
            Reset();
        }

        private bool ManagersContains(NetworkManager nm)
        {
            for (int i = 0; i < _managers.Count; i++)
            {
                if (ReferenceEquals(_managers[i], nm)) return true;
            }
            return false;
        }

        private void Subscribe(ManagedObjects objects)
        {
            if (objects == null) return;
            objects.OnSpawnedAdd += OnSpawnedChangedHandler;
            objects.OnSpawnedRemove += OnSpawnedChangedHandler;
            objects.OnSpawnedClear += OnSpawnedClearHandler;
        }

        private void Unsubscribe(ManagedObjects objects)
        {
            if (objects == null) return;
            objects.OnSpawnedAdd -= OnSpawnedChangedHandler;
            objects.OnSpawnedRemove -= OnSpawnedChangedHandler;
            objects.OnSpawnedClear -= OnSpawnedClearHandler;
        }

        private void OnSpawnedChangedHandler(int objectId, NetworkObject networkObject)
        {
            Changed?.Invoke();
        }

        private void OnSpawnedClearHandler()
        {
            Changed?.Invoke();
        }
    }
}
