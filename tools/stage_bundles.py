# Copy the game bundles that a set of ROE outfits needs into one staging folder,
# so AssetRipper can load just those (the full folder has 14,317 bundles).
#   python stage_bundles.py <out dir> a08 g04
#   python stage_bundles.py <out dir> --nude a01 g01     the family nude bases (clothes burst, RoeNudeBody): body, its
#                                                        material and textures, the meta prefab; then tools/import_nude.py
import fnmatch
import io
import json
import os
import shutil
import sys

AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'
# the per-character bundle lists of ripper_tpose's HQ-material cache (D:\roe_exports was deleted 10-06; its lists kept here)
CACHE = r'E:\game_export\RiseOfEros\_meta\roe_exports_leftovers_20261006\_hq_materials'


def patterns(cid):
    fam = cid[0]
    # (the suit's own chara_bare_pc_<id>_nk* / bare_blend_shape_pc_<id>_nk* bundles are left out: rigs and clips of
    # its H scenes, nothing the fight uses; the nude body for the clothes burst is the family's, --nude)
    return [
        f'chara_armor_pc_{cid}_*', f'chara_mat_armor_pc_{cid}_*', f'chara_tex_armor_pc_{cid}_*',
        f'meta_armor_pc_{cid}_*', f'meta_armor_localize_audio_pc_{cid}_*',
        f'sfx_battlefield_pc_{cid}*', f'sfx_battlefield_pc_{fam}01*',
        f'chara_*_pc_{fam}_common*',
        f'voice_battlefield_english_pc_{fam}01*', f'voice_battlefield_japanese_pc_{fam}01*',
        'heros_shader_collection.ab',
        # the skin LUT of the suits' skin and faces (pc_common_skin_rgbx_Lut) lives with the nude bodies' textures
        'chara_tex_bare_common_prelude.ab',
    ]


def nude_patterns(base):
    """A nude base (a01, g01): its body bundle, material and textures (named _tutorial / _prelude for some), meta prefab."""
    return [
        f'chara_bare_pc_{base}_nk*.ab', f'meta_bare_pc_{base}_nk.ab',
        f'chara_mat_bare_pc_{base}_nk*.ab', f'chara_tex_bare_pc_{base}_nk*.ab',
        'chara_mat_bare_common*.ab', 'chara_tex_bare_common*.ab', 'heros_shader_collection.ab',
    ]


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    out = sys.argv[1]
    nude = '--nude' in sys.argv
    ids = [a for a in sys.argv[2:] if a != '--nude']
    names = sorted(os.listdir(AB))
    want = set()
    for cid in ids:
        if nude:
            # (the _fm_ bodies are another version of a family's body: only when asked for, e.g. --nude g01_fm)
            for pat in nude_patterns(cid):
                want |= set(n for n in fnmatch.filter(names, pat) if '_fm_' not in n or '_fm' in cid)
            continue
        for pat in patterns(cid):
            want |= set(fnmatch.filter(names, pat))
        cache = os.path.join(CACHE, cid + '.json')
        if os.path.exists(cache):
            for b in json.load(open(cache, encoding='utf-8'))['bundles']:
                if b in names:
                    want.add(b)
                else:
                    print('  cache names a bundle the game no longer has:', b)
    os.makedirs(out, exist_ok=True)
    total = 0
    for n in sorted(want):
        src = os.path.join(AB, n)
        dst = os.path.join(out, n)
        size = os.path.getsize(src)
        total += size
        if not (os.path.exists(dst) and os.path.getsize(dst) == size):
            shutil.copy2(src, dst)
    print(f'{len(want)} bundles, {total / 1e6:.0f} MB -> {out}')
    for n in sorted(want):
        print(f'  {os.path.getsize(os.path.join(out, n)) / 1e3:10.0f} KB  {n}')


if __name__ == '__main__':
    main()
