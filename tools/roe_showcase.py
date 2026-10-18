"""An outfit's showcase - what Rise of Eros' menus play when a heroine is touched - for the fight's select screen
(user 10-06: "选人环节也做个动画，ROE里面标准的动画").

Every outfit's hd bundle (chara_armor_pc_<id>_hd & ld_hd.ab: the model the menus show) carries idle_02 (looping) and two
timelines, idle02_react01 and idle02_react02: each an animation track with one react clip and an audio track with one
voice line, under a placeholder name ("Kart_affim").  meta_armor_pc_<id>_hd & ld.ab maps that name to a localized asset
per language (1 English, 2 Japanese: pc_b10_jp, pc_b10_02_jp, ...); meta_armor_localize_audio_pc_<id>_hd & ld.ab holds
those, each pointing at an AudioClip in a voice bundle it depends on (the game's bundle manifest, Manifest.ab, names
them: b10's lines are in voice_meta3d_japanese_pc_b03, a08's in ..._pc_a07 - earlier outfits of the family).

    python tools/roe_showcase.py <id> [<id> ...] [--lang 2]
Writes Assets/ROE/<id>/showcase_voice/<clip>.wav (the game's audio, never committed: Assets/ROE is ignored) and
tools/roe/<id>_showcase.json (names and timings only) for RoeFightScene.Build.
"""
import argparse
import json
import os
import re

import UnityPy

from import_ripped import CLIP_ALIASES      # the game's spelling slips (b14's and m02's recat_01 is the fighter's react_01)

AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

_manifest = None


def manifest():
    """The game's bundle manifest: bundle name -> the bundles it depends on."""
    global _manifest
    if _manifest is None:
        env = UnityPy.load(os.path.join(AB, 'Manifest.ab'))
        tree = next(o for o in env.objects if o.type.name == 'MonoBehaviour').read_typetree()
        _manifest = {k: v['m_Dependencies'] for k, v in zip(tree['m_Keys'], tree['m_Values'])}
    return _manifest


def trees(env):
    for obj in env.objects:
        if obj.type.name == 'MonoBehaviour':
            try:
                yield obj, obj.read_typetree()
            except Exception:
                continue


def timelines(cid):
    """The hd bundle's showcase timelines: name -> (react clip, its length, voice placeholder, the voice's start).  A recolour
    (b14_outfit1, tools/roe_outfit.py) has no hd bundle of its own: its suit's (its voice lines are its own, meta_armor_pc_<id>)."""
    env = UnityPy.load(os.path.join(AB, f'chara_armor_pc_{re.sub(r"_outfit[0-9]+$", "", cid)}_hd & ld_hd.ab'))
    by_id = {o.path_id: o for o in env.objects}
    out = {}
    for _, t in trees(env):
        if 'm_Tracks' not in t:
            continue
        entry = {'timeline': t['m_Name'], 'clip': None, 'length': 0.0, 'voice': None, 'voiceAt': 0.0}
        for ref in t['m_Tracks']:
            track = by_id.get(ref['m_PathID'])
            if track is None:
                continue
            tt = track.read_typetree()
            for c in tt.get('m_Clips', []):
                asset = by_id.get(c['m_Asset']['m_PathID'])
                at = asset.read_typetree() if asset is not None else {}
                clip_ref = at.get('m_Clip')
                if 'm_ApplyFootIK' in at or 'm_MatchTargetFields' in at:          # an animation clip
                    anim = by_id.get(clip_ref['m_PathID']) if clip_ref and clip_ref.get('m_FileID') == 0 else None
                    name = anim.read().m_Name if anim is not None else c.get('m_DisplayName')
                    entry['clip'] = CLIP_ALIASES.get(name, name)
                    entry['length'] = round(float(c.get('m_Duration', 0.0)), 3)
                elif 'm_bufferingTime' in at:                                       # an audio clip
                    entry['voice'] = c.get('m_DisplayName')
                    entry['voiceAt'] = round(float(c.get('m_Start', 0.0)), 3)
        if entry['clip']:
            out[t['m_Name']] = entry
    return out


def localized(cid, lang):
    """Voice placeholder -> the localized asset's name in that language (meta_armor_pc_<id>)."""
    env = UnityPy.load(os.path.join(AB, f'meta_armor_pc_{cid}_hd & ld.ab'))
    out = {}
    for _, t in trees(env):
        for m in t.get('_audioClipMappings', []):
            for loc in m.get('_localizationMappings', []):
                if str(loc.get('_voiceLocalizationLanguage')) == str(lang):
                    out[m['_playableClipName']] = loc['_audioClipAsset']
    return out


def audio_of(cid, asset_name):
    """The AudioClip a localized asset points at: (clip name, wav bytes) - looked up in the voice bundles the localize bundle
    depends on (the manifest), by the object's id."""
    name = f'meta_armor_localize_audio_pc_{cid}_hd & ld'
    env = UnityPy.load(os.path.join(AB, name + '.ab'))
    ref = None
    for _, t in trees(env):
        if t.get('m_Name') == asset_name:
            ref = t.get('_audioClip')
    if not ref or not ref.get('m_PathID'):
        return None
    for dep in manifest().get(name, []):
        path = os.path.join(AB, dep + '.ab')
        if not os.path.exists(path):
            continue
        venv = UnityPy.load(path)
        for obj in venv.objects:
            if obj.path_id == ref['m_PathID'] and obj.type.name == 'AudioClip':
                clip = obj.read()
                samples = clip.samples
                if samples:
                    return clip.m_Name, next(iter(samples.values())), dep
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('ids', nargs='+')
    ap.add_argument('--lang', default='2', help='1 English, 2 Japanese (the fight\'s voices are Japanese)')
    a = ap.parse_args()
    for cid in a.ids:
        lines = localized(cid, a.lang)
        out_dir = os.path.join(PROJECT, 'Assets', 'ROE', cid, 'showcase_voice')
        os.makedirs(out_dir, exist_ok=True)
        reactions = []
        for name, e in sorted(timelines(cid).items()):
            asset = lines.get(e['voice']) if e['voice'] else None
            got = audio_of(cid, asset) if asset else None
            audio = ''
            if got:
                clip_name, wav, bundle = got
                path = os.path.join(out_dir, clip_name + '.wav')
                with open(path, 'wb') as f:
                    f.write(wav)
                audio = os.path.relpath(path, PROJECT).replace('\\', '/')
                e['voiceClip'] = clip_name
                e['voiceBundle'] = bundle
            e['audio'] = audio
            reactions.append(e)
            print(f'{cid} {name}: {e["clip"]} {e["length"]:.2f} s, voice {e["voice"]} -> {asset} -> {e.get("voiceClip", "-")} '
                  f'({e.get("voiceBundle", "-")}) at {e["voiceAt"]:.2f} s')
        spec = {
            'id': cid,
            'note': 'ROE\'s menu showcase of this outfit (tools/roe_showcase.py): idle_02 loops; each timeline plays its react clip '
                    'and one voice line (voiceAt: seconds into the clip). Audio extracted to Assets/ROE (not committed).',
            'reactions': reactions,
        }
        path = os.path.join(PROJECT, 'tools', 'roe', f'{cid}_showcase.json')
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(spec, f, ensure_ascii=False, indent=2)
            f.write('\n')
        print(path)


if __name__ == '__main__':
    main()
