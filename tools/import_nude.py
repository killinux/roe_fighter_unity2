# The family nude bases for the clothes burst (RoeNudeBody, Editor/Burst/<id>.json "nude"): from an AssetRipper
# export of the nude bundles, only the body mesh, its prefab (and avatar), materials, textures and the meta prefab
# go into Assets/ROE - the body bundles also hold the H scenes' animations (65 MB for a01) and pull in other
# characters' bodies as dependencies.  The fighters' manifest is left alone.
#
#   python tools\stage_bundles.py _work\bundles_nude --nude a01 g01
#   python tools\rip.py --bundles _work\bundles_nude --out _work\ripped_nude --log _work\assetripper_nude.log
#   python tools\import_nude.py a01 g01
import argparse
import fnmatch
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('bases', nargs='+', help='a01 g01 ...')
    ap.add_argument('--src', default=os.path.join(PROJECT, '_work', 'ripped_nude', 'ExportedProject', 'Assets', 'AssetBundles'))
    ap.add_argument('--sel', default=os.path.join(PROJECT, '_work', 'ripped_nude_sel'))
    a = ap.parse_args()
    out = os.path.join(a.sel, 'AssetBundles')
    if os.path.isdir(a.sel):
        shutil.rmtree(a.sel)
    os.makedirs(out)
    bundles = sorted(os.listdir(a.src))
    picked = 0

    def take(bundle, patterns):
        nonlocal picked
        for f in os.listdir(os.path.join(a.src, bundle)):
            if f.endswith('.meta') or not any(fnmatch.fnmatch(f, p) for p in patterns):
                continue
            os.makedirs(os.path.join(out, bundle), exist_ok=True)
            if os.path.isdir(os.path.join(a.src, bundle, f)):
                shutil.copytree(os.path.join(a.src, bundle, f), os.path.join(out, bundle, f), dirs_exist_ok=True)
                picked += 1
                continue
            for g in (f, f + '.meta'):
                if os.path.exists(os.path.join(a.src, bundle, g)):
                    shutil.copy2(os.path.join(a.src, bundle, g), os.path.join(out, bundle, g))
            picked += 1

    for base in a.bases:
        for b in fnmatch.filter(bundles, f'chara_bare_pc_{base}_nk*'):
            if '_fm_' not in b or '_fm' in base:
                take(b, [f'pc_{base}_nk_body.asset', f'pc_{base}_nk.prefab', f'pc_{base}_nkAvatar.asset'])
        for b in fnmatch.filter(bundles, f'meta_bare_pc_{base}_nk'):
            take(b, [f'pc_{base}_nk_bs.prefab'])
        for b in fnmatch.filter(bundles, f'chara_mat_bare_pc_{base}_nk*') + fnmatch.filter(bundles, f'chara_tex_bare_pc_{base}_nk*'):
            if '_fm_' not in b or '_fm' in base:
                take(b, ['*'])
    for b in fnmatch.filter(bundles, 'chara_tex_bare_common_prelude') + ['heros_shader_collection']:
        if b in bundles:
            take(b, ['*'])
    print(f'{picked} files picked into {out}')
    subprocess.run([sys.executable, os.path.join(HERE, 'import_ripped.py'), '--src', a.sel, '--keep-manifest'], check=True)
    # the suits' and faces' materials want the skin LUT under the GUID the first rip gave it (that rip did not have
    # this bundle): a copy of it under that GUID (README, clothes burst - "皮肤 LUT 一直缺着")
    lut = os.path.join(PROJECT, 'Assets', 'ROE', 'common', 'chara_tex_bare_common_prelude', 'pc_common_skin_rgbx_Lut.png')
    alias = lut.replace('.png', '_armor_ref.png')
    if os.path.exists(lut) and not os.path.exists(alias):
        import re
        shutil.copy2(lut, alias)
        meta = open(lut + '.meta', encoding='utf-8').read()
        meta = meta.replace(re.search(r'guid: ([0-9a-f]{32})', meta).group(1), '0a100c26625aa3c4f94639ffb55bdf49', 1)
        open(alias + '.meta', 'w', encoding='utf-8', newline='\n').write(meta)
        print('skin LUT alias written:', alias)


if __name__ == '__main__':
    main()
