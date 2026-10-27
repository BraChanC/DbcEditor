namespace DbcEditor.Models;

public sealed class DbcDatabase
{
    public string FilePath { get; set; } = "";
    public string NetworkName { get; set; } = "Network";
    public string Version { get; set; } = "";
    public string Protocol { get; set; } = "CAN";
    public string Comment { get; set; } = "";
    /// <summary>Symbols listed under NS_ (order preserved).</summary>
    public List<string> NewSymbols { get; } = new();
    /// <summary>Optional obsolete baud from BS_: &lt;baud&gt; : &lt;btr1&gt;,&lt;btr2&gt;.</summary>
    public uint? BaudRate { get; set; }
    public int? Btr1 { get; set; }
    public int? Btr2 { get; set; }
    public List<DbcNode> Nodes { get; } = new();
    public List<DbcMessage> Messages { get; } = new();
    public List<DbcSignal> AllSignals { get; } = new();
    public Dictionary<string, string> NetworkAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbcAttrDef> AttrDefs { get; } = new();
    public List<DbcValueTable> ValueTables { get; } = new();
    public List<DbcEnvVar> EnvVars { get; } = new();
    public List<DbcSignalGroup> SignalGroups { get; } = new();
    public List<DbcSignalType> SignalTypes { get; } = new();
    public List<DbcRelAttr> RelAttributes { get; } = new();
    /// <summary>Extra transmitters from BO_TX_BU_ (message id → node names).</summary>
    public Dictionary<uint, List<string>> MessageTransmitters { get; } = new();

    public DbcNode? FindNode(string name) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    public DbcMessage? FindMessage(uint id) =>
        Messages.FirstOrDefault(m => m.Id == id);

