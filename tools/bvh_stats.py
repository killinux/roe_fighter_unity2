# What a BVH clip does, in numbers - to tell a crouch from a guard from a jump without looking at every one
# (DOA6 motions are numbered, not named).
#   python tools/bvh_stats.py <dir or files...> [--scale 0.01]
# Per clip: length, hips height (start / min / max / end, metres), how far the hips travel (forward = +Z, the
# direction DOA6 clips face; side = X), the feet's highest point above the floor (> 0.3 m: airborne), the frames
# both feet are off the floor (first-last), and how much the pose moves (mean joint speed, m/s: ~0 for a held pose).
import argparse
import glob
import math
import os
import re


def parse(path):
    text = open(path, encoding='utf-8', errors='ignore').read()
    head, motion = text.split('MOTION', 1)
    joints, stack, order = [], [], []
    for line in head.splitlines():
        t = line.split()
        if not t:
            continue
        if t[0] in ('ROOT', 'JOINT'):
            joints.append({'name': t[1], 'parent': stack[-1] if stack else -1, 'offset': None, 'channels': []})
            order.append(len(joints) - 1)
        elif t[0] == 'End':
            joints.append({'name': '_end', 'parent': stack[-1], 'offset': None, 'channels': []})
        elif t[0] == '{':
            stack.append(len(joints) - 1)
        elif t[0] == '}':
            stack.pop()
        elif t[0] == 'OFFSET':
            joints[stack[-1]]['offset'] = [float(x) for x in t[1:4]]
        elif t[0] == 'CHANNELS':
            joints[stack[-1]]['channels'] = t[2:]
    lines = [l for l in motion.splitlines() if l.strip()]
    frames = int(re.search(r'Frames:\s*(\d+)', motion).group(1))
    dt = float(re.search(r'Frame Time:\s*([0-9.eE+-]+)', motion).group(1))
    data = [[float(x) for x in l.split()] for l in lines[2:2 + frames]]
    return joints, data, dt


def rot(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    if axis == 'X':
        return [[1, 0, 0], [0, c, -s], [0, s, c]]
    if axis == 'Y':
        return [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    return [[c, -s, 0], [s, c, 0], [0, 0, 1]]


def mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def apply(m, v):
    return [sum(m[i][k] * v[k] for k in range(3)) for i in range(3)]


def positions(joints, row, scale):
    out, mats, i = [], [], 0
    for j in joints:
        local = list(j['offset'] or [0, 0, 0])
        m = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
        for ch in j['channels']:
            v = row[i]
            i += 1
            if ch.endswith('position'):
                local['XYZ'.index(ch[0])] = v
            else:
                m = mul(m, rot(ch[0], v))
        if j['parent'] < 0:
            p, M = local, m
        else:
            pp, pm = out[j['parent']], mats[j['parent']]
            p = [a + b for a, b in zip(pp, apply(pm, local))]
            M = mul(pm, m)
        out.append(p)
        mats.append(M)
    return [[c * scale for c in p] for p in out]


def stats(path, scale):
    joints, data, dt = parse(path)
    names = [j['name'].lower() for j in joints]
    hips = 0
    feet = [k for k, n in enumerate(names) if re.search(r'(foot|toe)', n)]
    pos = [positions(joints, r, scale) for r in data]
    floor = min(min(p[k][1] for k in feet) for p in pos) if feet else 0.0
    hy = [p[hips][1] - floor for p in pos]
    lift = [min(p[k][1] for k in feet) - floor for p in pos] if feet else [0] * len(pos)
    off = [i for i, l in enumerate(lift) if l > 0.05]
    speed = []
    for a, b in zip(pos, pos[1:]):
        s = sum(math.dist(x, y) for x, y in zip(a, b) if x is not None) / len(a)
        speed.append(s / dt)
    travel = [pos[-1][hips][i] - pos[0][hips][i] for i in range(3)]
    return {
        'name': os.path.splitext(os.path.basename(path))[0], 'seconds': round(len(data) * dt, 3), 'frames': len(data),
        'hips': (round(hy[0], 2), round(min(hy), 2), round(max(hy), 2), round(hy[-1], 2)),
        'travel': (round(travel[2], 2), round(travel[0], 2)), 'feet_max': round(max(max(p[k][1] for k in feet) - floor for p in pos), 2) if feet else 0,
        'off': (off[0], off[-1]) if off else None, 'motion': round(sum(speed) / max(1, len(speed)), 2),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('paths', nargs='+')
    ap.add_argument('--scale', type=float, default=0.01)
    a = ap.parse_args()
    files = []
    for p in a.paths:
        files += sorted(glob.glob(os.path.join(p, '*.bvh'))) if os.path.isdir(p) else [p]
    print(f"{'clip':22s} {'sec':>5s} {'hips start/min/max/end':>24s} {'fwd':>6s} {'side':>6s} {'feet':>5s} {'off frames':>11s} {'move':>5s}")
    for f in files:
        s = stats(f, a.scale)
        off = f"{s['off'][0]}-{s['off'][1]}" if s['off'] else '-'
        print(f"{s['name']:22s} {s['seconds']:5.2f} {str(s['hips']):>24s} {s['travel'][0]:6.2f} {s['travel'][1]:6.2f} {s['feet_max']:5.2f} {off:>11s} {s['motion']:5.2f}")


if __name__ == '__main__':
    main()
