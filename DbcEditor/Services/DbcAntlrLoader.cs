using System.Globalization;
using System.IO;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using DbcEditor.Antlr;
using DbcEditor.Models;

namespace DbcEditor.Services;

/// <summary>
/// Loads a Vector/CANoe DBC via ANTLR: 35 segment keywords + SG_MUL_VAL_,
/// including NS_/BS_ metadata, BA_REL_, EV_DATA_, signal-type family, and
/// SIG_VALTYPE_ with or without colon.
/// </summary>
public sealed class DbcAntlrLoader
{
    public DbcDatabase Load(string path)
    {
        var text = DbcTextEncoding.ReadAllText(path);
        var input = new AntlrInputStream(text);
        var lexer = new DbcLexer(input);
        var tokens = new CommonTokenStream(lexer);
        var parser = new DbcParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ThrowingErrorListener());
        var tree = parser.dbcFile();
        var visitor = new DbcModelVisitor(Path.GetFileNameWithoutExtension(path));
        visitor.Visit(tree);
        visitor.Database.FilePath = path;
        Finalize(visitor.Database);
        return visitor.Database;
    }

    private static void Finalize(DbcDatabase db)
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

        // Vector: empty BU_: still lists transmitters/receivers as Network nodes / ECUs
        EnsureNodesFromUsage(db);

        foreach (var msg in db.Messages)
        {
            // Vector: no BA_DEF_ GenMsgSendType → Tx Method shows n/a (never invent Cyclic)
            if (string.IsNullOrWhiteSpace(msg.TxMethod))
            {
                var sendDef = AttrDefault(db, "BO_", "GenMsgSendType");
                msg.TxMethod = sendDef != null ? MapSendType(sendDef) : "";
            }
            if (string.IsNullOrWhiteSpace(msg.CycleTime))
                msg.CycleTime = AttrDefault(db, "BO_", "GenMsgCycleTime") ?? "0";

            EnsureAttr(msg.Attributes, db, "BO_", "NmMessage", "No");
            EnsureAttr(msg.Attributes, db, "BO_", "DiagState", "No");
            EnsureAttr(msg.Attributes, db, "BO_", "DiagRequest", "No");
            EnsureAttr(msg.Attributes, db, "BO_", "DiagResponse", "No");
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgILSupport", "Yes");
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgCycleTimeFast", "0");
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgDelayTime", "0");
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgNrOfRepetition", "0");
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgStartDelayTime", "0");
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgSendType", msg.TxMethod);
            EnsureAttr(msg.Attributes, db, "BO_", "GenMsgCycleTime", msg.CycleTime);
            ResolveCategoryFilter(msg.Attributes, msg.Name, out var cat, out var catSrc, out var filt, out var filtSrc);
            msg.Category = cat;
            msg.CategorySource = catSrc;
            msg.FilterTag = filt;
            msg.FilterSource = filtSrc;
        }

        foreach (var sig in db.AllSignals)
        {
            EnsureAttr(sig.Attributes, db, "SG_", "GenSigSendType", "Cyclic");
            EnsureAttr(sig.Attributes, db, "SG_", "GenSigStartValue", "0");
            EnsureAttr(sig.Attributes, db, "SG_", "GenSigInactiveValue", "0");
            EnsureAttr(sig.Attributes, db, "SG_", "GenSigInvalidValue", "0");

            if (sig.Attributes.TryGetValue("GenSigStartValue", out var start) &&
                !string.IsNullOrWhiteSpace(start))
                sig.InitialValue = start;

            ResolveCategoryFilter(sig.Attributes, sig.Name, out var cat, out var catSrc, out var filt, out var filtSrc);
            sig.Category = cat;
            sig.CategorySource = catSrc;
            sig.FilterTag = filt;
            sig.FilterSource = filtSrc;
        }

        foreach (var node in db.Nodes)
        {
            EnsureAttr(node.Attributes, db, "BU_", "NodeLayerModules", "CANoeILNLVector.dll");
            EnsureAttr(node.Attributes, db, "BU_", "NmNode", "no");
        }

        // Vector Value Descriptions: ascending by raw value
        foreach (var sig in db.AllSignals)
            SortValueDescriptions(sig.ValueDescriptions);
        foreach (var ev in db.EnvVars)
            SortValueDescriptions(ev.ValueDescriptions);
        foreach (var table in db.ValueTables)
            SortValueDescriptions(table.Entries);
        foreach (var st in db.SignalTypes)
            SortValueDescriptions(st.ValueDescriptions);
    }

    private static void SortValueDescriptions(List<DbcValueDesc> list)
    {
        if (list.Count <= 1) return;
        list.Sort((a, b) => a.Value.CompareTo(b.Value));
    }

    /// <summary>
    /// When BU_ is empty (e.g. Large.dbc), Vector still shows nodes referenced by
    /// BO_ transmitters, SG_ receivers, EV_ access lists, and BO_TX_BU_.
    /// </summary>
    private static void EnsureNodesFromUsage(DbcDatabase db)
    {
        var known = new HashSet<string>(
            db.Nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!known.Add(name)) return;
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

    /// <summary>
    /// Prefer BA_ attribute values for "CAT_" / "FILTER" / "FILTER_";
    /// otherwise detect engineer naming prefixes CAT_* / FILTER_*.
    /// Attribute names are unrelated to obsolete NS_ segment keywords CAT_ / FILTER.
    /// </summary>
    private static void ResolveCategoryFilter(
        Dictionary<string, string> attrs, string objectName,
        out string category, out string categorySource,
        out string filterTag, out string filterSource)
    {
        category = "";
        categorySource = "";
        filterTag = "";
        filterSource = "";

        if (TryAttr(attrs, "CAT_", out var catAttr) || TryAttr(attrs, "CAT", out catAttr))
        {
            category = catAttr;
            categorySource = "attr";
        }
        else if (TryNamePrefix(objectName, "CAT_", out var catRest))
        {
            category = catRest;
            categorySource = "name";
        }

        if (TryAttr(attrs, "FILTER", out var filtAttr) ||
            TryAttr(attrs, "FILTER_", out filtAttr))
        {
            filterTag = filtAttr;
            filterSource = "attr";
        }
        else if (TryNamePrefix(objectName, "FILTER_", out var filtRest) ||
                 TryNamePrefix(objectName, "FILTER", out filtRest))
        {
            filterTag = filtRest;
            filterSource = "name";
        }
    }

    private static bool TryAttr(Dictionary<string, string> attrs, string name, out string value)
    {
        if (attrs.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            value = v.Trim();
            return true;
        }
        value = "";
        return false;
    }

    private static bool TryNamePrefix(string name, string prefix, out string remainder)
    {
        remainder = "";
        if (string.IsNullOrEmpty(name) || name.Length <= prefix.Length) return false;
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        remainder = name[prefix.Length..];
        return remainder.Length > 0;
    }

    private static string? AttrDefault(DbcDatabase db, string objectType, string name)
    {
        var hit = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ObjectType, objectType, StringComparison.OrdinalIgnoreCase));
        if (hit != null && !string.IsNullOrEmpty(hit.DefaultValue))
            return hit.DefaultValue;
        hit = db.AttrDefs.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(hit?.DefaultValue) ? null : hit!.DefaultValue;
    }

    /// <summary>
    /// Copy BA_DEF_DEF_ onto the object only when defined. Never invent attrs
    /// (e.g. NodeLayerModules / NmNode) that Vector would hide without BA_DEF_.
    /// </summary>
    private static void EnsureAttr(Dictionary<string, string> attrs, DbcDatabase db,
        string objectType, string name, string fallback)
    {
        if (attrs.ContainsKey(name)) return;
        var fromDef = AttrDefault(db, objectType, name);
        if (fromDef != null)
            attrs[name] = fromDef;
        // fallback ignored: must not invent CANoe / Net Management without BA_DEF_
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

    private sealed class ThrowingErrorListener : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            throw new InvalidDataException($"DBC parse error L{line}:{charPositionInLine} {msg}");
        }
    }

    private sealed class DbcModelVisitor : DbcBaseVisitor<object?>
    {
        public DbcDatabase Database { get; } = new();
        private readonly Dictionary<string, DbcEnvVar> _envByName = new(StringComparer.OrdinalIgnoreCase);

        public DbcModelVisitor(string networkName) => Database.NetworkName = networkName;

        public override object? VisitVersion(DbcParser.VersionContext context)
        {
            Database.Version = Unquote(context.STRING_LITERAL()?.GetText());
            return null;
        }

        public override object? VisitNewSymbols(DbcParser.NewSymbolsContext context)
        {
            for (var i = 0; i < context.ChildCount; i++)
            {
                var t = context.GetChild(i).GetText();
                if (t is "NS_" or ":" or null) continue;
                if (string.IsNullOrWhiteSpace(t)) continue;
                Database.NewSymbols.Add(t);
            }
            return null;
        }

        public override object? VisitBitTiming(DbcParser.BitTimingContext context)
        {
            var ints = context.INT();
            if (ints.Length >= 1) Database.BaudRate = ParseUInt(ints[0].GetText());
            if (ints.Length >= 2) Database.Btr1 = int.Parse(ints[1].GetText());
            if (ints.Length >= 3) Database.Btr2 = int.Parse(ints[2].GetText());
            return null;
        }

        public override object? VisitNodes(DbcParser.NodesContext context)
        {
            foreach (var id in context.IDENTIFIER())
                Database.Nodes.Add(new DbcNode { Name = id.GetText() });
            return null;
        }

        public override object? VisitMessage(DbcParser.MessageContext context)
        {
            var ints = context.INT();
            var ids = context.IDENTIFIER();
            var rawId = ParseUInt(ints[0].GetText());
            var msg = new DbcMessage
            {
                Id = rawId,
                Name = ids[0].GetText(),
                Dlc = int.Parse(ints[1].GetText()),
                Transmitter = ids[1].GetText(),
                IdFormat = DbcMessage.InferIdFormat(rawId)
            };
            foreach (var sigCtx in context.signal())
            {
                var sig = BuildSignal(sigCtx, msg);
                msg.Signals.Add(sig);
                Database.AllSignals.Add(sig);
            }
            Database.Messages.Add(msg);
            return null;
        }

        public override object? VisitComment(DbcParser.CommentContext context)
        {
            var remark = Unquote(context.STRING_LITERAL()?.GetText());
            var kind = KindToken(context, 1);

            if (kind == "SG_" && context.INT() != null && context.IDENTIFIER() != null)
            {
                var sig = Database.FindSignal(ParseUInt(context.INT().GetText()), context.IDENTIFIER().GetText());
                if (sig != null) sig.Comment = remark;
                return null;
            }

            if (kind == "BO_" && context.INT() != null)
            {
                var msg = Database.FindMessage(ParseUInt(context.INT().GetText()));
                if (msg != null) msg.Comment = remark;
                return null;
            }

            if (kind == "BU_" && context.IDENTIFIER() != null)
            {
                var node = Database.FindNode(context.IDENTIFIER().GetText());
                if (node != null) node.Comment = remark;
                return null;
            }

            if (kind == "EV_" && context.IDENTIFIER() != null)
            {
                if (_envByName.TryGetValue(context.IDENTIFIER().GetText(), out var ev))
                    ev.Comment = remark;
                return null;
            }

            if (!string.IsNullOrEmpty(remark))
            {
                Database.NetworkAttributes["CM_"] = remark;
                Database.Comment = remark;
            }
            return null;
        }

        public override object? VisitAttributeDef(DbcParser.AttributeDefContext context)
        {
            var name = Unquote(context.STRING_LITERAL()?.GetText());
            if (string.IsNullOrEmpty(name)) return null;

            var def = new DbcAttrDef
            {
                Name = name,
                ObjectType = context.objectType()?.GetText() ?? ""
            };

            var vt = context.attrValueType();
            if (vt != null)
            {
                var t = vt.GetText();
                if (t.StartsWith("ENUM", StringComparison.Ordinal))
                {
                    def.ValueType = "ENUM";
                    foreach (var s in vt.STRING_LITERAL())
                        def.EnumValues.Add(Unquote(s.GetText()));
                }
                else if (t.StartsWith("INT", StringComparison.Ordinal)) def.ValueType = "INT";
                else if (t.StartsWith("HEX", StringComparison.Ordinal)) def.ValueType = "HEX";
                else if (t.StartsWith("FLOAT", StringComparison.Ordinal)) def.ValueType = "FLOAT";
                else if (t.StartsWith("STRING", StringComparison.Ordinal)) def.ValueType = "STRING";

                var nums = vt.number();
                if (nums.Length >= 1) def.Minimum = nums[0].GetText();
                if (nums.Length >= 2) def.Maximum = nums[1].GetText();
            }

            Database.AttrDefs.Add(def);
            return null;
        }

        public override object? VisitAttributeDefault(DbcParser.AttributeDefaultContext context)
        {
            var sl = context.STRING_LITERAL();
            if (sl.Length < 1) return null;
            var name = Unquote(sl[0].GetText());
            var val = sl.Length >= 2
                ? Unquote(sl[1].GetText())
                : (context.number()?.GetText() ?? "");
            foreach (var def in Database.AttrDefs.Where(a =>
                         string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
                def.DefaultValue = val;
            return null;
        }

        public override object? VisitAttributeValue(DbcParser.AttributeValueContext context)
        {
            var sl = context.STRING_LITERAL();
            if (sl.Length < 1) return null;
            var aname = Unquote(sl[0].GetText());
            var num = context.number()?.GetText();
            var strVal = sl.Length >= 2 ? Unquote(sl[^1].GetText()) : null;
            var aval = num ?? strVal ?? "";
            aval = MapEnumIndex(aname, aval);

            var kind = KindToken(context, 2);
            var msgIdTok = context.INT();
            var idTok = context.IDENTIFIER();

            if (kind == "SG_" && msgIdTok != null && idTok != null)
            {
                var sig = Database.FindSignal(ParseUInt(msgIdTok.GetText()), idTok.GetText());
                if (sig != null)
                {
                    sig.Attributes[aname] = aval;
                    if (aname.Equals("GenSigStartValue", StringComparison.OrdinalIgnoreCase))
                        sig.InitialValue = string.IsNullOrEmpty(aval) ? "0" : aval;
                }
                return null;
            }

            if (kind == "BO_" && msgIdTok != null)
            {
                var msg = Database.FindMessage(ParseUInt(msgIdTok.GetText()));
                if (msg != null)
                {
                    msg.Attributes[aname] = aval;
                    if (aname.Equals("GenMsgCycleTime", StringComparison.OrdinalIgnoreCase))
                        msg.CycleTime = string.IsNullOrEmpty(aval) ? "0" : aval;
                    else if (aname.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase))
                        msg.TxMethod = MapSendType(aval);
                    else if (aname.Equals("VFrameFormat", StringComparison.OrdinalIgnoreCase))
                    {
                        msg.IdFormat = MapFrameFormat(aval, msg.Id);
                        // Keep DBC bit-31 flag in sync with extended formats
                        msg.Id = DbcMessage.EncodeId(msg.CanId, msg.IdFormat);
                    }
                }
                return null;
            }

            if (kind == "BU_" && idTok != null)
            {
                var node = Database.FindNode(idTok.GetText());
                if (node != null)
                {
                    node.Attributes[aname] = aval;
                    if (aname.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                        aname.Equals("NmStationAddress", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ulong.TryParse(aval, out var addr))
                            node.Address = $"0x{addr:X}";
                        else if (aval.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            node.Address = aval;
                    }
                }
                return null;
            }

            if (kind == "EV_" && idTok != null)
            {
                if (_envByName.TryGetValue(idTok.GetText(), out var ev))
                    ev.Attributes[aname] = aval;
                return null;
            }

            Database.NetworkAttributes[aname] = aval;
            return null;
        }

        public override object? VisitBaRel(DbcParser.BaRelContext context)
        {
            var sl = context.STRING_LITERAL();
            if (sl.Length < 1) return null;
            var aname = Unquote(sl[0].GetText());
            var num = context.number()?.GetText();
            var strVal = sl.Length >= 2 ? Unquote(sl[^1].GetText()) : null;
            var aval = MapEnumIndex(aname, num ?? strVal ?? "");

            var text = context.GetText();
            var ids = context.IDENTIFIER();
            var msgIdTok = context.INT();

            if (text.Contains("BU_SG_REL_", StringComparison.Ordinal) &&
                ids.Length >= 2 && msgIdTok != null)
            {
                Database.RelAttributes.Add(new DbcRelAttr
                {
                    Name = aname,
                    RelType = "BU_SG_REL_",
                    NodeName = ids[0].GetText(),
                    MessageId = ParseUInt(msgIdTok.GetText()),
                    SignalName = ids[1].GetText(),
                    Value = aval
                });
                return null;
            }

            if (text.Contains("BU_BO_REL_", StringComparison.Ordinal) &&
                ids.Length >= 1 && msgIdTok != null)
            {
                Database.RelAttributes.Add(new DbcRelAttr
                {
                    Name = aname,
                    RelType = "BU_BO_REL_",
                    NodeName = ids[0].GetText(),
                    MessageId = ParseUInt(msgIdTok.GetText()),
                    Value = aval
                });
                return null;
            }

            if (text.Contains("BU_EV_REL_", StringComparison.Ordinal) && ids.Length >= 2)
            {
                Database.RelAttributes.Add(new DbcRelAttr
                {
                    Name = aname,
                    RelType = "BU_EV_REL_",
                    NodeName = ids[0].GetText(),
                    EnvVarName = ids[1].GetText(),
                    Value = aval
                });
            }
            return null;
        }

        public override object? VisitValueTable(DbcParser.ValueTableContext context)
        {
            var name = context.IDENTIFIER()?.GetText();
            if (string.IsNullOrEmpty(name)) return null;
            var table = new DbcValueTable { Name = name };
            var ints = context.INT();
            var strs = context.STRING_LITERAL();
            var n = Math.Min(ints.Length, strs.Length);
            for (var i = 0; i < n; i++)
            {
                table.Entries.Add(new DbcValueDesc
                {
                    Value = ParseLong(ints[i].GetText()),
                    Description = Unquote(strs[i].GetText())
                });
            }
            Database.ValueTables.Add(table);
            return null;
        }

        public override object? VisitValueDescription(DbcParser.ValueDescriptionContext context)
        {
            // VAL_ <msgId> <sigName> (INT STRING)* ;
            // VAL_ EV_? <envName> (INT STRING)* ;
            var kind = KindToken(context, 1);
            var ints = context.INT();
            var id = context.IDENTIFIER()?.GetText() ?? "";
            var strs = context.STRING_LITERAL();

            if (kind != "EV_" && ints.Length >= 1 && !string.IsNullOrEmpty(id))
            {
                // Signal form: first INT is message id
                var msgId = ParseUInt(ints[0].GetText());
                var sig = Database.FindSignal(msgId, id);
                if (sig != null)
                {
                    var valueInts = ints.Skip(1).ToArray();
                    var n = Math.Min(valueInts.Length, strs.Length);
                    for (var i = 0; i < n; i++)
                    {
                        sig.ValueDescriptions.Add(new DbcValueDesc
                        {
                            Value = ParseLong(valueInts[i].GetText()),
                            Description = Unquote(strs[i].GetText())
                        });
                    }
                    return null;
                }
            }

            if (_envByName.TryGetValue(id, out var ev))
            {
                var n = Math.Min(ints.Length, strs.Length);
                for (var i = 0; i < n; i++)
                {
                    ev.ValueDescriptions.Add(new DbcValueDesc
                    {
                        Value = ParseLong(ints[i].GetText()),
                        Description = Unquote(strs[i].GetText())
                    });
                }
            }
            return null;
        }

        public override object? VisitSignalValueType(DbcParser.SignalValueTypeContext context)
        {
            var ints = context.INT();
            var id = context.IDENTIFIER();
            if (ints.Length < 2 || id == null) return null;
            var sig = Database.FindSignal(ParseUInt(ints[0].GetText()), id.GetText());
            if (sig != null)
                sig.SigValType = int.Parse(ints[1].GetText());
            return null;
        }

        public override object? VisitSignalGroup(DbcParser.SignalGroupContext context)
        {
            var ints = context.INT();
            var ids = context.IDENTIFIER();
            if (ints.Length < 2 || ids.Length < 1) return null;
            var g = new DbcSignalGroup
            {
                MessageId = ParseUInt(ints[0].GetText()),
                Name = ids[0].GetText(),
                Repetitions = int.Parse(ints[1].GetText())
            };
            for (var i = 1; i < ids.Length; i++)
                g.SignalNames.Add(ids[i].GetText());
            Database.SignalGroups.Add(g);
            return null;
        }

        public override object? VisitEnvVar(DbcParser.EnvVarContext context)
        {
            var ids = context.IDENTIFIER();
            var nums = context.number();
            var ints = context.INT();
            if (ids.Length < 1) return null;
            var ev = new DbcEnvVar
            {
                Name = ids[0].GetText(),
                Type = ints.Length > 0 ? int.Parse(ints[0].GetText()) : 0,
                Min = nums.Length > 0 ? ParseDouble(nums[0].GetText()) : 0,
                Max = nums.Length > 1 ? ParseDouble(nums[1].GetText()) : 0,
                Unit = Unquote(context.STRING_LITERAL()?.GetText()),
                InitialValue = nums.Length > 2 ? ParseDouble(nums[2].GetText()) : 0,
                EvId = ints.Length > 1 ? ParseUInt(ints[1].GetText()) : 0,
                AccessType = ids.Length > 1 ? ids[1].GetText() : ""
            };
            for (var i = 2; i < ids.Length; i++)
                ev.AccessNodes.Add(ids[i].GetText());
            Database.EnvVars.Add(ev);
            _envByName[ev.Name] = ev;
            return null;
        }

        public override object? VisitEnvVarData(DbcParser.EnvVarDataContext context)
        {
            var name = context.IDENTIFIER()?.GetText();
            var len = context.INT()?.GetText();
            if (name != null && _envByName.TryGetValue(name, out var ev) && len != null)
                ev.DataLength = int.Parse(len);
            return null;
        }

        public override object? VisitBoTxBu(DbcParser.BoTxBuContext context)
        {
            var msgId = ParseUInt(context.INT().GetText());
            var list = Database.MessageTransmitters.TryGetValue(msgId, out var existing)
                ? existing
                : Database.MessageTransmitters[msgId] = new List<string>();
            foreach (var id in context.IDENTIFIER())
            {
                var n = id.GetText();
                if (!list.Contains(n, StringComparer.OrdinalIgnoreCase))
                    list.Add(n);
            }
            return null;
        }

        public override object? VisitSgMulVal(DbcParser.SgMulValContext context)
        {
            // Extended multiplexing ranges — store as Multiplexing hint if still default
            var ints = context.INT();
            var ids = context.IDENTIFIER();
            if (ints.Length < 1 || ids.Length < 2) return null;
            var sig = Database.FindSignal(ParseUInt(ints[0].GetText()), ids[0].GetText());
            if (sig == null) return null;
            var muxor = ids[1].GetText();
            var ranges = new List<string>();
            // INT pairs after message id: from-to repeated (grammar: INT '-' INT (, INT '-' INT)*)
            // Token layout: msgId, then for each range two INTs
            for (var i = 1; i + 1 < ints.Length; i += 2)
                ranges.Add($"{ints[i].GetText()}-{ints[i + 1].GetText()}");
            if (ranges.Count > 0)
            {
                var rangeText = $"Mux {muxor} [{string.Join(",", ranges)}]";
                // Preserve m<n>M multiplexor role when SG_MUL_VAL_ adds ranges
                sig.Multiplexing = sig.Multiplexing.Contains("Multiplexor", StringComparison.Ordinal)
                    ? $"{rangeText} / Multiplexor"
                    : rangeText;
            }
            return null;
        }

        public override object? VisitSgType(DbcParser.SgTypeContext context)
        {
            var ids = context.IDENTIFIER();
            var ints = context.INT();
            var nums = context.number();
            if (ids.Length < 2 || ints.Length < 2 || nums.Length < 5) return null;
            Database.SignalTypes.Add(new DbcSignalType
            {
                Name = ids[0].GetText(),
                Length = int.Parse(ints[0].GetText()),
                IsLittleEndian = ints[1].GetText() == "1",
                IsSigned = context.sign()?.GetText() == "-",
                Factor = ParseDouble(nums[0].GetText()),
                Offset = ParseDouble(nums[1].GetText()),
                Min = ParseDouble(nums[2].GetText()),
                Max = ParseDouble(nums[3].GetText()),
                Unit = Unquote(context.STRING_LITERAL()?.GetText()),
                DefaultValue = ParseDouble(nums[4].GetText()),
                ValueTableName = ids[1].GetText()
            });
            return null;
        }

        public override object? VisitSgTypeVal(DbcParser.SgTypeValContext context)
        {
            var name = context.IDENTIFIER()?.GetText();
            if (string.IsNullOrEmpty(name)) return null;
            var type = Database.FindSignalType(name) ?? new DbcSignalType { Name = name };
            if (!Database.SignalTypes.Contains(type))
                Database.SignalTypes.Add(type);
            var ints = context.INT();
            var strs = context.STRING_LITERAL();
            var n = Math.Min(ints.Length, strs.Length);
            for (var i = 0; i < n; i++)
            {
                type.ValueDescriptions.Add(new DbcValueDesc
                {
                    Value = ParseLong(ints[i].GetText()),
                    Description = Unquote(strs[i].GetText())
                });
            }
            return null;
        }

        public override object? VisitSigTypeRef(DbcParser.SigTypeRefContext context)
        {
            var msgIdTok = context.INT();
            var ids = context.IDENTIFIER();
            if (msgIdTok == null || ids.Length < 2) return null;
            var sig = Database.FindSignal(ParseUInt(msgIdTok.GetText()), ids[0].GetText());
            if (sig != null)
                sig.SignalTypeRef = ids[1].GetText();
            return null;
        }

        public override object? VisitBaDefSgType(DbcParser.BaDefSgTypeContext context)
        {
            var name = Unquote(context.STRING_LITERAL()?.GetText());
            if (string.IsNullOrEmpty(name)) return null;
            var def = new DbcAttrDef { Name = name, ObjectType = "SGTYPE_" };
            var vt = context.attrValueType();
            if (vt != null)
            {
                var t = vt.GetText();
                if (t.StartsWith("ENUM", StringComparison.Ordinal))
                {
                    def.ValueType = "ENUM";
                    foreach (var s in vt.STRING_LITERAL())
                        def.EnumValues.Add(Unquote(s.GetText()));
                }
                else if (t.StartsWith("INT", StringComparison.Ordinal)) def.ValueType = "INT";
                else if (t.StartsWith("HEX", StringComparison.Ordinal)) def.ValueType = "HEX";
                else if (t.StartsWith("FLOAT", StringComparison.Ordinal)) def.ValueType = "FLOAT";
                else if (t.StartsWith("STRING", StringComparison.Ordinal)) def.ValueType = "STRING";
                var nums = vt.number();
                if (nums.Length >= 1) def.Minimum = nums[0].GetText();
                if (nums.Length >= 2) def.Maximum = nums[1].GetText();
            }
            Database.AttrDefs.Add(def);
            return null;
        }

        public override object? VisitBaSgType(DbcParser.BaSgTypeContext context)
        {
            var sl = context.STRING_LITERAL();
            if (sl.Length < 1) return null;
            var aname = Unquote(sl[0].GetText());
            var typeName = context.IDENTIFIER()?.GetText();
            if (string.IsNullOrEmpty(typeName)) return null;
            var aval = sl.Length >= 2
                ? Unquote(sl[^1].GetText())
                : (context.number()?.GetText() ?? "");
            aval = MapEnumIndex(aname, aval);
            var type = Database.FindSignalType(typeName) ?? new DbcSignalType { Name = typeName };
            if (!Database.SignalTypes.Contains(type))
                Database.SignalTypes.Add(type);
            type.Attributes[aname] = aval;
            return null;
        }

        public override object? VisitSigTypeValType(DbcParser.SigTypeValTypeContext context)
        {
            var name = context.IDENTIFIER()?.GetText();
            var typeTok = context.INT();
            if (string.IsNullOrEmpty(name) || typeTok == null) return null;
            var type = Database.FindSignalType(name) ?? new DbcSignalType { Name = name };
            if (!Database.SignalTypes.Contains(type))
                Database.SignalTypes.Add(type);
            type.SigValType = int.Parse(typeTok.GetText());
            return null;
        }

        private string MapEnumIndex(string attrName, string raw)
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                return raw;
            var def = Database.AttrDefs.FirstOrDefault(a =>
                string.Equals(a.Name, attrName, StringComparison.OrdinalIgnoreCase) &&
                a.ValueType == "ENUM" && a.EnumValues.Count > 0);
            if (def != null && idx >= 0 && idx < def.EnumValues.Count)
                return def.EnumValues[idx];
            return raw;
        }

        private static DbcSignal BuildSignal(DbcParser.SignalContext ctx, DbcMessage msg)
        {
            var ints = ctx.INT();
            var nums = ctx.number();
            var sig = new DbcSignal
            {
                Name = ctx.IDENTIFIER().GetText(),
                StartBit = int.Parse(ints[0].GetText()),
                Length = int.Parse(ints[1].GetText()),
                IsLittleEndian = ints[2].GetText() == "1",
                IsSigned = ctx.sign()?.GetText() == "-",
                Factor = ParseDouble(nums[0].GetText()),
                Offset = ParseDouble(nums[1].GetText()),
                Min = ParseDouble(nums[2].GetText()),
                Max = ParseDouble(nums[3].GetText()),
                Unit = Unquote(ctx.STRING_LITERAL()?.GetText()),
                MessageName = msg.Name,
                MessageId = msg.Id,
                Multiplexing = FormatMux(ctx.multiplexer()?.GetText())
            };
            var recv = ctx.receiverList();
            if (recv != null)
            {
                foreach (var r in recv.IDENTIFIER())
                    sig.Receivers.Add(r.GetText());
            }
            return sig;
        }

        private static string FormatMux(string? mux)
        {
            if (string.IsNullOrWhiteSpace(mux)) return "-";
            mux = mux.Trim();
            if (mux.Equals("M", StringComparison.Ordinal)) return "Multiplexor";

            // Extended multiplex: m<n>M — multiplexed by n and also a multiplexor
            var mNm = System.Text.RegularExpressions.Regex.Match(mux, @"^m(\d+)M$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mNm.Success)
                return $"Multiplexed signal {mNm.Groups[1].Value} / Multiplexor";

            // Basic multiplexed: m<n>
            var mn = System.Text.RegularExpressions.Regex.Match(mux, @"^m(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mn.Success)
                return $"Multiplexed signal {mn.Groups[1].Value}";

            if (mux.StartsWith('m') || mux.StartsWith('M'))
                return $"Multiplexed signal {mux.TrimStart('m', 'M')}";
            return mux;
        }

        private static string MapFrameFormat(string v, uint id) => v switch
        {
            "0" or "StandardCAN" => "CAN Standard",
            "1" or "ExtendedCAN" => "CAN Extended",
            "14" or "StandardCAN_FD" => "CAN FD Standard",
            "15" or "ExtendedCAN_FD" => "CAN FD Extended",
            "J1939PG" => "CAN Extended",
            _ => DbcMessage.InferIdFormat(id)
        };

        private static string KindToken(ParserRuleContext context, int childIndex)
        {
            if (context.ChildCount <= childIndex) return "";
            return context.GetChild(childIndex).GetText();
        }

        private static string Unquote(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                return s.Substring(1, s.Length - 2).Replace("\\\"", "\"").Replace("\"\"", "\"");
            return s;
        }

        private static uint ParseUInt(string s) =>
            s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(s, 16)
                : uint.Parse(s);

        private static long ParseLong(string s) =>
            s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(s, 16)
                : long.Parse(s);

        private static double ParseDouble(string s) =>
            double.Parse(s, CultureInfo.InvariantCulture);
    }
}
