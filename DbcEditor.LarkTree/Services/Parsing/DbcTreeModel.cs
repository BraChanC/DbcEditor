using System.Globalization;
using System.Text.RegularExpressions;
using DbcEditor.Models;
using EtAlii.Lark;

namespace DbcEditor.Services.Parsing;

/// <summary>Phase 3: apply statement records onto <see cref="DbcDatabase"/> in file order, then shared finalize.</summary>
static class DbcTreeModel
{
    public static DbcDatabase Build(List<object?> stmts, string displayName)
    {
        var db = new DbcDatabase { NetworkName = displayName };
        foreach (var item in stmts)
        {
            if (item is not Dictionary<string, object?> s) continue;
            switch (s.GetValueOrDefault("kind") as string)
            {
                case "version": db.Version = Str(s, "text"); break;
                case "ns":
                    foreach (var n in List<string>(s, "items")) db.NewSymbols.Add(n);
                    break;
                case "bs":
                    if (s["baud"] is uint baud) db.BaudRate = baud;
                    if (s["btr1"] is int b1) db.Btr1 = b1;
                    if (s["btr2"] is int b2) db.Btr2 = b2;
                    break;
                case "bu":
                    foreach (var name in List<string>(s, "nodes"))
                        if (db.FindNode(name) == null) db.Nodes.Add(new DbcNode { Name = name });
                    break;
                case "message": ApplyMessage(db, s); break;
                case "cm": ApplyCm(db, s); break;
                case "ba_def": ApplyBaDef(db, s); break;
                case "ba_def_def": ApplyBaDefDef(db, s); break;
                case "ba": ApplyBa(db, s); break;
                case "val_table": ApplyValTable(db, s); break;
                case "val": ApplyVal(db, s); break;
                case "sig_group": ApplySigGroup(db, s); break;
                case "sig_valtype": ApplySigValType(db, s); break;
                case "ev": ApplyEv(db, s); break;
                case "ev_data": ApplyEvData(db, s); break;
                case "bo_tx": ApplyBoTx(db, s); break;
                case "sg_mul": ApplySgMul(db, s); break;
            }
        }
        DbcModelFinalize.Apply(db);
        return db;
    }

    static void ApplyMessage(DbcDatabase db, Dictionary<string, object?> s)
    {
        var id = (uint)s["id"]!;
        var msg = new DbcMessage
        {
            Id = id,
            Name = Str(s, "name"),
            Dlc = (int)s["dlc"]!,
            Transmitter = Str(s, "tx"),
            IdFormat = DbcMessage.InferIdFormat(id)
        };
        foreach (var sigItem in (List<object?>)s["sigs"]!)
        {
            if (sigItem is not Dictionary<string, object?> sig) continue;
            var signal = new DbcSignal
            {
                Name = Str(sig, "name"),
                StartBit = (int)sig["start"]!,
                Length = (int)sig["len"]!,
                IsLittleEndian = (int)sig["order"]! == 1,
                IsSigned = (bool)sig["signed"]!,
                Factor = (double)sig["factor"]!,
                Offset = (double)sig["offset"]!,
                Min = (double)sig["min"]!,
                Max = (double)sig["max"]!,
                Unit = Str(sig, "unit"),
                MessageName = msg.Name,
                MessageId = msg.Id,
                Multiplexing = FormatMux(Str(sig, "mux"))
            };
            foreach (var r in List<string>(sig, "recvs")) signal.Receivers.Add(r);
            msg.Signals.Add(signal);
            db.AllSignals.Add(signal);
        }
        db.Messages.Add(msg);
    }

