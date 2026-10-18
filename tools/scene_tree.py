# Print the hierarchy of a ripped Unity scene (.unity, text YAML) with world positions, and the
# data of the game's own scripts (which Unity strips on load because the scripts do not exist
# in this project - the YAML still has every field, e.g. the Cinemachine camera orbits).
#   python scene_tree.py <scene.unity> [--scripts] [--find NAME]
#     --scripts    also print the fields of every MonoBehaviour
#     --find NAME  print only the subtree(s) whose root is called NAME
import re
import sys

import numpy as np
import yaml

CLASS = {1: 'GameObject', 4: 'Transform', 20: 'Camera', 23: 'MeshRenderer', 33: 'MeshFilter', 64: 'MeshCollider',
         65: 'BoxCollider', 81: 'AudioListener', 82: 'AudioSource', 108: 'Light', 114: 'MonoBehaviour',
         198: 'ParticleSystem', 199: 'ParticleSystemRenderer', 215: 'ReflectionProbe', 220: 'LightProbeGroup',
         224: 'RectTransform'}


def load(path):
    text = open(path, encoding='utf-8').read()
    docs = {}
    for m in re.finditer(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- |\Z)', text, re.S | re.M):
        cid, fid, body = int(m.group(1)), int(m.group(2)), m.group(3)
        try:
            data = yaml.safe_load(body)
        except yaml.YAMLError:
            continue
        if isinstance(data, dict) and data:
            docs[fid] = (cid, next(iter(data.values())))
    return docs


def quat_matrix(q):
    x, y, z, w = q['x'], q['y'], q['z'], q['w']
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def local_matrix(t):
    m = np.eye(4)
    s = t['m_LocalScale']
    m[:3, :3] = quat_matrix(t['m_LocalRotation']) @ np.diag([s['x'], s['y'], s['z']])
    p = t['m_LocalPosition']
    m[:3, 3] = [p['x'], p['y'], p['z']]
    return m


def short(v, depth=0):
    if isinstance(v, dict):
        if set(v) <= {'x', 'y', 'z', 'w'}:
            return '(' + ', '.join(f'{v[k]:.4g}' for k in v) + ')'
        if set(v) == {'fileID'} or set(v) == {'fileID', 'guid', 'type'}:
            return f"->{v.get('fileID')}" + (f" {v['guid'][:8]}" if 'guid' in v else '')
        if depth > 3:
            return '{...}'
        return '{' + ', '.join(f'{k}: {short(x, depth + 1)}' for k, x in v.items()) + '}'
    if isinstance(v, list):
        if len(v) > 8:
            return f'[{len(v)} items: ' + ', '.join(short(x, depth + 1) for x in v[:3]) + ', ...]'
        return '[' + ', '.join(short(x, depth + 1) for x in v) + ']'
    if isinstance(v, float):
        return f'{v:.5g}'
    return str(v)


def main():
    path = sys.argv[1]
    scripts = '--scripts' in sys.argv
    find = sys.argv[sys.argv.index('--find') + 1] if '--find' in sys.argv else None
    docs = load(path)
    transforms = {fid: d for fid, (cid, d) in docs.items() if cid in (4, 224)}
    world = {}

    def world_of(fid):
        if fid not in world:
            t = transforms[fid]
            father = t['m_Father']['fileID']
            m = local_matrix(t)
            world[fid] = world_of(father) @ m if father in transforms else m
        return world[fid]

    def show(fid, indent, on):
        t = transforms[fid]
        go = docs[t['m_GameObject']['fileID']][1]
        m = world_of(fid)
        pos = m[:3, 3]
        fwd = m[:3, 2] / max(1e-9, np.linalg.norm(m[:3, 2]))
        comps = []
        for c in go['m_Component']:
            cfid = c['component']['fileID']
            if cfid not in docs:
                continue
            cid, d = docs[cfid]
            if cid in (4, 224):
                continue
            name = CLASS.get(cid, f'class{cid}')
            if cid == 114:
                name = 'Script:' + d.get('m_Script', {}).get('guid', '?')[:8]
            comps.append((name, cid, d))
        active = bool(go.get('m_IsActive', 1))
        now = on and active
        print(f"{'  ' * indent}{go['m_Name']}{'' if active else ' [off]'}  pos ({pos[0]:.2f}, {pos[1]:.2f}, {pos[2]:.2f})"
              f" fwd ({fwd[0]:.2f}, {fwd[1]:.2f}, {fwd[2]:.2f})  {' '.join(c[0] for c in comps)}")
        for name, cid, d in comps:
            if cid == 20:
                print(f"{'  ' * indent}    Camera fov {d.get('field of view')} near {d.get('near clip plane')} far {d.get('far clip plane')}")
            if cid == 114 and scripts:
                for k, v in d.items():
                    if k.startswith('m_') and k in ('m_ObjectHideFlags', 'm_CorrespondingSourceObject', 'm_PrefabInstance',
                                                    'm_PrefabAsset', 'm_GameObject', 'm_EditorHideFlags', 'm_Script',
                                                    'm_Name', 'm_EditorClassIdentifier', 'm_Enabled'):
                        continue
                    print(f"{'  ' * indent}    {k}: {short(v)}")
        for c in t.get('m_Children') or []:
            if c['fileID'] in transforms:
                show(c['fileID'], indent + 1, now)

    roots = [fid for fid, t in transforms.items() if t['m_Father']['fileID'] not in transforms]
    if find:
        for fid, t in transforms.items():
            if docs[t['m_GameObject']['fileID']][1]['m_Name'] == find:
                show(fid, 0, True)
    else:
        for fid in roots:
            show(fid, 0, True)


if __name__ == '__main__':
    main()
