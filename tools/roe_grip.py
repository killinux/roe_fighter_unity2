"""Where a ROE character holds her weapon, from her game's own animation (user 10-06: sword strikes for Inase, a08).

    python tools/roe_grip.py <decoded clip .json> <id> [--frames a-b] [--pair B=A --pair-clip <clip .json>]
                             [--out tools/roe/<id>_grip.json] [--slack 0.01] [--turn 2] [--bones a,b] [--holders a,b]
                             [--burst B=N]

The decoded clip is ripper_tpose's (scripts/riseoferos decode_roe_clip.py; the archive keeps them at
E:/game_export/RiseOfEros/<name>/vmd/<model>/_clips/<clip>.json, tools/roe_clips.py decodes any outfit's into _work):
every bone's local transform per frame on the hd skeleton, as the game plays it.  For each bone whose name looks like a
weapon's (Point007*, *weapon*, *Prop*; --bones names them), its place and turn in each holder's frame over the clip (the
two hands; --holders other bones too); a holder it does not move in (within --slack metres and --turn degrees) holds it.
--burst B=N: B is gone once the clothes burst takes stage N off (b10, Kart: her pistols sit in holsters on her thighs,
which come off in stage 1).
--frames: only these frames of the clip (a08 holds her greatsword two ways: in the battle stance by the pommel, the blade down
on the ground; while she cuts, in skill_01 frames 58-111, the blade out from the thumb's side - the strikes take that one).
--pair B=A: bone B is not held on its own but goes with A as in --pair-clip's first frame (a08's two greatswords are one in her
stance - Point007_R on Point007_L - and split into one per hand in her skills; the strikes keep them one, in the right hand).
Writes {"id", "clip", "frames", "grips": [{"bone", "hand", "position", "rotation", "slack", "turn"}]} in the game's (Unity's) axes:
hand.position + hand.rotation * position, hand.rotation * rotation.  RoeWeaponGrip (Unity) puts it on the fighter.
Measured here and not in Unity: sampled on the imported rig the game's battle stance turns a08's right hand a few degrees
otherwise (the hand is 0.5-1.7 cm off), and her sword's bone sits at its tip 1.23 m away - 15 cm of slack.
"""
import argparse
import json
import math
import os
import re

import numpy as np


def qmul(a, b):
    x1, y1, z1, w1 = a
    x2, y2, z2, w2 = b
    return (w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2, w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
            w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2, w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2)


def qinv(q):
    return (-q[0], -q[1], -q[2], q[3])


def qrot(q, v):
    u = np.array(q[:3])
    w = q[3]
    v = np.array(v, float)
    return 2 * np.dot(u, v) * u + (w * w - np.dot(u, u)) * v + 2 * w * np.cross(u, v)