    static void ApplyCm(DbcDatabase db, Dictionary<string, object?> s)
    {
        var text = Str(s, "text");
        switch (Str(s, "where"))
        {
            case "bu":
                var node = db.FindNode(Str(s, "name"));
                if (node != null) node.Comment = text;
                break;
            case "bo":
                var msg = db.FindMessage((uint)s["id"]!);
                if (msg != null) msg.Comment = text;
                break;
            case "sg":
                var sig = db.FindSignal((uint)s["id"]!, Str(s, "name"));
                if (sig != null) sig.Comment = text;
                break;
            case "ev":
                var ev = db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, Str(s, "name"), StringComparison.OrdinalIgnoreCase));
                if (ev != null) ev.Comment = text;
                break;
            default:
                db.Comment = text;
                db.NetworkAttributes["CM_"] = text;
                break;
        }
    }

    static void ApplyBaDef(DbcDatabase db, Dictionary<string, object?> s)
    {
        var def = new DbcAttrDef
        {
            Name = Str(s, "name"),
            ObjectType = Str(s, "obj"),
            ValueType = Str(s, "vtype"),
            Minimum = s.GetValueOrDefault("min") as string ?? "",
            Maximum = s.GetValueOrDefault("max") as string ?? ""
        };
        if (s.GetValueOrDefault("enum") is List<string> values)
            foreach (var v in values) def.EnumValues.Add(v);
        db.AttrDefs.Add(def);
    }

    static void ApplyBaDefDef(DbcDatabase db, Dictionary<string, object?> s)
    {
        var name = Str(s, "name");
        var val = Str(s, "value");
        foreach (var def in db.AttrDefs.Where(a =>
                     string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            def.DefaultValue = val;
    }

    static void ApplyBa(DbcDatabase db, Dictionary<string, object?> s)
    {
        var aname = Str(s, "attr");
        var aval = MapEnumIndex(db, aname, Str(s, "value"));
        switch (Str(s, "where"))
        {
            case "bu":
                var n = Str(s, "name");
                var node = db.FindNode(n) ?? new DbcNode { Name = n };
                if (db.FindNode(n) == null) db.Nodes.Add(node);
                node.Attributes[aname] = aval;
                if (aname.Contains("Address", StringComparison.OrdinalIgnoreCase))
                    node.Address = aval.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? aval
                        : (ulong.TryParse(aval, out var addr) ? $"0x{addr:X}" : aval);
                break;
            case "bo":
                var msg = db.FindMessage((uint)s["id"]!);
                if (msg != null)
                {
                    msg.Attributes[aname] = aval;
                    if (aname.Equals("GenMsgCycleTime", StringComparison.OrdinalIgnoreCase))
                        msg.CycleTime = aval;
                    else if (aname.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase))
                        msg.TxMethod = aval;
                }
                break;
            case "sg":
                var sig = db.FindSignal((uint)s["id"]!, Str(s, "name"));
                if (sig != null) sig.Attributes[aname] = aval;
                break;
            case "ev":
                var ev = db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, Str(s, "name"), StringComparison.OrdinalIgnoreCase));
                if (ev != null) ev.Attributes[aname] = aval;
                break;
            default:
                db.NetworkAttributes[aname] = aval;
                break;
        }
    }

    static void ApplyValTable(DbcDatabase db, Dictionary<string, object?> s)
    {
        var table = new DbcValueTable { Name = Str(s, "name") };
        foreach (var (value, desc) in Pairs(s))
            table.Entries.Add(new DbcValueDesc { Value = value, Description = desc });
        db.ValueTables.Add(table);
    }

    static void ApplyVal(DbcDatabase db, Dictionary<string, object?> s)
    {
        var pairs = Pairs(s);
        switch (Str(s, "where"))
        {
            case "ev":
                var ev = db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, Str(s, "name"), StringComparison.OrdinalIgnoreCase));
                if (ev != null)
                    foreach (var (value, desc) in pairs)
                        ev.ValueDescriptions.Add(new DbcValueDesc { Value = value, Description = desc });
                break;
            case "msg":
                var sig = db.FindSignal((uint)s["id"]!, Str(s, "name"));
                if (sig != null)
                    foreach (var (value, desc) in pairs)
                        sig.ValueDescriptions.Add(new DbcValueDesc { Value = value, Description = desc });
                break;
            default:
                var env = db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, Str(s, "name"), StringComparison.OrdinalIgnoreCase));
                if (env != null)
                    foreach (var (value, desc) in pairs)
                        env.ValueDescriptions.Add(new DbcValueDesc { Value = value, Description = desc });
                break;
        }
    }

    static void ApplySigGroup(DbcDatabase db, Dictionary<string, object?> s)
    {
        var g = new DbcSignalGroup
        {
            MessageId = (uint)s["id"]!,
            Name = Str(s, "name"),
            Repetitions = (int)s["reps"]!
        };
        foreach (var name in List<string>(s, "signals")) g.SignalNames.Add(name);
        db.SignalGroups.Add(g);
    }

    static void ApplySigValType(DbcDatabase db, Dictionary<string, object?> s)
    {
        var sig = db.FindSignal((uint)s["id"]!, Str(s, "name"));
        if (sig != null) sig.SigValType = (int)s["vt"]!;
    }

    static void ApplyEv(DbcDatabase db, Dictionary<string, object?> s)
    {
        var ev = new DbcEnvVar
        {
            Name = Str(s, "name"),
            Type = (int)s["type"]!,
            Min = (double)s["min"]!,
            Max = (double)s["max"]!,
            Unit = Str(s, "unit"),
            InitialValue = (double)s["init"]!,
            EvId = (uint)s["evid"]!,
            AccessType = Str(s, "access")
        };
        foreach (var n in List<string>(s, "nodes")) ev.AccessNodes.Add(n);
        db.EnvVars.Add(ev);
    }

    static void ApplyEvData(DbcDatabase db, Dictionary<string, object?> s)
    {
        var ev = db.EnvVars.FirstOrDefault(e =>
            string.Equals(e.Name, Str(s, "name"), StringComparison.OrdinalIgnoreCase));
        if (ev != null) ev.DataLength = (int)s["len"]!;
    }

    static void ApplyBoTx(DbcDatabase db, Dictionary<string, object?> s)
    {
        var id = (uint)s["id"]!;
        if (!db.MessageTransmitters.TryGetValue(id, out var list))
            db.MessageTransmitters[id] = list = new List<string>();
        list.AddRange(List<string>(s, "nodes"));
    }

    static void ApplySgMul(DbcDatabase db, Dictionary<string, object?> s)
    {
        var ranges = List<string>(s, "ranges");
        var sig = db.FindSignal((uint)s["id"]!, Str(s, "sig"));
        if (sig == null || ranges.Count == 0) return;
        var rangeText = $"Mux {Str(s, "mux")} [{string.Join(",", ranges)}]";
        sig.Multiplexing = (sig.Multiplexing ?? "").Contains("Multiplexor", StringComparison.Ordinal)
            ? $"{rangeText} / Multiplexor"
            : rangeText;
    }

    static string MapEnumIndex(DbcDatabase db, string attrName, string aval)
    {
        if (!int.TryParse(aval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
            return aval;
        var def = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, attrName, StringComparison.OrdinalIgnoreCase) &&
            a.ValueType == "ENUM" && a.EnumValues.Count > 0);
        if (def != null && idx >= 0 && idx < def.EnumValues.Count)
            return def.EnumValues[idx];
        return aval;
    }

    /// <summary>Same labels as the token parser, so m32 and m40 stay mutually exclusive.</summary>
    static string FormatMux(string? mux)
    {
        if (string.IsNullOrWhiteSpace(mux)) return "-";
        mux = mux.Trim();
        if (mux.Equals("M", StringComparison.Ordinal)) return "Multiplexor";

        var mNm = Regex.Match(mux, @"^m(\d+)M$", RegexOptions.IgnoreCase);
        if (mNm.Success)
            return $"Multiplexed signal {mNm.Groups[1].Value} / Multiplexor";

        var mn = Regex.Match(mux, @"^m(\d+)$", RegexOptions.IgnoreCase);
        if (mn.Success)
            return $"Multiplexed signal {mn.Groups[1].Value}";

        if (mux.StartsWith('m') || mux.StartsWith('M'))
            return $"Multiplexed signal {mux.TrimStart('m', 'M')}";
        return mux;
    }

    static string Str(Dictionary<string, object?> s, string key) => s.GetValueOrDefault(key) as string ?? "";

    static List<T> List<T>(Dictionary<string, object?> s, string key) =>
        s.GetValueOrDefault(key) as List<T> ?? new List<T>();

    static List<(long, string)> Pairs(Dictionary<string, object?> s) =>
        s.GetValueOrDefault("pairs") as List<(long, string)> ?? new List<(long, string)>();
}

static class DbcLarkTree
{
    static readonly Lark Parser = Lark.FromGrammar(DbcLarkGrammar.Text).UseLalr().Build();

    public static DbcDatabase Parse(string text, string displayName)
    {
        var tree = Parser.Parse(text);
        var stmts = (List<object?>)new DbcTreeTransformer().Transform(tree)!;
        return DbcTreeModel.Build(stmts, displayName);
    }
}
