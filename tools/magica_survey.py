# How Rise of Eros uses Magica Cloth 2, and which of our skirt chains are one piece of cloth (docs/magica-cloth-2.md).
#   python magica_survey.py settings              every MagicaCloth component in the lobby bundles: parameter table
#   python magica_survey.py links a08             skirt chains that share skinned vertices / triangles
#   python magica_survey.py picture out.png       a08 + g04 skirts coloured by chain, chains and the cross links
# The game keeps Magica Cloth only on the lobby prefab (meta_armor_<outfit> & ld.ab); the battle prefab has none.
import argparse
import collections
import fnmatch
import math
import os
import re
import sys

import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

DIRS = [r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles',
        os.path.expandvars(r'%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\AssetBundles')]
SKIRT = re.compile(r'skirt', re.I)


def bundles(pattern):
    hits = {}
    for root in DIRS:
        for d, _dirs, files in os.walk(root):
            for f in files:
                if fnmatch.fnmatch(f, pattern):
                    p = os.path.join(d, f)
                    if f not in hits or os.path.getmtime(p) > os.path.getmtime(hits[f]):
                        hits[f] = p
    return [hits[k] for k in sorted(hits)]


# ---- settings

PARAMS = {
    'gravity': lambda p: p['gravity'],
    'damping': lambda p: round(p['damping']['value'], 3),
    'radius': lambda p: round(p['radius']['value'], 3),
    'angle restoration': lambda p: round(p['angleRestorationConstraint']['stiffness']['value'], 2) if p['angleRestorationConstraint']['useAngleRestoration'] else 'off',
    'velocity attenuation': lambda p: round(p['angleRestorationConstraint']['velocityAttenuation'], 2),
    'angle limit': lambda p: '%g%s' % (p['angleLimitConstraint']['limitAngle']['value'], ' x depth curve' if p['angleLimitConstraint']['limitAngle']['useCurve'] else '') if p['angleLimitConstraint']['useAngleLimit'] else 'off',
    'world / local inertia': lambda p: (p['inertiaConstraint']['worldInertia'], p['inertiaConstraint']['localInertia']),
    'particle speed limit': lambda p: p['inertiaConstraint']['particleSpeedLimit']['value'] if p['inertiaConstraint']['particleSpeedLimit']['use'] else 'off',
    'tether compression': lambda p: p['tetherConstraint']['distanceCompression'],
    'distance stiffness': lambda p: p['distanceConstraint']['stiffness']['value'],
    'collision mode': lambda p: {0: 'none', 1: 'point', 2: 'edge'}.get(p['colliderCollisionConstraint']['mode']),
    'backstop / max distance': lambda p: (p['motionConstraint']['useBackstop'], p['motionConstraint']['useMaxDistance']),
    'animation pose ratio': lambda p: p['animationPoseRatio'],
    'connection mode': lambda p: {0: 'Line', 1: 'AutomaticMesh', 2: 'SequentialLoopMesh', 3: 'SequentialNonLoopMesh'}.get(p['connectionMode']),
    'rotational interpolation / root rotation': lambda p: (p['rotationalInterpolation'], p['rootRotation']),
}


def magica_components(path):
    env = UnityPy.load(path)
    objs = {o.path_id: o for o in env.objects}
    names = {o.path_id: o.read().m_Name for o in env.objects if o.type.name == 'GameObject'}
    tr_go = {}
    for o in env.objects:
        if o.type.name == 'Transform':
            tr_go[o.path_id] = o.read_typetree()['m_GameObject']['m_PathID']
    for o in env.objects:
        if o.type.name != 'MonoBehaviour':
            continue
        tt = o.read_typetree()
        if 'serializeData' not in tt:
            continue
        sd = tt['serializeData']
        roots = [names.get(tr_go.get(r['m_PathID']), '?') for r in sd.get('rootBones', []) if r['m_PathID']]
        yield {'type': sd.get('clothType'), 'roots': roots, 'params': sd}


