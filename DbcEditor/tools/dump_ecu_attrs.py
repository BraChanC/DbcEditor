# -*- coding: utf-8 -*-
"""What Vector ECU vs Node Attributes should show for this DBC."""
from pathlib import Path
t = Path(r"E:\dbc\CV01_CAN_Matrix_PT_V1.19_20250429_FLR.dbc").read_bytes().decode("gbk")
print("BA_DEF_ with object type:")
for line in t.splitlines():
    if line.startswith("BA_DEF_"):
        print(" ", line[:160])
print("\nNote: DBC has no separate ECU object type.")
print("BA_DEF_ BU_ attrs belong to Node dialog, not ECU dialog.")
print("Vector ECU Attributes stays empty when only BU_ defs exist.")
