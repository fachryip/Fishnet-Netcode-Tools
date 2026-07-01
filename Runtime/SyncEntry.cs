namespace FishNetNetworkTools
{
    /// <summary>
    /// Classification of a SyncType field, derived from its declared type.
    /// </summary>
    public enum SyncKind
    {
        SyncVar,
        List,
        Dictionary,
        HashSet,
        Unknown
    }

    /// <summary>
    /// A single readable SyncType value snapshot for display. Plain value object:
    /// no reflection types cross this boundary, so the inspector UI never sees a FieldInfo.
    /// </summary>
    public readonly struct SyncEntry
    {
        /// <summary>Declared field name on the NetworkBehaviour.</summary>
        public readonly string Name;

        /// <summary>Friendly type name, e.g. "SyncVar&lt;int&gt;" or "SyncList&lt;int&gt;".</summary>
        public readonly string TypeName;

        /// <summary>Kind classification (scalar SyncVar vs collection vs unknown).</summary>
        public readonly SyncKind Kind;

        /// <summary>Scalar value as text, or a count summary like "(n=3)" for collections.</summary>
        public readonly string ValueText;

        /// <summary>Element count for collection SyncTypes; -1 for scalars and unknown entries.</summary>
        public readonly int Count;

        public SyncEntry(string name, string typeName, SyncKind kind, string valueText, int count)
        {
            Name = name;
            TypeName = typeName;
            Kind = kind;
            ValueText = valueText;
            Count = count;
        }
    }

    /// <summary>
    /// Result of the startup reflection self-check. Lets the UI degrade gracefully
    /// (still show objects/ownership/observers even when the SyncVar readout is
    /// unavailable on an untested FishNet version) instead of throwing.
    /// </summary>
    public readonly struct ValidationResult
    {
        /// <summary>True when reflection assumptions hold and SyncVar values can be read.</summary>
        public readonly bool Ok;

        /// <summary>Detected FishNet version, or "unknown".</summary>
        public readonly string FishNetVersion;

        /// <summary>User-facing message explaining the degradation; populated when not Ok.</summary>
        public readonly string Message;

        public ValidationResult(bool ok, string fishNetVersion, string message)
        {
            Ok = ok;
            FishNetVersion = fishNetVersion;
            Message = message;
        }
    }
}