def settings(_args):
    paths = bundles('meta_armor_*.ab')
    table = {k: collections.Counter() for k in PARAMS}
    n, cloth_like = 0, []
    for p in paths:
        for c in magica_components(p):
            n += 1
            for k, f in PARAMS.items():
                table[k][str(f(c['params']))] += 1
            pieces = [r for r in c['roots'] if re.search(r'skirt|cloak|shawl|veil|scarf', r, re.I)]
            if pieces:
                cloth_like.append((os.path.basename(p), pieces))
    print('%d lobby bundles, %d MagicaCloth components (types: %s)' % (len(paths), n, 'all BoneCloth' if n else '-'))
    for k, c in table.items():
        print('  %-42s %s' % (k, ', '.join('%s x%d' % kv for kv in c.most_common())))
    print('cloth-like roots (everything else is hair, bangs, ribbons, ornaments):')
    for b, r in cloth_like:
        print('  %-40s %s' % (b, r))


# ---- the prefab: transforms, skin

def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


class Prefab:
    """The outfit's hd battle prefab (pc_<id>_hd): transforms in its own pose and the skinned meshes under it."""

    def __init__(self, cid):
        path = bundles('chara_armor_pc_%s_hd & ld_hd.ab' % cid)
        if not path:
            sys.exit('no hd bundle for %s' % cid)
        env = UnityPy.load(path[0])
        self.objs = {o.path_id: o for o in env.objects}
        gname = {o.path_id: o.read().m_Name for o in env.objects if o.type.name == 'GameObject'}
        self.go, self.parent, self.local, self.kids = {}, {}, {}, collections.defaultdict(list)
        for o in env.objects:
            if o.type.name == 'Transform':
                t = o.read_typetree()
                self.go[o.path_id] = t['m_GameObject']['m_PathID']
                self.parent[o.path_id] = t['m_Father']['m_PathID']
                self.kids[t['m_Father']['m_PathID']].append(o.path_id)
                self.local[o.path_id] = [np.array(list(t[k].values())) for k in ('m_LocalPosition', 'm_LocalRotation', 'm_LocalScale')]
        self.name = {t: gname.get(g, '?') for t, g in self.go.items()}
        root = next(t for t in self.go if self.name[t] == 'pc_%s_hd' % cid and self.parent[t] not in self.go)
        self.mine, st = set(), [root]
        while st:
            t = st.pop()
            self.mine.add(t)
            st.extend(self.kids[t])
        self._world = {}

    def world(self, t):
        if t not in self._world:
            pos, rot, sc = self.local[t]
            m = np.eye(4)
            m[:3, :3] = qmat(rot) * sc
            m[:3, 3] = pos
            p = self.parent.get(t, 0)
            self._world[t] = self.world(p) @ m if p in self.local else m
        return self._world[t]

    def depth(self, t):
        d = 0
        while t in self.parent:
            t = self.parent[t]
            d += 1
        return d

    def piece(self, t):
        """The cloth piece of a skirt bone: its chain below the last skirt bone with two or more skirt children
        (a08's Skirt_L_00 hangs three chains); the ornament chains of a08 count as one piece per side."""
        n = self.name[t]
        m = re.search(r'Skirt_Chain_([LR])', n)
        if m:
            return 'Skirt_Chain_' + m.group(1)

        def skirt_kids(u):
            return sum(1 for c in self.kids[u] if SKIRT.search(self.name[c]) and 'Chain' not in self.name[c])
        while self.parent[t] in self.go and SKIRT.search(self.name[self.parent[t]]) and skirt_kids(self.parent[t]) == 1:
            t = self.parent[t]
        return self.name[t]

    def skins(self):
        """(vertex world positions, [(bone transform, weight)...] per vertex, triangles) of each skinned mesh."""
        for o in self.objs.values():
            if o.type.name != 'SkinnedMeshRenderer':
                continue
            r = o.read_typetree()
            trs = [k for k, v in self.go.items() if v == r['m_GameObject']['m_PathID']]
            if not trs or trs[0] not in self.mine:
                continue
            bones = [b['m_PathID'] for b in r['m_Bones']]
            mobj = self.objs[r['m_Mesh']['m_PathID']]
            h = MeshHandler(mobj.read())
            h.process()
            if not h.m_BoneWeights:
                continue
            bind = [np.array([[bp['e%d%d' % (i, j)] for j in range(4)] for i in range(4)]) for bp in mobj.read_typetree()['m_BindPose']]
            skin = [self.world(b) @ bp if b in self.local else None for b, bp in zip(bones, bind)]
            pos, wts = [], []
            for v, idx, w in zip(h.m_Vertices, h.m_BoneIndices, h.m_BoneWeights):
                p, tot, bw = np.zeros(4), 0.0, []
                for i, ww in zip(idx, w):
                    if ww > 0 and i < len(bones) and skin[i] is not None:
                        p += ww * (skin[i] @ np.array([v[0], v[1], v[2], 1.0]))
                        tot += ww
                        bw.append((bones[i], ww))
                pos.append(p[:3] / max(tot, 1e-9))
                wts.append(bw)
            yield pos, wts, [tri for sub in h.get_triangles() for tri in sub]


