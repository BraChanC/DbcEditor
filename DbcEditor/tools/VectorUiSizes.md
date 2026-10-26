# Vector CANdb++ 3.1 UI sizes (measured from live `candb.exe`)

Measured with `tools/measure_candb_windows.py` against:
`E:\dbc\CV01_CAN_Matrix_PT_V1.19_20250429_FLR.dbc`

| Window | Size (outer) |
|--------|----------------|
| Main editor (Overall View) | **1279 × 597** (also seen 1279 × 743) |
| Network / ECU / property sheets | **813 × 556** |

## DbcEditor mapping

| Window | Target |
|--------|--------|
| `MainWindow` | 1280 × 600, left tree 320 |
| Network / ECU / NetworkNode / Node / NodeTxMessage / Signal / Message / EnvVar | 813 × 556 |
| Node Rx Signal / Node - Tx Message (measured live) | 813 × 556 |

Font: Tahoma 11 (Vector-like).
