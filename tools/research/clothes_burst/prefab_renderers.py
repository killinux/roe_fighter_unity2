"""Read-only: list renderers in a Unity YAML prefab with their mesh + materials (resolved via .meta GUIDs)."""
import os, re, sys, json

ROOT = r"E:/code/othercode/roe_fighter_unity/Assets"


def guid_map(root):
    m = {}
    for dp, dn, fn in os.walk(root):
        for f in fn:
            if f.endswith('.meta'):
                p = os.path.join(dp, f)
                try:
                    with open(p, 'r', encoding='utf-8', errors='ignore') as fh:
                        for line in fh:
                            if line.startswith('guid:'):
                                m[line.split(':', 1)[1].strip()] = p[:-5].replace(os.sep, '/')
                                break
                except Exception:
                    pass
    return m


def parse(path):
    txt = open(path, 'r', encoding='utf-8', errors='ignore').read()
    docs = re.split(r'^--- !u!(\d+) &(-?\d+).*$', txt, flags=re.M)
    objs = {}
    for i in range(1, len(docs), 3):
        cls, fid, body = docs[i], docs[i + 1], docs[i + 2]
        objs[fid] = (int(cls), body)
    return objs


def section(body, key):
    if key not in body:
        return ''
    rest = body.split(key, 1)[1]
    # stop at next top-level key of the component (two-space indent + m_)
    mm = re.search(r'^  m_\w+:', rest, flags=re.M)
    return rest[:mm.start()] if mm else rest


def main(prefab, gm):
    objs = parse(prefab)
    names = {}
    for fid, (cls, body) in objs.items():
        if cls == 1:
            mm = re.search(r'm_Name: (.*)', body)
            names[fid] = mm.group(1).strip() if mm else '?'
    out = []
    for fid, (cls, body) in objs.items():
        if cls in (137, 23):  # SkinnedMeshRenderer, MeshRenderer
            go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body).group(1)
            mats = re.findall(r'\{fileID: (-?\d+), guid: ([0-9a-f]+), type: \d+\}', section(body, 'm_Materials:'))
            mesh = re.search(r'm_Mesh: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]+))?', body)
            enabled = re.search(r'm_Enabled: (\d)', body)
            bones = re.findall(r'\{fileID: -?\d+\}', section(body, 'm_Bones:'))
            bsw = section(body, 'm_BlendShapeWeights:').strip()
            meshp = None
            if mesh:
                meshp = gm.get(mesh.group(2), mesh.group(2)) if mesh.group(2) else mesh.group(1)
            out.append({
                'renderer': names.get(go, go), 'class': 'SMR' if cls == 137 else 'MR',
                'enabled': enabled.group(1) if enabled else '?',
                'mesh': os.path.basename(meshp) if meshp else None,
                'materials': [os.path.basename(gm.get(g, g)) for _, g in mats],
                'bones': len(bones), 'blendShapeWeights': bsw[:60],
            })
    return out


if __name__ == '__main__':
    gm = guid_map(ROOT)
    for p in sys.argv[1:]:
        print('=====', p)
        for r in main(p, gm):
            print(json.dumps(r, ensure_ascii=False))