def skirt_vertices(pf):
    """Every skirt vertex: (position, its pieces with weights, dominant piece) per mesh, plus the triangles."""
    for pos, wts, tris in pf.skins():
        info = []
        for p, bw in zip(pos, wts):
            acc = collections.Counter()
            tot = sum(w for _, w in bw)
            for b, w in bw:
                if b in pf.mine and SKIRT.search(pf.name[b]):
                    acc[pf.piece(b)] += w
            dom = acc.most_common(1)[0][0] if acc and acc.most_common(1)[0][1] > 0.5 * tot else None
            info.append((p, {k: v for k, v in acc.items() if v > 0.02 * tot}, dom))
        yield info, tris


def links(args):
    pf = Prefab(args.id)
    per, shared, spans = collections.Counter(), collections.Counter(), collections.Counter()
    for info, tris in skirt_vertices(pf):
        for _p, pieces, dom in info:
            ks = sorted(pieces)
            if dom:
                per[dom] += 1
            for i in range(len(ks)):
                for j in range(i + 1, len(ks)):
                    shared[(ks[i], ks[j])] += 1
        for tri in tris:
            ds = sorted({info[i][2] for i in tri if info[i][2]})
            for i in range(len(ds)):
                for j in range(i + 1, len(ds)):
                    spans[(ds[i], ds[j])] += 1
    print('%s skirt pieces (vertices whose strongest bones are the piece\'s):' % args.id)
    for k, v in per.most_common():
        print('  %-22s %5d' % (k, v))
    print('triangles across two pieces (one piece of cloth) / vertices weighted to both:')
    for k in sorted(set(spans) | set(shared), key=lambda k: -spans[k] * 1000 - shared[k]):
        if 'Chain' in k[0] and 'Chain' in k[1]:
            continue
        print('  %-22s %-22s %5d  %5d' % (k[0], k[1], spans[k], shared[k]))


# ---- picture

LINKS = {'a08': [['Skirt_L_08', 'Skirt_L_01', 'Skirt_L_16'], ['Skirt_R_16', 'Skirt_R_01', 'Skirt_R_08'], ['Skirt_M_00', 'Skirt_M_01']],
         'g04': [['Skirt_front_007', 'Skirt_front_01']]}
TITLES = {'a08': 'a08 Inase：每侧一整片布挂在前、侧、后 3 条链上',
          'g04': 'g04 Luffee：前片一整片布挂在 2 条链上，其余各片各 1 条链'}
PALETTE = [(230, 25, 75), (60, 180, 75), (0, 130, 200), (245, 130, 48), (145, 30, 180), (70, 240, 240), (240, 50, 230),
           (210, 160, 0), (0, 128, 128), (170, 110, 40), (128, 0, 0), (0, 0, 128), (128, 128, 0)]
VIEWS = [('正面', lambda p: (-p[0], p[1])), ('左侧面（人朝右）', lambda p: (p[2], p[1])), ('俯视（前方朝下）', lambda p: (-p[0], -p[2]))]


