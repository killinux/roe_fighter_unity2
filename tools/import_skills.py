# Copy what a unit's skills need from an AssetRipper export into the Unity project.
#   python tools/import_skills.py suit_ines_8 suit_luf_4 [--src _work/ripped_skills/ExportedProject/Assets]
# Result: Assets/ROE/skills/<bundle>/...   (unit prefab: skills/gameplay_prefab_<unit>/<Unit>.prefab)
#
# The export of a gameplay prefab drags in ~900 bundles (every buff effect of the game hangs on
# a shared settings asset).  Only what the unit's own prefab reaches is copied: its timelines,
# the effect prefabs on them, their materials, meshes and textures, the sounds.
# Three kinds of references are rewritten on the way:
#   - assets the project already has (the character's model, sounds ...): AssetRipper invents new
#     GUIDs on every export, so they are matched by bundle + path and the old GUID is kept;
#   - scripts of Unity packages (Timeline, URP ...): AssetRipper writes stubs with its own
#     GUIDs; the real package scripts in Library/PackageCache are used instead;
#   - the game's shaders: replaced by this project's shaders (SHADER_MAP in import_ripped.py).
import argparse
import glob
import io
import os
import re
import shutil
import zlib
import sys

from import_ripped import PROJECT, SHADER_MAP, clean, read_guid, shader_guid

# bundles that are never copied and never followed
SKIP_BUNDLES = {'heros_shader_collection', 'package_cinemachine_prelude', 'audio_mixer_runtime', 'heros_builtin_asset',
                'gameplay_common_setting_prelude', 'gameplay_mix_prelude'}
TEXT_EXT = {'.prefab', '.playable', '.mat', '.asset', '.controller', '.overrideController', '.unity', '.signal'}
PACKAGES = {  # AssetRipper script folder (= assembly) -> package folders in Library/PackageCache
    'Unity.Timeline': ['com.unity.timeline@*'],
    'Unity.RenderPipelines.Universal.Runtime': ['com.unity.render-pipelines.universal@*'],
    'Unity.RenderPipelines.Core.Runtime': ['com.unity.render-pipelines.core@*'],
    'UnityEngine.UI': ['com.unity.ugui@*'],
    'Unity.TextMeshPro': ['com.unity.ugui@*', 'com.unity.textmeshpro@*'],
}
GUID_RE = re.compile(r'guid: ([0-9a-f]{32})')
EXPOSED_USE_RE = re.compile(r'exposedName:\s*\n\s+id: (-?\d+)')
EXPOSED_TABLE_RE = re.compile(r'(m_References:\n)((?:[ \t]+- -?\d+: \{[^}\n]*\}\n)+)')
EXPOSED_ENTRY_RE = re.compile(r'^([ \t]+- )(-?\d+)(: \{)', re.M)


def exposed_name(number):
    return 'roe_' + number.replace('-', 'm')


def property_id(name):
    """Unity's id of a PropertyName: CRC32 of the name, as a signed 32-bit number."""
    c = zlib.crc32(name.encode('utf-8'))
    return c - (1 << 32) if c >= (1 << 31) else c


def fix_exposed_names(text):
    """
    A timeline clip names the scene object it controls through an "exposed reference"; the
    director holds the table name -> object.  A built game keeps only the hash of each name and
    AssetRipper writes that number where Unity's text format expects the name itself, so nothing
    resolves and every effect would be created at the world origin.  Any name works as long as
    both sides use the same one: the number becomes the name.  The director looks names up by
    binary search, so its table is written sorted by the hash of the new names.
    """
    text = EXPOSED_USE_RE.sub(lambda m: 'exposedName: ' + exposed_name(m.group(1)), text)

    def table(m):
        entries = []
        for line in m.group(2).splitlines(keepends=True):
            e = EXPOSED_ENTRY_RE.match(line)
            name = exposed_name(e.group(2))
            entries.append((property_id(name), e.group(1) + name + line[e.end(2):]))
        return m.group(1) + ''.join(line for _id, line in sorted(entries))

    return EXPOSED_TABLE_RE.sub(table, text)


