using System;
using System.Collections.Generic;
using FishNet.Managing;
using UnityEditor;
using UnityEngine;

namespace FishNetNetworkTools.Editor
{
    /// <summary>
    /// Editor window that shows live FishNet network state during Play Mode: the spawned
    /// NetworkObjects of a chosen NetworkManager, each object's ownership and observer count, and,
    /// for the selected object, its NetworkBehaviours with their live SyncVar/SyncType values (via
    /// SyncTypeAdapter). The list and detail panel update as objects spawn and despawn.
    /// </summary>
    public class NetworkInspectorWindow : EditorWindow
    {
        private const string MenuPath = "Tools/FishNet Network Tools/Network Inspector";
        private const string WindowTitle = "FishNet Network Inspector";

        // IMGUI windows don't auto-repaint during Play Mode; this throttles the driven repaint so the
        // list and values stay live without laying out on every editor frame.
        private const double RepaintInterval = 0.1;

        private NetworkInspectorModel _model;
        private int _selectedObjectId = -1;
        private string _search = string.Empty;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private double _lastRepaint;
        private ValidationResult _validation;
        private bool _validated;
        private readonly List<BehaviourEntry> _behaviourBuffer = new List<BehaviourEntry>();

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<NetworkInspectorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        private void OnEnable()
        {
            _model = new NetworkInspectorModel();
            _model.Changed += Repaint;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            _model.Changed -= Repaint;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _model.Dispose();
        }

        private void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - _lastRepaint < RepaintInterval) return;

            _lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                _model.Reset();

            // The adapter's reflection caches and FishNet's statics reset on domain reload, so
            // re-validate and drop the selection at the start of every play session.
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                _selectedObjectId = -1;
                _validated = false;
            }
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect live network state.", MessageType.Info);
                return;
            }

            _model.Refresh();

            if (!_validated)
            {
                _validation = SyncTypeAdapter.ValidateSyncTypeReflection();
                _validated = true;
            }

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawObjectList();
            DrawDetailPane();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            IReadOnlyList<NetworkManager> managers = _model.Managers;
            if (managers.Count == 0)
            {
                GUILayout.Label("No NetworkManager found.", EditorStyles.toolbarButton);
            }
            else if (managers.Count == 1)
            {
                GUILayout.Label("Manager: " + managers[0].gameObject.name + " (auto)", EditorStyles.toolbarButton);
            }
            else
            {
                var names = new string[managers.Count];
                int current = 0;
                for (int i = 0; i < managers.Count; i++)
                {
                    names[i] = managers[i].gameObject.name;
                    if (ReferenceEquals(managers[i], _model.Selected)) current = i;
                }

                int chosen = EditorGUILayout.Popup(current, names, EditorStyles.toolbarPopup);
                if (chosen != current)
                    _model.Select(managers[chosen]);
            }

            string viewLabel = _model.View == NetworkView.Server ? "Server view"
                : _model.View == NetworkView.Client ? "Client view"
                : "not started";
            GUILayout.Label(viewLabel, EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();
            GUILayout.Label("Search:", EditorStyles.miniLabel);
            _search = EditorGUILayout.TextField(_search, GUILayout.Width(160f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawObjectList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.42f));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            IReadOnlyList<ObjectRow> rows = _model.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                ObjectRow row = rows[i];
                if (!MatchesSearch(row)) continue;

                string line = row.Name + "  #" + row.ObjectId + "   owner: " + FormatOwnerLabel(row) +
                    "   obs: " + FormatObserverCount(row);

                GUIStyle style = row.ObjectId == _selectedObjectId ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(line, style))
                    _selectedObjectId = row.ObjectId;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetailPane()
        {
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (!_validation.Ok)
                EditorGUILayout.HelpBox(_validation.Message, MessageType.Warning);

            if (_selectedObjectId < 0)
            {
                EditorGUILayout.LabelField("Select an object to inspect.");
            }
            else if (!_model.GetBehaviourEntries(_selectedObjectId, _behaviourBuffer))
            {
                EditorGUILayout.HelpBox("Object despawned.", MessageType.Info);
            }
            else
            {
                DrawSelectedObjectHeader();
                DrawBehaviours();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedObjectHeader()
        {
            IReadOnlyList<ObjectRow> rows = _model.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].ObjectId != _selectedObjectId) continue;

                ObjectRow row = rows[i];
                EditorGUILayout.LabelField(row.Name + "  #" + row.ObjectId);
                EditorGUILayout.LabelField("Owner: " + FormatOwnerLabel(row));
                EditorGUILayout.LabelField("Observers: " + FormatObserverCount(row));
                break;
            }
        }

        private void DrawBehaviours()
        {
            for (int i = 0; i < _behaviourBuffer.Count; i++)
            {
                BehaviourEntry behaviour = _behaviourBuffer[i];
                EditorGUILayout.LabelField(behaviour.TypeName, EditorStyles.boldLabel);

                EditorGUI.indentLevel++;
                if (behaviour.Syncs.Count == 0)
                {
                    EditorGUILayout.LabelField("(no SyncTypes)");
                }
                else
                {
                    for (int j = 0; j < behaviour.Syncs.Count; j++)
                    {
                        SyncEntry sync = behaviour.Syncs[j];
                        EditorGUILayout.LabelField(sync.Name + "  (" + sync.TypeName + ")  = " + sync.ValueText);
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        private bool MatchesSearch(ObjectRow row)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            if (row.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return row.ObjectId.ToString().IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatOwnerLabel(ObjectRow row)
        {
            string label = row.OwnerId == -1 ? "none" : row.OwnerId.ToString();
            if (row.IsOwner) label += " (local)";
            return label;
        }

        private string FormatObserverCount(ObjectRow row)
        {
            return _model.View == NetworkView.Server ? row.ObserverCount.ToString() : "n/a";
        }
    }
}
