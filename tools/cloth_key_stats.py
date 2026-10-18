"""What a ROE character's own game clips do with her loose bones: per clip, how far her skirt, sashes and hair swing.

    python tools/cloth_key_stats.py g04 [--kinds skirt,ribbon,hair]

The game has no physics in battle: every clip keys these bones by hand (docs/inase-skirt.md).  The keys come from the game's
bundles through skirt_key_models.load (ripper_tpose's decode_roe_clip.py, read only; cached in _work/skirt_keys_<id>.json).
Each bone of a kind (the bone cloth's name rules, RoeBoneCloth.Kinds) is measured as the fighter's cloth checks measure it:
the direction to its first child in the pelvis frame, against the same direction in her battle stance (idle_01's first
frame; the bind pose if she has no idle_01) - how far the clip swings it out from how it hangs when she stands.  Per clip and kind: the
largest swing and its bone, and the mean over the kind's bones of each one's largest swing.
"""
import re
import sys

import numpy as np

import skirt_key_models as skm

KINDS = [  # RoeBoneCloth.Kinds, first match wins
    ("chain", re.compile(r"skirt_chain|^chain\d|chain_arm|dec_wrist|^tassel_", re.I)),
    ("skirt", re.compile(r"skirt", re.I)),
    ("hair", re.compile(r"hair|braid|bangs|hari", re.I)),
    ("ribbon", re.compile(r"riband|ribbon|^sleeve_", re.I)),
    ("breast", re.compile(r"breast|chest_[lr]", re.I)),
]
ORDER = ["idle_01", "idle_02", "idle_ur01", "react_01", "react_02", "hurt", "skill_01", "skill_02", "skill_03", "die", "rip"]


def kind_of(name):
    for kind, rx in KINDS:
        if rx.search(name):
            return kind
    return None


def main():
    cid = sys.argv[1] if len(sys.argv) > 1 else "g04"
    kinds = ["skirt", "ribbon", "hair"]
    if "--kinds" in sys.argv:
        kinds = sys.argv[sys.argv.index("--kinds") + 1].split(",")
    data = skm.load(cid)
    bones = data["bones"]
    names = [b["name"] for b in bones]
    pelvis = names.index("Bip001 Pelvis")
    child = {}
    for j, b in enumerate(bones):
        p = b["parent"]
        if p is not None and p >= 0 and p not in child and np.linalg.norm(b["pos"]) > 1e-4:
            child[p] = j
    groups = {k: [i for i, n in enumerate(names) if kind_of(n) == k and i in child] for k in kinds}
    print(f"{cid}: " + ", ".join(f"{k} {len(v)} bones" for k, v in groups.items()))

    # the reference: her battle stance (idle_01's first frame), else the bind pose
    if "idle_01" in data["clips"]:
        R0, P0 = skm.pose(data, "idle_01")
        G0, _ = skm.fk(bones, R0[:1], P0[:1])
    else:
        G0, _ = skm.fk(bones, np.array([b["rot"] for b in bones], float)[None], np.array([b["pos"] for b in bones], float)[None])

    def dirs(G, i):
        off = np.array(bones[child[i]]["pos"], float)
        return skm.qrot(skm.qinv(G[:, pelvis]), skm.qrot(G[:, i], off))

    clips = [c for c in ORDER if c in data["clips"]] + sorted(c for c in data["clips"] if c not in ORDER)
    print(f"\nswing against her battle stance, pelvis frame (deg): largest (its bone) / mean of each bone's largest")
    print(f"  {'clip':10} {'length':>6}  " + "  ".join(f"{k:>34}" for k in kinds))
    for c in clips:
        R, P = skm.pose(data, c)
        G, _ = skm.fk(bones, R, P)
        cells = []
        for k in kinds:
            if not groups[k]:
                cells.append(f"{'-':>34}")
                continue
            peaks = []
            for i in groups[k]:
                a, b = dirs(G, i), dirs(G0, i)
                cos = (a * b).sum(1) / (np.linalg.norm(a, axis=1) * np.linalg.norm(b, axis=1) + 1e-12)
                peaks.append(float(np.degrees(np.arccos(np.clip(cos, -1, 1))).max()))
            top = int(np.argmax(peaks))
            cells.append(f"{peaks[top]:5.0f} ({names[groups[k][top]][:18]:>18}) / {np.mean(peaks):4.0f}")
        print(f"  {c:10} {len(R) / skm.FPS:5.1f}s  " + "  ".join(cells))


if __name__ == "__main__":
    main()
