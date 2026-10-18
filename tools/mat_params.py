# Material parameters of the fighters, per shader: which values the game's materials actually use.
#   python tools/mat_params.py [a08 g04 ...] [--props _BaseMap,_RimColor,...] [--shader ROE/Character]
# Reads Assets/RoeFighter/Generated/<id>/<id>.prefab, follows its material references and prints
# texture scale/offset, floats, colours and keywords of every material, grouped by shader.
import argparse
import io
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def guid_index(*dirs):
    out = {}
    for d in dirs:
        for root, _dirs, files in os.walk(os.path.join(ROOT, d)):
            for f in files:
                if f.endswith('.meta'):
                    with open(os.path.join(root, f), encoding='utf-8', errors='replace') as fh:
                        m = re.search(r'guid: ([0-9a-f]{32})', fh.read(400))
                    if m:
                        out[m.group(1)] = os.path.join(root, f[:-5])
    return out


def parse_material(path):
    text = open(path, encoding='utf-8').read()
    mat = {'name': re.search(r'm_Name: (.*)', text).group(1).strip(), 'tex': {}, 'float': {}, 'color': {}}
    m = re.search(r'm_Shader: \{fileID: -?\d+, guid: ([0-9a-f]{32})', text)
    mat['shader'] = m.group(1) if m else None
    m = re.search(r'm_ValidKeywords:(.*?)\n  m_\w', text, re.S)
    mat['keywords'] = re.findall(r'- (\S+)', m.group(1)) if m else []
    tex = re.search(r'm_TexEnvs:(.*?)\n    m_(?:Ints|Floats)', text, re.S)
    if tex:
        entries, name = [], None
        for line in tex.group(1).split('\n'):
            head = re.match(r' {4,6}(?:- )?(\w+):\s*$', line)
            if head:
                name = head.group(1)
                entries.append([name, ''])
            elif name and line.startswith(' ' * 8):
                entries[-1][1] += line + '\n'
        for name, body in entries:
            t = re.search(r'm_Texture: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?', body)
            sc = re.search(r'm_Scale: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', body)
            of = re.search(r'm_Offset: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', body)
            mat['tex'][name] = {
                'guid': t.group(2) if t and t.group(1) != '0' else None,
                'st': (float(sc.group(1)), float(sc.group(2)), float(of.group(1)), float(of.group(2))) if sc and of else None,
            }
    fl = re.search(r'm_Floats:(.*?)\n    m_Colors', text, re.S)
    if fl:
        for name, v in re.findall(r'(?:- )?(\w+): ([-\d.e]+)\s*$', fl.group(1), re.M):
            mat['float'][name] = float(v)
    co = re.search(r'm_Colors:(.*?)(?:\n  m_|\Z)', text, re.S)
    if co:
        for name, r, g, b, a in re.findall(r'(?:- )?(\w+): \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}', co.group(1)):
            mat['color'][name] = (float(r), float(g), float(b), float(a))
    return mat


def fmt(v):
    if isinstance(v, tuple):
        return '(' + ', '.join(f'{x:.3g}' for x in v) + ')'
    return f'{v:.3g}'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('ids', nargs='*', default=['a08', 'g04'])
    ap.add_argument('--props', default='')
    ap.add_argument('--shader', default='')
    args = ap.parse_args()
    index = guid_index('Assets/ROE', 'Assets/RoeFighter')
    shader_names = {}
    for guid, path in index.items():
        if path.endswith('.shader'):
            m = re.search(r'Shader "([^"]+)"', open(path, encoding='utf-8').read())
            shader_names[guid] = m.group(1) if m else os.path.basename(path)
    wanted = [p for p in args.props.split(',') if p]
    for unit in args.ids:
        prefab = os.path.join(ROOT, 'Assets/RoeFighter/Generated', unit, unit + '.prefab')
        guids = []
        for g in re.findall(r'\{fileID: 2100000, guid: ([0-9a-f]{32})', open(prefab, encoding='utf-8').read()):
            if g not in guids:
                guids.append(g)
        print(f'=== {unit}: {len(guids)} materials')
        by_shader = {}
        for g in guids:
            if g not in index:
                print('   missing material', g)
                continue
            mat = parse_material(index[g])
            by_shader.setdefault(shader_names.get(mat['shader'], str(mat['shader'])), []).append(mat)
        for shader, mats in sorted(by_shader.items()):
            if args.shader and args.shader != shader:
                continue
            print(f'  -- {shader} ({len(mats)})')
            for mat in mats:
                parts = []
                for name, t in sorted(mat['tex'].items()):
                    if wanted and name not in wanted:
                        continue
                    if t['guid'] is None and not wanted:
                        continue
                    st = t['st']
                    tag = 'tex' if t['guid'] else 'none'
                    if st and st != (1.0, 1.0, 0.0, 0.0):
                        tag += ' ST' + fmt(st)
                    parts.append(f'{name}={tag}')
                for name, v in sorted(mat['float'].items()):
                    if wanted and name not in wanted:
                        continue
                    if not wanted and name.startswith(('_Src', '_Dst', '_Z', '_Cull', '_Queue', '_Enable', '_Surface', '_IsDecal')):
                        continue
                    parts.append(f'{name}={fmt(v)}')
                for name, v in sorted(mat['color'].items()):
                    if wanted and name not in wanted:
                        continue
                    parts.append(f'{name}={fmt(v)}')
                print(f'     {mat["name"]}  [{" ".join(mat["keywords"])}]')
                print('        ' + '  '.join(parts))


if __name__ == '__main__':
    main()
