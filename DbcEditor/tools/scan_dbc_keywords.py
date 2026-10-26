# -*- coding: utf-8 -*-
"""Scan a DBC for top-level keyword frequency."""
from pathlib import Path
from collections import Counter
import re

path = Path(r"E:\dbc\CV01_CAN_Matrix_PT_V1.19_20250429_FLR.dbc")
t = path.read_bytes().decode("gbk")
# first token of each non-empty, non-indented-ish statement line
keys = Counter()
for line in t.splitlines():
    s = line.strip()
    if not s or s.startswith("//"):
        continue
    m = re.match(r"^([A-Z][A-Z0-9_]*)", s)
    if m:
        keys[m.group(1)] += 1
print("Keyword counts:")
for k, n in keys.most_common():
    print(f"  {k}: {n}")
# sample VAL_
print("\nVAL_ samples:")
for line in t.splitlines():
    if line.startswith("VAL_"):
        print(line[:140])
print("\nBA_ SG_ samples:")
n = 0
for line in t.splitlines():
    if ' SG_ ' in line and line.startswith("BA_"):
        print(line[:140])
        n += 1
        if n >= 8:
            break
