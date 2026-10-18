# Copy an AssetRipper export of one battle stage into the Unity project.
#   python tools/import_stage.py <stage name> [--src _work/ripped_stage_<name>/ExportedProject/Assets]
# Result: Assets/ROE/stages/<stage name>/...   (scene under .../scene/)
# Assets whose GUID the project already has (shared with the characters or another stage) are
# not copied again; materials are re-pointed to this project's shaders like in import_ripped.py.
import argparse
import io
import os
import re
import shutil
import sys

from import_ripped import PROJECT, SHADER_MAP, SKIP_BUNDLES, clean, read_guid, shader_guid

SKIP_TOP = {'Scripts', 'Plugins', 'Editor'}


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('name')
    ap.add_argument('--src', default='')
    a = ap.parse_args()
    src = os.path.abspath(a.src or os.path.join(PROJECT, '_work', 'ripped_stage_' + a.name, 'ExportedProject', 'Assets'))
    dst = os.path.join(PROJECT, 'Assets', 'ROE', 'stages', a.name)

    have = set()
    for d, _dirs, files in os.walk(os.path.join(PROJECT, 'Assets')):
        if os.path.abspath(d).startswith(os.path.abspath(dst)):
            continue
        for f in files:
            if f.endswith('.meta'):
                g = read_guid(os.path.join(d, f))
                if g:
                    have.add(g)

    remap = {}
    for d, _dirs, files in os.walk(os.path.join(src, 'AssetBundles', 'heros_shader_collection')):
        for f in files:
            if f.endswith('.shader'):
                m = re.search(r'Shader "([^"]+)"', open(os.path.join(d, f), encoding='utf-8', errors='replace').read(300))
                g = read_guid(os.path.join(d, f + '.meta'))
                if m and g and m.group(1) in SHADER_MAP:
                    remap[g] = shader_guid(SHADER_MAP[m.group(1)])

    copied = shared = remapped = 0
    unmapped = {}
    for top in sorted(os.listdir(src)):
        if top in SKIP_TOP or not os.path.isdir(os.path.join(src, top)):
            continue
        for d, _dirs, files in os.walk(os.path.join(src, top)):
            rel = os.path.relpath(d, src).split(os.sep)
            if rel[0] == 'AssetBundles':
                if len(rel) < 2 or rel[1] in SKIP_BUNDLES:
                    continue
                target_dir = os.path.join(dst, clean(rel[1]), *rel[2:])
            else:
                target_dir = os.path.join(dst, 'scene', rel[-1]) if any(f.endswith('.unity') for f in files) else os.path.join(dst, 'scene', *rel[1:])
            for f in files:
                if f.endswith('.meta'):
                    continue
                s = os.path.join(d, f)
                meta = s + '.meta'
                g = read_guid(meta) if os.path.exists(meta) else None
                if g and g in have:
                    shared += 1
                    continue
                os.makedirs(target_dir, exist_ok=True)
                t = os.path.join(target_dir, f)
                if f.endswith('.mat'):
                    text = open(s, encoding='utf-8').read()
                    m = re.search(r'm_Shader: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 3\}', text)
                    if m and m.group(2) in remap:
                        text = text.replace(m.group(0), 'm_Shader: {fileID: 4800000, guid: %s, type: 3}' % remap[m.group(2)])
                        remapped += 1
                    elif m:
                        unmapped[m.group(2)] = unmapped.get(m.group(2), 0) + 1
                    open(t, 'w', encoding='utf-8', newline='\n').write(text)
                else:
                    shutil.copy2(s, t)
                if os.path.exists(meta):
                    shutil.copy2(meta, t + '.meta')
                copied += 1
    print(f'{a.name}: copied {copied} assets to {dst}, {shared} already in the project, {remapped} materials re-pointed, '
          f'{sum(unmapped.values())} materials on {len(unmapped)} shaders without a replacement')


if __name__ == '__main__':
    main()
