"""
How Fiona's Vindictus animations spread a bend over the spine and neck joints a Unity humanoid does not map (spine_02,
spine_04, neck_02), and how her twist bones follow their limbs - read from the .psa clips (tools/vdf_anims.py export).

  python tools/vdf_research/twist_spine_stats.py [--only REGEX]

UE Viewer's keys are Unreal's local rotations mirrored in Y and conjugated (VdfAnims' check); BONENAMES holds the skeleton's
reference pose as Unreal has it.  Bones point along their local +X (Unreal).

Spine: a humanoid puts the whole bend between two mapped joints into the upper one (spine_03 for spine_02 + spine_03).
Spreading it back - the lower joint takes a share s of the pair's bend, the upper one the rest, the upper bone's world
rotation unchanged (RoeUeRig) - is fitted per pair: the s that brings the lower joint closest to the game's.
Twist bones: the twist (about +X) of each, against the twist of its limb bone and of the next joint, fitted as one share.
"""
import argparse
import glob
import os
import re
import struct
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "_work", "vdf", "anim_umodel")
SKIP = r"Interactive|Ladder|Narrow|Campsite|Quick_|Slope|_Jump_|_BIP$|_AS_TEST|Emo_Dance|Emo_Followme|Emo_Rest|Drink_Potion"


def read(path):
    data = open(path, "rb").read()
    off, chunks = 0, {}
    while off < len(data):
        cid, _, size, count = struct.unpack_from("<20siii", data, off)
        chunks[cid.split(b"\0")[0].decode()] = (off + 32, size, count)
        off += 32 + size * count
    o, size, count = chunks["BONENAMES"]
    names, bind = [], []
    for i in range(count):
        b = o + i * size
        names.append(data[b:b + 64].split(b"\0")[0].decode())
        bind.append(struct.unpack_from("<4f", data, b + 76))
    o, size, count = chunks["ANIMKEYS"]
    keys = np.frombuffer(data, dtype=np.float32, count=count * 8, offset=o).reshape(-1, len(names), 8).astype(np.float64)
    q = keys[:, :, 3:7].copy()
    q[:, :, 1] *= -1.0          # mirrored in Y + conjugated -> Unreal's (x, -y, z, w)
    return names, np.array(bind), q


def qmul(a, b):
    ax, ay, az, aw = np.moveaxis(a, -1, 0)
    bx, by, bz, bw = np.moveaxis(b, -1, 0)
    return np.stack([aw * bx + ax * bw + ay * bz - az * by,
                     aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw,
                     aw * bw - ax * bx - ay * by - az * bz], -1)


def qinv(a):
    return a * np.array([-1.0, -1.0, -1.0, 1.0])


def qangle(a):
    return np.degrees(2.0 * np.arccos(np.clip(np.abs(a[..., 3]), 0.0, 1.0)))


def qpow(a, s):
    """slerp(identity, a, s)"""
    a = np.where(a[..., 3:4] < 0, -a, a)
    half = np.arccos(np.clip(a[..., 3], -1.0, 1.0))
    sin = np.sin(half)
    axis = np.where(sin[..., None] > 1e-9, a[..., :3] / np.maximum(sin, 1e-9)[..., None], 0.0)
    return np.concatenate([axis * np.sin(half * s)[..., None], np.cos(half * s)[..., None]], -1)


