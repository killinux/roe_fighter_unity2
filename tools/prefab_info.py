# Summarise a Unity .prefab (text YAML) exported by AssetRipper: hierarchy, renderers,
# animator, scripts.  References are resolved through the .meta files under <assets root>.
#   python prefab_info.py <assets root> <prefab> [--tree N] [--bones]
import argparse
import io
import os
import re
import sys


def guid_map(root):
    out = {}
    for d, _dirs, files in os.walk(root):
        for f in files:
            if f.endswith('.meta'):
                t = open(os.path.join(d, f), encoding='utf-8', errors='replace').read(400)
                m = re.search(r'guid: ([0-9a-f]{32})', t)
                if m:
                    out[m.group(1)] = os.path.relpath(os.path.join(d, f[:-5]), root).replace(os.sep, '/')
    return out


def docs_of(path):
    """fileID -> (class id, type name, text) for every object in the file."""
    text = open(path, encoding='utf-8').read()
    out = {}
    for m in re.finditer(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n(\w+):\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S):
        out[m.group(2)] = (int(m.group(1)), m.group(3), m.group(4))
    return out


def field(body, name):
    m = re.search(r'^  ' + re.escape(name) + r': (.*)$', body, re.M)
    return m.group(1).strip() if m else None


def ref(s):
    """'{fileID: 1, guid: abc, type: 2}' -> (fileID, guid)"""
    if not s:
        return None, None
    f = re.search(r'fileID: (-?\d+)', s)
    g = re.search(r'guid: ([0-9a-f]{32})', s)
    return (f.group(1) if f else None), (g.group(1) if g else None)


class Prefab:
    def __init__(self, path):
        self.docs = docs_of(path)
        self.go_name = {}
        self.go_comps = {}
        self.tr_go = {}
        self.tr_parent = {}
        self.tr_children = {}
        self.go_tr = {}
        self.tr_local = {}
        for fid, (cid, typ, body) in self.docs.items():
            if typ == 'GameObject':
                self.go_name[fid] = field(body, 'm_Name')
                self.go_comps[fid] = re.findall(r'- component: \{fileID: (-?\d+)\}', body)
            elif typ in ('Transform', 'RectTransform'):
                go = ref(field(body, 'm_GameObject'))[0]
                self.tr_go[fid] = go
                self.go_tr[go] = fid
                self.tr_parent[fid] = ref(field(body, 'm_Father'))[0]
                kids = re.search(r'^  m_Children:(.*?)^  m_Father', body, re.M | re.S)
                self.tr_children[fid] = re.findall(r'fileID: (-?\d+)', kids.group(1)) if kids else []
                self.tr_local[fid] = tuple(field(body, k) for k in ('m_LocalPosition', 'm_LocalRotation', 'm_LocalScale'))
        self.roots = [t for t, p in self.tr_parent.items() if p in (None, '0')]

    def name(self, tr):
        return self.go_name.get(self.tr_go.get(tr), '?')

    def path(self, tr):
        parts = []
        while tr and tr != '0' and tr in self.tr_go:
            parts.append(self.name(tr))
            tr = self.tr_parent.get(tr)
        return '/'.join(reversed(parts))

    def count(self, tr):
        return 1 + sum(self.count(c) for c in self.tr_children.get(tr, []))

    def tree(self, tr, depth, limit, out):
        comps = []
        for c in self.go_comps.get(self.tr_go[tr], []):
            typ = self.docs[c][1]
            if typ not in ('Transform',):
                comps.append(typ)
        kids = self.tr_children.get(tr, [])
        out.append('  ' * depth + f'{self.name(tr)}' + (f'  [{", ".join(comps)}]' if comps else '') +
                   (f'  (+{self.count(tr) - 1} below)' if depth >= limit and kids else ''))
        if depth < limit:
            for c in kids:
                self.tree(c, depth + 1, limit, out)


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('assets')
    ap.add_argument('prefab')
    ap.add_argument('--tree', type=int, default=3)
    ap.add_argument('--bones', action='store_true')
    a = ap.parse_args()
    g2p = guid_map(a.assets)
    p = Prefab(a.prefab)
    print(f'{os.path.basename(a.prefab)}: {len(p.go_name)} GameObjects, {len(p.docs)} objects')
    for r in p.roots:
        out = []
        p.tree(r, 0, a.tree, out)
        print('\n'.join(out))
        print('  root local TRS:', p.tr_local[r])

    def show(s):
        fid, guid = ref(s)
        if guid:
            return g2p.get(guid, 'guid ' + guid) + (f' #{fid}' if fid else '')
        return f'local {fid}' if fid and fid != '0' else 'None'

    for fid, (cid, typ, body) in p.docs.items():
        go = ref(field(body, 'm_GameObject'))[0]
        where = p.path(p.go_tr.get(go)) if go in p.go_tr else '?'
        if typ == 'SkinnedMeshRenderer':
            mats = re.search(r'^  m_Materials:(.*?)^  m_\w+:', body, re.M | re.S)
            mats = [show(x) for x in re.findall(r'- (\{[^}]*\})', mats.group(1))] if mats else []
            bones = re.search(r'^  m_Bones:(.*?)^  m_\w+:', body, re.M | re.S)
            nb = len(re.findall(r'fileID', bones.group(1))) if bones else 0
            rootb = ref(field(body, 'm_RootBone'))[0]
            print(f'SKINNED {where}: mesh={show(field(body, "m_Mesh"))} bones={nb} rootBone={p.name(rootb) if rootb in p.tr_go else rootb}'
                  f' enabled={field(body, "m_Enabled")} shadows={field(body, "m_CastShadows")}')
            for m in mats:
                print('     mat', m)
            if a.bones and bones:
                names = [p.name(x) for x in re.findall(r'fileID: (-?\d+)', bones.group(1))]
                print('     bones:', ', '.join(names))
        elif typ == 'MeshRenderer':
            mats = re.search(r'^  m_Materials:(.*?)^  m_\w+:', body, re.M | re.S)
            mats = [show(x) for x in re.findall(r'- (\{[^}]*\})', mats.group(1))] if mats else []
            print(f'MESHRENDERER {where}: {mats}')
        elif typ == 'MeshFilter':
            print(f'MESHFILTER {where}: mesh={show(field(body, "m_Mesh"))}')
        elif typ == 'Animator':
            print(f'ANIMATOR {where}: avatar={show(field(body, "m_Avatar"))} controller={show(field(body, "m_Controller"))}'
                  f' rootMotion={field(body, "m_ApplyRootMotion")} culling={field(body, "m_CullingMode")}')
        elif typ == 'MonoBehaviour':
            keys = re.findall(r'^  (\w+):', body, re.M)
            keys = [k for k in keys if not k.startswith('m_')]
            print(f'SCRIPT {where}: {show(field(body, "m_Script"))} enabled={field(body, "m_Enabled")} fields={keys[:14]}')
        elif typ not in ('GameObject', 'Transform'):
            print(f'{typ.upper()} {where}')


if __name__ == '__main__':
    main()
