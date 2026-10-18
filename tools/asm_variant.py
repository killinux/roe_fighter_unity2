# Print chosen variants from a shader_asm.py dump without the boilerplate.
#   python asm_variant.py <dump.txt> <stage: v|f> "<keywords in the header, space separated, or 'no keywords'>" [...more keyword sets]
import re
import sys


def main():
    path, stage = sys.argv[1], sys.argv[2]
    want = [' '.join(sorted(k.split())) for k in sys.argv[3:]]
    lines = open(path, encoding='utf-8').read().split('\n')
    head = re.compile(r'^    ---- prog(Vertex|Fragment) \[(.*?)\]')
    blocks = []
    cur = None
    for line in lines:
        m = head.match(line)
        if m:
            cur = {'stage': m.group(1), 'kw': ' '.join(sorted(m.group(2).split())), 'lines': [line]}
            blocks.append(cur)
        elif line.startswith('    prog') or line.startswith('  Pass') or line.startswith('SubShader'):
            cur = None
        elif cur is not None:
            cur['lines'].append(line)
    for b in blocks:
        if b['stage'][0].lower() != stage[0].lower() or b['kw'] not in want:
            continue
        for line in b['lines']:
            s = line.strip()
            if not s or s.startswith('//') or s.startswith('dcl_temps') or 'Approximately' in s:
                continue
            print(line)
        print()


if __name__ == '__main__':
    main()
