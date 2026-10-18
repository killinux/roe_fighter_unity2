"""Read-only: compare the family nude base body (FBX from D:/roe_exports) with the suit's skin submesh
(ripped Unity mesh).  Which nude-body triangles also exist in the suit (same surface) and which are
missing (deleted under the outfit)?  Renders nude body: grey = also in suit skin, red = missing in suit."""
import os, sys, json
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(__file__))
import unity_mesh as um
import fbx_bin

A = r"E:/code/othercode/roe_fighter_unity/Assets/ROE"
OUT = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'img')

CASES = {
    'a08': dict(nude='D:/roe_exports/a01/pc_a01_nk_bs/FBX_GameObjects/pc_a01_nk_bs/pc_a01_nk_bs.fbx', body_model='pc_a01_nk_body',
                suit=[(A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset', 0)],
                outfit=[(A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset', 1), (A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body2.asset', 0)]),
    'g04': dict(nude='D:/roe_exports/g01/pc_g01_nk_bs/FBX_GameObjects/pc_g01_nk_bs/pc_g01_nk_bs.fbx', body_model='pc_g01_nk_body',
                suit=[(A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset', 1), (A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset', 0)],
                outfit=[(A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset', 2)]),
}


def hash_nn(Q, P, cell=0.004):
    """nearest distance from each Q to point set P (only searches the 27 neighbouring cells)."""
    key = np.floor(P / cell).astype(np.int64)
    d = {}
    for i, k in enumerate(map(tuple, key)):
        d.setdefault(k, []).append(i)
    out = np.full(len(Q), np.inf)
    qk = np.floor(Q / cell).astype(np.int64)
    offs = [(a, b, c) for a in (-1, 0, 1) for b in (-1, 0, 1) for c in (-1, 0, 1)]
    for i, k in enumerate(qk):
        best = np.inf
        for o in offs:
            lst = d.get((k[0] + o[0], k[1] + o[1], k[2] + o[2]))
            if lst:
                dd = np.linalg.norm(P[lst] - Q[i], axis=1).min()
                if dd < best:
                    best = dd
        out[i] = best
    return out


def render(P, T, colors, fname, view, bounds):
    lo, hi = bounds
    W, H = 420, 900
    s = min((W - 20) / (hi[0] - lo[0]), (H - 20) / (hi[2] - lo[2]))
    img = Image.new('RGB', (W, H), (235, 235, 240))
    dr = ImageDraw.Draw(img)
    cen = P[T].mean(axis=1)
    e1 = P[T[:, 1]] - P[T[:, 0]]
    e2 = P[T[:, 2]] - P[T[:, 0]]
    n = np.cross(e1, e2)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    sign = 1 if view == 'front' else -1
    order = np.argsort(-(sign * cen[:, 1]))
    for i in order:
        t = T[i]
        xs = (P[t, 0] - lo[0]) * s
        if view != 'front':
            xs = (hi[0] - lo[0]) * s - xs
        pts = [(10 + xs[k], H - 10 - (P[t[k], 2] - lo[2]) * s) for k in range(3)]
        sh = 0.35 + 0.65 * abs(n[i, 1])
        dr.polygon(pts, fill=tuple(int(c * sh) for c in colors[i]))
    img.save(os.path.join(OUT, fname))


def main():
    rep = {}
    for ch, cfg in CASES.items():
        root, _ = fbx_bin.load(cfg['nude'])
        g = [x for x in fbx_bin.geometries(root) if x['model'] == cfg['body_model']][0]
        NV = g['verts']
        polys = g['polys']
        mpp = g['mat_per_poly']
        body_polys = [p for p, m in zip(polys, mpp) if m == 0]  # material 0 = *_nk_body
        NT = np.array([p for p in body_polys if len(p) == 3])
        # suit skin vertices
        SP = []
        for path, si in cfg['suit']:
            m = um.load(path)
            T = um.submesh_tris(m, si, 2)
            SP.append(m['pos'][np.unique(T)])
        SP = np.concatenate(SP)
        OP = []
        for path, si in cfg['outfit']:
            m = um.load(path)
            T = um.submesh_tris(m, si, 2)
            OP.append(m['pos'][np.unique(T)])
        OP = np.concatenate(OP)
        best = None
        for flip in (1, -1):
            Q = NV * np.array([flip, 1, 1])
            used = np.unique(NT)
            dist = np.full(len(NV), np.inf)
            dist[used] = hash_nn(Q[used], SP)
            match = dist < 0.002
            tri_ok = match[NT].all(axis=1)
            area = um.tri_area(Q, NT)
            frac = area[tri_ok].sum() / area.sum()
            if best is None or frac > best[0]:
                best = (frac, flip, tri_ok, area, Q, dist)
        frac, flip, tri_ok, area, Q, dist = best
        # missing triangles: how far is the outfit from them? (is the gap covered by the outfit?)
        miss = ~tri_ok
        cen = Q[NT[miss]].mean(axis=1)
        od = hash_nn(cen, OP, cell=0.01)
        od = np.where(np.isfinite(od), od, 0.05)
        rep[ch] = dict(nude_body_tris=int(len(NT)), nude_body_area_m2=round(float(area.sum()), 3),
                       x_flip=flip, matched_tris=int(tri_ok.sum()), matched_area_m2=round(float(area[tri_ok].sum()), 3),
                       matched_area_frac=round(float(frac), 3), missing_area_m2=round(float(area[miss].sum()), 3),
                       missing_with_outfit_within_1cm_frac=round(float(area[miss][od < 0.01].sum() / max(area[miss].sum(), 1e-9)), 3),
                       median_match_dist_mm=round(float(np.median(dist[np.isfinite(dist)]) * 1000), 3))
        # where are the missing pieces (by height band, metres from the floor)
        bands = {}
        zc = Q[NT].mean(axis=1)[:, 2]
        for lo_, hi_, name in [(0, 0.12, 'feet 0-0.12'), (0.12, 0.5, 'shins 0.12-0.5'), (0.5, 0.85, 'thighs 0.5-0.85'),
                               (0.85, 1.05, 'hips/crotch 0.85-1.05'), (1.05, 1.22, 'belly/waist 1.05-1.22'),
                               (1.22, 1.45, 'chest/arms 1.22-1.45'), (1.45, 1.8, 'neck/head 1.45+')]:
            sel = (zc >= lo_) & (zc < hi_)
            bands[name] = dict(total_m2=round(float(area[sel].sum()), 3), missing_m2=round(float(area[sel & miss].sum()), 3))
        rep[ch]['missing_by_height'] = bands
        colors = np.where(tri_ok[:, None], np.array([[205, 190, 180]]), np.array([[220, 40, 40]]))
        b = (Q[np.unique(NT)].min(0), Q[np.unique(NT)].max(0))
        render(Q, NT, colors, f'{ch}_nude_vs_suit_front.png', 'front', b)
        render(Q, NT, colors, f'{ch}_nude_vs_suit_back.png', 'back', b)
    print(json.dumps(rep, indent=1, ensure_ascii=False))
    json.dump(rep, open(os.path.join(OUT, 'nude_vs_suit.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)


if __name__ == '__main__':
    main()