def picture(args):
    from PIL import Image, ImageDraw, ImageFont
    font = ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc', 22)
    small = ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc', 17)
    rows = []
    for cid in ('a08', 'g04'):
        pf = Prefab(cid)
        verts = [(p, dom) for info, _t in skirt_vertices(pf) for p, _k, dom in info if dom]
        chains = collections.defaultdict(list)
        for t in sorted((t for t in pf.mine if SKIRT.search(pf.name[t])), key=pf.depth):
            chains[pf.piece(t)].append(pf.world(t)[:3, 3])
        groups = sorted(set(chains) | {g for _, g in verts})
        colors = {g: (150, 150, 150) if 'Chain' in g else PALETTE[i % len(PALETTE)] for i, g in enumerate(groups)}
        cross = [(a[k], b[k]) for seq in LINKS[cid] for ga, gb in zip(seq, seq[1:])
                 for a, b in [(chains.get(ga, []), chains.get(gb, []))] for k in range(min(len(a), len(b)))]
        img = Image.new('RGB', (1500, 640), (255, 255, 255))
        d = ImageDraw.Draw(img)
        d.text((14, 8), TITLES[cid], font=font, fill=(0, 0, 0))
        for vi, (vname, f) in enumerate(VIEWS):
            x0, y0, x1, y1 = 10 + vi * 330, 74, 330 + vi * 330, 610
            d.text((x0 + 6, 50), vname, font=small, fill=(60, 60, 60))
            pts = np.array([f(p) for p, _ in verts] + [f(q) for c in chains.values() for q in c])
            lo, hi = pts.min(0), pts.max(0)
            s = min(x1 - x0, y1 - y0) / (max(hi - lo) * 1.08)
            cx, cy = (lo + hi) / 2

            def T(p):
                u, v = f(p)
                return x0 + (x1 - x0) / 2 + (u - cx) * s, y0 + (y1 - y0) / 2 - (v - cy) * s
            for p, g in verts:
                x, y = T(p)
                d.ellipse((x - 1.2, y - 1.2, x + 1.2, y + 1.2), fill=tuple(int(c * 0.7 + 255 * 0.3) for c in colors[g]))
            for a, b in cross:
                (xa, ya), (xb, yb) = T(a), T(b)
                n = max(2, int(math.hypot(xb - xa, yb - ya) / 6))
                for k in range(0, n, 2):
                    d.line([(xa + (xb - xa) * k / n, ya + (yb - ya) * k / n),
                            (xa + (xb - xa) * (k + 1) / n, ya + (yb - ya) * (k + 1) / n)], fill=(0, 0, 0), width=3)
            for g, c in chains.items():
                q = [T(p) for p in c]
                thin = 'Chain' in g
                if len(q) > 1:
                    d.line(q, fill=(110, 110, 110) if thin else tuple(int(v * 0.8) for v in colors[g]), width=2 if thin else 5)
                r = 2 if thin else 5
                for x, y in q:
                    d.ellipse((x - r, y - r, x + r, y + r), fill=colors[g], outline=(0, 0, 0))
        y = 60
        d.text((1010, y), '颜色 = 顶点挂在哪条链上', font=small, fill=(0, 0, 0))
        y += 28
        for g in groups:
            n = sum(1 for _, gg in verts if gg == g)
            d.rectangle((1010, y + 4, 1030, y + 20), fill=colors[g])
            d.text((1038, y), '%s（%d 节，%d 顶点）' % (g, len(chains.get(g, [])), n), font=small, fill=(0, 0, 0))
            y += 25
        d.line([(1010, y + 22), (1050, y + 22)], fill=(0, 0, 0), width=3)
        d.text((1058, y + 10), '虚线：Magica 式横向连接（建议加）', font=small, fill=(0, 0, 0))
        rows.append(img)
    out = Image.new('RGB', (1500, 640 * len(rows)), (255, 255, 255))
    for i, r in enumerate(rows):
        out.paste(r, (0, 640 * i))
    out.save(args.out)
    print('->', args.out)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest='cmd', required=True)
    sub.add_parser('settings').set_defaults(fn=settings)
    p = sub.add_parser('links')
    p.add_argument('id')
    p.set_defaults(fn=links)
    p = sub.add_parser('picture')
    p.add_argument('out')
    p.set_defaults(fn=picture)
    args = ap.parse_args()
    args.fn(args)


if __name__ == '__main__':
    main()
