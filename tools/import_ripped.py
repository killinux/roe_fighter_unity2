# Copy the AssetRipper export of the staged ROE bundles into the Unity project (Assets/ROE),
# point the game's materials at this project's shaders, and write a manifest the editor
# scripts read.  Re-runnable: unchanged files are skipped.
#
#   python tools/import_ripped.py                       # _work/ripped -> Assets/ROE
#   python tools/import_ripped.py --src <ExportedProject/Assets> --dst <Assets/ROE>
#   python tools/import_ripped.py --src _work/ripped_more/ExportedProject/Assets    # more characters next to the ones there
#
# What is NOT copied: the game's shader stubs (AssetRipper cannot recover shader code; the
# materials are re-pointed to the shaders in Assets/RoeFighter/Shaders), script stubs,
# Cinemachine / audio-mixer packages.
#
# Adding characters later (a rip of only the new ones): AssetRipper gives every asset a new GUID on
# each export, and the new rip also holds the shared bundles the project already has (family heads,
# common textures, the skin LUT ...).  An asset the project already has keeps its GUID and is not
# copied again; the new files' references to it are rewritten to that GUID, so nothing that already
# points at it (prefabs, scenes, generated assets) breaks.  The manifest keeps the characters that are
# not in this rip (--fresh-manifest: only this rip's).
import argparse
import hashlib
import io
import json
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)

SKIP_BUNDLES = {'heros_shader_collection', 'package_cinemachine_prelude', 'audio_mixer_runtime', 'heros_builtin_asset'}

# game shader name -> this project's shader file (Assets/RoeFighter/Shaders/<file>.shader)
SHADER_MAP = {
    'Pinkcore/Heros/ErosLit/Character': 'RoeCharacter',
    'Pinkcore/Heros/SimpleLit/Character': 'RoeCharacter',
    # m02's body2/3: the character shader plus dissolve / fresnel / distortion extras, all off in the suits (lit as usual)
    'Pinkcore/Heros/ErosLit/CharacterVFX': 'RoeCharacter',
    'Pinkcore/Heros/SimpleLit/CharacterVFX': 'RoeCharacter',
    'Pinkcore/Heros/Skin': 'RoeSkin',
    'Pinkcore/Heros/KajiyaKayHair': 'RoeHair',
    'Pinkcore/Heros/SimpleLit/Hair': 'RoeHair',
    # b14's outfit1 (10-17): the game's newer anisotropic hair; RoeHair reads its shifts, shrinks and the two highlight colours
    # by the same names (its packed specular / occlusion / shadow-mask map, _Anisotropy and transmission are not used)
    'Pinkcore/Heros/AnisoHair': 'RoeHair',
    'Pinkcore/Heros/Eye': 'RoeEye',
    'Pinkcore/Heros/Eyebrow': 'RoeEyebrow',
    'Pinkcore/Heros/ErosLit/Environment': 'RoeEnvironment',
    'Pinkcore/Heros/SimpleLit/Environment': 'RoeEnvironment',
    'Pinkcore/Skybox/FogCubemap': 'RoeSkybox',
    # effects (rebuilt from the disassembly of the game's shaders, see tools/shader_asm.py)
    'Pinkcore/Particles/Default': 'RoeParticleDefault',
    'Pinkcore/Particles/Dissolve': 'RoeParticleDissolve',
    'Pinkcore/Particles/Unlit': 'RoeParticleUnlit',
    'Pinkcore/Particles/Distortion': 'RoeParticleDistortion',
    'Pinkcore/Particles/MutateDistortion': 'RoeParticleMutateDistortion',
    'Pinkcore/Particles/UnlitMaster': 'RoeParticleUnlitMaster',
}

CLIP_ALIASES = {  # the game's own spelling slips
    'skill01': 'skill_01', 'skill02': 'skill_02', 'skill03': 'skill_03',
    'skil_l01': 'skill_01', 'skil_l02': 'skill_02', 'skil_l03': 'skill_03',
    'idlel_01': 'idle_01', 'dle': 'die', 'recat_01': 'react_01', 'recat_02': 'react_02',
}


