using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishNet.Object; // NetworkBehaviour — the one rock-stable FishNet type this adapter hard-references.

namespace FishNetNetworkTools
{
    /// <summary>
    /// The single place where reflection into FishNet lives. Given a <see cref="NetworkBehaviour"/>,
    /// it returns a readable snapshot of that behaviour's SyncType values, plus a startup validator
    /// that degrades gracefully on an untested FishNet version. Nothing reflective crosses its public
    /// surface (callers only ever see <see cref="SyncEntry"/> / <see cref="ValidationResult"/>), so a
    /// FishNet version bump is a one-file fix here.
    ///
    /// Why string/reflection instead of direct type references: this package ships as source and
    /// compiles inside the user's project. The SyncType machinery (notably the <c>.Internal</c>
    /// <c>SyncBase</c>) is the surface most likely to churn. Resolving it by name means a future
    /// FishNet rename degrades to a friendly "SyncVar readout unavailable" message instead of breaking
    /// the user's compile (which would take down the object tree too). Only <see cref="NetworkBehaviour"/>,
    /// the central stable type, is referenced directly.
    ///
    /// Verified against FishNet 4.7.2:
    ///   SyncBase  : FishNet.Object.Synchronizing.Internal.SyncBase   (public base of all SyncTypes)
    ///   SyncVar   : FishNet.Object.Synchronizing.SyncVar&lt;T&gt;     — public T Value { get; } (no side effects)
    ///   SyncList / SyncDictionary / SyncHashSet                       — public int Count { get; }
    ///   Version   : FishNet.Managing.NetworkManager.FISHNET_VERSION   (public const string)
    /// To support a new FishNet version, update the string constants below and re-run the validator.
    /// </summary>
    public static class SyncTypeAdapter
    {
        // --- Tested FishNet surface, matched as strings so an API change degrades instead of failing to compile ---
        private const string TestedFishNetVersion = "4.7.2";
        private const string FishNetRuntimeAssembly = "FishNet.Runtime";
        private const string SyncBaseTypeName = "FishNet.Object.Synchronizing.Internal.SyncBase";
        private const string SyncVarOpenTypeName = "FishNet.Object.Synchronizing.SyncVar`1";
        private const string NetworkManagerTypeName = "FishNet.Managing.NetworkManager";
        private const string VersionFieldName = "FISHNET_VERSION";
        private const string ValuePropertyName = "Value";
        private const string CountPropertyName = "Count";

        // --- Caches: IMGUI repaints often, so never re-reflect a type per repaint ---
        private static readonly Dictionary<Type, FieldInfo[]> _syncFieldsByType = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, PropertyInfo> _valuePropByType = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, PropertyInfo> _countPropByType = new Dictionary<Type, PropertyInfo>();

        private static Type _syncBaseType;
        private static bool _syncBaseResolved;

