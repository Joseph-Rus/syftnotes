#!/usr/bin/env python3
"""What grew between two heap snapshots, by type: gcdump-diff.py A.txt B.txt [rounds]
A.txt and B.txt are `dotnet-gcdump report X.gcdump` outputs. The report's first column is the size of one object of
that type (variable-size types are bucketed), so growth is (count after - count before) x that size."""
import re, sys
def load(p):
    d = {}
    for line in open(p, errors='replace'):
        m = re.match(r'\s*([\d,]+)\s+([\d,]+)\s+(.*\S)\s*$', line)
        if not m: continue
        size = int(m[1].replace(',', '')); count = int(m[2].replace(',', '')); name = m[3]
        d[name] = (size, d.get(name, (0, 0))[1] + count)
    return d
A, B = load(sys.argv[1]), load(sys.argv[2])
rounds = int(sys.argv[3]) if len(sys.argv) > 3 else 1
rows = sorted(((B.get(n, (0, 0))[1] - A.get(n, (0, 0))[1]) * max(A.get(n, (0, 0))[0], B.get(n, (0, 0))[0]),
               B.get(n, (0, 0))[1] - A.get(n, (0, 0))[1], n) for n in set(A) | set(B))[::-1]
total = sum(r[0] for r in rows)
print(f"net growth {total / 1e3:.0f} KB over {rounds} round(s) = {total / 1e3 / rounds:.1f} KB each")
for grew, count, name in rows[:40]:
    print(f"{grew:>9} {count:>6} ({count / rounds:.1f}/round)  {name[:130]}")
