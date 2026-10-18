"""
Fiona's own animations from Vindictus: Defying Fate (2024-03 pre-alpha, UE 5.3) as ActorX .psa files.

  python tools/vdf_anims.py export [--only REGEX] [--force]
      every AnimSequence of her inventory (tools/vdf_research/anim_inventory.py -> _work/vdf/research/fiona_animations.json)
      exported with UE Viewer (spiritovod's UE5 build) into _work/vdf/anim_umodel/<package path>.psa; one call per package
      (UE Viewer takes no wildcards).  Facial clips are left out (her face is not animated in the fight).
  python tools/vdf_anims.py list
      the exported clips: frames, length, how far the root travels and turns.

The pak key is handed to UE Viewer as a file (-aes=@file) and never printed.  Game data: personal use only, never committed.
"""
import argparse
import json
import os
import re
import struct
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORK = os.path.join(ROOT, "_work", "vdf")
OUT = os.path.join(WORK, "anim_umodel")
INVENTORY = os.path.join(WORK, "research", "fiona_animations.json")
UMODEL = r"E:\tools\umodel_specific\materials\umodel_materials_ue5.exe"
PAKS = r"E:\tools\vindictus\Vindictus\Content\Paks"
KEYFILE = r"E:\tools\vindictus\_download\aes_key.txt"


def sequences(only=None):
    rows = json.load(open(INVENTORY, encoding="utf-8"))
    out = [r["path"] for r in rows if r["class"] == "AnimSequence" and "/Facial/" not in r["path"]]
    if only:
        out = [p for p in out if re.search(only, p, re.I)]
    return out


def psa_path(package):
    return os.path.join(OUT, *package.split("/")) + ".psa"


def export(args):
    todo = sequences(args.only)
    done = failed = 0
    for i, package in enumerate(todo):
        target = psa_path(package)
        if os.path.exists(target) and not args.force:
            continue
        cmd = [UMODEL, "-game=ue5.3", f"-path={PAKS}", f"-aes=@{KEYFILE}", "-export", f"-out={OUT}", package]
        run = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
        if os.path.exists(target):
            done += 1
        else:
            failed += 1
            tail = [l for l in run.stdout.splitlines() if "aes" not in l.lower() and "key" not in l.lower()][-3:]
            print(f"FAILED {package}: {' | '.join(tail)}")
        if (i + 1) % 20 == 0:
            print(f"  {i + 1}/{len(todo)}", flush=True)
    print(f"{done} exported, {failed} failed, {len(todo) - done - failed} already there ({len(todo)} sequences) -> {OUT}")


def read_psa(path, keys=True):
    """bones [(name, q, p)], frames, rate, keys as a flat float list (frame, bone, [px py pz qx qy qz qw time])."""
    data = open(path, "rb").read()
    off, chunks = 0, {}
    while off < len(data):
        cid, _, size, count = struct.unpack_from("<20siii", data, off)
        chunks[cid.split(b"\0")[0].decode()] = (off + 32, size, count)
        off += 32 + size * count
    o, size, count = chunks["BONENAMES"]
    bones = []
    for i in range(count):
        b = o + i * size
        bones.append((data[b:b + 64].split(b"\0")[0].decode(), struct.unpack_from("<4f", data, b + 76), struct.unpack_from("<3f", data, b + 92)))
    o, size, count = chunks["ANIMINFO"]
    rate, frames = struct.unpack_from("<f", data, o + 128 + 24)[0], struct.unpack_from("<i", data, o + 128 + 36)[0]
    k = None
    if keys:
        o, size, count = chunks["ANIMKEYS"]
        k = (data, o)
    return bones, frames, rate, k


def key(k, nb, frame, bone):
    data, o = k
    return struct.unpack_from("<8f", data, o + (frame * nb + bone) * 32)


def list_clips(args):
    rows = []
    for package in sequences(args.only):
        path = psa_path(package)
        if not os.path.exists(path):
            continue
        bones, frames, rate, k = read_psa(path)
        nb = len(bones)
        r0, r1 = key(k, nb, 0, 0), key(k, nb, frames - 1, 0)
        travel = [(r1[i] - r0[i]) / 100.0 for i in range(3)]
        rows.append((package.split("/")[-1], frames, frames / rate if rate else 0, travel))
    for name, frames, seconds, t in rows:
        print(f"{name:58s} {frames:5d} fr {seconds:6.2f} s  root travel x {t[0]:+6.2f} y {t[1]:+6.2f} z {t[2]:+6.2f} m")
    print(f"{len(rows)} clips")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    e = sub.add_parser("export")
    e.add_argument("--only")
    e.add_argument("--force", action="store_true")
    l = sub.add_parser("list")
    l.add_argument("--only")
    args = ap.parse_args()
    {"export": export, "list": list_clips}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
