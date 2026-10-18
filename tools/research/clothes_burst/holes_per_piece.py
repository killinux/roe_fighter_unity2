"""Read-only: for each outfit piece (connected component), how much body surface is MISSING in the suit's
skin right under it (nude-base triangles not on the suit skin, nearest outfit piece within 4 cm)?
= the hole you would see if that piece were removed without a body underneath."""
import os, sys, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um
import fbx_bin
from nude_vs_suit import CASES as NCASES
from surf_test import sample
from outfit_pieces import CASES as PCASES, components, smr_bones

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'img')


def nn_index(Q, P, cell=0.02):
    key = np.floor(P / cell).astype(np.int64)
    d = {}
    for i, k in enumerate(map(tuple, key)):
        d.setdefault(k, []).append(i)
    idx = np.full(len(Q), -1)
    dist = np.full(len(Q), np.inf)
    for i, q in enumerate(Q):
        k = np.floor(q / cell).astype(np.int64)
        for a in (-1, 0, 1):
            for b in (-1, 0, 1):
                for c in (-1, 0, 1):
                    lst = d.get((k[0] + a, k[1] + b, k[2] + c))
                    if lst:
                        dd = np.linalg.norm(P[lst] - q, axis=1)
                        j = int(np.argmin(dd))
                        if dd[j] < dist[i]:
                            dist[i], idx[i] = dd[j], lst[j]
    return idx, dist


def main():
    rep = {}
    for ch in ('a08', 'g04'):
        cfg = NCASES[ch]
        root, _ = fbx_bin.load(cfg['nude'])
        g = [x for x in fbx_bin.geometries(root) if x['model'] == cfg['body_model']][0]
        NT = np.array([p for p, m in zip(g['polys'], g['mat_per_poly']) if m == 0 and len(p) == 3])
        NV = g['verts'] * np.array([-1, 1, 1])
        S = np.concatenate([sample(um.load(p)['pos'], um.submesh_tris(um.load(p), si, 2)) for p, si in cfg['suit']])
        used = np.unique(NT)
        _, dv = nn_index(NV[used], S, cell=0.01)
        dfull = np.full(len(NV), np.inf)
        dfull[used] = dv
        tri_ok = (dfull[NT] < 0.003).all(axis=1)
        miss = ~tri_ok
        cen = NV[NT[miss]].mean(axis=1)
        area = um.tri_area(NV, NT)[miss]
        # outfit pieces: all outfit vertices with a (renderer, piece id) label
        allP, allLab, names = [], [], []
        for c2, path, si, prefab, rname in PCASES:
            if c2 != ch:
                continue
            m = um.load(path)
            T = um.submesh_tris(m, si, 2)
            w = um.weld(m['pos'], 1e-4)
            lab = components(T, w)
            bones = smr_bones(prefab, rname)
            bw, bi = m['bw'], m['bi']
            for l in np.unique(lab):
                vs = np.unique(T[lab == l])
                acc = {}
                for k in range(4):
                    for b_, ww in zip(bi[vs, k], bw[vs, k]):
                        if ww > 0:
                            acc[int(b_)] = acc.get(int(b_), 0) + float(ww)
                top = max(acc.items(), key=lambda x: x[1])[0]
                z = m['pos'][vs, 2]
                names.append(f'{rname}: {bones[top]} z{z.min():.2f}-{z.max():.2f} ({int((lab == l).sum())} tris)')
                allP.append(m['pos'][vs])
                allLab.append(np.full(len(vs), len(names) - 1))
        allP = np.concatenate(allP)
        allLab = np.concatenate(allLab)
        idx, dist = nn_index(cen, allP, cell=0.02)
        acc = {}
        uncovered = 0.0
        for a, i, d in zip(area, idx, dist):
            if i < 0 or d > 0.04:
                uncovered += a
                continue
            acc[names[allLab[i]]] = acc.get(names[allLab[i]], 0) + a
        rows = sorted(acc.items(), key=lambda x: -x[1])
        rep[ch] = dict(missing_total_cm2=round(float(area.sum() * 1e4)), not_near_any_piece_cm2=round(uncovered * 1e4),
                       by_piece=[(k, round(v * 1e4)) for k, v in rows])
        print('==', ch, 'missing skin %.0f cm2, not within 4 cm of an outfit piece %.0f cm2' % (area.sum() * 1e4, uncovered * 1e4))
        for k, v in rows[:14]:
            print('   %6.0f cm2  under %s' % (v * 1e4, k))
    json.dump(rep, open(os.path.join(OUT, 'holes_per_piece.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)


if __name__ == '__main__':
    main()
