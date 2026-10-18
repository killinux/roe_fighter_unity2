"""Read-only illustration: static T-pose previews of the proposed burst stages.
Grey-pink = the suit's own skin, orange-pink = body patches from the family nude base (where the suit has no skin),
colours = outfit groups still on.  Not a game render; flat shading, painter's algorithm."""
import os, sys, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um
import fbx_bin
from outfit_pieces import CASES, components, smr_bones
from stage_groups import RULES
from nude_vs_suit import CASES as NCASES
from surf_test import sample
from holes_per_piece import nn_index

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'img')
STAGE = {
    'a08': {'长裙片': 1, '胯甲': 1, '腰后片': 1, '肩甲+臂链': 1, '裙链': 1, 'other': 1, '头冠': 9,
            '护手/前臂甲': 2, '护胫': 2, '项圈': 2, '胸甲': 3, '护裆': 3, '凉鞋': 9},
    'g04': {'腰部大件': 1, '后片': 1, '左飘带': 1, '前片': 1, '裙饰/腰饰': 1, 'other': 1,
            '上衣': 2, '项圈': 2, '腰带': 2, '手套': 2, '内裤': 3, '头饰': 9, '鞋': 9},
}
SKIN = {'a08': (r"E:/code/othercode/roe_fighter_unity/Assets/ROE/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset", 0),
        'g04': (r"E:/code/othercode/roe_fighter_unity/Assets/ROE/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset", 1)}
COL = {1: (90, 160, 220), 2: (120, 200, 120), 3: (230, 150, 60), 9: (150, 120, 190)}


def tri_list(P, T, col):
    n = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    cen = P[T].mean(1)
    return [(cen[i, 1], P[T[i]], col if isinstance(col, tuple) else col[i], 0.45 + 0.55 * abs(n[i, 1])) for i in range(len(T))]


def draw(polys, W, H, lo, hi, view):
    s = min((W - 20) / (hi[0] - lo[0]), (H - 20) / (hi[2] - lo[2]))
    img = Image.new('RGB', (W, H), (240, 240, 244))
    dr = ImageDraw.Draw(img)
    sign = 1 if view == 'front' else -1
    for depth, pts3, col, shade in sorted(polys, key=lambda p: -sign * p[0]):
        xs = (pts3[:, 0] - lo[0]) * s
        if view != 'front':
            xs = (hi[0] - lo[0]) * s - xs
        dr.polygon([(10 + xs[k], H - 10 - (pts3[k, 2] - lo[2]) * s) for k in range(3)], fill=tuple(int(c * shade) for c in col))
    return img


def main():
    try:
        font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
    except Exception:
        font = None
    for ch in ('a08', 'g04'):
        # outfit triangles with group + stage
        outfit = []      # (P, T, stage per tri, piece label per tri)
        allP, allStage = [], []
        for c2, path, si, prefab, rname in CASES:
            if c2 != ch:
                continue
            m = um.load(path)
            T = um.submesh_tris(m, si, 2)
            lab = components(T, um.weld(m['pos'], 1e-4))
            bones = smr_bones(prefab, rname)
            st = np.zeros(len(T), int)
            for l in np.unique(lab):
                sel = np.where(lab == l)[0]
                vs = np.unique(T[sel])
                acc = {}
                for k in range(4):
                    for b_, ww in zip(m['bi'][vs, k], m['bw'][vs, k]):
                        if ww > 0:
                            acc[int(b_)] = acc.get(int(b_), 0) + float(ww)
                top = bones[max(acc.items(), key=lambda x: x[1])[0]]
                z = (float(m['pos'][vs, 2].min()), float(m['pos'][vs, 2].max()))
                name = next((g for g, f in RULES[ch] if f(top, z)), 'other')
                st[sel] = STAGE[ch].get(name, 1)
                allP.append(m['pos'][vs])
                allStage.append(np.full(len(vs), STAGE[ch].get(name, 1)))
            outfit.append((m['pos'], T, st))
        allP = np.concatenate(allP)
        allStage = np.concatenate(allStage)
        # nude patches: missing triangles + the stage of the nearest outfit piece covering them
        cfg = NCASES[ch]
        root, _ = fbx_bin.load(cfg['nude'])
        g = [x for x in fbx_bin.geometries(root) if x['model'] == cfg['body_model']][0]
        NT = np.array([p for p, mm in zip(g['polys'], g['mat_per_poly']) if mm == 0 and len(p) == 3])
        NV = g['verts'] * np.array([-1, 1, 1])
        S = np.concatenate([sample(um.load(p)['pos'], um.submesh_tris(um.load(p), si, 2)) for p, si in cfg['suit']])
        used = np.unique(NT)
        _, dv = nn_index(NV[used], S, cell=0.01)
        dfull = np.full(len(NV), np.inf)
        dfull[used] = dv
        miss = ~((dfull[NT] < 0.003).all(axis=1))
        MT = NT[miss]
        idx, dist = nn_index(NV[MT].mean(1), allP, cell=0.02)
        patch_stage = np.where((idx >= 0) & (dist < 0.04), allStage[np.maximum(idx, 0)], 0)   # 0 = not under any piece (always show)
        skinP, si0 = SKIN[ch]
        sm = um.load(skinP)
        ST = um.submesh_tris(sm, si0, 2)
        lo = np.minimum(sm['pos'][np.unique(ST)].min(0), allP.min(0))
        hi = np.maximum(sm['pos'][np.unique(ST)].max(0), allP.max(0))
        tiles = []
        for stage in (0, 1, 2, 3):
            polys = tri_list(sm['pos'], ST, (205, 190, 182))
            show = (patch_stage > 0) & (patch_stage <= stage)
            if show.any():
                polys += tri_list(NV, MT[show], (240, 150, 120))
            for P, T, st in outfit:
                keep = (st > stage)
                if keep.any():
                    cols = [COL[min(int(x), 9)] if int(x) in COL else (120, 120, 120) for x in st[keep]]
                    polys += tri_list(P, T[keep], cols)
            for view in ('front', 'back'):
                tiles.append((stage, view, draw(polys, 300, 600, lo, hi, view)))
        sheet = Image.new('RGB', (300 * 8, 650), (255, 255, 255))
        dr = ImageDraw.Draw(sheet)
        for i, (stage, view, im) in enumerate(tiles):
            sheet.paste(im, (300 * i, 40))
            dr.text((300 * i + 10, 8), f'{"原样" if stage == 0 else "第" + "一二三"[stage - 1] + "段后"} · {"正" if view == "front" else "背"}', fill=(0, 0, 0), font=font)
        sheet.save(os.path.join(OUT, f'{ch}_stages.png'))
        print(ch, 'patch area by stage (cm2):', {s: round(float(um.tri_area(NV, MT[patch_stage == s]).sum() * 1e4)) for s in (0, 1, 2, 3, 9)})


if __name__ == '__main__':
    main()