def script_remap(src):
    """AssetRipper stub script GUID -> GUID of the real package script."""
    remap, missing = {}, []
    cache = os.path.join(PROJECT, 'Library', 'PackageCache')
    for assembly, patterns in PACKAGES.items():
        sdir = os.path.join(src, 'Scripts', assembly)
        if not os.path.isdir(sdir):
            continue
        real = {}
        for pat in patterns:
            for pkg in glob.glob(os.path.join(cache, pat)):
                for d, _dirs, files in os.walk(pkg):
                    for f in files:
                        if f.endswith('.cs.meta'):
                            real.setdefault(f[:-8], os.path.join(d, f))
        for d, _dirs, files in os.walk(sdir):
            for f in files:
                if f.endswith('.cs.meta'):
                    stub = read_guid(os.path.join(d, f))
                    if f[:-8] in real:
                        remap[stub] = read_guid(real[f[:-8]])
                    else:
                        missing.append(f'{assembly}/{f[:-8]}')
    return remap, missing


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('units', nargs='+')
    ap.add_argument('--src', default=os.path.join(PROJECT, '_work', 'ripped_skills', 'ExportedProject', 'Assets'))
    a = ap.parse_args()
    src = os.path.abspath(a.src)
    src_ab = os.path.join(src, 'AssetBundles')
    roe = os.path.join(PROJECT, 'Assets', 'ROE')
    dst = os.path.join(roe, 'skills')

    # ---- the export: guid -> file
    by_guid = {}
    for d, _dirs, files in os.walk(src_ab):
        rel = os.path.relpath(d, src_ab).split(os.sep)
        for f in files:
            if f.endswith('.meta'):
                continue
            meta = os.path.join(d, f + '.meta')
            if os.path.exists(meta):
                g = read_guid(meta)
                if g:
                    by_guid[g] = (rel[0], '/'.join(rel[1:] + [f]), os.path.join(d, f))
    print(f'{len(by_guid)} assets in the export')

    # ---- what the project already has: (bundle folder, path inside) -> guid
    have = {}
    own = set()             # the part of it that an earlier run of this tool brought in
    for d, _dirs, files in os.walk(roe):
        parts = os.path.relpath(d, roe).split(os.sep)
        if parts[0] == '.':
            continue
        if parts[0] == 'stages':
            if len(parts) < 3:
                continue
            bundle, inner = parts[2], parts[3:]
        else:
            if len(parts) < 2:
                continue
            bundle, inner = parts[1], parts[2:]
        for f in files:
            if f.endswith('.meta') and not os.path.isdir(os.path.join(d, f[:-5])):
                g = read_guid(os.path.join(d, f))
                if g:
                    have[(bundle, '/'.join(inner + [f[:-5]]))] = g
                    if parts[0] == 'skills':
                        own.add(g)

    # ---- reference rewriting
    remap, missing_scripts = script_remap(src)
    n_scripts = len(remap)
    shader_names = {}
    for d, _dirs, files in os.walk(os.path.join(src_ab, 'heros_shader_collection')):
        for f in files:
            if f.endswith('.shader'):
                m = re.search(r'Shader "([^"]+)"', open(os.path.join(d, f), encoding='utf-8', errors='replace').read(300))
                g = read_guid(os.path.join(d, f + '.meta'))
                if m and g:
                    shader_names[g] = m.group(1)
                    if m.group(1) in SHADER_MAP:
                        remap[g] = shader_guid(SHADER_MAP[m.group(1)])
    existing = {}
    for g, (bundle, inner, _path) in by_guid.items():
        old = have.get((clean(bundle), inner))
        if old:
            existing[g] = old
            if old != g:
                remap[g] = old

    # ---- walk from the unit prefabs
    roots = []
    for unit in a.units:
        bdir = os.path.join(src_ab, 'gameplay_prefab_' + unit)
        found = [f for f in os.listdir(bdir) if f.lower() == unit.lower() + '.prefab'] if os.path.isdir(bdir) else []
        if not found:
            print(f'{unit}: no prefab called like the unit in {bdir}')
            continue
        roots.append(read_guid(os.path.join(bdir, found[0] + '.meta')))
    wanted, queue = set(roots), list(roots)
    unknown_refs = 0
    while queue:
        g = queue.pop()
        bundle, inner, path = by_guid[g]
        if os.path.splitext(path)[1] not in TEXT_EXT:
            continue
        if os.path.getsize(path) > 8_000_000 and not path.endswith(('.prefab', '.playable')):
            continue        # meshes and animation data: no references inside
        for ref in set(GUID_RE.findall(open(path, encoding='utf-8', errors='replace').read())):
            if ref in wanted or ref in remap and ref not in existing:
                continue
            if ref not in by_guid:
                unknown_refs += 1
                continue
            if by_guid[ref][0] in SKIP_BUNDLES:
                continue
            wanted.add(ref)
            if ref not in existing or existing[ref] in own:     # the character's own assets are not searched again
                queue.append(ref)

    # ---- copy
    copied = reused = 0
    per_bundle = {}
    unmapped_shaders = {}
    for g in sorted(wanted):
        bundle, inner, path = by_guid[g]
        if g in existing:
            reused += 1
            continue
        target = os.path.join(dst, clean(bundle), *inner.split('/'))
        os.makedirs(os.path.dirname(target), exist_ok=True)
        ext = os.path.splitext(path)[1]
        if ext in TEXT_EXT and os.path.getsize(path) <= 8_000_000 or ext in ('.prefab', '.playable'):
            text = open(path, encoding='utf-8', errors='replace').read()
            if ext == '.mat':
                m = re.search(r'm_Shader: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 3\}', text)
                if m and m.group(2) in shader_names and shader_names[m.group(2)] in SHADER_MAP:
                    text = text.replace(m.group(0), 'm_Shader: {fileID: 4800000, guid: %s, type: 3}' % remap[m.group(2)])
                elif m:
                    name = shader_names.get(m.group(2), m.group(2))
                    unmapped_shaders.setdefault(name, []).append(os.path.basename(path))
            text = GUID_RE.sub(lambda m: 'guid: ' + remap.get(m.group(1), m.group(1)), text)
            if ext in ('.prefab', '.playable', '.asset'):
                text = fix_exposed_names(text)
            open(target, 'w', encoding='utf-8', newline='\n').write(text)
        else:
            shutil.copy2(path, target)
        shutil.copy2(path + '.meta', target + '.meta')
        copied += 1
        per_bundle[bundle] = per_bundle.get(bundle, 0) + 1

    print(f'{len(wanted)} assets reached from {len(roots)} unit prefabs: {copied} copied to {dst}, {reused} already in the project; '
          f'{n_scripts} package scripts re-pointed, {unknown_refs} references to things outside the export (built-in assets, scripts)')
    for b, n in sorted(per_bundle.items()):
        print(f'  {n:4d}  {b}')
    for name, mats in sorted(unmapped_shaders.items()):
        print(f'  shader without a replacement: {name}: {len(mats)} materials ({", ".join(sorted(mats)[:6])}{" ..." if len(mats) > 6 else ""})')
    if missing_scripts:
        print(f'  package scripts not found in Library/PackageCache: {", ".join(sorted(missing_scripts)[:12])}')


if __name__ == '__main__':
    main()
