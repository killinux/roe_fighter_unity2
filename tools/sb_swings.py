"""The sword swings in a Stellar Blade character's clips, from the game's keys (user 10-18, Raven's strikes): forward
kinematics of her skeleton (.pskx) posed by each clip (tools/sb_anims.py's .psa: keys read mirrored in Y with the children
conjugated - the reading SbAnims finds), the blade's tip a set length along the prop bone's x (both Eve's sword and
Raven's long sword lie along Bip001-Prop1's x), relative to her Root (the game's travel taken out); per clip the swings
(tip speed over a threshold) with their start, peak and end, the tip's height and how far ahead of her pelvis it reaches.

    python tools/sb_swings.py --skeleton _work/sb/anim/SB/Content/Art/Character/Monster/CH_M_NA_53/CH_M_NA_53_Skeleton.pskx \\
        --folder SB/Content/Art/Character/Monster/CH_M_NA_53/Animation --blade 155.4 M_Raven_Slash M_Raven_SlashCombo

The skeleton: CUE4Parse CLI -p ".../<char>_Skeleton.*" into _work/sb/anim.  Speeds are smoothed over 3 keys; a key where
the prop bone jumps reads as a very fast swing (the times stay right).  Units: cm, m/s, seconds."""
import argparse
import os
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sb_anims  # noqa: E402


def refskel(path):
    data = open(path, "rb").read()
    off = 0
    while off + 32 <= len(data):
        cid = data[off:off + 20].split(b"\0")[0].decode("ascii", "replace")
        _, size, count = struct.unpack_from("<iii", data, off + 20)
        body = off + 32
        if cid == "REFSKELT":
            return [(data[body + i * size: body + i * size + 64].split(b"\0")[0].decode(),
                     struct.unpack_from("<i", data, body + i * size + 72)[0]) for i in range(count)]
        off = body + size * count
    raise SystemExit(f"no skeleton in {path}")


def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def pose(parents, local):
    """component-space rotations and positions; the children's quaternions conjugated (the clips' reading)"""
    R, P = [None] * len(parents), [None] * len(parents)
    for i, (p, q) in enumerate(local):
        m = qmat(q if i == 0 else (-q[0], -q[1], -q[2], q[3]))
        if parents[i] < 0 or i == 0:
            R[i], P[i] = m, np.array(p)
        else:
            j = parents[i]
            R[i], P[i] = R[j] @ m, P[j] + R[j] @ np.array(p)
    return R, P


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("clips", nargs="+")
    ap.add_argument("--skeleton", required=True)
    ap.add_argument("--folder", required=True, help="the clips' game folder (as tools/sb_anims.py --folder)")
    ap.add_argument("--prop", default="Bip001-Prop1")
    ap.add_argument("--blade", type=float, default=155.4, help="the tip's distance along the prop bone's x, cm")
    ap.add_argument("--min-speed", type=float, default=6.0, help="m/s: slower is no swing")
    a = ap.parse_args()
    sb_anims.ANIM = a.folder
    skel = refskel(a.skeleton)
    names = [n for n, _ in skel]
    parents = [p if i > 0 else -1 for i, (_, p) in enumerate(skel)]
    root, prop, pelvis = names.index("Root"), names.index(a.prop), names.index("Bip001-Pelvis")
    for clip in a.clips:
        bones, (rate, frames), keys = sb_anims.read(sb_anims.psa_path(clip))
        order = [bones.index(n) for n in names]
        step = frames / (rate * max(1, frames - 1))
        tips, pel = [], []
        for f in range(frames):
            local = []
            for i in range(len(names)):
                k = sb_anims.key(keys, len(bones), f, order[i])
                local.append((k[:3], k[3:]))
            R, P = pose(parents, local)
            tips.append(P[prop] + R[prop][:, 0] * a.blade - P[root])
            pel.append(P[pelvis] - P[root])
        tips, pel = np.array(tips), np.array(pel)
        v = np.convolve(np.linalg.norm(np.diff(tips, axis=0), axis=1) / step / 100.0, np.ones(3) / 3, mode="same")
        swings, on = [], None
        for i, x in enumerate(np.append(v, 0.0)):
            if x >= a.min_speed and on is None:
                on = i
            elif x < a.min_speed and on is not None:
                swings.append((on, on + int(np.argmax(v[on:i])), i))
                on = None
        print(f"== {clip}: {frames * step:.2f} s, {len(swings)} swings")
        for s0, pk, s1 in swings:
            print(f"   {s0 * step:5.2f}-{s1 * step:5.2f} s, peak {pk * step:5.2f} s {v[pk]:5.1f} m/s, tip height {tips[pk][2]:4.0f} cm, "
                  f"{tips[pk][0] - pel[pk][0]:+4.0f} cm ahead of the pelvis")


if __name__ == "__main__":
    main()
