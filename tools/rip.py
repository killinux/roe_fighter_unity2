# One command for the AssetRipper part: settings, load the staged bundles (pulling in missing
# dependencies), export as a Unity project.  The AssetRipper server must be running:
#   E:\tools\AssetRipper_1.3.14\AssetRipper.GUI.Free.exe --headless --port 5599 --log-path _work\assetripper.log
#
#   python tools\rip.py [--bundles _work\bundles] [--out _work\ripped] [--log _work\assetripper.log]
import argparse
import io
import os
import shutil
import subprocess
import sys
import time

import ar

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)

SETTINGS = dict(
    BundledAssetsExportMode='GroupByBundleName',    # one folder per game bundle: tells same-named assets apart
    ShaderExportMode='Dummy',                       # shader code cannot be recovered; we only need the property lists
    ScriptContentLevel='Level2', AudioExportFormat='Default', ImageExportFormat='Png',
    LightmapTextureExportFormat='Yaml', SpriteExportMode='Yaml', TextExportMode='Parse',
    ScriptLanguageVersion='AutoSafe', ScriptExportMode='Hybrid',
    DefaultVersion='0.0.0a0', TargetVersion='0.0.0a0',
)


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('--bundles', default=os.path.join(PROJECT, '_work', 'bundles'))
    ap.add_argument('--out', default=os.path.join(PROJECT, '_work', 'ripped'))
    ap.add_argument('--log', default=os.path.join(PROJECT, '_work', 'assetripper.log'))
    ap.add_argument('--index', default=os.path.join(PROJECT, '_work', 'cab_index.json'))
    a = ap.parse_args()
    # the server resolves paths against its own working directory
    a.bundles, a.out, a.log, a.index = (os.path.abspath(p) for p in (a.bundles, a.out, a.log, a.index))

    try:
        ar.get('/')
    except Exception as e:
        sys.exit(f'AssetRipper is not answering on {ar.BASE} ({e}). Start it first, see the top of this file.')
    if not os.path.exists(a.index):
        subprocess.run([sys.executable, os.path.join(HERE, 'cab_index.py'), 'build', a.index], check=True)

    print('reset', ar.post('/Reset', {})[0])
    print('settings', ar.post('/Settings/Update', SETTINGS)[0])      # only possible while nothing is loaded
    subprocess.run([sys.executable, os.path.join(HERE, 'ar_load.py'), a.bundles, a.index, a.log], check=True)

    if os.path.isdir(a.out):
        shutil.rmtree(a.out)
    t0 = time.time()
    status, _ = ar.post('/Export/UnityProject', {'Path': a.out})
    assets = os.path.join(a.out, 'ExportedProject', 'Assets')
    count = sum(len(files) for _d, _s, files in os.walk(assets)) if os.path.isdir(assets) else 0
    print(f'export {status} in {time.time() - t0:.0f} s: {count} files under {assets}')


if __name__ == '__main__':
    main()
