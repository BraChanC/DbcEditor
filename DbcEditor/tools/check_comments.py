# -*- coding: utf-8 -*-
from pathlib import Path
import re
import sys

path = Path(sys.argv[1] if len(sys.argv) > 1 else r"E:\dbc\CV01_CAN_Matrix_PT_V1.19_20250429_FLR.dbc")
b = path.read_bytes()
# detect
try:
    b.decode("utf-8")
    print("utf8: OK")
except UnicodeDecodeError as e:
    print("utf8: FAIL", e)

t = b.decode("gbk")
bos = re.findall(r"^BO_\s+(\d+)\s+(\w+)", t, re.M)
print("messages", len(bos), "sample", bos[:5])
cms_bo = re.findall(r'^CM_\s+BO_\s+(\d+)\s+"(.*?)"\s*;', t, re.M | re.S)
cms_sg = re.findall(r'^CM_\s+SG_\s+(\d+)\s+(\w+)\s+"(.*?)"\s*;', t, re.M | re.S)
cms_bu = re.findall(r'^CM_\s+BU_\s+(\w+)\s+"(.*?)"\s*;', t, re.M | re.S)
print("CM BO", len(cms_bo), cms_bo[:3])
print("CM SG", len(cms_sg))
print("CM BU", len(cms_bu), cms_bu[:3])
m = re.search(r'^CM_\s+"(.*?)"\s*;', t, re.M | re.S)
if m:
    print("net CM head:", m.group(1)[:160].replace("\n", "\\n"))
# id match: do SG comments reference existing message ids?
ids = {int(i) for i, _ in bos}
orphan = [(mid, name) for mid, name, _ in cms_sg if int(mid) not in ids]
print("orphan SG comments (msg id missing):", orphan[:10], "count", len(orphan))
# show a few SG comments
for mid, name, cmt in cms_sg[:5]:
    print(f"  SG {mid} {name}: {cmt[:60]!r}")
