"""Compare skeletons (FBX skin clusters: bone name + bind matrix TransformLink) of nude base vs suit FBX."""
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fbx_bin

def clusters(path):
    root, _ = fbx_bin.load(path)
    objs = root.first('Objects'); conns = root.first('Connections')
    models = {c.props[0]: c.props[1].split('\x00')[0] for c in objs.children if c.name == 'Model'}
    deformers = {c.props[0]: c for c in objs.children if c.name == 'Deformer' and len(c.props) > 2 and c.props[2] == 'Cluster'}
    out = {}
    for ch, par in [(c.props[1], c.props[2]) for c in conns.children if c.name == 'C']:
        if ch in models and par in deformers:
            tl = deformers[par].first('TransformLink')
            if tl is not None:
                out[models[ch]] = np.array(tl.props[0]).reshape(4, 4)
    limbs = [c.props[1].split('\x00')[0] for c in objs.children if c.name == 'Model' and len(c.props) > 2 and c.props[2] == 'LimbNode']
    return out, limbs

nude = {'a08': 'D:/roe_exports/a01/pc_a01_nk_bs/FBX_GameObjects/pc_a01_nk_bs/pc_a01_nk_bs.fbx',
        'g04': 'D:/roe_exports/g01/pc_g01_nk_bs/FBX_GameObjects/pc_g01_nk_bs/pc_g01_nk_bs.fbx'}
suit = {'a08': 'D:/roe_exports/a08/pc_a08_hd/FBX_GameObjects/pc_a08_hd/pc_a08_hd.fbx',
        'g04': 'D:/roe_exports/g04/pc_g04_hd/FBX_GameObjects/pc_g04_hd/pc_g04_hd.fbx'}
for ch in ('a08', 'g04'):
    nc, nl = clusters(nude[ch]); sc, sl = clusters(suit[ch])
    shared = sorted(set(nc) & set(sc))
    only_n = sorted(set(nc) - set(sl))
    diffs = []
    for b in shared:
        d = np.linalg.norm(nc[b][3, :3] - sc[b][3, :3])  # translation row (FBX matrices are row-major with translation in last row)
        diffs.append((d, b))
    diffs.sort(reverse=True)
    print('==', ch, 'nude skin bones', len(nc), 'suit skin bones', len(sc), 'suit limb nodes', len(sl), 'shared skin bones', len(shared))
    print('   nude skin bones missing from suit skeleton:', len(only_n), only_n[:25])
    print('   bind position diff (m) of shared bones: median %.4f, max %.4f (%s)' % (np.median([d for d, _ in diffs]), diffs[0][0], diffs[0][1]))
    print('   top diffs:', [(b, round(float(d), 4)) for d, b in diffs[:8]])
