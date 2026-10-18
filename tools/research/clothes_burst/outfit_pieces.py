"""Read-only: split each outfit submesh of a08 / g04 into connected pieces (welded by position) and
describe them: triangles, area, height range, dominant bones (by skin weight).  Also: which pieces
lie over the holes in the skin (nude-base triangles missing from the suit skin) -> those pieces, if removed,
would show a hole unless a body is put underneath."""
import os, re, sys, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_mesh as um

A = r"E:/code/othercode/roe_fighter_unity/Assets/ROE"
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'img')


def smr_bones(prefab, renderer):
    txt = open(prefab, encoding='utf-8', errors='ignore').read()
    docs = re.split(r'^--- !u!(\d+) &(-?\d+).*$', txt, flags=re.M)
    objs = {docs[i + 1]: (int(docs[i]), docs[i + 2]) for i in range(1, len(docs), 3)}
    names = {f: re.search(r'm_Name: (.*)', b).group(1).strip() for f, (c, b) in objs.items() if c == 1}
    tr_go = {f: re.search(r'm_GameObject: \{fileID: (-?\d+)\}', b).group(1) for f, (c, b) in objs.items() if c == 4}
    for f, (c, b) in objs.items():
        if c == 137:
            go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', b).group(1)
            if names.get(go) == renderer:
                seg = b.split('m_Bones:', 1)[1]
                seg = seg[:re.search(r'^  m_\w+:', seg, flags=re.M).start()]
                ids = re.findall(r'\{fileID: (-?\d+)\}', seg)
                return [names.get(tr_go.get(i), '?') for i in ids]
    return None


def components(T, w):
    """connected components of triangles sharing welded vertices"""
    parent = np.arange(w.max() + 1)

    def find(x):
        r = x
        while parent[r] != r:
            r = parent[r]
        while parent[x] != r:
            parent[x], x = r, parent[x]
        return r
    WT = w[T]
    for a, b, c in WT:
        ra, rb, rc = find(a), find(b), find(c)
        if ra != rb:
            parent[ra] = rb
        rb = find(b)
        if rc != rb:
            parent[rc] = rb
    lab = np.array([find(x) for x in WT[:, 0]])
    return lab


CASES = [
    ('a08', A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body1.asset', 1, A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd.prefab', 'pc_a08_hd_body1'),
    ('a08', A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd_body2.asset', 0, A + '/a08/chara_armor_pc_a08_hd_ld_hd/pc_a08_hd.prefab', 'pc_a08_hd_body2'),
    ('g04', A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd_body.asset', 2, A + '/g04/chara_armor_pc_g04_hd_ld_hd/pc_g04_hd.prefab', 'pc_g04_hd_body'),
]


def main():
    out = {}
    for ch, path, si, prefab, rname in CASES:
        m = um.load(path)
        bones = smr_bones(prefab, rname)
        P = m['pos']
        T = um.submesh_tris(m, si, 2)
        w = um.weld(P, 1e-4)
        lab = components(T, w)
        area = um.tri_area(P, T)
        bw, bi = m.get('bw'), m.get('bi')
        rows = []
        for l in np.unique(lab):
            sel = lab == l
            vs = np.unique(T[sel])
            z = P[vs, 2]
            acc = {}
            if bw is not None:
                for k in range(4):
                    for b_, ww in zip(bi[vs, k], bw[vs, k]):
                        if ww > 0:
                            acc[int(b_)] = acc.get(int(b_), 0) + float(ww)
            top = sorted(acc.items(), key=lambda x: -x[1])[:3]
            tot = sum(acc.values()) or 1
            rows.append(dict(tris=int(sel.sum()), area_cm2=round(float(area[sel].sum() * 1e4), 1),
                             z=f'{z.min():.2f}-{z.max():.2f}', x=f'{P[vs,0].min():+.2f}..{P[vs,0].max():+.2f}',
                             bones=[f'{bones[b_] if bones and b_ < len(bones) else b_}:{v / tot:.2f}' for b_, v in top]))
        rows.sort(key=lambda r: -r['area_cm2'])
        out[f'{ch} {rname}[{si}]'] = dict(pieces=len(rows), tris=int(len(T)), rows=rows)
        print(f'== {ch} {rname}[{si}]: {len(rows)} pieces, {len(T)} tris')
        for r in rows[:28]:
            print('   %6d tris %8.1f cm2  z %s  x %s  %s' % (r['tris'], r['area_cm2'], r['z'], r['x'], ', '.join(r['bones'])))
        small = [r for r in rows if r['area_cm2'] < 5]
        print('   ... %d pieces < 5 cm2 (total %.0f cm2)' % (len(small), sum(r['area_cm2'] for r in small)))
    json.dump(out, open(os.path.join(OUT, 'outfit_pieces.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)


if __name__ == '__main__':
    main()
