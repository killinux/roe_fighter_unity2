import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fbx_bin
from bones_cmp import clusters
R = 'D:/roe_exports'
suits = {'a': 'D:/roe_exports/a08/pc_a08_hd/FBX_GameObjects/pc_a08_hd/pc_a08_hd.fbx', 'g': 'D:/roe_exports/g04/pc_g04_hd/FBX_GameObjects/pc_g04_hd/pc_g04_hd.fbx'}
comps = [('a', 'a01/Bra_obj001'), ('a', 'a01/Panties_obj001'), ('a', 'a01/Underwear_obj001'), ('a', 'a01/Swimbra_obj001'),
         ('g', 'g01/Underwear_obj001'), ('g', 'g01/SportBra_obj001'), ('g', 'g01/Swimbra_obj001'), ('g', 'g01/StockingsBroken_obj001'), ('g', 'g01/Stockings_obj001')]
sl = {k: set(clusters(v)[1]) for k, v in suits.items()}
for fam, c in comps:
    name = c.split('/')[1]
    f = f'{R}/{c}/FBX_GameObjects/{name}/{name}.fbx'
    if not os.path.exists(f):
        print('missing', f); continue
    root, _ = fbx_bin.load(f)
    gs = fbx_bin.geometries(root)
    cl, _ = clusters(f)
    miss = sorted(set(cl) - sl[fam])
    for g in gs:
        V = g['verts']
        print(f'{c:32s} verts {len(V):6d} polys {len(g["polys"]):6d} z {V[:,2].min():.2f}-{V[:,2].max():.2f}  bones {len(cl):3d}  not in {fam}-suit rig: {miss[:8]}{"..." if len(miss) > 8 else ""}')
