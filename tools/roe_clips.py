"""An outfit's game clips decoded onto its hd skeleton, as JSON (what tools/roe_grip.py reads): ripper_tpose's decoder
(scripts/riseoferos decode_roe_clip.py), reading the game's bundles only, writing nothing but the JSON (no VMD, nothing in
the export archive - export_roe_motions.py there does all of that for the outfits it exports).
    python tools/roe_clips.py <id> [<out dir>] [--clips idle_01,skill_01]
Out: _work/roe_clips/<id>/<clip>.json (30 frames a second); bones only the battle rig has (b14's sword) are copied
onto the hd skeleton first (graft).  ROE_RIPPER: the ripper_tpose checkout
(default E:/code/othercode/ripper_tpose); UnityPy needed.
"""
import argparse
import os
import sys

RIPPER = os.environ.get("ROE_RIPPER", r"E:\code\othercode\ripper_tpose")
sys.path.insert(0, os.path.join(RIPPER, "scripts", "riseoferos"))
import UnityPy  # noqa: E402

import decode_roe_clip as dec  # noqa: E402
from roe_motion_common import bundles  # noqa: E402

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def graft(skeleton, ld):
    """The low-detail rig's bones the hd one lacks, appended under their nearest ancestor the hd rig has (by name) with
    their ld local pose, as RoeFighterBuilder.GraftBone does in Unity - b14's sword hangs on Wp_01 under Bip001 Prop1, its
    tassel on Wp_02..04, bones only the battle rig has, so her clips' keys on them were dropped.  They keep their ld path's
    CRC: the battle clips find them by it."""
    have = {}
    for i, b in enumerate(skeleton):
        have.setdefault(b["name"], i)
    added = []
    for b in ld:                                       # parents come before their children (read_skeleton walks down)
        if b["name"] in have or b["parent"] < 0 or ld[b["parent"]]["name"] not in have:
            continue
        bone = {k: v for k, v in b.items() if k not in ("bind", "tf")}
        bone["parent"] = have[ld[b["parent"]]["name"]]
        bone["tf"] = -1
        have[b["name"]] = len(skeleton)
        skeleton.append(bone)
        added.append(bone)
    return added


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("id")
    ap.add_argument("out", nargs="?")
    ap.add_argument("--clips", help="a,b: only these")
    a = ap.parse_args()
    out = a.out or os.path.join(PROJECT, "_work", "roe_clips", a.id)
    found = bundles(a.id)
    if not found.get("showcase"):
        sys.exit("no bundle chara_armor_pc_%s_hd & ld_hd.ab" % a.id)
    envs = {"showcase": UnityPy.load(found["showcase"])}
    if found.get("battle"):
        envs["battle"] = UnityPy.load(found["battle"])
    skeleton = dec.read_skeleton(envs["showcase"])     # the hd prefab's skeleton (the battle clips live with the low-detail one)
    if "battle" in envs:
        for b in graft(skeleton, dec.read_skeleton(envs["battle"])):
            print("bone %s copied from the ld rig under %s" % (b["name"], skeleton[b["parent"]]["name"]))
    os.makedirs(out, exist_ok=True)
    wanted = set(a.clips.split(",")) if a.clips else None
    for group, env in envs.items():
        clips = dec.clips_in(env)
        for name, seconds in dec.list_clips(env):
            if wanted and name not in wanted:
                continue
            print(group, dec.decode_clip(clips[name], skeleton, os.path.join(out, name + ".json")), flush=True)


if __name__ == "__main__":
    main()
