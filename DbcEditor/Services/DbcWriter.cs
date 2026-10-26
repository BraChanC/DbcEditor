using System.Globalization;
using System.Text;
using DbcEditor.Models;

namespace DbcEditor.Services;

/// <summary>
/// Serialize <see cref="DbcDatabase"/> to Vector-style DBC text.
/// Section order: VERSION → NS_ → BS_ → BU_ → BO_/SG_ → ... comments/attrs/VAL_/…
/// </summary>
public static class DbcWriter
{
    public static string Write(DbcDatabase db)
    {
        var sb = new StringBuilder();
        WriteVersion(sb, db);
        WriteNs(sb, db);
        WriteBs(sb, db);
        WriteBu(sb, db);
        WriteMessages(sb, db);
        WriteSgMulVal(sb, db);
        WriteValueTables(sb, db);
        WriteComments(sb, db);
        WriteAttrDefs(sb, db);
        WriteAttrDefaults(sb, db);
        WriteAttrValues(sb, db);
        WriteRelAttrs(sb, db);
        WriteValueDescriptions(sb, db);
        WriteEnvVars(sb, db);
        WriteBoTxBu(sb, db);
        WriteSigValTypes(sb, db);
        WriteSignalGroups(sb, db);
        WriteSignalTypes(sb, db);
        return sb.ToString();
    }

    private static void WriteVersion(StringBuilder sb, DbcDatabase db)
    {
        sb.AppendLine($"VERSION \"{Esc(db.Version)}\"");
        sb.AppendLine();
    }

    private static void WriteNs(StringBuilder sb, DbcDatabase db)
    {
        sb.AppendLine("NS_ :");
        if (db.NewSymbols.Count == 0)
        {
            // Minimal Vector-ish symbol set when file had empty NS_
            foreach (var s in DefaultNsSymbols)
                sb.AppendLine($"\t{s}");
        }
        else
        {
            foreach (var s in db.NewSymbols)
                sb.AppendLine($"\t{s}");
        }
        sb.AppendLine();
    }

    private static readonly string[] DefaultNsSymbols =
    {
        "NS_DESC_", "CM_", "BA_DEF_", "BA_", "VAL_", "CAT_DEF_", "CAT_", "FILTER",
        "BA_DEF_DEF_", "EV_DATA_", "ENVVAR_DATA_", "SGTYPE_", "SGTYPE_VAL_", "BA_DEF_SGTYPE_",
        "BA_SGTYPE_", "SIG_TYPE_REF_", "VAL_TABLE_", "SIG_GROUP_", "SIG_VALTYPE_",
        "SIGTYPE_VALTYPE_", "BO_TX_BU_", "BA_DEF_REL_", "BA_REL_", "BA_DEF_DEF_REL_",
        "BU_SG_REL_", "BU_EV_REL_", "BU_BO_REL_", "SG_MUL_VAL_"
    };

    private static void WriteBs(StringBuilder sb, DbcDatabase db)
    {
        if (db.BaudRate is { } baud)
        {
            if (db.Btr1 is { } b1 && db.Btr2 is { } b2)
                sb.AppendLine($"BS_: {baud}:{b1},{b2}");
            else
                sb.AppendLine($"BS_: {baud}");
        }
        else
            sb.AppendLine("BS_:");
        sb.AppendLine();
    }

    private static void WriteBu(StringBuilder sb, DbcDatabase db)
    {
        sb.Append("BU_:");
        foreach (var n in db.Nodes)
            sb.Append(' ').Append(n.Name);
        sb.AppendLine();
        sb.AppendLine();
    }

    private static void WriteMessages(StringBuilder sb, DbcDatabase db)
    {
        foreach (var m in db.Messages)
        {
            var tx = string.IsNullOrWhiteSpace(m.Transmitter) ? "Vector__XXX" : m.Transmitter;
            sb.AppendLine($"BO_ {m.Id} {m.Name}: {m.Dlc} {tx}");
            foreach (var s in m.Signals)
                WriteSignal(sb, s);
            sb.AppendLine();
        }
    }