    public DbcSignal? FindSignal(uint msgId, string name) =>
        AllSignals.FirstOrDefault(s =>
            s.MessageId == msgId &&
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public DbcSignalType? FindSignalType(string name) =>
        SignalTypes.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class DbcNode
{
    public string Name { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Address { get; set; } = "0x0";
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DbcAttrDef
{
    public string Name { get; set; } = "";
    /// <summary>Empty = network; otherwise BU_ / BO_ / SG_ / EV_ / REL types.</summary>
    public string ObjectType { get; set; } = "";
    public string ValueType { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string Minimum { get; set; } = "";
    public string Maximum { get; set; } = "";
    public List<string> EnumValues { get; } = new();
}

public sealed class DbcMessage
{
    /// <summary>Vector extended-frame flag in DBC message IDs (bit 31).</summary>
    public const uint ExtendedIdFlag = 0x8000_0000u;
    /// <summary>29-bit CAN identifier mask.</summary>
    public const uint CanIdMask = 0x1FFF_FFFFu;

    public string Name { get; set; } = "";
    /// <summary>Raw DBC message id (may include <see cref="ExtendedIdFlag"/>).</summary>
    public uint Id { get; set; }
    public int Dlc { get; set; }
    public string Transmitter { get; set; } = "";
    public string Comment { get; set; } = "";
    public string TxMethod { get; set; } = "";
    public string CycleTime { get; set; } = "";
    public string IdFormat { get; set; } = "CAN Standard";
    public List<DbcSignal> Signals { get; } = new();
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolved category: BA_ "CAT_" value, else name prefix <c>CAT_*</c> remainder
    /// (e.g. <c>CAT_ABS_Info</c> → <c>ABS_Info</c>).
    /// </summary>
    public string Category { get; set; } = "";
    /// <summary><c>attr</c> | <c>name</c> | empty.</summary>
    public string CategorySource { get; set; } = "";
    /// <summary>Resolved filter tag from BA_ "FILTER"/"FILTER_" or name prefix <c>FILTER_*</c>.</summary>
    public string FilterTag { get; set; } = "";
    /// <summary><c>attr</c> | <c>name</c> | empty.</summary>
    public string FilterSource { get; set; } = "";

    /// <summary>29-bit (or 11-bit) CAN id with bit 31 cleared for display.</summary>
    public uint CanId => Id & CanIdMask;
    public bool HasExtendedIdFlag => (Id & ExtendedIdFlag) != 0;
    public string IdHex => $"0x{CanId:X}";
    /// <summary>Vector tree id: <c>0x4D2</c> or <c>0x10E1x</c> for extended.</summary>
    public string IdTreeLabel =>
        HasExtendedIdFlag || IdFormat.Contains("Extended", StringComparison.OrdinalIgnoreCase)
            ? $"{IdHex}x"
            : IdHex;
    public string TreeTitle => $"{Name} ({IdTreeLabel})";

    /// <summary>
    /// Infer standard vs extended from bit 31 (Vector) or from id width when the flag is absent.
    /// </summary>
    public static string InferIdFormat(uint rawId) =>
        (rawId & ExtendedIdFlag) != 0 || (rawId & ~0x7FFu) != 0
            ? "CAN Extended"
            : "CAN Standard";

    /// <summary>Store <paramref name="canId"/> and set/clear bit 31 from IdFormat.</summary>
    public static uint EncodeId(uint canId, string idFormat)
    {
        var id = canId & CanIdMask;
        if (idFormat.Contains("Extended", StringComparison.OrdinalIgnoreCase))
            id |= ExtendedIdFlag;
        return id;
    }
}

public sealed class DbcSignal
{
    public string Name { get; set; } = "";
    public int StartBit { get; set; }
    /// <summary>CANdb++ Startbit. Motorola shows the LSB; <see cref="StartBit"/> stays the DBC MSB.</summary>
    public int VectorStartBit => Services.DbcBitLayout.VectorStartBit(this);
    public int Length { get; set; }
    public bool IsLittleEndian { get; set; } = true;
    public bool IsSigned { get; set; }
    public double Factor { get; set; } = 1;
    public double Offset { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public string Unit { get; set; } = "";
    public List<string> Receivers { get; } = new();
    public string MessageName { get; set; } = "";
    public uint MessageId { get; set; }
    public string Multiplexing { get; set; } = "";
    /// <summary>Vector list display: empty multiplexing shown as "-".</summary>
    public string MultiplexingDisplay =>
        string.IsNullOrWhiteSpace(Multiplexing) ? "-" : Multiplexing;
    public string InitialValue { get; set; } = "0";
    public string Comment { get; set; } = "";
    /// <summary>SIG_VALTYPE_: 0=integer, 1=32-bit float, 2=64-bit double.</summary>
    public int SigValType { get; set; }
    /// <summary>SIG_TYPE_REF_ target type name.</summary>
    public string? SignalTypeRef { get; set; }
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbcValueDesc> ValueDescriptions { get; } = new();
    /// <summary>Comment on the synthetic VtSig_* value table (Vector Definition tab).</summary>
    public string ValueTableComment { get; set; } = "";

    /// <summary>BA_ "CAT_" or name prefix <c>CAT_*</c> remainder.</summary>
    public string Category { get; set; } = "";
    public string CategorySource { get; set; } = "";
    /// <summary>BA_ "FILTER"/"FILTER_" or name prefix <c>FILTER_*</c> remainder.</summary>
    public string FilterTag { get; set; } = "";
    public string FilterSource { get; set; } = "";
}

public sealed class DbcValueDesc
{
    public long Value { get; set; }
    public string Description { get; set; } = "";
    /// <summary>CANdb++ Value Descriptions column. File still stores the decimal raw value.</summary>
    public string ValueHex => Value < 0 ? $"-0x{-Value:X}" : $"0x{Value:X}";
}

public sealed class DbcValueTable
{
    public string Name { get; set; } = "";
    public List<DbcValueDesc> Entries { get; } = new();
}

public sealed class DbcEnvVar
{
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public string Unit { get; set; } = "";
    public double InitialValue { get; set; }
    public uint EvId { get; set; }
    public string AccessType { get; set; } = "";
    public List<string> AccessNodes { get; } = new();
    public string Comment { get; set; } = "";
    public int? DataLength { get; set; }
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbcValueDesc> ValueDescriptions { get; } = new();
    /// <summary>Comment on the synthetic VtEv_* value table.</summary>
    public string ValueTableComment { get; set; } = "";
}

public sealed class DbcSignalGroup
{
    public uint MessageId { get; set; }
    public string Name { get; set; } = "";
    public int Repetitions { get; set; }
    public string Comment { get; set; } = "";
    public List<string> SignalNames { get; } = new();
}

/// <summary>SGTYPE_ reusable signal template.</summary>
public sealed class DbcSignalType
{
    public string Name { get; set; } = "";
    public int Length { get; set; }
    public bool IsLittleEndian { get; set; } = true;
    public bool IsSigned { get; set; }
    public double Factor { get; set; } = 1;
    public double Offset { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public string Unit { get; set; } = "";
    public double DefaultValue { get; set; }
    public string ValueTableName { get; set; } = "";
    /// <summary>SIGTYPE_VALTYPE_: 0/1/2/3.</summary>
    public int? SigValType { get; set; }
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbcValueDesc> ValueDescriptions { get; } = new();
}

/// <summary>BA_REL_ assignment on a node–object pair.</summary>
public sealed class DbcRelAttr
{
    public string Name { get; set; } = "";
    /// <summary>BU_SG_REL_ / BU_BO_REL_ / BU_EV_REL_.</summary>
    public string RelType { get; set; } = "";
    public string NodeName { get; set; } = "";
    public uint? MessageId { get; set; }
    public string? SignalName { get; set; }
    public string? EnvVarName { get; set; }
    public string Value { get; set; } = "";
}
