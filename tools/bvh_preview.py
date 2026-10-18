# Look inside a BVH clip: stick figures over time plus the speed of hands and feet, to find
# where single punches / kicks start and end.
#   python tools/bvh_preview.py <clip.bvh> [--out sheet.png] [--every 6] [--from 0] [--to -1] [--scale 0]
# Prints the speed peaks of each hand and foot (frame, m/s) and draws a sheet: side view (x/y)
# of every n-th frame, numbered, with a speed plot underneath.
# Joint names are recognised the way RoeMocap does it (Bandai "Hand_L", CMU / LAFAN1 / Mixamo
# "LeftHand", 3ds Max "Bip01 L Hand", ASF "lwrist"); units from the leg length unless --scale.
import argparse
import io
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def parse_bvh(path):
    lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
    joints, stack, channels = [], [], []
    i = 0
    while i < len(lines):
        tok = lines[i].split()
        if not tok:
            i += 1
            continue
        if tok[0] in ('ROOT', 'JOINT'):
            joints.append({'name': tok[1], 'parent': stack[-1] if stack else -1, 'offset': None, 'channels': []})
            stack.append(len(joints) - 1)
        elif tok[0] == 'End':
            joints.append({'name': joints[stack[-1]]['name'] + '_end', 'parent': stack[-1], 'offset': None, 'channels': [], 'end': True})
            stack.append(len(joints) - 1)
        elif tok[0] == 'OFFSET':
            joints[stack[-1]]['offset'] = np.array([float(v) for v in tok[1:4]])
        elif tok[0] == 'CHANNELS':
            joints[stack[-1]]['channels'] = tok[2:]
            for c in tok[2:]:
                channels.append((stack[-1], c))
        elif tok[0] == '}':
            stack.pop()
        elif tok[0] == 'MOTION':
            break
        i += 1
    frames = int(lines[i + 1].split()[-1])
    dt = float(lines[i + 2].split()[-1])
    data = np.array([[float(v) for v in l.split()] for l in lines[i + 3:i + 3 + frames] if l.strip()])
    return joints, channels, data, dt


SIDED = {'hipjoint', 'femur', 'tibia', 'foot', 'toes', 'toe', 'clavicle', 'humerus', 'radius', 'wrist', 'hand', 'fingers',
         'thumb', 'shoulder', 'collar', 'arm', 'forearm', 'upleg', 'leg', 'hip', 'thigh', 'calf', 'knee', 'ankle', 'elbow'}


def split_name(name):
    """(side, core) of a joint name, as RoeMocap.Split: 'LeftUpLeg' -> ('L', 'upleg'), 'lfemur' -> ('L', 'femur')."""
    n = name.rsplit(':', 1)[-1]
    n = re.sub(r'^(bip0*1|mixamorig|def)[ _\-.]+', '', n, flags=re.I)
    parts = [p.lower() for p in re.split(r'[ _\-.]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])', n) if p]
    side = ''
    if len(parts) > 1:
        if parts[0] in ('left', 'l', 'right', 'r'):
            side = 'L' if parts.pop(0)[0] == 'l' else 'R'
        elif parts[-1] in ('left', 'l', 'right', 'r'):
            side = 'L' if parts.pop()[0] == 'l' else 'R'
    core = ''.join(parts)
    if not side and len(core) > 1 and core[0] in 'lr' and core[1:] in SIDED:
        side, core = core[0].upper(), core[1:]
    return side, core


def find_bones(joints):
    """Indices of the hips, hands and feet (keys Hips, Hand_L, Hand_R, Foot_L, Foot_R) and the thigh + shin joints."""
    parts = [split_name(j['name']) if not j.get('end') else ('', '') for j in joints]

    def find(side, *cores):
        for c in cores:
            hits = [i for i, p in enumerate(parts) if p == (side, c)]
            if hits:
                return hits[0]
        return None

    def up(i):
        p = joints[i]['parent']
        while p >= 0 and re.search('twist|roll', parts[p][1]):
            p = joints[p]['parent']
        return p

    out = {}
    for side in 'LR':
        hand, foot = find(side, 'wrist', 'hand'), find(side, 'foot', 'ankle')
        if hand is not None:
            out['Hand_' + side] = hand
        if foot is not None:
            out['Foot_' + side] = foot
            out['Shin_' + side] = up(foot)
            out['Thigh_' + side] = up(up(foot))
    chains = []
    for k in ('Thigh_L', 'Thigh_R'):
        if k in out:
            c, i = [], out[k]
            while i >= 0:
                c.insert(0, i)
                i = joints[i]['parent']
            chains.append(c)
    if len(chains) == 2:
        common = [a for a, b in zip(*chains) if a == b]
        out['Hips'] = common[-1]
    return out, parts


def auto_scale(joints, bones):
    """Metres per BVH unit from thigh + shin: cm, m or inches when it fits, else a 0.84 m leg."""
    leg = np.linalg.norm(joints[bones['Shin_L']]['offset']) + np.linalg.norm(joints[bones['Foot_L']]['offset'])
    if 55 < leg < 130:
        return 0.01
    if 0.55 < leg < 1.3:
        return 1.0
    if 22 < leg < 51:
        return 0.0254
    return 0.84 / max(leg, 1e-6)


