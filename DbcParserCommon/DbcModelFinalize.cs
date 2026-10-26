using DbcEditor.Models;

namespace DbcEditor.Services.Parsing;

/// <summary>Post-parse Vector-style defaults / inferred nodes (shared by all front-ends).</summary>
public static class DbcModelFinalize
{
    public static void Apply(DbcDatabase db)
    {
        if (db.NetworkAttributes.TryGetValue("CM_", out var cm) && !string.IsNullOrEmpty(cm))
            db.Comment = cm;

        if (db.NetworkAttributes.TryGetValue("BusType", out var bus) && !string.IsNullOrWhiteSpace(bus))
            db.Protocol = bus;
        else if (AttrDefault(db, "", "BusType") is { Length: > 0 } busDef)
            db.Protocol = busDef;
        else if (db.Messages.Any(m => m.IdFormat.Contains("FD", StringComparison.OrdinalIgnoreCase)))
            db.Protocol = "CAN FD";

        if (db.NetworkAttributes.TryGetValue("DBName", out var dbName) && !string.IsNullOrWhiteSpace(dbName))
            db.NetworkName = dbName;

        EnsureNodesFromUsage(db);

        foreach (var msg in db.Messages)
        {
            if (string.IsNullOrWhiteSpace(msg.TxMethod))
            {
                var sendDef = AttrDefault(db, "BO_", "GenMsgSendType");
                msg.TxMethod = sendDef != null ? MapSendType(sendDef) : "";
            }
            if (string.IsNullOrWhiteSpace(msg.CycleTime))
                msg.CycleTime = AttrDefault(db, "BO_", "GenMsgCycleTime") ?? "0";

            if (msg.Attributes.TryGetValue("GenMsgSendType", out var st))
                msg.TxMethod = MapSendType(st);
            if (msg.Attributes.TryGetValue("GenMsgCycleTime", out var ct) && !string.IsNullOrWhiteSpace(ct))
                msg.CycleTime = ct;

            ResolveCategoryFilter(msg.Attributes, msg.Name, out var cat, out var catSrc, out var filt, out var filtSrc);
            msg.Category = cat;
            msg.CategorySource = catSrc;
            msg.FilterTag = filt;
            msg.FilterSource = filtSrc;
        }

        foreach (var sig in db.AllSignals)
        {
            if (sig.Attributes.TryGetValue("GenSigStartValue", out var start) &&
                !string.IsNullOrWhiteSpace(start))
                sig.InitialValue = start;

            ResolveCategoryFilter(sig.Attributes, sig.Name, out var cat, out var catSrc, out var filt, out var filtSrc);
            sig.Category = cat;
            sig.CategorySource = catSrc;
            sig.FilterTag = filt;
            sig.FilterSource = filtSrc;
        }

        foreach (var sig in db.AllSignals)
            SortValueDescriptions(sig.ValueDescriptions);
        foreach (var ev in db.EnvVars)
            SortValueDescriptions(ev.ValueDescriptions);
        foreach (var table in db.ValueTables)
            SortValueDescriptions(table.Entries);
        foreach (var st in db.SignalTypes)
            SortValueDescriptions(st.ValueDescriptions);
    }

    private static void EnsureNodesFromUsage(DbcDatabase db)
    {
        var known = new HashSet<string>(db.Nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || !known.Add(name)) return;
            db.Nodes.Add(new DbcNode { Name = name });
        }

        foreach (var m in db.Messages)
        {
            Add(m.Transmitter);
            if (db.MessageTransmitters.TryGetValue(m.Id, out var txs))
                foreach (var t in txs) Add(t);
        }
        foreach (var s in db.AllSignals)
            foreach (var r in s.Receivers)
                Add(r);
        foreach (var ev in db.EnvVars)
            foreach (var n in ev.AccessNodes)
                Add(n);
    }

    private static string? AttrDefault(DbcDatabase db, string objectType, string name)
    {
        var hit = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.ObjectType, objectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (hit != null && !string.IsNullOrEmpty(hit.DefaultValue))
            return hit.DefaultValue;
        hit = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(hit?.DefaultValue) ? null : hit!.DefaultValue;
    }

    private static string MapSendType(string v) => v switch
    {
        "0" => "Cyclic",
        "1" => "Event",
        "2" => "IfActive",
        "3" => "CE",
        "4" => "CA",
        "5" => "NoMsgSendType",
        _ => string.IsNullOrWhiteSpace(v) ? "" : v
    };

    private static void SortValueDescriptions(List<DbcValueDesc> list)
    {
        if (list.Count <= 1) return;
        list.Sort((a, b) => a.Value.CompareTo(b.Value));
    }

    private static void ResolveCategoryFilter(
        Dictionary<string, string> attrs, string objectName,
        out string category, out string categorySource,
        out string filterTag, out string filterSource)
    {
        category = ""; categorySource = "";
        filterTag = ""; filterSource = "";

        if (attrs.TryGetValue("CAT_", out var catAttr) && !string.IsNullOrWhiteSpace(catAttr))
        {
            category = catAttr;
            categorySource = "attr";
        }
        else if (objectName.StartsWith("CAT_", StringComparison.OrdinalIgnoreCase) && objectName.Length > 4)
        {
            category = objectName[4..];
            categorySource = "name";
        }

        if (attrs.TryGetValue("FILTER", out var f1) && !string.IsNullOrWhiteSpace(f1))
        {
            filterTag = f1;
            filterSource = "attr";
        }
        else if (attrs.TryGetValue("FILTER_", out var f2) && !string.IsNullOrWhiteSpace(f2))
        {
            filterTag = f2;
            filterSource = "attr";
        }
        else if (objectName.StartsWith("FILTER_", StringComparison.OrdinalIgnoreCase) && objectName.Length > 7)
        {
            filterTag = objectName[7..];
            filterSource = "name";
        }
    }
}
