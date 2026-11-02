using System.Globalization;
using EtAlii.Lark;

namespace DbcEditor.Services.Parsing;

/// <summary>Phase 2: syntax tree to an ordered list of statement dictionaries. Repeated DBC statements must not collapse into one map.</summary>
sealed class DbcTreeTransformer : Transformer
{
    public object start(List<object?> c) => c[0]!;
    public object dbc(List<object?> c) => c;

    public object version(List<object?> c) => Stmt("version", ("text", Unquote(Text(c[1]))));

    public object ns(List<object?> c) =>
        Stmt("ns", ("items", c.Skip(1).Select(Text).Where(s => s.Length > 0).ToList()));

    public object ns_item(List<object?> c) => Text(c[0]);

    public object bs(List<object?> c)
    {
        var nums = c.Skip(1).Where(x => x is Token t && t.Type == "NUMBER").Select(U).ToList();
        return Stmt("bs",
            ("baud", nums.Count > 0 ? nums[0] : null),
            ("btr1", nums.Count > 1 ? (int)nums[1] : (int?)null),
            ("btr2", nums.Count > 2 ? (int)nums[2] : (int?)null));
    }

    public object bu(List<object?> c) =>
        Stmt("bu", ("nodes", c.Skip(1).Select(Text).Where(s => s.Length > 0).ToList()));

    public object message(List<object?> c) => Stmt("message",
        ("id", U(c[1])),
        ("name", Text(c[2])),
        ("dlc", (int)U(c[3])),
        ("tx", Text(c[4])),
        ("sigs", c.Skip(5).ToList()));

    public object signal(List<object?> c)
    {
        var i = 1;
        var name = Text(c[i++]);
        var mux = "";
        if (i < c.Count && c[i] is MuxMark mark)
        {
            mux = mark.Text;
            i++;
        }
        var start = (int)U(c[i++]);
        var len = (int)U(c[i++]);
        var order = (int)U(c[i++]);
        var signed = false;
        if (i < c.Count && c[i] is Token sign && (sign.Value == "+" || sign.Value == "-"))
        {
            signed = sign.Value == "-";
            i++;
        }
        var factor = ((Num)c[i++]!).Value;
        var offset = ((Num)c[i++]!).Value;
        var min = ((Num)c[i++]!).Value;
        var max = ((Num)c[i++]!).Value;
        var unit = Unquote(Text(c[i++]));
        var recvs = i < c.Count && c[i] is List<string> list ? list : new List<string>();
        return Stmt("signal",
            ("name", name), ("mux", mux), ("start", start), ("len", len), ("order", order),
            ("signed", signed), ("factor", factor), ("offset", offset), ("min", min), ("max", max),
            ("unit", unit), ("recvs", recvs));
    }

    public object mux(List<object?> c) => new MuxMark(Text(c[0]));
    public object receivers(List<object?> c) => c.Select(Text).Where(s => s.Length > 0).ToList();

    public object signed(List<object?> c)
    {
        if (c.Count == 1)
        {
            var alone = Text(c[0]);
            return new Num(alone, ParseNum(alone));
        }
        var sign = Text(c[0]);
        var raw = Text(c[1]);
        var value = ParseNum(raw);
        return sign == "-" ? new Num("-" + raw, -value) : new Num(raw, value);
    }

    public object cm(List<object?> c)
    {
        var body = Dict(c[1]);
        body["kind"] = "cm";
        return body;
    }

    public object cm_body(List<object?> c)
    {
        if (c.Count == 1)
            return Stmt("cm", ("where", "net"), ("text", Unquote(Text(c[0]))));
        var where = Text(c[0]);
        if (where == "BU_") return new Dictionary<string, object?> { ["where"] = "bu", ["name"] = Text(c[1]), ["text"] = Unquote(Text(c[2])) };
        if (where == "BO_") return new Dictionary<string, object?> { ["where"] = "bo", ["id"] = U(c[1]), ["text"] = Unquote(Text(c[2])) };
        if (where == "SG_") return new Dictionary<string, object?> { ["where"] = "sg", ["id"] = U(c[1]), ["name"] = Text(c[2]), ["text"] = Unquote(Text(c[3])) };
        if (where == "EV_") return new Dictionary<string, object?> { ["where"] = "ev", ["name"] = Text(c[1]), ["text"] = Unquote(Text(c[2])) };
        return new Dictionary<string, object?> { ["where"] = "net", ["text"] = "" };
    }

    public object ba_def_def(List<object?> c) => Stmt("ba_def_def",
        ("name", Unquote(Text(c[1]))),
        ("value", c[2] as string ?? Text(c[2])));

    public object ba_def(List<object?> c)
    {
        var kw = Text(c[0]);
        var i = 1;
        var obj = "";
        if (kw is "BA_DEF_" or "BA_DEF_REL_" && c[i] is string os)
        {
            obj = os;
            i++;
        }
        if (kw == "BA_DEF_SGTYPE_") obj = "SGTYPE_";
        var name = Unquote(Text(c[i++]));
        var typ = Dict(c[i]);
        return Stmt("ba_def",
            ("name", name), ("obj", obj),
            ("vtype", typ["type"]), ("min", typ.GetValueOrDefault("min")), ("max", typ.GetValueOrDefault("max")),
            ("enum", typ.GetValueOrDefault("enum")));
    }

    public object obj(List<object?> c) => Text(c[0]);

