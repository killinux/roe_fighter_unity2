import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um
from outfit_pieces import components
m = um.load(r"E:/code/othercode/roe_fighter_unity/Assets/ROE/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body2.asset")
T = um.submesh_tris(m, 0, 2); P = m['pos']
lab = components(T, um.weld(P, 1e-4))
areas = {l: um.tri_area(P, T[lab == l]).sum() for l in np.unique(lab)}
big = max(areas, key=areas.get)
TT = T[lab == big]
c = P[TT].mean(1)
n = np.cross(P[TT[:, 1]] - P[TT[:, 0]], P[TT[:, 2]] - P[TT[:, 0]]); n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
cell = 0.004
key = np.floor(c / cell).astype(np.int64)
d = {}
for i, k in enumerate(map(tuple, key)): d.setdefault(k, []).append(i)
paired = 0
for i in range(len(TT)):
    k = key[i]; found = False
    for a in (-1, 0, 1):
        for b in (-1, 0, 1):
            for cc in (-1, 0, 1):
                for j in d.get((k[0]+a, k[1]+b, k[2]+cc), []):
                    if j != i and np.linalg.norm(c[j]-c[i]) < 0.004 and np.dot(n[i], n[j]) < -0.8:
                        found = True; break
                if found: break
            if found: break
        if found: break
    paired += found
print('largest piece tris', len(TT), 'area cm2 %.0f' % (areas[big]*1e4), 'tris with an opposite-facing twin within 4 mm: %.1f%%' % (100*paired/len(TT)))