def world(bones, frame):
    n = len(bones)
    wp, wr = [None] * n, [None] * n
    for i, b in enumerate(bones):
        f = frame.get(str(i))
        pos = f["pos"] if f else b["pos"]
        rot = f["rot"] if f else b["rot"]
        p = b["parent"]
        if p < 0:
            wp[i], wr[i] = np.array(pos, float), tuple(rot)
        else:
            wp[i] = wp[p] + qrot(wr[p], pos)
            wr[i] = qmul(wr[p], tuple(rot))
    return wp, wr


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clip")
    ap.add_argument("id")
    ap.add_argument("--frames", help="a-b: only these frames")
    ap.add_argument("--pair", action="append", default=[], help="B=A: B goes with A as in --pair-clip")
    ap.add_argument("--pair-clip")
    ap.add_argument("--out")
    ap.add_argument("--slack", type=float, default=0.01)
    ap.add_argument("--turn", type=float, default=2.0)
    ap.add_argument("--bones", help="a,b: measure these bones (default: the names that look like a weapon's)")
    ap.add_argument("--holders", default="Bip001 R Hand,Bip001 L Hand",
                    help="the bones that may hold them (b10's pistols sit in holsters: Bip001 R Thigh, Bip001 L Thigh)")
    ap.add_argument("--burst", action="append", default=[],
                    help="B=N: bone B is gone once the clothes burst takes stage N off (b10's pistols go with their holsters)")
    a = ap.parse_args()
    d = json.load(open(a.clip, encoding="utf-8"))
    bones = d["bones"]
    names = [b["name"] for b in bones]
    hands = {n: names.index(n) for n in a.holders.split(",") if n in names}
    weap = ([names.index(n) for n in a.bones.split(",")] if a.bones else
            [i for i, n in enumerate(names) if re.search(r"(?i)^point007|weapon|prop\d", n) and n not in hands])
    frames = d["frames"]
    lo, hi = (int(x) for x in a.frames.split("-")) if a.frames else (0, len(frames) - 1)
    hi = min(hi, len(frames) - 1)
    picks = sorted(set(list(range(lo, hi + 1, max(1, (hi - lo) // 12))) + [hi]))
    poses = [world(bones, frames[f]) for f in picks]
    grips = []
    for w in weap:
        best = None
        for hn, h in hands.items():
            locs = np.array([qrot(qinv(wr[h]), wp[w] - wp[h]) for wp, wr in poses])
            rots = [qmul(qinv(wr[h]), wr[w]) for wp, wr in poses]
            rots = np.array([q if np.dot(q, rots[0]) >= 0 else [-x for x in q] for q in rots])
            slack = float(max(np.linalg.norm(p - q) for p in locs for q in locs))
            turn = float(max(2 * math.degrees(math.acos(min(1.0, abs(float(np.dot(p, q)))))) for p in rots for q in rots))
            if best is None or slack < best[1]:
                q = rots.mean(0)
                q = q / np.linalg.norm(q)
                best = (hn, slack, turn, locs.mean(0), q)
        hn, slack, turn, pos, rot = best
        held = slack <= a.slack and turn <= a.turn
        print(f"{names[w]}: in {hn} {slack * 100:.2f} cm / {turn:.2f} deg over {len(picks)} frames ({lo}-{hi}) of {d.get('clip')}"
              f"{'' if held else ' - not held'}")
        if held:
            grips.append({"bone": names[w], "hand": hn, "position": [round(float(x), 5) for x in pos],
                          "rotation": [round(float(x), 6) for x in rot], "slack": round(slack, 4), "turn": round(turn, 3)})
    for tag in a.burst:
        b_name, stage = tag.split("=")
        for g in grips:
            if g["bone"] == b_name:
                g["burstStage"] = int(stage)
    # weapons that go with another one (one sword of two)
    if a.pair:
        pd = json.load(open(a.pair_clip, encoding="utf-8")) if a.pair_clip else d
        pb = pd["bones"]
        pn = [b["name"] for b in pb]
        wp, wr = world(pb, pd["frames"][0])
        for pair in a.pair:
            b_name, a_name = pair.split("=")
            ga = next((g for g in grips if g["bone"] == a_name), None)
            if ga is None:
                print(f"--pair {pair}: {a_name} is not held")
                continue
            ia, ib = pn.index(a_name), pn.index(b_name)
            rel_p = qrot(qinv(wr[ia]), wp[ib] - wp[ia])
            rel_r = qmul(qinv(wr[ia]), wr[ib])
            pos = np.array(ga["position"]) + qrot(tuple(ga["rotation"]), rel_p)
            rot = qmul(tuple(ga["rotation"]), rel_r)
            grips = [g for g in grips if g["bone"] != b_name]
            grips.append({"bone": b_name, "hand": ga["hand"], "position": [round(float(x), 5) for x in pos],
                          "rotation": [round(float(x), 6) for x in rot], "slack": ga["slack"], "turn": ga["turn"], "with": a_name})
            print(f"{b_name}: with {a_name} as in {pd.get('clip')} (offset {np.round(rel_p, 3)})")
    out = a.out or os.path.join(os.path.dirname(os.path.abspath(__file__)), "roe", f"{a.id}_grip.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    json.dump({"id": a.id, "clip": d.get("clip"), "frames": f"{lo}-{hi}", "source": os.path.basename(a.clip), "grips": grips}, open(out, "w", encoding="utf-8"), indent=1)
    print(f"{len(grips)} grip(s) -> {out}")


if __name__ == "__main__":
    main()
