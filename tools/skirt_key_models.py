"""How a ROE character's hand-keyed skirt relates to her legs: models fitted on some of her game clips, judged on the others.

    python tools/skirt_key_models.py [a08]

Rise of Eros keys its skirts by hand in every clip (no physics in battle; docs/inase-skirt.md).  Here the keys are read from
the game's bundles (ripper_tpose's decode_roe_clip.py, read only; cached in _work/skirt_keys_<id>.json) and each skirt bone's
local rotation (against its rest, as a rotation vector) is predicted from the thighs' rotations against the pelvis - plus,
as variants, the pelvis against gravity and the thighs' angular velocity:
  pinned   the rest pose (the skirt not moved at all)
  linear   a linear map (ridge)
  ramps    a ramp per direction, max(0, k x angle) - the rule Stellar Blade's skirt Control Rig uses
  knn      the six nearest frames' keys, by distance in the features
The skirt is then posed with the predicted rotations and each bone's direction (to its first skirt child) compared with the
keyed one in the pelvis frame: RMS degrees on clips the model did not see (RoeHelperFit's split, then leave-one-clip-out).
"""
import json
import os
import re
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
RIPPER = r"E:\code\othercode\ripper_tpose\scripts\riseoferos"
sys.path.insert(0, RIPPER)

TRAIN = ["idle_01", "react_01", "skill_01", "skill_03", "hurt"]
TEST = ["idle_02", "react_02", "skill_02"]
FPS = 30.0


# ---- quaternions (x, y, z, w), Unity's convention (q * v rotates v)
def qmul(a, b):
    ax, ay, az, aw = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    bx, by, bz, bw = b[..., 0], b[..., 1], b[..., 2], b[..., 3]
    return np.stack([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz], -1)


def qinv(a):
    return a * np.array([-1, -1, -1, 1.0])


def qrot(q, v):
    u = q[..., :3]
    w = q[..., 3:4]
    t = 2 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


def qlog(q):
    q = q * np.where(q[..., 3:4] < 0, -1, 1)
    v = q[..., :3]
    s = np.linalg.norm(v, axis=-1, keepdims=True)
    ang = 2 * np.arctan2(s, q[..., 3:4])
    return np.where(s > 1e-9, v / np.maximum(s, 1e-12) * ang, 2 * v)


def qexp(r):
    a = np.linalg.norm(r, axis=-1, keepdims=True)
    axis = np.where(a > 1e-9, r / np.maximum(a, 1e-12), 0)
    return np.concatenate([axis * np.sin(a / 2), np.cos(a / 2)], -1)


def load(cid):
    cache = os.path.join(PROJECT, "_work", f"skirt_keys_{cid}.json")
    if os.path.exists(cache):
        return json.load(open(cache, encoding="utf-8"))
    import UnityPy
    import decode_roe_clip as dec
    import roe_physics_survey as survey
    bones = dec.read_skeleton(UnityPy.load(survey.one(f"chara_armor_pc_{cid}_hd & ld_hd.ab")), None)
    clips = {}
    # the battle clips are in the LD bundle (a08's is named _prelude), the showcase clips in the HD one
    for pattern in (f"chara_armor_pc_{cid}_hd & ld_ld_prelude.ab", f"chara_armor_pc_{cid}_hd & ld_ld.ab", f"chara_armor_pc_{cid}_hd & ld_hd.ab"):
        try:
            path = survey.one(pattern)
        except SystemExit:
            continue
        env = UnityPy.load(path)
        for clip in env.objects:
            if clip.type.name != "AnimationClip":
                continue
            with tempfile.TemporaryDirectory() as tmp:
                out = os.path.join(tmp, "c.json")
                dec.decode_clip(clip, bones, out, FPS)
                clips[clip.peek_name()] = json.load(open(out, encoding="utf-8"))["frames"]
    data = {"bones": bones, "clips": clips}
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    json.dump(data, open(cache, "w", encoding="utf-8"))
    return data


def pose(data, name):
    """Per frame: local rotations (F, B, 4) and positions (F, B, 3), unkeyed channels at rest."""
    bones = data["bones"]
    frames = data["clips"][name]
    rest_r = np.array([b["rot"] for b in bones], float)
    rest_p = np.array([b["pos"] for b in bones], float)
    R = np.repeat(rest_r[None], len(frames), 0)
    P = np.repeat(rest_p[None], len(frames), 0)
    for f, fr in enumerate(frames):
        for k, ch in fr.items():
            if not k.isdigit():
                continue
            if "rot" in ch:
                R[f, int(k)] = ch["rot"]
            if "pos" in ch:
                P[f, int(k)] = ch["pos"]
    return R, P


def fk(bones, R, P):
    G = np.zeros_like(R)
    T = np.zeros_like(P)
    for i, b in enumerate(bones):
        p = b["parent"]
        if p < 0:
            G[:, i], T[:, i] = R[:, i], P[:, i]
        else:
            G[:, i] = qmul(G[:, p], R[:, i])
            T[:, i] = T[:, p] + qrot(G[:, p], P[:, i])
    return G, T


def rest_world(bones, i):
    q = np.array(bones[i]["rot"], float)
    p = bones[i]["parent"]
    while p >= 0:
        q = qmul(np.array(bones[p]["rot"], float), q)
        p = bones[p]["parent"]
    return q


def features(d, kind):
    if kind == "pose":
        return d["X"]
    if kind == "pose+grav":
        return np.concatenate([d["X"], d["grav"]], 1)
    return np.concatenate([d["X"], d["grav"], 0.05 * d["vel"]], 1)