def rot(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    if axis == 'X':
        return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    if axis == 'Y':
        return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def forward_kinematics(joints, channels, row, scale=0.01):
    """World positions (metres, BVH axes: y up) of every joint for one frame."""
    local_pos = [j['offset'].copy() if j['offset'] is not None else np.zeros(3) for j in joints]
    local_rot = [np.eye(3) for _ in joints]
    for k, (ji, c) in enumerate(channels):
        v = row[k]
        if c.endswith('position'):
            local_pos[ji]['XYZ'.index(c[0])] = v
        else:
            local_rot[ji] = local_rot[ji] @ rot(c[0], v)
    world_pos, world_rot = [None] * len(joints), [None] * len(joints)
    for i, j in enumerate(joints):
        if j['parent'] < 0:
            world_rot[i] = local_rot[i]
            world_pos[i] = local_pos[i]
        else:
            p = j['parent']
            world_rot[i] = world_rot[p] @ local_rot[i]
            world_pos[i] = world_pos[p] + world_rot[p] @ local_pos[i]
    return np.array(world_pos) * scale


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('bvh')
    ap.add_argument('--out', default='')
    ap.add_argument('--every', type=int, default=6)
    ap.add_argument('--from', dest='start', type=int, default=0)
    ap.add_argument('--to', type=int, default=-1)
    ap.add_argument('--scale', type=float, default=0, help='metres per BVH unit (0: from the leg length)')
    a = ap.parse_args()
    joints, channels, data, dt = parse_bvh(a.bvh)
    bones, parts = find_bones(joints)
    scale = a.scale or auto_scale(joints, bones)
    end = len(data) if a.to < 0 else min(a.to, len(data))
    pos = np.array([forward_kinematics(joints, channels, data[f], scale) for f in range(len(data))])
    track = {k: bones[k] for k in ('Hand_L', 'Hand_R', 'Foot_L', 'Foot_R', 'Hips') if k in bones}
    # speeds relative to the hips (strikes, not walking)
    speed = {}
    for k, ji in track.items():
        rel = pos[:, ji] - (pos[:, track['Hips']] if k != 'Hips' else 0)
        v = np.linalg.norm(np.diff(rel, axis=0), axis=1) / dt
        speed[k] = np.concatenate([[0], v])
    print(f'{os.path.basename(a.bvh)}: {len(data)} frames at {1 / dt:.0f} fps, {len(joints)} joints, {scale:.4g} m/unit; '
          + ' '.join(f'{k}={joints[i]["name"]}' for k, i in bones.items()))
    for k in ('Hand_L', 'Hand_R', 'Foot_L', 'Foot_R'):
        if k not in speed:
            continue
        v = speed[k]
        peaks = [f for f in range(2, len(v) - 2) if v[f] == v[f - 2:f + 3].max() and v[f] > 3.0]
        print(f'  {k:7s} peaks (frame, m/s): ' + ' '.join(f'{f}:{v[f]:.1f}' for f in peaks))
    hip = pos[:, track['Hips']]
    print(f'  hips travel: x {hip[-1, 0] - hip[0, 0]:+.2f} z {hip[-1, 2] - hip[0, 2]:+.2f} m')

    if not a.out:
        return
    frames = list(range(a.start, end, a.every))
    cols = 10
    cw, chh = 150, 220
    rows = math.ceil(len(frames) / cols)
    img = Image.new('RGB', (cols * cw, rows * chh + 200), (25, 25, 30))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 14)
    colours = {'L': (90, 160, 255), 'R': (255, 120, 90)}
    for n, f in enumerate(frames):
        ox, oy = (n % cols) * cw + cw // 2, (n // cols) * chh + chh - 20
        p = pos[f]
        root = p[track['Hips']].copy()
        for i, j in enumerate(joints):
            if j['parent'] < 0 or j.get('end'):
                continue
            q = p[j['parent']]
            col = colours.get(parts[i][0], (220, 220, 220))
            # side view: x right (BVH x), y up; also a faint front view z
            x0, y0 = ox + (q[2] - root[2]) * 90, oy - q[1] * 90
            x1, y1 = ox + (p[i][2] - root[2]) * 90, oy - p[i][1] * 90
            d.line([x0, y0, x1, y1], fill=col, width=3)
        d.text((ox - cw // 2 + 4, oy - chh + 24), str(f), fill=(255, 255, 0), font=font)
    # speed plot
    top = rows * chh + 10
    W = cols * cw
    for k, col in (('Hand_L', (90, 160, 255)), ('Hand_R', (255, 120, 90)), ('Foot_L', (90, 255, 160)), ('Foot_R', (255, 220, 90))):
        if k not in speed:
            continue
        v = speed[k][a.start:end]
        pts = [(i * W / max(len(v) - 1, 1), top + 180 - min(v[i], 12) * 15) for i in range(len(v))]
        d.line(pts, fill=col, width=2)
    for f in range(a.start, end, 30):
        x = (f - a.start) * W / max(end - a.start - 1, 1)
        d.line([x, top, x, top + 180], fill=(70, 70, 70))
        d.text((x + 2, top), str(f), fill=(160, 160, 160), font=font)
    img.save(a.out)
    print('  sheet:', a.out)


if __name__ == '__main__':
    main()
