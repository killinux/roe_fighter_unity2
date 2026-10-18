import os, sys, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um, fbx_bin
from nude_vs_suit import CASES, hash_nn
from surf_test import sample
for ch, cfg in CASES.items():
    root, _ = fbx_bin.load(cfg['nude'])
    g = [x for x in fbx_bin.geometries(root) if x['model'] == cfg['body_model']][0]
    NT = np.array([p for p, m in zip(g['polys'], g['mat_per_poly']) if m == 0 and len(p) == 3])
    NV = g['verts'] * np.array([-1, 1, 1])
    Ns = sample(NV, NT)
    for path, si in cfg['suit']:
        m = um.load(path); T = um.submesh_tris(m, si, 2)
        sv = m['pos'][np.unique(T)]
        d = hash_nn(sv, Ns, cell=0.02)
        far = sv[(d > 0.006)]
        print(ch, os.path.basename(path), si, 'verts', len(sv), 'far>6mm', len(far))
        if len(far):
            # cluster by coarse region
            zb = np.round(far[:, 2], 1); xb = np.sign(far[:, 0]); yb = np.sign(far[:, 1])
            keys, cnt = np.unique(np.stack([zb, xb, yb], 1), axis=0, return_counts=True)
            for k, c in sorted(zip(map(tuple, keys), cnt), key=lambda x: -x[1])[:10]:
                print('   z~%.1f x%+d y%+d : %d verts, max %.1f mm' % (k[0], k[1], k[2], c, 1000 * d[(d > 0.006)][(zb == k[0]) & (xb == k[1]) & (yb == k[2])].max()))