    public object ba_type(List<object?> c)
    {
        var kind = Text(c[0]);
        var d = new Dictionary<string, object?> { ["type"] = kind };
        if (kind == "ENUM")
            d["enum"] = c.Skip(1).Select(x => Unquote(Text(x))).ToList();
        else
        {
            var nums = c.Skip(1).OfType<Num>().ToList();
            if (nums.Count > 0) d["min"] = nums[0].Raw;
            if (nums.Count > 1) d["max"] = nums[1].Raw;
        }
        return d;
    }

    public object ba(List<object?> c)
    {
        var body = Dict(c[2]);
        body["kind"] = "ba";
        body["attr"] = Unquote(Text(c[1]));
        return body;
    }

    public object ba_body(List<object?> c)
    {
        if (c[0] is string only)
            return new Dictionary<string, object?> { ["where"] = "net", ["value"] = only };
        var where = Text(c[0]);
        if (where == "BU_") return new Dictionary<string, object?> { ["where"] = "bu", ["name"] = Text(c[1]), ["value"] = c[2] };
        if (where == "BO_") return new Dictionary<string, object?> { ["where"] = "bo", ["id"] = U(c[1]), ["value"] = c[2] };
        if (where == "SG_") return new Dictionary<string, object?> { ["where"] = "sg", ["id"] = U(c[1]), ["name"] = Text(c[2]), ["value"] = c[3] };
        if (where == "EV_") return new Dictionary<string, object?> { ["where"] = "ev", ["name"] = Text(c[1]), ["value"] = c[2] };
        return new Dictionary<string, object?> { ["where"] = "net", ["value"] = "" };
    }

    public object attr_value(List<object?> c) => c[0] is Num n ? n.Raw : Unquote(Text(c[0]));

    public object val_table(List<object?> c) => Stmt("val_table",
        ("name", Text(c[1])),
        ("pairs", Pairs(c.Skip(2))));

    public object val(List<object?> c)
    {
        var body = Dict(c[1]);
        body["kind"] = "val";
        return body;
    }

    public object val_body(List<object?> c)
    {
        string where;
        uint id = 0;
        string name;
        int i;
        if (Text(c[0]) == "EV_")
        {
            where = "ev";
            name = Text(c[1]);
            i = 2;
        }
        else if (c[0] is Token t && t.Type == "NUMBER")
        {
            where = "msg";
            id = U(c[0]);
            name = Text(c[1]);
            i = 2;
        }
        else
        {
            where = "env";
            name = Text(c[0]);
            i = 1;
        }
        return new Dictionary<string, object?>
        {
            ["where"] = where, ["id"] = id, ["name"] = name, ["pairs"] = Pairs(c.Skip(i))
        };
    }

    public object val_pair(List<object?> c) => (Long(c[0]), Unquote(Text(c[1])));

    public object sig_group(List<object?> c) => Stmt("sig_group",
        ("id", U(c[1])), ("name", Text(c[2])), ("reps", (int)U(c[3])),
        ("signals", c.Skip(4).Select(Text).Where(s => s.Length > 0).ToList()));

    public object sig_valtype(List<object?> c) => Stmt("sig_valtype",
        ("id", U(c[1])), ("name", Text(c[2])), ("vt", (int)U(c[^1])));

    public object ev(List<object?> c) => Stmt("ev",
        ("name", Text(c[1])),
        ("type", (int)U(c[2])),
        ("min", ((Num)c[3]!).Value),
        ("max", ((Num)c[4]!).Value),
        ("unit", Unquote(Text(c[5]))),
        ("init", ((Num)c[6]!).Value),
        ("evid", U(c[7])),
        ("access", Text(c[8])),
        ("nodes", c.Skip(9).Select(Text).Where(s => s.Length > 0).ToList()));

    public object ev_data(List<object?> c) => Stmt("ev_data",
        ("name", Text(c[1])), ("len", (int)U(c[2])));

    public object bo_tx(List<object?> c) => Stmt("bo_tx",
        ("id", U(c[1])),
        ("nodes", c.Skip(2).Select(Text).Where(s => s.Length > 0).ToList()));

    public object sg_mul(List<object?> c) => Stmt("sg_mul",
        ("id", U(c[1])), ("sig", Text(c[2])), ("mux", Text(c[3])),
        ("ranges", c.Skip(4).OfType<string>().ToList()));

    public object mul_range(List<object?> c) =>
        c.Count == 1 ? $"{Text(c[0])}-{Text(c[0])}" : $"{Text(c[0])}-{Text(c[1])}";

    public object other(List<object?> c) => Stmt("other");

    static Dictionary<string, object?> Stmt(string kind, params (string Key, object? Value)[] fields)
    {
        var d = new Dictionary<string, object?> { ["kind"] = kind };
        foreach (var (key, value) in fields) d[key] = value;
        return d;
    }

    static Dictionary<string, object?> Dict(object? o) => (Dictionary<string, object?>)o!;

    static List<(long, string)> Pairs(IEnumerable<object?> items) =>
        items.OfType<ValueTuple<long, string>>().ToList();

    internal static string Text(object? o) => o switch
    {
        Token t => t.Value,
        string s => s,
        _ => ""
    };

    internal static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1];
        return text.Replace("\"\"", "\"", StringComparison.Ordinal)
                   .Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    internal static uint U(object? o)
    {
        var s = Text(o);
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s, 16);
        if (s.Contains('.') || s.Contains('e') || s.Contains('E'))
            return (uint)double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        return uint.Parse(s, CultureInfo.InvariantCulture);
    }

    static long Long(object? o)
    {
        var s = Text(o);
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(s, 16);
        return long.Parse(s, CultureInfo.InvariantCulture);
    }

    static double ParseNum(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s, 16);
        return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    readonly record struct Num(string Raw, double Value);
    sealed record MuxMark(string Text);
}