def twist_x(a):
    """twist about +X in degrees, -180..180"""
    a = np.where(a[..., 3:4] < 0, -a, a)
    return np.degrees(2.0 * np.arctan2(a[..., 0], a[..., 3]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(SRC, "**", "*.psa"), recursive=True))
    files = [f for f in files if (re.search(a.only, os.path.basename(f), re.I) if a.only else not re.search(SKIP, os.path.splitext(os.path.basename(f))[0]))]
    pairs = [("spine_02", "spine_03"), ("spine_04", "spine_05"), ("neck_02", "head")]
    twists = {
        "upperarm_twist_01_l": ("upperarm_l", "lowerarm_l"), "upperarm_twist_02_l": ("upperarm_l", "lowerarm_l"),
        "lowerarm_twist_01_l": ("lowerarm_l", "hand_l"), "lowerarm_twist_02_l": ("lowerarm_l", "hand_l"),
        "thigh_twist_01_l": ("thigh_l", "calf_l"), "thigh_twist_02_l": ("thigh_l", "calf_l"),
        "calf_twist_01_l": ("calf_l", "foot_l"), "calf_twist_02_l": ("calf_l", "foot_l"),
    }
    twists.update({k[:-1] + "r": (v[0][:-1] + "r", v[1][:-1] + "r") for k, v in list(twists.items())})
    grid = np.linspace(0.0, 1.0, 21)
    pair_err = {p: np.zeros(len(grid)) for p in pairs}
    pair_n = {p: 0 for p in pairs}
    pair_bend = {p: [] for p in pairs}
    tw = {k: {"self": [], "limb": [], "next": []} for k in twists}
    for path in files:
        names, bind, q = read(path)
        idx = {n: i for i, n in enumerate(names)}
        if not all(b in idx for p in pairs for b in p):
            print("  (not her body skeleton, left out:", os.path.basename(path), len(names), "bones)")
            continue
        for lo, hi in pairs:
            i2, i3 = idx[lo], idx[hi]
            b2, b3 = bind[i2], bind[i3]
            q2, q3 = q[:, i2], q[:, i3]
            # the pair's bend in the upper bone's bind frame: E = (b2 b3)^-1 q2 q3
            e = qmul(qinv(qmul(b2, b3)), qmul(q2, q3))
            pair_bend[(lo, hi)].append(qangle(e))
            e_lo = qmul(qmul(b3, e), qinv(b3))     # the same rotation in the lower bone's frame
            for k, s in enumerate(grid):
                want = qmul(b2, qpow(e_lo, s))
                pair_err[(lo, hi)][k] += qangle(qmul(qinv(want), q2)).sum()
            pair_n[(lo, hi)] += len(q2)
        for t, (limb, nxt) in twists.items():
            if t not in idx:
                continue
            it, il, inx = idx[t], idx[limb], idx[nxt]
            dt = qmul(qinv(bind[it]), q[:, it])
            dl = qmul(qinv(bind[il]), q[:, il])
            dn = qmul(qinv(bind[inx]), q[:, inx])
            tw[t]["self"].append(twist_x(dt))
            tw[t]["limb"].append(twist_x(dl))
            tw[t]["next"].append(twist_x(dn))
            tw[t].setdefault("swing", []).append(qangle(qmul(dt, qinv(qpow(np.concatenate([np.sin(np.radians(twist_x(dt))[..., None] / 2) * np.array([1.0, 0, 0]), np.cos(np.radians(twist_x(dt))[..., None] / 2)], -1), 1.0)))))
    print(f"{len(files)} clips")
    print("\nSPINE / NECK: the lower joint's share s of the pair's bend (mean angle error of the lower joint, degrees)")
    for p in pairs:
        err = pair_err[p] / max(1, pair_n[p])
        best = int(np.argmin(err))
        bend = np.concatenate(pair_bend[p])
        print(f"  {p[0]:9s} + {p[1]:9s}: bend mean {bend.mean():5.1f} deg, 95% {np.percentile(bend, 95):5.1f}; "
              f"s=0 (humanoid as is) {err[0]:5.2f} deg, best s={grid[best]:.2f} {err[best]:5.2f} deg, s=0.5 {err[10]:5.2f} deg")
    print("\nTWIST BONES: own twist about the bone (deg) against the limb bone's and the next joint's twist")
    for t in sorted(tw):
        if not tw[t]["self"]:
            continue
        s, l, n = (np.concatenate(tw[t][k]) for k in ("self", "limb", "next"))
        sw = np.concatenate(tw[t]["swing"])
        res = []
        for name, x in (("limb", l), ("next", n)):
            k = float(np.dot(x, s) / max(1e-9, np.dot(x, x)))
            r = s - k * x
            res.append(f"{name}: share {k:+.2f}, left {np.sqrt(np.mean(r * r)):5.2f}")
        # both together
        A = np.stack([l, n], 1)
        coef, *_ = np.linalg.lstsq(A, s, rcond=None)
        r = s - A @ coef
        print(f"  {t:22s} twist rms {np.sqrt(np.mean(s * s)):6.2f} (swing mean {sw.mean():5.2f}); {res[0]}; {res[1]}; "
              f"both {coef[0]:+.2f}/{coef[1]:+.2f} left {np.sqrt(np.mean(r * r)):5.2f}")


if __name__ == "__main__":
    sys.exit(main())
