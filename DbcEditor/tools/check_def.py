# -*- coding: utf-8 -*-
from pathlib import Path
t = Path(r"E:\dbc\CV01_CAN_Matrix_PT_V1.19_20250429_FLR.dbc").read_bytes().decode("gbk")
for line in t.splitlines():
    if "GenMsgSendType" in line or "NmMessage" in line or line.startswith("BA_DEF_ BO_"):
        if line.startswith("BA_DEF") or line.startswith("BA_DEF_DEF"):
            print(line[:160])
