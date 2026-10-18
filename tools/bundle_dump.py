# Look inside one game bundle without ripping it: object list, and the full data of the
# game's own script objects (the bundles carry type trees, so every field is readable).
#   python bundle_dump.py <bundle name or path> [--list] [--json out.json] [--grep TEXT]
#     --list   only the object list (type, path id, name, script)
#     --json   write every MonoBehaviour as JSON (keyed by path id) to a file
#     --grep   print the MonoBehaviours whose JSON contains TEXT
import json
import os
import sys

import UnityPy

AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'


def clean(o, depth=0):
    """Type-tree data -> plain JSON: object references become {file, path}, bytes become a length."""
    if isinstance(o, dict):
        if set(o) == {'m_FileID', 'm_PathID'}:
            return {'ref': o['m_PathID'], 'file': o['m_FileID']} if o['m_PathID'] else None
        return {k: clean(v, depth + 1) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [clean(v, depth + 1) for v in o]
    if isinstance(o, (bytes, bytearray)):
        return f'<{len(o)} bytes>'
    return o


def main():
    name = sys.argv[1]
    path = name if os.path.exists(name) else os.path.join(AB, name if name.endswith('.ab') else name + '.ab')
    env = UnityPy.load(path)
    scripts = {}
    names = {}
    for obj in env.objects:
        if obj.type.name == 'MonoScript':
            d = obj.read()
            scripts[obj.path_id] = (getattr(d, 'm_Namespace', '') + '.' if getattr(d, 'm_Namespace', '') else '') + d.m_ClassName
        elif obj.type.name == 'GameObject':
            names[obj.path_id] = obj.read().m_Name
    out = {}
    rows = []
    for obj in env.objects:
        t = obj.type.name
        label = ''
        script = ''
        if t == 'MonoBehaviour':
            try:
                tree = obj.read_typetree()
            except Exception as e:      # no type tree for this one
                rows.append((t, obj.path_id, f'<unreadable: {e}>', ''))
                continue
            s = tree.get('m_Script', {})
            script = scripts.get(s.get('m_PathID'), f"external:{s.get('m_FileID')}:{s.get('m_PathID')}")
            go = tree.get('m_GameObject', {}).get('m_PathID')
            label = names.get(go, tree.get('m_Name', ''))
            data = clean(tree)
            data['_script'] = script
            data['_gameObject'] = label
            out[str(obj.path_id)] = data
        else:
            try:
                d = obj.read()
                label = getattr(d, 'm_Name', '') or ''
            except Exception:
                label = '?'
        rows.append((t, obj.path_id, label, script))

    if '--json' in sys.argv:
        dst = sys.argv[sys.argv.index('--json') + 1]
        with open(dst, 'w', encoding='utf-8') as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
        print(f'{len(out)} script objects -> {dst}')
    if '--grep' in sys.argv:
        text = sys.argv[sys.argv.index('--grep') + 1]
        for pid, data in out.items():
            s = json.dumps(data, ensure_ascii=False)
            if text in s:
                print(pid, data['_script'], data['_gameObject'])
                print('   ', s[:3000])
        return
    counts = {}
    for t, pid, label, script in rows:
        counts[t] = counts.get(t, 0) + 1
    print(os.path.basename(path), {k: v for k, v in sorted(counts.items(), key=lambda kv: -kv[1])})
    print('containers:', list(env.container.keys())[:40])
    if '--list' in sys.argv:
        for t, pid, label, script in rows:
            if t in ('Transform', 'RectTransform'):
                continue
            print(f'  {t:24} {pid:22} {label}  {script}')
    else:
        used = {}
        for t, pid, label, script in rows:
            if script:
                used[script] = used.get(script, 0) + 1
        for k, v in sorted(used.items(), key=lambda kv: -kv[1]):
            print(f'  {v:4} x {k}')


if __name__ == '__main__':
    main()