    private static void WriteSignal(StringBuilder sb, DbcSignal s)
    {
        var mux = MuxToDbc(s.Multiplexing);
        var muxPart = string.IsNullOrEmpty(mux) ? "" : $" {mux}";
        var order = s.IsLittleEndian ? 1 : 0;
        var sign = s.IsSigned ? "-" : "+";
        var recv = s.Receivers.Count > 0
            ? string.Join(",", s.Receivers)
            : "Vector__XXX";
        sb.AppendLine(
            $" SG_ {s.Name}{muxPart} : {s.StartBit}|{s.Length}@{order}{sign} " +
            $"({Fmt(s.Factor)},{Fmt(s.Offset)}) [{Fmt(s.Min)}|{Fmt(s.Max)}] " +
            $"\"{Esc(s.Unit)}\"  {recv}");
    }

    /// <summary>Reverse loader FormatMux / SG_MUL_VAL_ display → DBC mux token (mN / mNM / M).</summary>
    private static string MuxToDbc(string? display)
    {
        if (string.IsNullOrWhiteSpace(display) || display == "-") return "";
        var t = display.Trim();
        // Already a raw DBC mux token
        if (t == "M" || System.Text.RegularExpressions.Regex.IsMatch(t, @"^m\d+M?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return t.StartsWith('m') ? "m" + t[1..] : t;

        if (t.Equals("Multiplexor", StringComparison.OrdinalIgnoreCase))
            return "M";

        var both = System.Text.RegularExpressions.Regex.Match(t,
            @"^Multiplexed signal (\d+)\s*/\s*Multiplexor$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (both.Success) return $"m{both.Groups[1].Value}M";

        var one = System.Text.RegularExpressions.Regex.Match(t,
            @"^Multiplexed signal (\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (one.Success) return $"m{one.Groups[1].Value}";

        // SG_MUL_VAL_ display: "Mux <parent> [lo-hi] / Multiplexor" or "Mux <parent> [lo-hi]"
        var mulValMux = System.Text.RegularExpressions.Regex.Match(t,
            @"^Mux\s+\S+\s+\[(\d+)-(\d+)\]\s*/\s*Multiplexor$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mulValMux.Success)
            return $"m{mulValMux.Groups[1].Value}M";

        var mulVal = System.Text.RegularExpressions.Regex.Match(t,
            @"^Mux\s+\S+\s+\[(\d+)-(\d+)\]$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mulVal.Success)
            return $"m{mulVal.Groups[1].Value}";

        return "";
    }

    private static void WriteSgMulVal(StringBuilder sb, DbcDatabase db)
    {
        var any = false;
        foreach (var s in db.AllSignals)
        {
            var t = (s.Multiplexing ?? "").Trim();
            var m = System.Text.RegularExpressions.Regex.Match(t,
                @"^Mux\s+(\S+)\s+\[(\d+)-(\d+)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            sb.AppendLine($"SG_MUL_VAL_ {s.MessageId} {s.Name} {m.Groups[1].Value} {m.Groups[2].Value}-{m.Groups[3].Value} ;");
            any = true;
        }
        if (any) sb.AppendLine();
    }

    private static void WriteValueTables(StringBuilder sb, DbcDatabase db)
    {
        foreach (var table in db.ValueTables)
        {
            sb.Append($"VAL_TABLE_ {table.Name}");
            foreach (var e in table.Entries)
                sb.Append($" {e.Value} \"{Esc(e.Description)}\"");
            sb.AppendLine(" ;");
        }
        if (db.ValueTables.Count > 0) sb.AppendLine();
    }

    private static void WriteComments(StringBuilder sb, DbcDatabase db)
    {
        var netCm = !string.IsNullOrEmpty(db.Comment) ? db.Comment
            : db.NetworkAttributes.TryGetValue("CM_", out var cm) ? cm : "";
        if (!string.IsNullOrEmpty(netCm))
            sb.AppendLine($"CM_ \"{Esc(netCm)}\";");

        foreach (var n in db.Nodes.Where(n => !string.IsNullOrEmpty(n.Comment)))
            sb.AppendLine($"CM_ BU_ {n.Name} \"{Esc(n.Comment)}\";");

        foreach (var m in db.Messages.Where(m => !string.IsNullOrEmpty(m.Comment)))
            sb.AppendLine($"CM_ BO_ {m.Id} \"{Esc(m.Comment)}\";");

        foreach (var s in db.AllSignals.Where(s => !string.IsNullOrEmpty(s.Comment)))
            sb.AppendLine($"CM_ SG_ {s.MessageId} {s.Name} \"{Esc(s.Comment)}\";");

        foreach (var ev in db.EnvVars.Where(e => !string.IsNullOrEmpty(e.Comment)))
            sb.AppendLine($"CM_ EV_ {ev.Name} \"{Esc(ev.Comment)}\";");

        sb.AppendLine();
    }

    private static void WriteAttrDefs(StringBuilder sb, DbcDatabase db)
    {
        foreach (var d in db.AttrDefs)
        {
            var isRel = d.ObjectType is "BU_SG_REL_" or "BU_BO_REL_" or "BU_EV_REL_";
            var isSgType = d.ObjectType.Equals("SGTYPE_", StringComparison.OrdinalIgnoreCase);
            if (isSgType)
            {
                // Written with BA_DEF_SGTYPE_ near SGTYPE_ block if needed; skip here
                // unless we only have AttrDefs — emit BA_DEF_SGTYPE_
                sb.Append($"BA_DEF_SGTYPE_ \"{Esc(d.Name)}\" ");
            }
            else
            {
                var kw = isRel ? "BA_DEF_REL_" : "BA_DEF_";
                if (string.IsNullOrEmpty(d.ObjectType))
                    sb.Append($"{kw}  \"{Esc(d.Name)}\" ");
                else
                    sb.Append($"{kw} {d.ObjectType}  \"{Esc(d.Name)}\" ");
            }

            switch (d.ValueType.ToUpperInvariant())
            {
                case "ENUM":
                    sb.Append("ENUM  ");
                    sb.Append(string.Join(",", d.EnumValues.Select(v => $"\"{Esc(v)}\"")));
                    sb.AppendLine(";");
                    break;
                case "INT":
                    sb.AppendLine($"INT {NumOr0(d.Minimum)} {NumOr0(d.Maximum)};");
                    break;
                case "HEX":
                    sb.AppendLine($"HEX {NumOr0(d.Minimum)} {NumOr0(d.Maximum)};");
                    break;
                case "FLOAT":
                    sb.AppendLine($"FLOAT {NumOr0(d.Minimum)} {NumOr0(d.Maximum)};");
                    break;
                default:
                    sb.AppendLine("STRING ;");
                    break;
            }
        }
        if (db.AttrDefs.Count > 0) sb.AppendLine();
    }

    private static void WriteAttrDefaults(StringBuilder sb, DbcDatabase db)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in db.AttrDefs)
        {
            if (!seen.Add(d.Name)) continue;
            var isRel = d.ObjectType.EndsWith("REL_", StringComparison.Ordinal);
            var kw = isRel ? "BA_DEF_DEF_REL_" : "BA_DEF_DEF_";
            sb.AppendLine($"{kw}  \"{Esc(d.Name)}\" {FormatDefaultLiteral(d)};");
        }
        if (db.AttrDefs.Count > 0) sb.AppendLine();
    }

    private static string FormatDefaultLiteral(DbcAttrDef d)
    {
        var v = d.DefaultValue ?? "";
        if (d.ValueType.Equals("STRING", StringComparison.OrdinalIgnoreCase) ||
            d.ValueType.Equals("ENUM", StringComparison.OrdinalIgnoreCase))
            return $"\"{Esc(v)}\"";
        if (string.IsNullOrEmpty(v)) return "\"\"";
        return v;
    }

    private static void WriteAttrValues(StringBuilder sb, DbcDatabase db)
    {
        foreach (var kv in db.NetworkAttributes)
        {
            if (kv.Key.Equals("CM_", StringComparison.OrdinalIgnoreCase)) continue;
            WriteBa(sb, kv.Key, FormatBaValue(db, kv.Key, kv.Value));
        }

        foreach (var n in db.Nodes)
        foreach (var kv in n.Attributes)
            WriteBa(sb, kv.Key, FormatBaValue(db, kv.Key, kv.Value), "BU_", n.Name);

        foreach (var m in db.Messages)
        foreach (var kv in m.Attributes)
            WriteBa(sb, kv.Key, FormatBaValue(db, kv.Key, kv.Value), "BO_", m.Id.ToString(CultureInfo.InvariantCulture));

        foreach (var s in db.AllSignals)
        foreach (var kv in s.Attributes)
            WriteBa(sb, kv.Key, FormatBaValue(db, kv.Key, kv.Value), "SG_",
                s.MessageId.ToString(CultureInfo.InvariantCulture), s.Name);

        foreach (var ev in db.EnvVars)
        foreach (var kv in ev.Attributes)
            WriteBa(sb, kv.Key, FormatBaValue(db, kv.Key, kv.Value), "EV_", ev.Name);

        sb.AppendLine();
    }

    private static void WriteBa(StringBuilder sb, string name, string value,
        string? obj = null, string? a = null, string? b = null)
    {
        if (obj == null)
            sb.AppendLine($"BA_ \"{Esc(name)}\" {value};");
        else if (b == null)
            sb.AppendLine($"BA_ \"{Esc(name)}\" {obj} {a} {value};");
        else
            sb.AppendLine($"BA_ \"{Esc(name)}\" {obj} {a} {b} {value};");
    }

    private static string FormatBaValue(DbcDatabase db, string attrName, string raw)
    {
        var def = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, attrName, StringComparison.OrdinalIgnoreCase));
        if (def != null && def.ValueType.Equals("ENUM", StringComparison.OrdinalIgnoreCase) &&
            def.EnumValues.Count > 0)
        {
            var idx = def.EnumValues.FindIndex(v =>
                string.Equals(v, raw, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) return idx.ToString(CultureInfo.InvariantCulture);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return raw;
        }
        if (def != null &&
            (def.ValueType.Equals("INT", StringComparison.OrdinalIgnoreCase) ||
             def.ValueType.Equals("HEX", StringComparison.OrdinalIgnoreCase) ||
             def.ValueType.Equals("FLOAT", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrEmpty(raw)) return "0";
            return raw;
        }
        return $"\"{Esc(raw)}\"";
    }

    private static void WriteRelAttrs(StringBuilder sb, DbcDatabase db)
    {
        foreach (var r in db.RelAttributes)
        {
            var val = FormatBaValue(db, r.Name, r.Value);
            switch (r.RelType)
            {
                case "BU_SG_REL_" when r.MessageId != null && r.SignalName != null:
                    sb.AppendLine(
                        $"BA_REL_ \"{Esc(r.Name)}\" BU_SG_REL_ {r.NodeName} SG_ {r.MessageId.Value} {r.SignalName} {val};");
                    break;
                case "BU_BO_REL_" when r.MessageId != null:
                    sb.AppendLine(
                        $"BA_REL_ \"{Esc(r.Name)}\" BU_BO_REL_ {r.NodeName} BO_ {r.MessageId.Value} {val};");
                    break;
                case "BU_EV_REL_" when r.EnvVarName != null:
                    sb.AppendLine(
                        $"BA_REL_ \"{Esc(r.Name)}\" BU_EV_REL_ {r.NodeName} EV_ {r.EnvVarName} {val};");
                    break;
            }
        }
        if (db.RelAttributes.Count > 0) sb.AppendLine();
    }

    private static void WriteValueDescriptions(StringBuilder sb, DbcDatabase db)
    {
        foreach (var s in db.AllSignals.Where(s => s.ValueDescriptions.Count > 0))
        {
            sb.Append($"VAL_ {s.MessageId} {s.Name}");
            foreach (var v in s.ValueDescriptions)
                sb.Append($" {v.Value} \"{Esc(v.Description)}\"");
            sb.AppendLine(" ;");
        }
        foreach (var ev in db.EnvVars.Where(e => e.ValueDescriptions.Count > 0))
        {
            sb.Append($"VAL_ {ev.Name}");
            foreach (var v in ev.ValueDescriptions)
                sb.Append($" {v.Value} \"{Esc(v.Description)}\"");
            sb.AppendLine(" ;");
        }
        sb.AppendLine();
    }

    private static void WriteEnvVars(StringBuilder sb, DbcDatabase db)
    {
        foreach (var ev in db.EnvVars)
        {
            var access = string.IsNullOrWhiteSpace(ev.AccessType) ? "Unrestricted" : ev.AccessType;
            var nodes = ev.AccessNodes.Count > 0
                ? string.Join(",", ev.AccessNodes)
                : "Vector__XXX";
            sb.AppendLine(
                $"EV_ {ev.Name}: {ev.Type} [{Fmt(ev.Min)}|{Fmt(ev.Max)}] \"{Esc(ev.Unit)}\" " +
                $"{Fmt(ev.InitialValue)} {ev.EvId} {access} {nodes};");
            if (ev.DataLength is { } len)
                sb.AppendLine($"ENVVAR_DATA_ {ev.Name}: {len};");
        }
        if (db.EnvVars.Count > 0) sb.AppendLine();
    }

    private static void WriteBoTxBu(StringBuilder sb, DbcDatabase db)
    {
        foreach (var kv in db.MessageTransmitters)
        {
            if (kv.Value.Count == 0) continue;
            sb.AppendLine($"BO_TX_BU_ {kv.Key} : {string.Join(",", kv.Value)};");
        }
        if (db.MessageTransmitters.Count > 0) sb.AppendLine();
    }

    private static void WriteSigValTypes(StringBuilder sb, DbcDatabase db)
    {
        foreach (var s in db.AllSignals.Where(s => s.SigValType != 0))
            sb.AppendLine($"SIG_VALTYPE_ {s.MessageId} {s.Name} : {s.SigValType};");
        foreach (var s in db.AllSignals.Where(s => !string.IsNullOrEmpty(s.SignalTypeRef)))
            sb.AppendLine($"SIG_TYPE_REF_ {s.MessageId} {s.Name} : {s.SignalTypeRef};");
        if (db.AllSignals.Any(s => s.SigValType != 0 || !string.IsNullOrEmpty(s.SignalTypeRef)))
            sb.AppendLine();
    }

    private static void WriteSignalGroups(StringBuilder sb, DbcDatabase db)
    {
        foreach (var g in db.SignalGroups)
        {
            sb.Append($"SIG_GROUP_ {g.MessageId} {g.Name} {g.Repetitions} :");
            foreach (var n in g.SignalNames)
                sb.Append(' ').Append(n);
            sb.AppendLine(";");
        }
        if (db.SignalGroups.Count > 0) sb.AppendLine();
    }

    private static void WriteSignalTypes(StringBuilder sb, DbcDatabase db)
    {
        foreach (var t in db.SignalTypes)
        {
            var order = t.IsLittleEndian ? 1 : 0;
            var sign = t.IsSigned ? "-" : "+";
            sb.AppendLine(
                $"SGTYPE_ {t.Name} : {t.Length}@{order}{sign} ({Fmt(t.Factor)},{Fmt(t.Offset)}) " +
                $"[{Fmt(t.Min)}|{Fmt(t.Max)}] \"{Esc(t.Unit)}\" {Fmt(t.DefaultValue)}, " +
                $"{(string.IsNullOrEmpty(t.ValueTableName) ? "Vector__XXX" : t.ValueTableName)};");
            if (t.SigValType is { } vt)
                sb.AppendLine($"SIGTYPE_VALTYPE_ {t.Name} : {vt};");
            foreach (var kv in t.Attributes)
                sb.AppendLine($"BA_SGTYPE_ \"{Esc(kv.Key)}\" {t.Name} {FormatBaValue(db, kv.Key, kv.Value)};");
            if (t.ValueDescriptions.Count > 0)
            {
                sb.Append($"SGTYPE_VAL_ {t.Name}");
                foreach (var v in t.ValueDescriptions)
                    sb.Append($" {v.Value} \"{Esc(v.Description)}\"");
                sb.AppendLine(" ;");
            }
        }
    }

    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Fmt(double v) =>
        v.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string NumOr0(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "0" : s;
}