        // ----------------------------------------------------------------------------------------
        // Public surface
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Self-checks the reflection assumptions against the installed FishNet. Run once (on first
        /// window use). Never throws; on any mismatch returns <see cref="ValidationResult.Ok"/> = false
        /// with a user-facing message so the UI can show objects/ownership/observers without the
        /// SyncVar readout.
        /// </summary>
        public static ValidationResult ValidateSyncTypeReflection()
        {
            string version = DetectFishNetVersion();

            Type syncBase = GetSyncBaseType();
            if (syncBase == null)
                return Degraded(version, "FishNet SyncType base type '" + SyncBaseTypeName + "' was not found");

            Type syncVarOpen = ResolveFishNetType(SyncVarOpenTypeName);
            if (syncVarOpen == null)
                return Degraded(version, "FishNet SyncVar<T> type was not found");

            try
            {
                Type syncVarInt = syncVarOpen.MakeGenericType(typeof(int));
                if (!syncBase.IsAssignableFrom(syncVarInt))
                    return Degraded(version, "SyncVar<T> does not derive from the expected SyncType base");

                PropertyInfo valueProp = syncVarInt.GetProperty(
                    ValuePropertyName, BindingFlags.Instance | BindingFlags.Public);
                if (valueProp == null || !valueProp.CanRead)
                    return Degraded(version, "SyncVar<T>.Value is not a readable public property");
            }
            catch (Exception ex)
            {
                return Degraded(version, "reflection probe failed (" + ex.GetType().Name + ")");
            }

            return new ValidationResult(true, version, string.Empty);
        }

        /// <summary>
        /// Returns a snapshot of every SyncType value declared on <paramref name="nb"/>. Never throws:
        /// a field that breaks an assumption becomes a single <see cref="SyncKind.Unknown"/> entry rather
        /// than blanking the panel, and a missing FishNet SyncType base yields an empty list.
        /// </summary>
        public static IReadOnlyList<SyncEntry> GetSyncEntries(NetworkBehaviour nb)
        {
            if (nb == null) return Array.Empty<SyncEntry>();

            Type syncBase = GetSyncBaseType();
            if (syncBase == null) return Array.Empty<SyncEntry>();

            FieldInfo[] fields;
            try { fields = GetSyncFields(nb.GetType(), syncBase); }
            catch { return Array.Empty<SyncEntry>(); }

            if (fields.Length == 0) return Array.Empty<SyncEntry>();

            var entries = new List<SyncEntry>(fields.Length);
            for (int i = 0; i < fields.Length; i++)
                entries.Add(ReadField(nb, fields[i]));
            return entries;
        }

        // ----------------------------------------------------------------------------------------
        // Field enumeration (cached per concrete behaviour type)
        // ----------------------------------------------------------------------------------------

        // Mirrors FishNet's own CodeGen: walk from the concrete type up to (but not into)
        // NetworkBehaviour, collecting declared fields whose type derives from SyncBase. DeclaredOnly
        // per level is required because NonPublic does not return inherited private fields.
        private static FieldInfo[] GetSyncFields(Type behaviourType, Type syncBase)
        {
            if (_syncFieldsByType.TryGetValue(behaviourType, out var cached))
                return cached;

            var found = new List<FieldInfo>();
            Type stop = typeof(NetworkBehaviour);
            for (Type t = behaviourType; t != null && t != stop; t = t.BaseType)
            {
                FieldInfo[] declared = t.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < declared.Length; i++)
                {
                    if (syncBase.IsAssignableFrom(declared[i].FieldType))
                        found.Add(declared[i]);
                }
            }

            FieldInfo[] arr = found.ToArray();
            _syncFieldsByType[behaviourType] = arr;
            return arr;
        }

        // ----------------------------------------------------------------------------------------
        // Per-field read (never throws)
        // ----------------------------------------------------------------------------------------

