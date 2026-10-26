using DbcEditor.Services;

if (args.Length < 1)
{
    Console.WriteLine("Usage: DbcDump <file.dbc> [signalName]");
    return 1;
}

var db = new DbcAntlrLoader().Load(args[0]);
Console.WriteLine($"OK  msgs={db.Messages.Count} sigs={db.AllSignals.Count} nodes={db.Nodes.Count}");
Console.WriteLine($"    attrDefs={db.AttrDefs.Count} valTables={db.ValueTables.Count} env={db.EnvVars.Count}");
Console.WriteLine($"    NS_={db.NewSymbols.Count} baud={db.BaudRate} btr={db.Btr1},{db.Btr2}");
Console.WriteLine($"    relAttrs={db.RelAttributes.Count} sgTypes={db.SignalTypes.Count} sigGroups={db.SignalGroups.Count}");
Console.WriteLine($"    netComment len={db.Comment.Length}");
foreach (var r in db.RelAttributes)
    Console.WriteLine($"    REL {r.RelType} {r.Name} node={r.NodeName} msg={r.MessageId} sig={r.SignalName} env={r.EnvVarName} val={r.Value}");
foreach (var t in db.SignalTypes)
    Console.WriteLine($"    SGTYPE {t.Name} len={t.Length} vt={t.ValueTableName} valType={t.SigValType} attrs={t.Attributes.Count} vals={t.ValueDescriptions.Count}");
foreach (var s in db.AllSignals.Where(x => x.SigValType != 0 || x.SignalTypeRef != null))
    Console.WriteLine($"    SIGEXT {s.Name} SigValType={s.SigValType} TypeRef={s.SignalTypeRef}");
foreach (var e in db.EnvVars)
    Console.WriteLine($"    EV {e.Name} dataLen={e.DataLength}");

var withCmt = db.AllSignals.Count(s => !string.IsNullOrEmpty(s.Comment));
var withVal = db.AllSignals.Count(s => s.ValueDescriptions.Count > 0);
var withAttr = db.AllSignals.Count(s => s.Attributes.Count > 0);
Console.WriteLine($"    signals with CM_={withCmt} VAL_={withVal} BA_attrs={withAttr}");

var name = args.Length > 1 ? args[1] : "ABS_VehicleSpeed";
var sig = db.AllSignals.FirstOrDefault(s =>
    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
if (sig == null)
{
    Console.WriteLine($"Signal '{name}' not found");
    return 2;
}

Console.WriteLine($"\nSignal {sig.Name} @ msg {sig.MessageId} ({sig.MessageName})");
Console.WriteLine($"  Comment: {sig.Comment}");
Console.WriteLine($"  Attrs ({sig.Attributes.Count}):");
foreach (var kv in sig.Attributes.OrderBy(k => k.Key))
    Console.WriteLine($"    {kv.Key} = {kv.Value}");
Console.WriteLine($"  VAL_ ({sig.ValueDescriptions.Count}):");
foreach (var v in sig.ValueDescriptions)
    Console.WriteLine($"    {v.Value} = {v.Description}");
return 0;
