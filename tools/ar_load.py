# Load a staging folder into the AssetRipper server; copy in any bundle it reports as a
# missing dependency (looked up in the CAB index) and reload, until nothing is missing.
#   python ar_load.py <staging dir> <cab_index.json> <assetripper.log>
import io
import json
import os
import re
import shutil
import sys
import time

import ar

AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    stage, idx_path, log = sys.argv[1:4]
    idx = json.load(open(idx_path, encoding='utf-8'))
    for round_no in range(1, 9):
        ar.post('/Reset', {})
        mark = os.path.getsize(log)
        t0 = time.time()
        s, _ = ar.post('/LoadFolder', {'Path': stage})
        with open(log, encoding='utf-8', errors='replace') as f:
            f.seek(mark)
            new = f.read()
        missing = sorted(set(m.lower() for m in re.findall(r"archive:/(CAB-[0-9a-f]+)/", new)))
        print(f'round {round_no}: LoadFolder {s} in {time.time() - t0:.1f}s, {len(os.listdir(stage))} bundles, '
              f'{len(missing)} missing dependencies')
        added = 0
        for cab in missing:
            b = idx.get(cab)
            if not b:
                print('   not in any bundle:', cab)
                continue
            dst = os.path.join(stage, b)
            if not os.path.exists(dst):
                shutil.copy2(os.path.join(AB, b), dst)
                added += 1
                print(f'   + {b}  ({os.path.getsize(dst) / 1e3:.0f} KB)')
        if not added:
            others = [ln for ln in new.splitlines() if 'Warning' in ln or 'Error' in ln]
            print('   other warnings/errors:', len(others))
            for ln in others[:15]:
                print('     ', ln[:200])
            break


if __name__ == '__main__':
    main()