        private static SyncEntry ReadField(NetworkBehaviour nb, FieldInfo field)
        {
            string name = field.Name;
            Type fieldType = field.FieldType;
            string typeName = FriendlyTypeName(fieldType);
            SyncKind kind = Classify(fieldType);

            try
            {
                object instance = field.GetValue(nb);
                if (instance == null)
                    return new SyncEntry(name, typeName, kind, "(uninitialized)", -1);

                switch (kind)
                {
                    case SyncKind.SyncVar:
                    {
                        PropertyInfo valueProp = GetValueProperty(fieldType);
                        if (valueProp == null)
                            return new SyncEntry(name, typeName, SyncKind.Unknown, "(no readable Value)", -1);
                        object value = valueProp.GetValue(instance);
                        return new SyncEntry(name, typeName, SyncKind.SyncVar, value?.ToString() ?? "null", -1);
                    }
                    case SyncKind.List:
                    case SyncKind.Dictionary:
                    case SyncKind.HashSet:
                    {
                        PropertyInfo countProp = GetCountProperty(fieldType);
                        if (countProp == null)
                            return new SyncEntry(name, typeName, kind, "(no readable Count)", -1);
                        int count = Convert.ToInt32(countProp.GetValue(instance));
                        return new SyncEntry(name, typeName, kind, "(n=" + count + ")", count);
                    }
                    default:
                        // Other SyncTypes (e.g. SyncTimer / SyncStopwatch, or a user-defined SyncType):
                        // recognized as a SyncType but not summarized in v0.1.
                        return new SyncEntry(name, typeName, SyncKind.Unknown, "(SyncType)", -1);
                }
            }
            catch (Exception ex)
            {
                // Reading .Value / Count has no side effects and is safe pre-spawn, but values
                // are reset during despawn — so stay defensive and surface the reason instead of throwing.
                return new SyncEntry(name, typeName, SyncKind.Unknown, "(read error: " + ex.GetType().Name + ")", -1);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Classification + friendly names
        // ----------------------------------------------------------------------------------------

        // Classify by the open generic's name rather than resolved Type objects: robust to namespace
        // moves and avoids hard type references. All 4.x SyncTypes are generic.
        private static SyncKind Classify(Type fieldType)
        {
            if (!fieldType.IsGenericType) return SyncKind.Unknown;
            string n = fieldType.GetGenericTypeDefinition().Name;
            if (n.StartsWith("SyncVar", StringComparison.Ordinal)) return SyncKind.SyncVar;
            if (n.StartsWith("SyncList", StringComparison.Ordinal)) return SyncKind.List;
            if (n.StartsWith("SyncDictionary", StringComparison.Ordinal)) return SyncKind.Dictionary;
            if (n.StartsWith("SyncHashSet", StringComparison.Ordinal)) return SyncKind.HashSet;
            return SyncKind.Unknown;
        }

        private static PropertyInfo GetValueProperty(Type syncVarType)
        {
            if (_valuePropByType.TryGetValue(syncVarType, out var p)) return p;
            p = syncVarType.GetProperty(ValuePropertyName, BindingFlags.Instance | BindingFlags.Public);
            _valuePropByType[syncVarType] = p;
            return p;
        }

        private static PropertyInfo GetCountProperty(Type collectionType)
        {
            if (_countPropByType.TryGetValue(collectionType, out var p)) return p;
            p = collectionType.GetProperty(CountPropertyName, BindingFlags.Instance | BindingFlags.Public);
            _countPropByType[collectionType] = p;
            return p;
        }

        private static string FriendlyTypeName(Type t)
        {
            if (!t.IsGenericType) return t.Name;

            string baseName = t.Name;
            int tick = baseName.IndexOf('`');
            if (tick >= 0) baseName = baseName.Substring(0, tick);

            Type[] args = t.GetGenericArguments();
            var sb = new StringBuilder(baseName);
            sb.Append('<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FriendlyTypeName(args[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }

        // ----------------------------------------------------------------------------------------
        // FishNet type / version resolution (string-based for graceful degradation)
        // ----------------------------------------------------------------------------------------

        private static Type GetSyncBaseType()
        {
            if (_syncBaseResolved) return _syncBaseType;
            _syncBaseType = ResolveFishNetType(SyncBaseTypeName);
            _syncBaseResolved = true;
            return _syncBaseType;
        }

        // Reads FishNet's public version const from assembly metadata (the true installed value,
        // not an inlined copy). Returns "unknown" if the const is absent on this version.
        private static string DetectFishNetVersion()
        {
            try
            {
                Type nm = ResolveFishNetType(NetworkManagerTypeName);
                FieldInfo f = nm?.GetField(VersionFieldName, BindingFlags.Public | BindingFlags.Static);
                if (f == null) return "unknown";
                object v = f.GetRawConstantValue();
                return (v as string) ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static Type ResolveFishNetType(string fullName)
        {
            // Assembly-qualified lookup first; FishNet.Runtime is loaded alongside this assembly.
            Type t = Type.GetType(fullName + ", " + FishNetRuntimeAssembly);
            if (t != null) return t;

            // Fallback: scan loaded assemblies by full name (covers an unexpected assembly name).
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    t = assemblies[i].GetType(fullName);
                    if (t != null) return t;
                }
                catch
                {
                    // Some dynamic assemblies throw on GetType; skip them.
                }
            }
            return null;
        }

        private static ValidationResult Degraded(string version, string reason)
        {
            string message =
                "Live SyncVar readout is unavailable on this FishNet version (detected: " + version +
                ", tested: " + TestedFishNetVersion + "): " + reason +
                ". The object tree, ownership, and observers still work.";
            return new ValidationResult(false, version, message);
        }
    }
}
