# DBC keyword coverage (DbcAntlrLoader)

Grammar: `Grammar/Dbc.g4` · Loader: `Services/DbcAntlrLoader.cs`  
Verify: `dotnet run --project tools/DbcDump -- <file.dbc> [SignalName]`

| Keyword | Parsed into model |
|---------|-------------------|
| `VERSION` | `DbcDatabase.Version` |
| `NS_` | `NewSymbols` (28-item list) |
| `BS_` | `BaudRate` / `Btr1` / `Btr2` |
| `BU_` | `Nodes` |
| `BO_` / `SG_` | `Messages` / `Signals` |
| `CM_` (net/BU_/BO_/SG_/EV_) | `.Comment` on network/node/message/signal/env |
| `BA_DEF_` / `BA_DEF_REL_` | `AttrDefs` (+ ENUM values) |
| `BA_DEF_DEF_` / `BA_DEF_DEF_REL_` | `AttrDefs.DefaultValue` |
| `BA_` net/BU_/BO_/SG_/EV_ | `.Attributes` (+ CycleTime/TxMethod/InitialValue/Address) |
| `BA_REL_` (BU_SG_/BU_BO_/BU_EV_) | `RelAttributes` |
| `VAL_TABLE_` | `ValueTables` |
| `VAL_` | `Signal.ValueDescriptions` / env VAL_ |
| `SIG_VALTYPE_` (with/without `:`) | `Signal.SigValType` |
| `SIG_GROUP_` | `SignalGroups` |
| `EV_` / `ENVVAR_DATA_` / `EV_DATA_` | `EnvVars` (+ `DataLength`) |
| `BO_TX_BU_` | `MessageTransmitters` |
| `SG_MUL_VAL_` | `Signal.Multiplexing` ranges |
| `SGTYPE_` | `SignalTypes` |
| `SGTYPE_VAL_` | `SignalType.ValueDescriptions` |
| `SIG_TYPE_REF_` | `Signal.SignalTypeRef` |
| `BA_DEF_SGTYPE_` | `AttrDefs` (`ObjectType=SGTYPE_`) |
| `BA_SGTYPE_` | `SignalType.Attributes` |
| `SIGTYPE_VALTYPE_` | `SignalType.SigValType` |
| `NS_DESC_` / `CAT_DEF_` / `CAT_` / `FILTER` | symbol table and/or `ignoredLine` (obsolete segment bodies) |
| unknown | `ignoredLine` (parse continues) |

### CAT_ / FILTER (three different meanings)

| 形态 | 例子 | 绑定 |
|------|------|------|
| 属性定义/赋值 | `BA_DEF_ BO_ "CAT_" STRING ;` · `BA_ "CAT_" BO_ 1234 "Chassis" ;` · `BA_DEF_ SG_ "FILTER" INT 0 10 ;` | 普通 `AttrDefs` / `.Attributes`；`Finalize` → `Category` / `FilterTag`，`*Source=attr` |
| 命名前缀 | 报文 `CAT_ABS_Info` · 信号 `FILTER_VehicleSpeed` | 无对应 BA_ 时：`Category=ABS_Info` / `FilterTag=VehicleSpeed`，`*Source=name` |
| `NS_` 段关键字 | 符号表里的 `CAT_` `CAT_DEF_` `FILTER` | 仅认符号；正文无公开产生式 → `ignoredLine` |

属性名 `"CAT_"` / `"FILTER"` 与段关键字同名但路径不同：走 `BA_DEF_` / `BA_`，不走废弃段。

Finalize applies BO_/SG_ attribute defaults so Vector-style `*` defaults appear without requiring every `BA_` line.
