using System.Globalization;
using DbcEditor.Models;

namespace DbcEditor.Services.Parsing;

/// <summary>
/// Shared DBC model builder from a token stream (Pidgin / Sprache / Superpower front-ends).
/// </summary>
public static class DbcTokenModelParser
{
    public static DbcDatabase Parse(IReadOnlyList<DbcToken> tokens, string displayName)
    {
        var p = new Parser(tokens, displayName);
        p.ParseFile();
        DbcModelFinalize.Apply(p.Db);
        return p.Db;
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<DbcToken> _toks;
        private int _i;
        public DbcDatabase Db { get; }

        public Parser(IReadOnlyList<DbcToken> toks, string displayName)
        {
            _toks = toks;
            Db = new DbcDatabase { NetworkName = displayName };
        }

        private DbcToken Cur => _i < _toks.Count ? _toks[_i] : new DbcToken(DbcTokKind.Eof, "", 0, 0);
        private bool AtEof => Cur.Kind == DbcTokKind.Eof;

        private DbcToken Take()
        {
            var t = Cur;
            if (_i < _toks.Count) _i++;
            return t;
        }

        private bool LookIdent(string s) =>
            Cur.Kind == DbcTokKind.Ident &&
            string.Equals(Cur.Text, s, StringComparison.Ordinal);

        private void Expect(DbcTokKind k)
        {
            if (Cur.Kind != k)
                throw Fail($"expected {k}, got {Cur}");
            Take();
        }

        private string ExpectIdent()
        {
            if (Cur.Kind != DbcTokKind.Ident)
                throw Fail($"expected identifier, got {Cur}");
            return Take().Text;
        }

        private void OptionalSemi()
        {
            if (Cur.Kind == DbcTokKind.Semi) Take();
        }

        private Exception Fail(string msg) =>
            new DbcParseException($"DBC parse error at line {Cur.Line}, col {Cur.Column}: {msg}");

        public void ParseFile()
        {
            while (!AtEof)
                ParseStatement();
        }

        private void ParseStatement()
        {
            if (LookIdent("VERSION")) { ParseVersion(); return; }
            if (LookIdent("NS_")) { ParseNs(); return; }
            if (LookIdent("BS_")) { ParseBs(); return; }
            if (LookIdent("BU_")) { ParseBu(); return; }
            if (LookIdent("BO_")) { ParseBo(); return; }
            if (LookIdent("CM_")) { ParseCm(); return; }
            if (LookIdent("BA_DEF_DEF_") || LookIdent("BA_DEF_DEF_REL_")) { ParseBaDefDef(); return; }
            if (LookIdent("BA_DEF_") || LookIdent("BA_DEF_REL_") || LookIdent("BA_DEF_SGTYPE_")) { ParseBaDef(); return; }
            if (LookIdent("BA_REL_")) { ParseBaRel(); return; }
            if (LookIdent("BA_SGTYPE_")) { ParseBaSgType(); return; }
            if (LookIdent("BA_")) { ParseBa(); return; }
            if (LookIdent("VAL_TABLE_")) { ParseValTable(); return; }
            if (LookIdent("VAL_")) { ParseVal(); return; }
            if (LookIdent("SIG_VALTYPE_")) { ParseSigValType(); return; }
            if (LookIdent("SIG_GROUP_")) { ParseSigGroup(); return; }
            if (LookIdent("EV_")) { ParseEv(); return; }
            if (LookIdent("ENVVAR_DATA_") || LookIdent("EV_DATA_")) { ParseEvData(); return; }
            if (LookIdent("BO_TX_BU_")) { ParseBoTxBu(); return; }
            if (LookIdent("SG_MUL_VAL_")) { ParseSgMulVal(); return; }
            if (LookIdent("SGTYPE_VAL_")) { ParseSgTypeVal(); return; }
            if (LookIdent("SGTYPE_")) { ParseSgType(); return; }
            if (LookIdent("SIG_TYPE_REF_")) { ParseSigTypeRef(); return; }
            if (LookIdent("SIGTYPE_VALTYPE_")) { ParseSigTypeValType(); return; }

            // Unknown / obsolete line: skip until ';' or next keyword-ish boundary
            SkipUnknownLine();
        }

        private void SkipUnknownLine()
        {
            Take();
            while (!AtEof && Cur.Kind != DbcTokKind.Semi)
            {
                // Stop if we hit a known top-level keyword at start of "line" (heuristic: after we already took one)
                if (Cur.Kind == DbcTokKind.Ident && IsTopLevelKeyword(Cur.Text))
                    break;
                Take();
            }
            OptionalSemi();
        }

        private static bool IsTopLevelKeyword(string t) =>
            t is "VERSION" or "NS_" or "BS_" or "BU_" or "BO_" or "CM_" or "BA_DEF_" or "BA_DEF_DEF_"
                or "BA_DEF_REL_" or "BA_DEF_DEF_REL_" or "BA_" or "BA_REL_" or "VAL_" or "VAL_TABLE_"
                or "SIG_VALTYPE_" or "SIG_GROUP_" or "EV_" or "ENVVAR_DATA_" or "EV_DATA_"
                or "BO_TX_BU_" or "SG_MUL_VAL_" or "SGTYPE_" or "SGTYPE_VAL_" or "SIG_TYPE_REF_"
                or "BA_DEF_SGTYPE_" or "BA_SGTYPE_" or "SIGTYPE_VALTYPE_";

        private void ParseVersion()
        {
            Take();
            if (Cur.Kind == DbcTokKind.String)
                Db.Version = Take().Text;
            OptionalSemi();
        }

        private void ParseNs()
        {
            Take();
            if (Cur.Kind == DbcTokKind.Colon) Take();
            while (!AtEof && Cur.Kind == DbcTokKind.Ident)
            {
                // NS_ lists keyword names as symbols; only stop at next real section header
                if (Cur.Text is "BS_" or "BU_" or "BO_")
                    break;
                Db.NewSymbols.Add(Take().Text);
                if (Db.NewSymbols.Count > 500) break;
            }
            OptionalSemi();
        }

        private void ParseBs()
        {
            Take();
            if (Cur.Kind == DbcTokKind.Colon) Take();
            if (Cur.Kind == DbcTokKind.Int)
            {
                Db.BaudRate = ParseUInt(Take().Text);
                if (Cur.Kind == DbcTokKind.Colon)
                {
                    Take();
                    if (Cur.Kind == DbcTokKind.Int) Db.Btr1 = (int)ParseUInt(Take().Text);
                    if (Cur.Kind == DbcTokKind.Comma) Take();
                    if (Cur.Kind == DbcTokKind.Int) Db.Btr2 = (int)ParseUInt(Take().Text);
                }
            }
            OptionalSemi();
        }

        private void ParseBu()
        {
            Take();
            if (Cur.Kind == DbcTokKind.Colon) Take();
            while (Cur.Kind == DbcTokKind.Ident)
            {
                // Don't swallow following section headers (BO_ / CM_ / BA_…)
                if (IsTopLevelKeyword(Cur.Text))
                    break;
                var name = Take().Text;
                if (Db.FindNode(name) == null)
                    Db.Nodes.Add(new DbcNode { Name = name });
            }
            OptionalSemi();
        }

        private void ParseBo()
        {
            Take();
            var id = ParseUInt(ExpectIntText());
            var name = ExpectIdent();
            Expect(DbcTokKind.Colon);
            var dlc = (int)ParseUInt(ExpectIntText());
            var tx = ExpectIdent();
            var msg = new DbcMessage
            {
                Id = id,
                Name = name,
                Dlc = dlc,
                Transmitter = tx,
                IdFormat = DbcMessage.InferIdFormat(id)
            };
            while (LookIdent("SG_"))
                ParseSg(msg);
            Db.Messages.Add(msg);
        }

        private void ParseSg(DbcMessage msg)
        {
            Take(); // SG_
            var name = ExpectIdent();
            var mux = "";
            if (Cur.Kind == DbcTokKind.Ident)
            {
                // M / m0 / m1M glued as IDENT
                var t = Cur.Text;
                if (t == "M" || t.StartsWith('m'))
                {
                    mux = Take().Text;
                }
            }
            Expect(DbcTokKind.Colon);
            var start = (int)ParseUInt(ExpectIntText());
            Expect(DbcTokKind.Pipe);
            var len = (int)ParseUInt(ExpectIntText());
            Expect(DbcTokKind.At);
            var order = (int)ParseUInt(ExpectIntText());
            var signed = false;
            if (Cur.Kind == DbcTokKind.Plus) Take();
            else if (Cur.Kind == DbcTokKind.Minus) { Take(); signed = true; }

            Expect(DbcTokKind.LParen);
            var factor = ParseNumberValue();
            Expect(DbcTokKind.Comma);
            var offset = ParseNumberValue();
            Expect(DbcTokKind.RParen);
            Expect(DbcTokKind.LBracket);
            var min = ParseNumberValue();
            Expect(DbcTokKind.Pipe);
            var max = ParseNumberValue();
            Expect(DbcTokKind.RBracket);
            var unit = Cur.Kind == DbcTokKind.String ? Take().Text : "";

            var receivers = new List<string>();
            if (Cur.Kind == DbcTokKind.Ident)
            {
                receivers.Add(Take().Text);
                while (Cur.Kind == DbcTokKind.Comma)
                {
                    Take();
                    receivers.Add(ExpectIdent());
                }
            }

            var sig = new DbcSignal
            {
                Name = name,
                StartBit = start,
                Length = len,
                IsLittleEndian = order == 1,
                IsSigned = signed,
                Factor = factor,
                Offset = offset,
                Min = min,
                Max = max,
                Unit = unit,
                MessageName = msg.Name,
                MessageId = msg.Id,
                Multiplexing = FormatMux(mux)
            };
            foreach (var r in receivers) sig.Receivers.Add(r);
            msg.Signals.Add(sig);
            Db.AllSignals.Add(sig);
        }

        private void ParseCm()
        {
            Take();
            if (LookIdent("BU_"))
            {
                Take();
                var n = ExpectIdent();
                var text = ExpectString();
                var node = Db.FindNode(n);
                if (node != null) node.Comment = text;
                OptionalSemi();
                return;
            }
            if (LookIdent("BO_"))
            {
                Take();
                var id = ParseUInt(ExpectIntText());
                var text = ExpectString();
                var msg = Db.FindMessage(id);
                if (msg != null) msg.Comment = text;
                OptionalSemi();
                return;
            }
            if (LookIdent("SG_"))
            {
                Take();
                var id = ParseUInt(ExpectIntText());
                var sn = ExpectIdent();
                var text = ExpectString();
                var sig = Db.FindSignal(id, sn);
                if (sig != null) sig.Comment = text;
                OptionalSemi();
                return;
            }
            if (LookIdent("EV_"))
            {
                Take();
                var n = ExpectIdent();
                var text = ExpectString();
                var ev = Db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase));
                if (ev != null) ev.Comment = text;
                OptionalSemi();
                return;
            }
            // network comment
            var cm = ExpectString();
            Db.Comment = cm;
            Db.NetworkAttributes["CM_"] = cm;
            OptionalSemi();
        }

        private void ParseBaDef()
        {
            var rel = LookIdent("BA_DEF_REL_");
            var sgtype = LookIdent("BA_DEF_SGTYPE_");
            Take();
            var objType = "";
            if (!sgtype && Cur.Kind == DbcTokKind.Ident &&
                Cur.Text is "BU_" or "BO_" or "SG_" or "EV_" or "BU_SG_REL_" or "BU_BO_REL_" or "BU_EV_REL_")
                objType = Take().Text;
            if (sgtype) objType = "SGTYPE_";

            var name = ExpectString();
            var def = new DbcAttrDef { Name = name, ObjectType = objType };

            if (LookIdent("ENUM"))
            {
                Take();
                def.ValueType = "ENUM";
                while (Cur.Kind == DbcTokKind.String)
                {
                    def.EnumValues.Add(Take().Text);
                    if (Cur.Kind == DbcTokKind.Comma) Take();
                }
            }
            else if (LookIdent("STRING"))
            {
                Take();
                def.ValueType = "STRING";
            }
            else if (LookIdent("INT") || LookIdent("HEX") || LookIdent("FLOAT"))
            {
                def.ValueType = Take().Text.ToUpperInvariant();
                if (IsNumberToken())
                {
                    def.Minimum = TakeNumberText();
                    if (IsNumberToken()) def.Maximum = TakeNumberText();
                }
            }
            else
            {
                // consume until ;
                while (!AtEof && Cur.Kind != DbcTokKind.Semi) Take();
            }

            Db.AttrDefs.Add(def);
            _ = rel;
            OptionalSemi();
        }

        private void ParseBaDefDef()
        {
            Take();
            var name = ExpectString();
            var val = Cur.Kind == DbcTokKind.String ? Take().Text : TakeNumberText();
            foreach (var def in Db.AttrDefs.Where(a =>
                         string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
                def.DefaultValue = val;
            OptionalSemi();
        }

        private void ParseBa()
        {
            Take();
            var aname = ExpectString();
            string aval;
            if (LookIdent("BU_"))
            {
                Take();
                var n = ExpectIdent();
                aval = ReadAttrValue();
                aval = MapEnumIndex(aname, aval);
                var node = Db.FindNode(n) ?? new DbcNode { Name = n };
                if (Db.FindNode(n) == null) Db.Nodes.Add(node);
                node.Attributes[aname] = aval;
                if (aname.Equals("NodeLayerModules", StringComparison.OrdinalIgnoreCase) ||
                    aname.Contains("Address", StringComparison.OrdinalIgnoreCase))
                {
                    if (aname.Contains("Address", StringComparison.OrdinalIgnoreCase))
                        node.Address = aval.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? aval
                            : (ulong.TryParse(aval, out var addr) ? $"0x{addr:X}" : aval);
                }
            }
            else if (LookIdent("BO_"))
            {
                Take();
                var id = ParseUInt(ExpectIntText());
                aval = ReadAttrValue();
                aval = MapEnumIndex(aname, aval);
                var msg = Db.FindMessage(id);
                if (msg != null)
                {
                    msg.Attributes[aname] = aval;
                    if (aname.Equals("GenMsgCycleTime", StringComparison.OrdinalIgnoreCase))
                        msg.CycleTime = aval;
                    else if (aname.Equals("GenMsgSendType", StringComparison.OrdinalIgnoreCase))
                        msg.TxMethod = aval;
                }
            }
            else if (LookIdent("SG_"))
            {
                Take();
                var id = ParseUInt(ExpectIntText());
                var sn = ExpectIdent();
                aval = ReadAttrValue();
                aval = MapEnumIndex(aname, aval);
                var sig = Db.FindSignal(id, sn);
                if (sig != null) sig.Attributes[aname] = aval;
            }
            else if (LookIdent("EV_"))
            {
                Take();
                var n = ExpectIdent();
                aval = ReadAttrValue();
                aval = MapEnumIndex(aname, aval);
                var ev = Db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase));
                if (ev != null) ev.Attributes[aname] = aval;
            }
            else
            {
                aval = ReadAttrValue();
                aval = MapEnumIndex(aname, aval);
                Db.NetworkAttributes[aname] = aval;
            }
            OptionalSemi();
        }

        private void ParseBaRel()
        {
            Take();
            var aname = ExpectString();
            if (LookIdent("BU_SG_REL_"))
            {
                Take();
                var node = ExpectIdent();
                if (LookIdent("SG_")) Take();
                var msgId = ParseUInt(ExpectIntText());
                var sig = ExpectIdent();
                var val = ReadAttrValue();
                Db.RelAttributes.Add(new DbcRelAttr
                {
                    Name = aname, RelType = "BU_SG_REL_", NodeName = node,
                    MessageId = msgId, SignalName = sig, Value = val
                });
            }
            else if (LookIdent("BU_BO_REL_"))
            {
                Take();
                var node = ExpectIdent();
                var msgId = ParseUInt(ExpectIntText());
                var val = ReadAttrValue();
                Db.RelAttributes.Add(new DbcRelAttr
                {
                    Name = aname, RelType = "BU_BO_REL_", NodeName = node,
                    MessageId = msgId, Value = val
                });
            }
            else if (LookIdent("BU_EV_REL_"))
            {
                Take();
                var node = ExpectIdent();
                if (LookIdent("EV_")) Take();
                var ev = ExpectIdent();
                var val = ReadAttrValue();
                Db.RelAttributes.Add(new DbcRelAttr
                {
                    Name = aname, RelType = "BU_EV_REL_", NodeName = node,
                    EnvVarName = ev, Value = val
                });
            }
            else
            {
                while (!AtEof && Cur.Kind != DbcTokKind.Semi) Take();
            }
            OptionalSemi();
        }

        private void ParseBaSgType()
        {
            Take();
            var aname = ExpectString();
            var typeName = ExpectIdent();
            var val = ReadAttrValue();
            var st = Db.FindSignalType(typeName);
            if (st != null) st.Attributes[aname] = val;
            OptionalSemi();
        }

        private void ParseValTable()
        {
            Take();
            var name = ExpectIdent();
            var table = new DbcValueTable { Name = name };
            while (Cur.Kind == DbcTokKind.Int)
            {
                var v = ParseLong(Take().Text);
                var d = ExpectString();
                table.Entries.Add(new DbcValueDesc { Value = v, Description = d });
            }
            Db.ValueTables.Add(table);
            OptionalSemi();
        }

        private void ParseVal()
        {
            Take();
            if (LookIdent("EV_"))
            {
                Take();
                var n = ExpectIdent();
                var ev = Db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase));
                while (Cur.Kind == DbcTokKind.Int)
                {
                    var v = ParseLong(Take().Text);
                    var d = ExpectString();
                    ev?.ValueDescriptions.Add(new DbcValueDesc { Value = v, Description = d });
                }
                OptionalSemi();
                return;
            }

            if (Cur.Kind == DbcTokKind.Int)
            {
                var msgId = ParseUInt(Take().Text);
                if (Cur.Kind == DbcTokKind.Ident)
                {
                    var sn = Take().Text;
                    // Could be signal VAL_ or env VAL_ without EV_ keyword (env name only)
                    var sig = Db.FindSignal(msgId, sn);
                    if (sig != null)
                    {
                        while (Cur.Kind == DbcTokKind.Int)
                        {
                            var v = ParseLong(Take().Text);
                            var d = ExpectString();
                            sig.ValueDescriptions.Add(new DbcValueDesc { Value = v, Description = d });
                        }
                        OptionalSemi();
                        return;
                    }
                }
                // fallback skip
                while (!AtEof && Cur.Kind != DbcTokKind.Semi) Take();
                OptionalSemi();
                return;
            }

            // VAL_ <envName> ...
            if (Cur.Kind == DbcTokKind.Ident)
            {
                var n = Take().Text;
                var ev = Db.EnvVars.FirstOrDefault(e =>
                    string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase));
                while (Cur.Kind == DbcTokKind.Int)
                {
                    var v = ParseLong(Take().Text);
                    var d = ExpectString();
                    ev?.ValueDescriptions.Add(new DbcValueDesc { Value = v, Description = d });
                }
            }
            OptionalSemi();
        }

        private void ParseSigValType()
        {
            Take();
            var msgId = ParseUInt(ExpectIntText());
            var sn = ExpectIdent();
            if (Cur.Kind == DbcTokKind.Colon) Take();
            var vt = (int)ParseUInt(ExpectIntText());
            var sig = Db.FindSignal(msgId, sn);
            if (sig != null) sig.SigValType = vt;
            OptionalSemi();
        }

        private void ParseSigGroup()
        {
            Take();
            var msgId = ParseUInt(ExpectIntText());
            var name = ExpectIdent();
            var reps = (int)ParseUInt(ExpectIntText());
            Expect(DbcTokKind.Colon);
            var g = new DbcSignalGroup { MessageId = msgId, Name = name, Repetitions = reps };
            while (Cur.Kind == DbcTokKind.Ident)
                g.SignalNames.Add(Take().Text);
            Db.SignalGroups.Add(g);
            OptionalSemi();
        }

        private void ParseEv()
        {
            Take();
            var name = ExpectIdent();
            Expect(DbcTokKind.Colon);
            var type = (int)ParseUInt(ExpectIntText());
            Expect(DbcTokKind.LBracket);
            var min = ParseNumberValue();
            Expect(DbcTokKind.Pipe);
            var max = ParseNumberValue();
            Expect(DbcTokKind.RBracket);
            var unit = Cur.Kind == DbcTokKind.String ? Take().Text : "";
            var init = ParseNumberValue();
            var evId = ParseUInt(ExpectIntText());
            var access = ExpectIdent();
            var ev = new DbcEnvVar
            {
                Name = name, Type = type, Min = min, Max = max,
                Unit = unit, InitialValue = init, EvId = evId, AccessType = access
            };
            while (Cur.Kind == DbcTokKind.Ident)
                ev.AccessNodes.Add(Take().Text);
            Db.EnvVars.Add(ev);
            OptionalSemi();
        }

        private void ParseEvData()
        {
            Take();
            var name = ExpectIdent();
            Expect(DbcTokKind.Colon);
            var len = (int)ParseUInt(ExpectIntText());
            var ev = Db.EnvVars.FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (ev != null) ev.DataLength = len;
            OptionalSemi();
        }

        private void ParseBoTxBu()
        {
            Take();
            var msgId = ParseUInt(ExpectIntText());
            Expect(DbcTokKind.Colon);
            var list = Db.MessageTransmitters.TryGetValue(msgId, out var existing)
                ? existing
                : Db.MessageTransmitters[msgId] = new List<string>();
            list.Add(ExpectIdent());
            while (Cur.Kind == DbcTokKind.Comma)
            {
                Take();
                list.Add(ExpectIdent());
            }
            OptionalSemi();
        }

        private void ParseSgMulVal()
        {
            Take();
            var msgId = ParseUInt(ExpectIntText());
            var sigName = ExpectIdent();
            var muxName = ExpectIdent();
            var ranges = new List<string>();
            while (Cur.Kind == DbcTokKind.Int)
            {
                var a = Take().Text;
                if (Cur.Kind == DbcTokKind.Minus) Take();
                var b = Cur.Kind == DbcTokKind.Int ? Take().Text : a;
                ranges.Add($"{a}-{b}");
                if (Cur.Kind == DbcTokKind.Comma) Take();
                else break;
            }
            var sig = Db.FindSignal(msgId, sigName);
            if (sig != null && ranges.Count > 0)
            {
                var rangeText = $"Mux {muxName} [{string.Join(",", ranges)}]";
                sig.Multiplexing = (sig.Multiplexing ?? "").Contains("Multiplexor", StringComparison.Ordinal)
                    ? $"{rangeText} / Multiplexor"
                    : rangeText;
            }
            OptionalSemi();
        }

        /// <summary>Same labels as the ANTLR loader, so m32 and m40 stay mutually exclusive.</summary>
        private static string FormatMux(string? mux)
        {
            if (string.IsNullOrWhiteSpace(mux)) return "-";
            mux = mux.Trim();
            if (mux.Equals("M", StringComparison.Ordinal)) return "Multiplexor";

            var mNm = System.Text.RegularExpressions.Regex.Match(mux, @"^m(\d+)M$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mNm.Success)
                return $"Multiplexed signal {mNm.Groups[1].Value} / Multiplexor";

            var mn = System.Text.RegularExpressions.Regex.Match(mux, @"^m(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mn.Success)
                return $"Multiplexed signal {mn.Groups[1].Value}";

            if (mux.StartsWith('m') || mux.StartsWith('M'))
                return $"Multiplexed signal {mux.TrimStart('m', 'M')}";
            return mux;
        }

        private void ParseSgType()
        {
            Take();
            var name = ExpectIdent();
            Expect(DbcTokKind.Colon);
            var len = (int)ParseUInt(ExpectIntText());
            Expect(DbcTokKind.At);
            var order = (int)ParseUInt(ExpectIntText());
            var signed = false;
            if (Cur.Kind == DbcTokKind.Plus) Take();
            else if (Cur.Kind == DbcTokKind.Minus) { Take(); signed = true; }
            Expect(DbcTokKind.LParen);
            var factor = ParseNumberValue();
            Expect(DbcTokKind.Comma);
            var offset = ParseNumberValue();
            Expect(DbcTokKind.RParen);
            Expect(DbcTokKind.LBracket);
            var min = ParseNumberValue();
            Expect(DbcTokKind.Pipe);
            var max = ParseNumberValue();
            Expect(DbcTokKind.RBracket);
            var unit = Cur.Kind == DbcTokKind.String ? Take().Text : "";
            var defVal = ParseNumberValue();
            if (Cur.Kind == DbcTokKind.Comma) Take();
            var vtName = Cur.Kind == DbcTokKind.Ident ? Take().Text : "";
            Db.SignalTypes.Add(new DbcSignalType
            {
                Name = name, Length = len, IsLittleEndian = order == 1, IsSigned = signed,
                Factor = factor, Offset = offset, Min = min, Max = max, Unit = unit,
                DefaultValue = defVal, ValueTableName = vtName
            });
            OptionalSemi();
        }

        private void ParseSgTypeVal()
        {
            Take();
            var name = ExpectIdent();
            var st = Db.FindSignalType(name);
            while (Cur.Kind == DbcTokKind.Int)
            {
                var v = ParseLong(Take().Text);
                var d = ExpectString();
                st?.ValueDescriptions.Add(new DbcValueDesc { Value = v, Description = d });
            }
            OptionalSemi();
        }

        private void ParseSigTypeRef()
        {
            Take();
            var msgId = ParseUInt(ExpectIntText());
            var sn = ExpectIdent();
            Expect(DbcTokKind.Colon);
            var typeName = ExpectIdent();
            var sig = Db.FindSignal(msgId, sn);
            if (sig != null) sig.SignalTypeRef = typeName;
            OptionalSemi();
        }

        private void ParseSigTypeValType()
        {
            Take();
            var name = ExpectIdent();
            if (Cur.Kind == DbcTokKind.Colon) Take();
            var vt = (int)ParseUInt(ExpectIntText());
            var st = Db.FindSignalType(name);
            if (st != null) st.SigValType = vt;
            OptionalSemi();
        }

        private string ExpectIntText()
        {
            if (Cur.Kind != DbcTokKind.Int)
                throw Fail($"expected INT, got {Cur}");
            return Take().Text;
        }

        private string ExpectString()
        {
            if (Cur.Kind != DbcTokKind.String)
                throw Fail($"expected string, got {Cur}");
            return Take().Text;
        }

        private bool IsNumberToken() =>
            Cur.Kind is DbcTokKind.Int or DbcTokKind.Float or DbcTokKind.Plus or DbcTokKind.Minus;

        private string TakeNumberText()
        {
            var sign = "";
            if (Cur.Kind == DbcTokKind.Plus) { Take(); }
            else if (Cur.Kind == DbcTokKind.Minus) { sign = "-"; Take(); }
            if (Cur.Kind is not (DbcTokKind.Int or DbcTokKind.Float))
                throw Fail($"expected number, got {Cur}");
            return sign + Take().Text;
        }

        private double ParseNumberValue() =>
            double.Parse(TakeNumberText(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private string ReadAttrValue()
        {
            if (Cur.Kind == DbcTokKind.String) return Take().Text;
            if (IsNumberToken()) return TakeNumberText();
            if (Cur.Kind == DbcTokKind.Ident) return Take().Text;
            return "";
        }

        private string MapEnumIndex(string attrName, string aval)
        {
            if (!int.TryParse(aval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                return aval;
            var def = Db.AttrDefs.FirstOrDefault(a =>
                string.Equals(a.Name, attrName, StringComparison.OrdinalIgnoreCase) &&
                a.ValueType == "ENUM" && a.EnumValues.Count > 0);
            if (def != null && idx >= 0 && idx < def.EnumValues.Count)
                return def.EnumValues[idx];
            return aval;
        }

        private static uint ParseUInt(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s, 16);
            return uint.Parse(s, CultureInfo.InvariantCulture);
        }

        private static long ParseLong(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(s, 16);
            return long.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
