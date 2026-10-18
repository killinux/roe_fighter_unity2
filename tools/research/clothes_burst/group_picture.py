"""Read-only: picture of the proposed burst groups (one colour per group), front and back."""
import os, sys, re, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um
from outfit_pieces import CASES, components, smr_bones
from stage_groups import RULES
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'img')
PAL = [(230, 25, 75), (60, 180, 75), (255, 225, 25), (0, 130, 200), (245, 130, 48), (145, 30, 180), (70, 240, 240), (240, 50, 230),
       (210, 245, 60), (250, 190, 212), (0, 128, 128), (220, 190, 255), (170, 110, 40), (128, 0, 0), (170, 255, 195), (128, 128, 0)]
SKIN = {'a08': (r"E:/code/othercode/roe_fighter_unity/Assets/ROE/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset", 0),
        'g04': (r"E:/code/othercode/roe_fighter_unity/Assets/ROE/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset", 1)}
def draw(polys, W, H, lo, hi, view):
    s = min((W - 20) / (hi[0] - lo[0]), (H - 60) / (hi[2] - lo[2]))
    img = Image.new('RGB', (W, H), (240, 240, 244)); dr = ImageDraw.Draw(img)
    sign = 1 if view == 'front' else -1
    polys = sorted(polys, key=lambda p: -sign * p[0])
    for depth, pts3, col, shade in polys:
        xs = (pts3[:, 0] - lo[0]) * s
        if view != 'front': xs = (hi[0] - lo[0]) * s - xs
        pts = [(10 + xs[k], H - 10 - (pts3[k, 2] - lo[2]) * s) for k in range(3)]
        dr.polygon(pts, fill=tuple(int(c * shade) for c in col))
    return img
for ch in ('a08', 'g04'):
    polys = []
    legend = {}
    P0, si0 = SKIN[ch]; m0 = um.load(P0); T0 = um.submesh_tris(m0, si0, 2)
    def add(P, T, colors):
        n = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]]); n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
        cen = P[T].mean(1)
        for i in range(len(T)):
            polys.append((cen[i, 1], P[T[i]], colors[i], 0.45 + 0.55 * abs(n[i, 1])))
    add(m0['pos'], T0, [(200, 200, 200)] * len(T0))
    for c2, path, si, prefab, rname in CASES:
        if c2 != ch: continue
        m = um.load(path); T = um.submesh_tris(m, si, 2)
        lab = components(T, um.weld(m['pos'], 1e-4)); bones = smr_bones(prefab, rname)
        cols = [None] * len(T)
        for l in np.unique(lab):
            sel = np.where(lab == l)[0]; vs = np.unique(T[sel]); acc = {}
            for k in range(4):
                for b_, ww in zip(m['bi'][vs, k], m['bw'][vs, k]):
                    if ww > 0: acc[int(b_)] = acc.get(int(b_), 0) + float(ww)
            top = bones[max(acc.items(), key=lambda x: x[1])[0]]
            z = (float(m['pos'][vs, 2].min()), float(m['pos'][vs, 2].max()))
            name = next((g for g, f in RULES[ch] if f(top, z)), 'other')
            if name not in legend: legend[name] = PAL[len(legend) % len(PAL)]
            for i in sel: cols[i] = legend[name]
        add(m['pos'], T, cols)
    allp = np.concatenate([p[1] for p in polys]); lo, hi = allp.min(0), allp.max(0)
    a = draw(polys, 460, 900, lo, hi, 'front'); b = draw(polys, 460, 900, lo, hi, 'back')
    sheet = Image.new('RGB', (460 * 2 + 300, 900), (255, 255, 255)); sheet.paste(a, (0, 0)); sheet.paste(b, (460, 0))
    dr = ImageDraw.Draw(sheet)
    try: font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
    except Exception: font = None
    for i, (k, c) in enumerate(legend.items()):
        dr.rectangle([940, 40 + i * 34, 966, 64 + i * 34], fill=c); dr.text((975, 40 + i * 34), k, fill=(0, 0, 0), font=font)
    sheet.save(os.path.join(OUT, f'{ch}_groups.png'))
    print(ch, legend)
