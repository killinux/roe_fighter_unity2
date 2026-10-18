"""Read-only: propose burst groups from the outfit pieces (group = rule on the dominant bone + height),
with triangles, area and the skin hole each group would expose (from holes_per_piece.json logic)."""
import os, re, sys, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um
from outfit_pieces import CASES, components, smr_bones

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'img')

RULES = {
    'a08': [
        ('头冠', lambda b, z: b == 'Bip001 Head'),
        ('肩甲+臂链', lambda b, z: b.startswith('Scapular') or b.startswith('chain_arm')),
        ('长裙片', lambda b, z: re.match(r'Skirt_[LR]_(0[1-9]|1\d)', b) is not None),
        ('胯甲', lambda b, z: re.match(r'Skirt_[LR]_00', b) is not None),
        ('裙链', lambda b, z: b.startswith('Skirt_Chain')),
        ('腰后片', lambda b, z: b in ('Bip001 Pelvis', 'Bip001 Spine') and z[1] > 1.02 and z[0] < 0.8 or b.startswith('Skirt_M')),
        ('护胫', lambda b, z: 'CalfTwist' in b),
        ('凉鞋', lambda b, z: b.endswith('Foot') or 'Toe' in b),
        ('护手/前臂甲', lambda b, z: 'ForeTwist' in b or b.endswith('Hand') or 'Forearm' in b),
        ('胸甲', lambda b, z: b in ('Bip001 Spine1',) or 'Breast' in b),
        ('项圈', lambda b, z: b in ('Bip001 Spine2', 'Collar') or 'Clavicle' in b),
        ('护裆', lambda b, z: b == 'Bip001 Pelvis' and z[1] <= 1.02),
    ],
    'g04': [
        ('头饰', lambda b, z: b == 'Bip001 Head' or b.startswith('dec_hari')),
        ('后片', lambda b, z: b.startswith('Skirt_Back')),
        ('左飘带', lambda b, z: b.startswith('Skirt_Left')),
        ('前片', lambda b, z: b.startswith('Skirt_front')),
        ('裙饰/腰饰', lambda b, z: b.startswith('Skirt_Dec') or b.startswith('Skirt_dec') or b.startswith('dec_wrist')),
        ('腰部大件', lambda b, z: b == 'Bip001 Pelvis' and z[0] < 0.5),
        ('内裤', lambda b, z: b == 'Bip001 Pelvis' and z[0] >= 0.5),
        ('鞋', lambda b, z: b.endswith('Foot') or 'Toe' in b or 'CalfTwist' in b),
        ('手套', lambda b, z: b.endswith('Hand') or 'ForeTwist' in b or 'Finger' in b),
        ('上衣', lambda b, z: b in ('Bip001 Spine2', 'Bip001 Spine1', 'Bip001 Spine') and z[1] < 1.45 and z[1] - z[0] > 0.15),
        ('腰带', lambda b, z: b in ('Bip001 Spine2', 'Bip001 Spine1') and z[1] - z[0] <= 0.15 and z[1] < 1.3),
        ('项圈', lambda b, z: b in ('Bip001 Spine2', 'Bip001 Neck') or b.startswith('shoulder')),
    ],
}


def main():
    holes = json.load(open(os.path.join(OUT, 'holes_per_piece.json'), encoding='utf-8'))
    res = {}
    for ch in ('a08', 'g04'):
        groups = {}
        for c2, path, si, prefab, rname in CASES:
            if c2 != ch:
                continue
            m = um.load(path)
            T = um.submesh_tris(m, si, 2)
            lab = components(T, um.weld(m['pos'], 1e-4))
            bones = smr_bones(prefab, rname)
            area = um.tri_area(m['pos'], T)
            for l in np.unique(lab):
                sel = lab == l
                vs = np.unique(T[sel])
                acc = {}
                for k in range(4):
                    for b_, ww in zip(m['bi'][vs, k], m['bw'][vs, k]):
                        if ww > 0:
                            acc[int(b_)] = acc.get(int(b_), 0) + float(ww)
                top = bones[max(acc.items(), key=lambda x: x[1])[0]]
                z = (float(m['pos'][vs, 2].min()), float(m['pos'][vs, 2].max()))
                name = next((g for g, f in RULES[ch] if f(top, z)), '其他(%s)' % top)
                key = f'{name}'
                gi = groups.setdefault(key, dict(pieces=0, tris=0, area_cm2=0.0, renderers=set()))
                gi['pieces'] += 1
                gi['tris'] += int(sel.sum())
                gi['area_cm2'] += float(area[sel].sum() * 1e4)
                gi['renderers'].add(rname.replace('pc_', '').replace('_hd_', ' '))
                # hole under this piece
                lbl = f'{rname}: {top} z{z[0]:.2f}-{z[1]:.2f} ({int(sel.sum())} tris)'
                gi.setdefault('hole_cm2', 0)
                for k2, v2 in holes[ch]['by_piece']:
                    if k2 == lbl:
                        gi['hole_cm2'] += v2
        rows = sorted(groups.items(), key=lambda x: -x[1]['area_cm2'])
        print('==', ch)
        for k, v in rows:
            print('  %-14s pieces %4d  tris %6d  area %7.0f cm2  hole %5d cm2  %s' % (k, v['pieces'], v['tris'], v['area_cm2'], v['hole_cm2'], ','.join(sorted(v['renderers']))))
        res[ch] = {k: dict(v, renderers=sorted(v['renderers'])) for k, v in rows}
    json.dump(res, open(os.path.join(OUT, 'stage_groups.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)


if __name__ == '__main__':
    main()
