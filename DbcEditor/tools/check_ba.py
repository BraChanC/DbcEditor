# -*- coding: utf-8 -*-
from pathlib import Path
import re
from collections import Counter

t = Path(r"E:\dbc\CV01_CAN_Matrix_PT_V1.19_20250429_FLR.dbc").read_bytes().decode("gbk")
bas = re.findall(r'^BA_ "([^"]+)" BO_ (\d+) (.+);', t, re.M)
print("BA BO count", len(bas), "attrs", Counter(a for a, _, _ in bas))
for a, i, v in bas[:20]:
    print(a, i, v)
print("--- defaults ---")
for line in t.splitlines():
    if line.startswith("BA_DEF_DEF_"):
        print(line[:120])
