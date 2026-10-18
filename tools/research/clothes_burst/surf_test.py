"""Surface-to-surface check (tessellation independent) between nude base body and suit skin."""
import os, sys, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um, fbx_bin
from nude_vs_suit import CASES, hash_nn, render, OUT

def sample(P, T, step=0.003):
    a, b, c = P[T[:, 0]], P[T[:, 1]], P[T[:, 2]]
    L = np.maximum(np.linalg.norm(b - a, axis=1), np.linalg.norm(c - a, axis=1))
    n = np.clip(np.ceil(L / step).astype(int), 1, 30)
    out = []
    for k in np.unique(n):
        sel = n == k
        u, v = np.meshgrid(np.arange(k + 1), np.arange(k + 1))
        m = (u + v) <= k
        u = u[m] / k; v = v[m] / k
        pts = a[sel, None, :] + (b[sel] - a[sel])[:, None, :] * u[None, :, None] + (c[sel] - a[sel])[:, None, :] * v[None, :, None]
        out.append(pts.reshape(-1, 3))
    return np.concatenate(out)

rep = {}
for ch, cfg in (CASES.items() if __name__ == '__main__' else []):
    root, _ = fbx_bin.load(cfg['nude'])
    g = [x for x in fbx_bin.geometries(root) if x['model'] == cfg['body_model']][0]
    NT = np.array([p for p, m in zip(g['polys'], g['mat_per_poly']) if m == 0 and len(p) == 3])
    SPs, STs = [], []
    for path, si in cfg['suit']:
        m = um.load(path); T = um.submesh_tris(m, si, 2)
        SPs.append((m['pos'], T))
    best = None
    for flip in (1, -1):
        NV = g['verts'] * np.array([flip, 1, 1])
        S = np.concatenate([sample(P, T) for P, T in SPs])
        used = np.unique(NT)
        d = np.full(len(NV), np.inf)
        d[used] = hash_nn(NV[used], S, cell=0.01)
        ok = d < 0.003
        tri_ok = ok[NT].all(axis=1)
        area = um.tri_area(NV, NT)
        frac = area[tri_ok].sum() / area.sum()
        if best is None or frac > best[0]:
            best = (frac, flip, tri_ok, area, NV, d)
    frac, flip, tri_ok, area, NV, d = best
    # reverse: suit skin verts -> nude surface
    Ns = sample(NV, NT)
    sv = np.concatenate([P[np.unique(T)] for P, T in SPs])
    rd = hash_nn(sv, Ns, cell=0.01)
    fin = rd[np.isfinite(rd)]
    rep[ch] = dict(x_flip=flip, nude_area_m2=round(float(area.sum()), 3), nude_area_on_suit_skin_m2=round(float(area[tri_ok].sum()), 3),
                   frac=round(float(frac), 3),
                   suit_skin_to_nude_surface_mm=dict(median=round(float(np.median(fin) * 1000), 2), p90=round(float(np.percentile(fin, 90) * 1000), 2),
                                                    p99=round(float(np.percentile(fin, 99) * 1000), 2), over_1cm=int((~np.isfinite(rd)).sum()), n=int(len(rd))))
    colors = np.where(tri_ok[:, None], np.array([[205, 190, 180]]), np.array([[220, 40, 40]]))
    b = (NV[np.unique(NT)].min(0), NV[np.unique(NT)].max(0))
    render(NV, NT, colors, f'{ch}_nude_vs_suit_surf_front.png', 'front', b)
    render(NV, NT, colors, f'{ch}_nude_vs_suit_surf_back.png', 'back', b)
    # also which suit skin verts are far from nude (shape differs)
if __name__ == '__main__':
    print(json.dumps(rep, indent=1))
    json.dump(rep, open(os.path.join(OUT, 'nude_vs_suit_surface.json'), 'w'), indent=1)
