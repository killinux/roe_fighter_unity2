# Look inside a mesh of a game bundle: sub-meshes, UV ranges, and a UV plot per sub-mesh.
#   python mesh_probe.py <bundle file> <mesh name> [out.png]
import io
import sys

import numpy as np
import UnityPy
from PIL import Image, ImageDraw


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    bundle, name = sys.argv[1], sys.argv[2]
    out = sys.argv[3] if len(sys.argv) > 3 else None
    env = UnityPy.load(bundle)
    for obj in env.objects:
        if obj.type.name != 'Mesh':
            continue
        m = obj.read()
        if m.m_Name != name:
            continue
        txt = m.export()  # OBJ text: v / vt / vn / g + f
        v, vt, groups = [], [], []
        cur = None
        for line in txt.splitlines():
            p = line.split()
            if not p:
                continue
            if p[0] == 'v':
                v.append([float(x) for x in p[1:4]])
            elif p[0] == 'vt':
                vt.append([float(x) for x in p[1:3]])
            elif p[0] == 'g':
                cur = []
                groups.append((p[1] if len(p) > 1 else str(len(groups)), cur))
            elif p[0] == 'f' and cur is not None:
                cur.append([int(x.split('/')[0]) - 1 for x in p[1:4]])
        v = np.array(v)
        vt = np.array(vt)
        print(f'{name}: {len(v)} verts, {len(vt)} uvs, {len(groups)} sub-meshes; bounds {v.min(0).round(3)} .. {v.max(0).round(3)}')
        S = 512
        sheet = Image.new('RGB', (S * len(groups), S), (20, 20, 20))
        d = ImageDraw.Draw(sheet)
        for gi, (gname, faces) in enumerate(groups):
            if not faces:
                print(f'  sub {gi} {gname}: empty')
                continue
            f = np.array(faces, dtype=np.int64)
            idx = np.unique(f)
            pv, puv = v[idx], vt[idx]
            print(f'  sub {gi} {gname}: {len(f)} tris, {len(idx)} verts, pos {pv.min(0).round(3)} .. {pv.max(0).round(3)}, '
                  f'uv {puv.min(0).round(3)} .. {puv.max(0).round(3)}')
            for tri in f:
                pts = [(gi * S + vt[i][0] * S, (1 - vt[i][1]) * S) for i in tri]
                d.polygon(pts, outline=(90, 200, 255))
            d.text((gi * S + 4, 4), f'{gi} {gname}', fill=(255, 255, 0))
        if out:
            sheet.save(out)
            print('wrote', out)
        return v, vt, groups
    print('mesh not found:', name)


if __name__ == '__main__':
    main()
