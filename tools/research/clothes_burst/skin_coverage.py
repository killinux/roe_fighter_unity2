"""Read-only analysis: is the suit's skin submesh a full body or only the visible skin?
Decodes the ripped Unity meshes (a08, g04), prints per-submesh stats, measures how much of each outfit
submesh has skin underneath (along the inward normal, <= 4 cm), and renders front/back pictures.
Output: out/clothes_burst/*.png"""
import os, sys, json
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(__file__))
import unity_mesh as um

A = r"E:/code/othercode/roe_fighter_unity/Assets/ROE"
OUT = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'img')
os.makedirs(OUT, exist_ok=True)

CHARS = {
    'a08': {
        'body1': (A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset', ['skin', 'body1(outfit)']),
        'body2': (A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body2.asset', ['body2(outfit)']),
        'head': (A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_head.asset', ['face', 'tooth', 'eyes', 'eyebrow', 'tears']),
    },
    'g04': {
        'body': (A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset', ['g_nk_face(neck)', 'skin', 'body(outfit)']),
        'head': (A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g03_hd_head.asset', ['face', 'tooth', 'eyebrow', 'eyes', 'tears']),
    },
}


def voxel_set(P, T, cell=0.01, step=0.004):
    """sample triangles densely, return set of occupied voxel keys"""
    keys = set()
    a, b, c = P[T[:, 0]], P[T[:, 1]], P[T[:, 2]]
    L = np.maximum(np.linalg.norm(b - a, axis=1), np.linalg.norm(c - a, axis=1))
    n = np.clip(np.ceil(L / step).astype(int), 1, 40)
    for k in np.unique(n):
        sel = n == k
        u, v = np.meshgrid(np.arange(k + 1), np.arange(k + 1))
        m = (u + v) <= k
        u = u[m] / k
        v = v[m] / k
        pts = a[sel, None, :] + (b[sel] - a[sel])[:, None, :] * u[None, :, None] + (c[sel] - a[sel])[:, None, :] * v[None, :, None]
        q = np.floor(pts.reshape(-1, 3) / cell).astype(np.int64)
        keys.update(map(tuple, np.unique(q, axis=0)))
    return keys


def covered_fraction(P, N, T_out, skin_keys, cell=0.01, max_d=0.04):
    """fraction (by area) of outfit triangles that have skin voxels within max_d along the inward normal"""
    cen = P[T_out].mean(axis=1)
    nrm = N[T_out].mean(axis=1)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
    area = um.tri_area(P, T_out)
    hit = np.zeros(len(T_out), bool)
    for d in np.arange(0.0, max_d + 1e-9, 0.005):
        q = np.floor((cen - nrm * d) / cell).astype(np.int64)
        hit |= np.array([tuple(x) in skin_keys for x in q])
    return float(area[hit].sum() / max(area.sum(), 1e-12)), hit


def render(parts, fname, view='front', size=(520, 1000), bounds=None):
    """parts: list of (P, N, T, rgb, alpha). Orthographic, mesh space x=right, z=up, y=depth."""
    allP = np.concatenate([p[0][np.unique(p[2])] for p in parts])
    lo, hi = (allP.min(0), allP.max(0)) if bounds is None else bounds
    W, H = size
    s = min((W - 20) / (hi[0] - lo[0]), (H - 20) / (hi[2] - lo[2]))
    img = Image.new('RGB', size, (235, 235, 240))
    dr = ImageDraw.Draw(img, 'RGBA')
    polys = []
    sign = 1 if view == 'front' else -1
    for P, N, T, rgb, alpha in parts:
        cen = P[T].mean(axis=1)
        nrm = N[T].mean(axis=1)
        nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
        depth = sign * cen[:, 1]          # bigger = farther (view along +y for front... decided empirically)
        shade = np.abs(nrm[:, 1]) * 0.75 + 0.25
        for i in range(len(T)):
            polys.append((depth[i], T[i], P, shade[i], rgb, alpha))
    polys.sort(key=lambda x: -x[0])
    for depth, t, P, sh, rgb, alpha in polys:
        xs = (P[t, 0] - lo[0]) * s * (1 if view == 'front' else -1)
        if view != 'front':
            xs = xs + (hi[0] - lo[0]) * s
        pts = [(10 + xs[k], H - 10 - (P[t[k], 2] - lo[2]) * s) for k in range(3)]
        col = tuple(int(c * sh) for c in rgb) + (alpha,)
        dr.polygon(pts, fill=col)
    img.save(os.path.join(OUT, fname))
    return (lo, hi)


def main():
    report = {}
    for ch, meshes in CHARS.items():
        loaded = {k: um.load(v[0]) for k, v in meshes.items()}
        rep = {}
        for k, m in loaded.items():
            isz = 2 if 'u2' in str(m['idx'].dtype) or m['idx'].max() < 65536 else 4
            m['isz'] = 2  # all these meshes use 16-bit indices (m_IndexFormat 0)
            for si, label in enumerate(meshes[k][1]):
                T = um.submesh_tris(m, si, 2)
                P = m['pos']
                area = um.tri_area(P, T).sum()
                nb, L, loops = um.boundary_stats(P, T)
                z = P[np.unique(T)][:, 2]
                rep[f'{k}[{si}] {label}'] = dict(tris=int(len(T)), verts=int(len(np.unique(T))), area_m2=round(float(area), 4),
                                                 boundary_edges=int(nb), boundary_len_m=round(float(L), 2), boundary_loops=int(loops),
                                                 z_min=round(float(z.min()), 3), z_max=round(float(z.max()), 3))
        # skin under outfit
        if ch == 'a08':
            b1 = loaded['body1']
            skinP, skinN, skinT = b1['pos'], b1['nrm'], um.submesh_tris(b1, 0, 2)
            outs = [('body1(outfit)', b1, um.submesh_tris(b1, 1, 2)), ('body2(outfit)', loaded['body2'], um.submesh_tris(loaded['body2'], 0, 2))]
        else:
            bd = loaded['body']
            skinP, skinN, skinT = bd['pos'], bd['nrm'], um.submesh_tris(bd, 1, 2)
            outs = [('body(outfit)', bd, um.submesh_tris(bd, 2, 2))]
        keys = voxel_set(skinP, skinT)
        for label, m, T in outs:
            frac, hit = covered_fraction(m['pos'], m['nrm'], T, keys)
            rep[f'outfit area with skin <=4cm beneath: {label}'] = round(frac, 3)
            m.setdefault('hits', {})[label] = hit
        report[ch] = rep
        # pictures
        parts_skin = [(skinP, skinN, skinT, (232, 190, 170), 255)]
        lo, hi = None, None
        allparts = []
        for label, m, T in outs:
            allparts.append((m['pos'], m['nrm'], T, (80, 110, 200), 150))
        b = render(parts_skin + allparts, f'{ch}_skin_plus_outfit_front.png', 'front')
        render(parts_skin + allparts, f'{ch}_skin_plus_outfit_back.png', 'back', bounds=b)
        render(parts_skin, f'{ch}_skin_only_front.png', 'front', bounds=b)
        render(parts_skin, f'{ch}_skin_only_back.png', 'back', bounds=b)
    print(json.dumps(report, indent=1, ensure_ascii=False))
    json.dump(report, open(os.path.join(OUT, 'skin_coverage.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)


if __name__ == '__main__':
    main()
