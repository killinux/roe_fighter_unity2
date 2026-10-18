import os, sys, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um, fbx_bin
from nude_vs_suit import CASES as NC
from surf_test import sample
from holes_per_piece import nn_index
from outfit_pieces import CASES
for ch in ('a08', 'g04'):
    cfg = NC[ch]
    root, _ = fbx_bin.load(cfg['nude'])
    g = [x for x in fbx_bin.geometries(root) if x['model'] == cfg['body_model']][0]
    NT = np.array([p for p, m in zip(g['polys'], g['mat_per_poly']) if m == 0 and len(p) == 3])
    NV = g['verts'] * np.array([-1, 1, 1])
    S = np.concatenate([sample(um.load(p)['pos'], um.submesh_tris(um.load(p), si, 2)) for p, si in cfg['suit']])
    used = np.unique(NT); _, dv = nn_index(NV[used], S, cell=0.01)
    d = np.full(len(NV), np.inf); d[used] = dv
    miss = ~((d[NT] < 0.003).all(axis=1)); MT = NT[miss]
    allP = np.concatenate([um.load(p)['pos'][np.unique(um.submesh_tris(um.load(p), si, 2))] for c2, p, si, _, _ in CASES if c2 == ch])
    # head mesh too
    head = {'a08': r"E:/code/othercode/roe_fighter_unity/Assets/ROE/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_head.asset",
            'g04': r"E:/code/othercode/roe_fighter_unity/Assets/ROE/g04/chara_armor_pc_g04_hd_ld_hd/pc_g03_hd_head.asset"}[ch]
    hm = um.load(head); HP = hm['pos']
    cen = NV[MT].mean(1)
    _, dout = nn_index(cen, allP, cell=0.02)
    unc = dout > 0.04
    _, dh = nn_index(cen[unc], HP, cell=0.02)
    A = um.tri_area(NV, MT)
    z = cen[unc][:, 2]
    print(ch, 'uncovered %.0f cm2;' % (A[unc].sum() * 1e4), 'z range %.2f-%.2f;' % (z.min(), z.max()),
          'within 2 cm of head mesh: %.0f cm2' % (A[unc][dh < 0.02].sum() * 1e4),
          '| by z band:', {f'{lo:.1f}': round(float(A[unc][(z >= lo) & (z < lo + 0.2)].sum() * 1e4)) for lo in np.arange(0, 1.8, 0.2) if A[unc][(z >= lo) & (z < lo + 0.2)].sum() > 0})
