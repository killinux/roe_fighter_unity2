"""What is the 'rip' clip?  Map hashed binding paths via CRC32 of transform paths from the prefab and
print the pelvis height / root over time for rip, die, idle_01 (read-only)."""
import re, zlib, sys
import numpy as np
PREF = r"E:/code/othercode/roe_fighter_unity/Assets/ROE/a08/chara_armor_pc_a08_hd_ld_ld_prelude/pc_a08_ld.prefab"
txt = open(PREF, encoding='utf-8', errors='ignore').read()
docs = re.split(r'^--- !u!(\d+) &(-?\d+).*$', txt, flags=re.M)
objs = {docs[i + 1]: (int(docs[i]), docs[i + 2]) for i in range(1, len(docs), 3)}
names = {f: re.search(r'm_Name: (.*)', b).group(1).strip() for f, (c, b) in objs.items() if c == 1}
tr = {}
for f, (c, b) in objs.items():
    if c == 4:
        go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', b).group(1)
        fa = re.search(r'm_Father: \{fileID: (-?\d+)\}', b).group(1)
        tr[f] = (names.get(go, '?'), fa)
def path(f):
    parts = []
    while f in tr and tr[f][1] != '0':
        parts.append(tr[f][0]); f = tr[f][1]
    return '/'.join(reversed(parts))
crc = {zlib.crc32(path(f).encode('utf-8')): path(f) for f in tr}
def curves(anim):
    a = open(anim, encoding='utf-8', errors='ignore').read()
    # m_PositionCurves blocks: curve keys then path
    out = {}
    pos = a.split('m_PositionCurves:')[1].split('m_ScaleCurves:')[0]
    for blk in re.split(r'\n  - curve:', pos)[1:]:
        p = re.search(r'path: (\S+)', blk).group(1)
        ks = re.findall(r'time: (\S+)\s+value: \{x: (\S+), y: (\S+), z: (\S+)\}', blk)
        out[p] = np.array([[float(x) for x in k] for k in ks]) if ks else None
    return out
for clip in ['rip', 'die', 'idle_01']:
    c = curves(f"E:/code/othercode/roe_fighter_unity/Assets/ROE/a08/chara_armor_pc_a08_hd_ld_ld_prelude/{clip}.anim")
    named = {}
    for p, v in c.items():
        nm = crc.get(int(p), p) if p.isdigit() else p
        named[nm] = v
    for key in [k for k in named if k.endswith('Bip001') or k.endswith('Bip001 Pelvis')][:2]:
        v = named[key]
        if v is None: continue
        print(clip, key, 'keys', len(v), 't %.2f-%.2f' % (v[0, 0], v[-1, 0]), 'pos first', v[0, 1:].round(3), 'last', v[-1, 1:].round(3), 'min/max per axis', v[:, 1:].min(0).round(3), v[:, 1:].max(0).round(3))
