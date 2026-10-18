# Which shader does each exported material use?  Run inside ExportedProject/Assets.
import collections
import glob
import io
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')


def guid_map():
    out = {}
    for root, _dirs, files in os.walk('.'):
        for f in files:
            if f.endswith('.meta'):
                t = open(os.path.join(root, f), encoding='utf-8', errors='replace').read(400)
                m = re.search(r'guid: ([0-9a-f]{32})', t)
                if m:
                    out[m.group(1)] = os.path.join(root, f[:-5]).replace(os.sep, '/')
    return out


def main():
    g2p = guid_map()
    print(len(g2p), 'assets with guids')
    use = collections.defaultdict(list)
    for p in glob.glob('AssetBundles/**/*.mat', recursive=True):
        t = open(p, encoding='utf-8').read()
        m = re.search(r'm_Shader: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?', t)
        if m and m.group(2):
            sh = g2p.get(m.group(2), 'guid ' + m.group(2))
        else:
            sh = 'fileID ' + (m.group(1) if m else '?')
        use[sh].append(os.path.basename(p)[:-4])
    for sh, mats in sorted(use.items()):
        name = ''
        if os.path.exists(sh):
            name = re.search(r'Shader "([^"]+)"', open(sh, encoding='utf-8').read()).group(1)
        print(f'{sh}  [{name}]')
        print('     ', ', '.join(sorted(mats)))
    print()
    dirs = collections.Counter(os.path.dirname(p) for p in g2p.values() if p.endswith('.shader'))
    print('shader dirs:', dirs.most_common(5))


if __name__ == '__main__':
    main()
