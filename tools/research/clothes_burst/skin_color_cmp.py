"""Read-only: does the nude base's body texture match the suit's skin texture where both surfaces exist?
For suit-skin vertices that lie on the nude surface (<= 2 mm from a nude vertex), sample the suit atlas at
the suit UV and the nude body texture at the nude UV of the nearest nude vertex, and compare colours."""
import os, sys, json
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um
import fbx_bin
from nude_vs_suit import hash_nn

A = r"E:/code/othercode/roe_fighter_unity/Assets/ROE"
CASES = {
    'a08': dict(nude='D:/roe_exports/a01/pc_a01_nk_bs/FBX_GameObjects/pc_a01_nk_bs/pc_a01_nk_bs.fbx', model='pc_a01_nk_body',
                nude_tex='D:/roe_exports/a01/_textures/pc_a01_nk_body_rgbx_Albedo.png',
                suit=(A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset', 0),
                suit_tex=A + '/a08/chara_tex_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1_rgbx_Albedo.png'),
    'g04': dict(nude='D:/roe_exports/g01/pc_g01_nk_bs/FBX_GameObjects/pc_g01_nk_bs/pc_g01_nk_bs.fbx', model='pc_g01_nk_body',
                nude_tex='D:/roe_exports/g01/_textures/pc_g01_nk_body_rgbx_Albedo.png',
                suit=(A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset', 1),
                suit_tex=A + '/g04/chara_tex_armor_pc_g04_hd_ld_hd/pc_g04_hd_body_rgbx_Abedo.png'),
}


def nude_uvs(g_node_root, model):
    objs = g_node_root.first('Objects')
    for g in objs.children:
        if g.name == 'Geometry' and len(g.props) > 2 and g.props[2] == 'Mesh':
            # match by vertex count of the requested model via geometries()
            pass
    return None


def geometry_with_uv(path, model):
    root, _ = fbx_bin.load(path)
    geos = fbx_bin.geometries(root)
    objs = root.first('Objects')
    gnodes = [c for c in objs.children if c.name == 'Geometry' and len(c.props) > 2 and c.props[2] == 'Mesh']
    for gi, gd in enumerate(geos):
        if gd['model'] != model:
            continue
        gn = gnodes[gi]
        luv = gn.first('LayerElementUV')
        uv = luv.first('UV').props[0].reshape(-1, 2)
        mapping = luv.first('MappingInformationType').props[0]
        if mapping == 'ByVertice':
            cp_uv = uv.copy()
        else:
            uvi = luv.first('UVIndex').props[0]
            pvi = gn.first('PolygonVertexIndex').props[0]
            vid = np.where(pvi < 0, ~pvi, pvi)
            cp_uv = np.full((len(gd['verts']), 2), np.nan)
            seen = np.zeros(len(gd['verts']), bool)
            for k in range(len(vid)):
                v = vid[k]
                if not seen[v]:
                    cp_uv[v] = uv[uvi[k]]
                    seen[v] = True
        mat0 = set()
        for p, m in zip(gd['polys'], gd['mat_per_poly']):
            if m == 0:
                mat0.update(p)
        return gd['verts'], cp_uv, np.array(sorted(mat0))
    raise RuntimeError('model not found')


def sample(img, uv, flip_v=True):
    H, W = img.shape[:2]
    u = np.mod(uv[:, 0], 1.0)
    v = np.mod(uv[:, 1], 1.0)
    x = np.clip((u * W).astype(int), 0, W - 1)
    y = np.clip(((1 - v) if flip_v else v) * H, 0, H - 1).astype(int)
    return img[y, x, :3].astype(float)


def main():
    rep = {}
    for ch, c in CASES.items():
        NV, NUV, body_ids = geometry_with_uv(c['nude'], c['model'])
        NV = NV * np.array([-1, 1, 1])
        m = um.load(c['suit'][0])
        T = um.submesh_tris(m, c['suit'][1], 2)
        sv_ids = np.unique(T)
        SP = m['pos'][sv_ids]
        SUV = m['uv0'][sv_ids]
        # nearest nude body vertex for each suit skin vertex (indices)
        P = NV[body_ids]
        cell = 0.004
        key = np.floor(P / cell).astype(np.int64)
        d = {}
        for i, k in enumerate(map(tuple, key)):
            d.setdefault(k, []).append(i)
        near = np.full(len(SP), -1)
        dist = np.full(len(SP), np.inf)
        for i, q in enumerate(SP):
            k = np.floor(q / cell).astype(np.int64)
            for a in (-1, 0, 1):
                for b in (-1, 0, 1):
                    for cc in (-1, 0, 1):
                        lst = d.get((k[0] + a, k[1] + b, k[2] + cc))
                        if lst:
                            dd = np.linalg.norm(P[lst] - q, axis=1)
                            j = int(np.argmin(dd))
                            if dd[j] < dist[i]:
                                dist[i], near[i] = dd[j], lst[j]
        ok = dist < 0.002
        imgS = np.asarray(Image.open(c['suit_tex']).convert('RGB'))
        imgN = np.asarray(Image.open(c['nude_tex']).convert('RGB'))
        cs = sample(imgS, SUV[ok])
        cn = sample(imgN, NUV[body_ids[near[ok]]])
        diff = np.abs(cs - cn)
        rep[ch] = dict(matched_suit_skin_verts=int(ok.sum()), of=int(len(SP)),
                       suit_mean_rgb=cs.mean(0).round(1).tolist(), nude_mean_rgb=cn.mean(0).round(1).tolist(),
                       abs_diff_mean=diff.mean(0).round(1).tolist(), abs_diff_p90=np.percentile(diff.max(1), 90).round(1),
                       frac_maxdiff_under_12=round(float((diff.max(1) < 12).mean()), 3))
    print(json.dumps(rep, indent=1))


if __name__ == '__main__':
    main()
