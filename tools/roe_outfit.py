"""A recolour of a ROE suit as a fighter of its own (user 10-17: b14's pink and white outfit1, "要").

    python tools/roe_outfit.py b14 outfit1        -> character b14_outfit1 in Assets/ROE/roe_manifest*.json

The game ships a suit's recolours as materials, textures and a showcase ("meta") prefab of their own
(chara_mat_armor_pc_<id>_outfitN, chara_tex_armor_pc_<id>_outfitN, meta_armor_pc_<id>_outfitN) on the suit's own meshes,
skeleton and clips; tools/stage_bundles.py takes them with the suit and tools/import_ripped.py copies them next to it,
listing the outfit's meta prefab (outfit_meta_prefabs) without making it a character.  This makes it one: the suit's
entry with the outfit's meta prefab, from which RoeFighterBuilder takes the materials (its renderers are named as the
suit's), named <id>_<outfit> as the game names its files (its weapon's material is wp_<family>_<number>_<outfit>_ld).
Run it again after a new rip of the suit (import_ripped keeps the entry but does not update it).
"""
import argparse
import copy
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from import_ripped import write_manifests  # noqa: E402

DST = os.path.join(os.path.dirname(HERE), 'Assets', 'ROE')


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('base', help='the suit (b14)')
    ap.add_argument('outfit', help='its recolour (outfit1)')
    a = ap.parse_args()
    manifest = json.load(open(os.path.join(DST, 'roe_manifest.json'), encoding='utf-8'))
    base = manifest.get(a.base)
    if base is None:
        sys.exit(f'no {a.base} in the manifest')
    want = f'pc_{a.base}_{a.outfit}_hd.prefab'
    metas = [p for p in base.get('outfit_meta_prefabs', []) if os.path.basename(p) == want]
    if not metas:
        sys.exit(f'{a.base} has no {want} (outfits: {base.get("outfit_meta_prefabs", [])}) - stage and rip the suit first')
    cid = f'{a.base}_{a.outfit}'
    entry = copy.deepcopy(base)
    entry.pop('outfit_meta_prefabs', None)
    entry['meta_prefab'] = metas[0]
    entry['outfit_of'] = a.base
    manifest[cid] = entry
    write_manifests(dict(sorted(manifest.items())), DST)
    print(f'{cid}: {a.base} with the materials of {metas[0]}; {len(entry["clips"])} clips, {len(entry["weapons"])} weapons, '
          f'{len(entry["sfx"])} sounds')


if __name__ == '__main__':
    main()