def shader_guid(file_stem):
    """Fixed GUID of one of this project's shaders (also written into its .meta)."""
    return hashlib.md5(('roe_fighter_unity/shader/' + file_stem).encode()).hexdigest()


def group_of(bundle):
    m = re.search(r'pc_([a-m]\d\d)(?![0-9])', bundle)
    if m:
        return m.group(1)
    m = re.search(r'pc_([a-m])_common', bundle)
    if m:
        return m.group(1) + '_common'
    return 'common'


def clean(name):
    return name.replace(' & ', '_').replace(' ', '_')


def write_manifests(manifest, dst):
    """roe_manifest.json (by id) and roe_manifest_unity.json (the same as a list: Unity's JsonUtility cannot read
    dictionaries).  An outfit of another suit (tools/roe_outfit.py) says whose it is (outfitOf)."""
    os.makedirs(dst, exist_ok=True)
    json.dump(manifest, open(os.path.join(dst, 'roe_manifest.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    unity = {'characters': []}
    for cid, e in sorted(manifest.items()):
        unity['characters'].append({
            'id': cid,
            'hdPrefab': e.get('hd_prefab', ''), 'hdAvatar': e.get('hd_avatar', ''),
            'metaPrefab': e.get('meta_prefab', ''), 'ldPrefab': e.get('ld_prefab', ''),
            'outfitOf': e.get('outfit_of', ''),
            'clips': [{'name': k, 'path': v} for k, v in sorted(e['clips'].items())],
            'weapons': e['weapons'], 'sfx': e['sfx'],
            'voices': [{'language': k, 'paths': v} for k, v in sorted(e['voice'].items())],
        })
    json.dump(unity, open(os.path.join(dst, 'roe_manifest_unity.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)


def read_guid(meta):
    t = open(meta, encoding='utf-8', errors='replace').read(400)
    m = re.search(r'guid: ([0-9a-f]{32})', t)
    return m.group(1) if m else None


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default=os.path.join(PROJECT, '_work', 'ripped', 'ExportedProject', 'Assets'))
    ap.add_argument('--dst', default=os.path.join(PROJECT, 'Assets', 'ROE'))
    ap.add_argument('--keep-manifest', action='store_true',
                    help='leave roe_manifest*.json as they are (an extra rip, e.g. the nude bases, next to the fighters)')
    ap.add_argument('--fresh-manifest', action='store_true',
                    help='write the manifest from this rip only (default: the characters already listed stay)')
    a = ap.parse_args()
    src_ab = os.path.join(a.src, 'AssetBundles')

    # game shader stub guid -> our shader guid
    remap = {}
    shader_dir = os.path.join(src_ab, 'heros_shader_collection')
    for d, _dirs, files in os.walk(shader_dir):
        for f in files:
            if f.endswith('.shader'):
                text = open(os.path.join(d, f), encoding='utf-8', errors='replace').read(300)
                m = re.search(r'Shader "([^"]+)"', text)
                g = read_guid(os.path.join(d, f + '.meta'))
                if m and g and m.group(1) in SHADER_MAP:
                    remap[g] = (m.group(1), shader_guid(SHADER_MAP[m.group(1)]))
    print(f'{len(remap)} game shaders mapped to project shaders')

    # assets the project already has keep their GUIDs (see the top of this file): new GUID -> the project's
    def target_dir(bundle, d):
        rel = os.path.relpath(d, os.path.join(src_ab, bundle))
        out_dir = os.path.join(a.dst, group_of(bundle), clean(bundle))
        return out_dir if rel == '.' else os.path.join(out_dir, rel)

    kept = {}
    for bundle in sorted(os.listdir(src_ab)):
        if not os.path.isdir(os.path.join(src_ab, bundle)) or bundle in SKIP_BUNDLES:
            continue
        for d, _dirs, files in os.walk(os.path.join(src_ab, bundle)):
            for f in files:
                if f.endswith('.meta') or not os.path.exists(os.path.join(d, f + '.meta')):
                    continue
                old_meta = os.path.join(target_dir(bundle, d), f + '.meta')
                if os.path.exists(old_meta):
                    new, old = read_guid(os.path.join(d, f + '.meta')), read_guid(old_meta)
                    if new and old and new != old:
                        kept[new] = old
    guid_re = re.compile(r'guid: ([0-9a-f]{32})')

    def keep_guids(text):
        return guid_re.sub(lambda m: 'guid: ' + kept.get(m.group(1), m.group(1)), text) if kept else text

    copied = skipped = remapped = 0
    unmapped = {}
    manifest = {}
    for bundle in sorted(os.listdir(src_ab)):
        bdir = os.path.join(src_ab, bundle)
        if not os.path.isdir(bdir) or bundle in SKIP_BUNDLES:
            continue
        group = group_of(bundle)
        for d, _dirs, files in os.walk(bdir):
            tdir = target_dir(bundle, d)
            os.makedirs(tdir, exist_ok=True)
            for f in files:
                s = os.path.join(d, f)
                t = os.path.join(tdir, f)
                if f.endswith('.meta'):
                    # an asset the project has keeps its own .meta (its GUID)
                    if not os.path.exists(t):
                        shutil.copy2(s, t)
                        copied += 1
                    else:
                        skipped += 1
                    continue
                if os.path.exists(t + '.meta') and os.path.exists(s + '.meta') and read_guid(t + '.meta') != read_guid(s + '.meta'):
                    skipped += 1        # already in the project under its own GUID (an earlier rip)
                    continue
                if f.endswith('.mat'):
                    text = open(s, encoding='utf-8').read()
                    m = re.search(r'm_Shader: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 3\}', text)
                    if m and m.group(2) in remap:
                        text = text.replace(m.group(0), 'm_Shader: {fileID: 4800000, guid: %s, type: 3}' % remap[m.group(2)][1])
                        remapped += 1
                    elif m:
                        unmapped.setdefault(m.group(2), []).append(f)
                    text = keep_guids(text)
                    if not (os.path.exists(t) and open(t, encoding='utf-8').read() == text):
                        open(t, 'w', encoding='utf-8', newline='\n').write(text)
                        copied += 1
                    else:
                        skipped += 1
                    continue
                if os.path.exists(t) and os.path.getsize(t) == os.path.getsize(s) and os.path.getmtime(t) >= os.path.getmtime(s):
                    skipped += 1
                    continue
                if kept and f.endswith(('.prefab', '.asset', '.anim', '.controller', '.overrideController', '.playable', '.mask')):
                    with open(s, 'rb') as fh:
                        yaml = fh.read(5) == b'%YAML'
                    if yaml:
                        text = open(s, encoding='utf-8', errors='surrogateescape').read()
                        new_text = keep_guids(text)
                        if new_text != text:
                            open(t, 'w', encoding='utf-8', errors='surrogateescape', newline='').write(new_text)
                            shutil.copystat(s, t)
                            copied += 1
                            continue
                shutil.copy2(s, t)
                copied += 1
        # the folder itself needs a .meta only if AssetRipper wrote one; Unity creates the rest

        # manifest
        unity_dir = 'Assets/ROE/%s/%s' % (group, clean(bundle))
        names = [f for f in os.listdir(bdir) if not f.endswith('.meta')]
        entry = manifest.setdefault(group, {'clips': {}, 'sfx': [], 'voice': {}, 'weapons': []})
        cid = group
        if bundle.startswith('chara_armor_pc_') and re.search(r'_hd$', bundle) and '_outfit' not in bundle and '_common_' not in bundle:
            if ('pc_%s_hd.prefab' % cid) in names:
                entry['hd_prefab'] = '%s/pc_%s_hd.prefab' % (unity_dir, cid)
                entry['hd_avatar'] = '%s/pc_%s_hdAvatar.asset' % (unity_dir, cid)
            for n in names:
                if n.endswith('.anim'):
                    stem = CLIP_ALIASES.get(n[:-5], n[:-5])
                    entry['clips'][stem] = '%s/%s' % (unity_dir, n)
                if n.startswith('wp_') and n.endswith('.prefab'):
                    entry['weapons'].append('%s/%s' % (unity_dir, n))
        elif bundle.startswith('chara_armor_pc_') and re.search(r'_ld(_prelude|_tutorial)?$', bundle) and '_outfit' not in bundle and '_common_' not in bundle:
            if ('pc_%s_ld.prefab' % cid) in names:
                entry['ld_prefab'] = '%s/pc_%s_ld.prefab' % (unity_dir, cid)
            for n in names:
                if n.endswith('.anim'):
                    stem = CLIP_ALIASES.get(n[:-5], n[:-5])
                    entry['clips'][stem] = '%s/%s' % (unity_dir, n)
        elif bundle.startswith('meta_armor_pc_') and '_outfit' not in bundle and 'localize' not in bundle:
            if ('pc_%s_hd.prefab' % cid) in names:
                entry['meta_prefab'] = '%s/pc_%s_hd.prefab' % (unity_dir, cid)
        elif bundle.startswith('meta_armor_pc_') and '_outfit' in bundle and 'localize' not in bundle:
            for n in names:
                if n.endswith('.prefab'):
                    entry.setdefault('outfit_meta_prefabs', []).append('%s/%s' % (unity_dir, n))
        elif bundle.startswith('sfx_battlefield_'):
            entry['sfx'] += ['%s/%s' % (unity_dir, n) for n in sorted(names) if n.endswith(('.ogg', '.wav'))]
        elif bundle.startswith('voice_battlefield_'):
            lang = bundle.split('_')[2]
            entry['voice'].setdefault(lang, [])
            entry['voice'][lang] += ['%s/%s' % (unity_dir, n) for n in sorted(names) if n.endswith(('.ogg', '.wav'))]

    manifest = {k: v for k, v in manifest.items() if any(v.get(x) for x in ('hd_prefab', 'clips', 'sfx', 'voice'))}
    print(f'{len(kept)} assets of this rip were already in the project: their GUIDs kept')
    if a.keep_manifest:
        print(f'copied {copied}, unchanged {skipped}, materials re-pointed {remapped}; manifest left as it was')
        for g, mats in unmapped.items():
            print(f'  shader {g} has no project shader: {", ".join(sorted(mats))}')
        return
    old_path = os.path.join(a.dst, 'roe_manifest.json')
    if not a.fresh_manifest and os.path.exists(old_path):
        old = json.load(open(old_path, encoding='utf-8'))
        kept_ids = sorted(k for k in old if k not in manifest)
        manifest = dict(sorted({**old, **manifest}.items()))
        print(f'manifest: {", ".join(kept_ids) or "nothing"} kept from before, {", ".join(sorted(set(manifest) - set(kept_ids)))} from this rip')
    write_manifests(manifest, a.dst)
    print(f'copied {copied}, unchanged {skipped}, materials re-pointed {remapped}')
    for g, mats in unmapped.items():
        print(f'  shader {g} has no project shader: {", ".join(sorted(mats))}')
    for cid, e in sorted(manifest.items()):
        print(f'  {cid}: hd={bool(e.get("hd_prefab"))} meta={bool(e.get("meta_prefab"))} ld={bool(e.get("ld_prefab"))} '
              f'clips={sorted(e["clips"])} weapons={len(e["weapons"])} sfx={len(e["sfx"])} '
              f'voice={ {k: len(v) for k, v in e["voice"].items()} }')


if __name__ == '__main__':
    main()
