"""
Which share of the bend the unmapped spine and neck joints should take back at run time (RoeUeRig), judged by where it
puts her upper chest and head: forward kinematics of pelvis -> spine_01..05 -> neck_01 -> neck_02 -> head with the game's
local rotations (all joints) against the humanoid's (the unmapped joint at its bind rotation, the mapped joint above it
carrying the pair's bend) with each share of it given back to the unmapped joint.

  python tools/vdf_research/spine_share_sim.py [--only REGEX]
"""
import argparse
import glob
import os
import re
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from twist_spine_stats import SRC, SKIP, qinv, qmul, qpow  # noqa: E402

CHAIN = ["pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "spine_05", "neck_01", "neck_02", "head"]
PAIRS = {"spine_02": "spine_03", "spine_04": "spine_05", "neck_02": "head"}     # unmapped joint -> the mapped one above it


def read(path):
    data = open(path, "rb").read()
    off, chunks = 0, {}
    while off < len(data):
        cid, _, size, count = struct.unpack_from("<20siii", data, off)
        chunks[cid.split(b"\0")[0].decode()] = (off + 32, size, count)
        off += 32 + size * count
    o, size, count = chunks["BONENAMES"]
    names, bq, bp = [], [], []
    for i in range(count):
        b = o + i * size
        names.append(data[b:b + 64].split(b"\0")[0].decode())
        bq.append(struct.unpack_from("<4f", data, b + 76))
        bp.append(struct.unpack_from("<3f", data, b + 92))
    o, size, count = chunks["ANIMKEYS"]
    keys = np.frombuffer(data, dtype=np.float32, count=count * 8, offset=o).reshape(-1, len(names), 8).astype(np.float64)
    q = keys[:, :, 3:7].copy()
    q[:, :, 1] *= -1.0
    return names, np.array(bq), np.array(bp), q


def rotate(q, v):
    u = q[..., :3]
    w = q[..., 3:4]
    t = 2.0 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


def fk(local_q, offsets):
    """world positions of the chain (pelvis frame at the origin), local_q: [frames, joints, 4]"""
    n = local_q.shape[1]
    world_q = local_q[:, 0].copy()
    pos = [np.zeros((local_q.shape[0], 3))]
    for j in range(1, n):
        pos.append(pos[-1] + rotate(world_q, np.broadcast_to(offsets[j], pos[-1].shape)))
        world_q = qmul(world_q, local_q[:, j])
    return np.stack(pos, 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(SRC, "**", "*.psa"), recursive=True))
    files = [f for f in files if (re.search(a.only, os.path.basename(f), re.I) if a.only else not re.search(SKIP, os.path.splitext(os.path.basename(f))[0]))]
    grid = np.linspace(0.0, 1.0, 11)
    # error of spine_05 and head per (pair, share) with the other pairs at their humanoid state (share 0) and all shares fitted jointly
    combos = [(s2, s4, sn) for s2 in grid for s4 in grid for sn in (0.0, 0.3, 0.5)]
    err = np.zeros((len(combos), 2))
    frames = 0
    for path in files:
        names, bq, bp, q = read(path)
        idx = {n: i for i, n in enumerate(names)}
        if not all(c in idx for c in CHAIN):
            continue
        ci = [idx[c] for c in CHAIN]
        lq = q[:, ci].copy()
        lq[:, 0] = np.array([0, 0, 0, 1.0])          # pelvis frame
        offsets = bp[ci]
        truth = fk(lq, offsets)
        for k, (s2, s4, sn) in enumerate(combos):
            hq = lq.copy()
            for low, s in (("spine_02", s2), ("spine_04", s4), ("neck_02", sn)):
                il, ih = CHAIN.index(low), CHAIN.index(PAIRS[low])
                b2, b3 = bq[ci[il]], bq[ci[ih]]
                e = qmul(qinv(qmul(b2, b3)), qmul(lq[:, il], lq[:, ih]))     # the pair's bend in the upper bind frame
                e_lo = qmul(qmul(b3, e), qinv(b3))
                hq[:, il] = qmul(b2, qpow(e_lo, s))
                hq[:, ih] = qmul(b3, qpow(e, 1.0 - s))
            got = fk(hq, offsets)
            err[k, 0] += np.linalg.norm(got[:, CHAIN.index("spine_05")] - truth[:, CHAIN.index("spine_05")], axis=1).sum()
            err[k, 1] += np.linalg.norm(got[:, CHAIN.index("head")] - truth[:, CHAIN.index("head")], axis=1).sum()
        frames += len(lq)
    err /= max(1, frames)
    base = combos.index((0.0, 0.0, 0.0))
    best = int(np.argmin(err[:, 0] + err[:, 1]))
    print(f"{len(files)} clips, {frames} frames; mean distance from the game's, cm (spine_05, head)")
    print(f"  humanoid as is (no share given back): {err[base, 0]:.2f}, {err[base, 1]:.2f}")
    print(f"  best shares spine_02 {combos[best][0]:.1f}, spine_04 {combos[best][1]:.1f}, neck_02 {combos[best][2]:.1f}: {err[best, 0]:.2f}, {err[best, 1]:.2f}")
    for s2, s4, sn in [(0.5, 0.5, 0.5), (0.2, 0.75 if 0.75 in grid else 0.8, 0.3), (0.3, 0.7, 0.3)]:
        k = min(range(len(combos)), key=lambda i: abs(combos[i][0] - s2) + abs(combos[i][1] - s4) + abs(combos[i][2] - sn))
        print(f"  shares {combos[k][0]:.1f} / {combos[k][1]:.1f} / {combos[k][2]:.1f}: {err[k, 0]:.2f}, {err[k, 1]:.2f}")


if __name__ == "__main__":
    sys.exit(main())