def ramps(X):
    return np.concatenate([np.maximum(X, 0), np.maximum(-X, 0)], 1)


def fit_linear(X, Y, lam=1e-2):
    A = np.concatenate([X, np.ones((len(X), 1))], 1)
    W = np.linalg.solve(A.T @ A + lam * np.eye(A.shape[1]), A.T @ Y)
    return lambda Z: np.concatenate([Z, np.ones((len(Z), 1))], 1) @ W


def fit_knn(X, Y, k=6):
    mu, sd = X.mean(0), X.std(0) + 1e-6

    def predict(Z):
        Zs, Xs = (Z - mu) / sd, (X - mu) / sd
        d = ((Zs[:, None, :] - Xs[None]) ** 2).sum(-1)
        nn = np.argsort(d, 1)[:, :k]
        w = 1.0 / (np.take_along_axis(d, nn, 1) + 1e-3)
        w /= w.sum(1, keepdims=True)
        return (Y[nn] * w[..., None]).sum(1)
    return predict


def main():
    cid = sys.argv[1] if len(sys.argv) > 1 else "a08"
    data = load(cid)
    bones = data["bones"]
    names = [b["name"] for b in bones]
    idx = {n: i for i, n in enumerate(names)}
    skirt = [i for i, n in enumerate(names) if re.match(r"Skirt_[A-Z]+_\d+$", n)]
    pelvis, lt, rt = idx["Bip001 Pelvis"], idx["Bip001 L Thigh"], idx["Bip001 R Thigh"]
    children = {i: [j for j in skirt if bones[j]["parent"] == i] for i in skirt}
    rest = np.array([bones[i]["rot"] for i in skirt])
    print(f"{cid}: {len(skirt)} skirt bones; clips " + ", ".join(f"{k} {len(v) / FPS:.1f} s" for k, v in data["clips"].items()))

    per = {}
    rest_l = qmul(qinv(rest_world(bones, pelvis)), rest_world(bones, lt))
    rest_r = qmul(qinv(rest_world(bones, pelvis)), rest_world(bones, rt))
    for name in data["clips"]:
        R, P = pose(data, name)
        G, _ = fk(bones, R, P)
        fl = qlog(qmul(qmul(qinv(G[:, pelvis]), G[:, lt]), qinv(rest_l)))
        fr = qlog(qmul(qmul(qinv(G[:, pelvis]), G[:, rt]), qinv(rest_r)))
        X = np.concatenate([fl, fr], 1)
        per[name] = dict(R=R, G=G, X=X, grav=qrot(qinv(G[:, pelvis]), np.array([0, -1.0, 0])),
                         vel=np.gradient(X, axis=0) * FPS, Y=qlog(qmul(qinv(rest[None]), R[:, skirt])))

    def error(d, Yhat):
        R = d["R"].copy()
        R[:, skirt] = qmul(rest[None], qexp(Yhat.reshape(len(R), len(skirt), 3)))
        G = d["G"].copy()
        for i in skirt:
            G[:, i] = qmul(G[:, bones[i]["parent"]], R[:, i])
        errs = []
        for i in skirt:
            off = np.array(bones[children[i][0]]["pos"] if children[i] else bones[i]["pos"], float)
            a = qrot(qinv(d["G"][:, pelvis]), qrot(d["G"][:, i], off))
            b = qrot(qinv(G[:, pelvis]), qrot(G[:, i], off))
            cos = (a * b).sum(1) / (np.linalg.norm(a, axis=1) * np.linalg.norm(b, axis=1) + 1e-12)
            errs.append(np.degrees(np.arccos(np.clip(cos, -1, 1))))
        return np.array(errs).T

    def run(train, test):
        out = {}
        for feat in ("pose", "pose+grav", "pose+vel"):
            Xtr = np.concatenate([features(per[c], feat) for c in train])
            Ytr = np.concatenate([per[c]["Y"].reshape(len(per[c]["Y"]), -1) for c in train])
            lin, ram, knn = fit_linear(Xtr, Ytr), fit_linear(ramps(Xtr), Ytr), fit_knn(Xtr, Ytr)
            for m, f in (("linear", lin), ("ramps", lambda Z: ram(ramps(Z))), ("knn", knn)):
                e = np.concatenate([error(per[c], f(features(per[c], feat))) for c in test])
                out[f"{m} ({feat})"] = float(np.sqrt((e ** 2).mean()))
        e = np.concatenate([error(per[c], np.zeros((len(per[c]["Y"]), len(skirt) * 3))) for c in test])
        out["pinned (rest)"] = float(np.sqrt((e ** 2).mean()))
        return out

    res = run(TRAIN, TEST)
    print(f"\nRoeHelperFit's split (train {' '.join(TRAIN)}; test {' '.join(TEST)}): RMS degrees")
    for k, v in sorted(res.items(), key=lambda kv: kv[1]):
        print(f"  {v:6.1f}  {k}")
    standing = TRAIN + TEST
    table = {}
    for c in standing:
        for k, v in run([x for x in standing if x != c], [c]).items():
            table.setdefault(k, {})[c] = v
    print("\nleave one clip out: RMS degrees on the clip left out")
    print("  " + " ".join(f"{c:>9}" for c in standing) + "      mean  model")
    for k, row in sorted(table.items(), key=lambda kv: np.mean(list(kv[1].values()))):
        print("  " + " ".join(f"{row[c]:9.1f}" for c in standing) + f"  {np.mean(list(row.values())):8.1f}  {k}")


if __name__ == "__main__":
    main()
