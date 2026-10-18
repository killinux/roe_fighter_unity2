"""Does Rise of Eros simulate anything in battle?  Every bundle the battle loads, scanned for physics (read only).

    python tools/roe_physics_scan.py [family ...] [--workers 8] [--limit-minutes 20]
    python tools/roe_physics_scan.py --report

Families (bundle name prefixes) default to what a battle loads: the stages (scene_battlefield_*, scene_bind_battlefield_*),
their environment pieces (env_level*), the fighters (chara_armor_*, chara_enemy_*), what they wear (accessory*), the skills'
data and effects (gameplay_skill_*, vfx_*), and for comparison the lobby (meta_armor_*, meta_bare_*).  In each bundle:
  - Unity's physics components: Rigidbody, colliders, joints, Cloth (Unity's own cloth), WindZone, ConstantForce,
    CharacterController (and the 2D ones);
  - scripts (MonoBehaviour -> MonoScript class name): Magica Cloth 2's components (MagicaCloth, Magica*Collider,
    MagicaWindZone, MagicaSettings) and any other whose name sounds like physics (spring, jiggle, dynamic bone, sway, wind,
    ragdoll, rope, chain, verlet, physics, cloth, bounce, wobble, swing);
  - particle systems whose collision or external-forces module is on (particles that bounce off the floor or feel wind);
  - Animators and animation clips, for scale.
Bundles are found in the install folder and the game's download folder (the newer copy of a name wins).  Results append
to _work/roe_physics_scan/scan.jsonl (resumable); --report prints the summary per family.
"""
import argparse
import collections
import json
import multiprocessing as mp
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
OUT = os.path.join(PROJECT, "_work", "roe_physics_scan")
DIRS = [r"D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles",
        os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\AssetBundles")]
FAMILIES = ["scene_battlefield_", "scene_bind_battlefield_", "env_level", "chara_armor_", "chara_enemy_", "accessory",
            "gameplay_skill_", "vfx_", "meta_armor_", "meta_bare_"]
PHYSICS_TYPES = {"Rigidbody", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider", "TerrainCollider", "WheelCollider",
                 "CharacterJoint", "HingeJoint", "SpringJoint", "FixedJoint", "ConfigurableJoint", "ArticulationBody", "Cloth",
                 "WindZone", "ConstantForce", "CharacterController", "Rigidbody2D", "BoxCollider2D", "CircleCollider2D",
                 "CapsuleCollider2D", "PolygonCollider2D", "EdgeCollider2D", "HingeJoint2D", "SpringJoint2D", "DistanceJoint2D"}
PHYSICS_WORDS = re.compile(r"magica|spring|jiggle|dynamic.?bone|sway|wind|ragdoll|rope|chain|verlet|physic|cloth|bounce|wobble|"
                           r"swing|collider|rigid|joint", re.I)
# named like physics but simulate nothing: input raycasts for clicks and gestures, the camera kept out of walls
NOT_SIMULATION = re.compile(r"Raycaster|CinemachineCollider|CinemachineConfiner|CinemachineImpulse|Cinemachine.*Impulse|"
                            r"MaterialPropertyLooper", re.I)       # (a material animator: "pROPErty")


def bundles(prefixes):
    found = {}
    for root in DIRS:
        if not os.path.isdir(root):
            continue
        for d, _dirs, files in os.walk(root):
            for f in files:
                if f.endswith(".ab") and any(f.startswith(p) for p in prefixes):
                    p = os.path.join(d, f)
                    if f not in found or os.path.getmtime(p) > os.path.getmtime(found[f]):
                        found[f] = p
    return [found[k] for k in sorted(found)]


def scan(path):
    import UnityPy
    out = {"bundle": os.path.basename(path), "types": {}, "scripts": {}, "physics_scripts": {}, "particles": 0,
           "particle_collision": 0, "particle_forces": 0, "animators": 0, "clips": 0}
    try:
        env = UnityPy.load(path)
        script_names = {}
        for obj in env.objects:
            t = obj.type.name
            if t in PHYSICS_TYPES:
                out["types"][t] = out["types"].get(t, 0) + 1
            elif t == "Animator":
                out["animators"] += 1
            elif t == "AnimationClip":
                out["clips"] += 1
            elif t == "MonoScript":
                try:
                    s = obj.read()
                    script_names[obj.path_id] = (getattr(s, "m_Namespace", "") + "." + s.m_ClassName).strip(".")
                except Exception:
                    pass
        for obj in env.objects:
            t = obj.type.name
            if t == "MonoBehaviour":
                name = "?"
                try:
                    mb = obj.read(check_read=False) if "check_read" in obj.read.__code__.co_varnames else obj.read()
                    ptr = mb.m_Script
                    if ptr.path_id in script_names and ptr.file_id == 0:
                        name = script_names[ptr.path_id]
                    else:
                        try:
                            name = ptr.read().m_ClassName
                        except Exception:
                            name = f"(external {ptr.file_id}:{ptr.path_id})"
                except Exception:
                    name = "(unreadable)"
                out["scripts"][name] = out["scripts"].get(name, 0) + 1
                if PHYSICS_WORDS.search(name):
                    out["physics_scripts"][name] = out["physics_scripts"].get(name, 0) + 1
            elif t == "ParticleSystem":
                out["particles"] += 1
                try:
                    tree = obj.read_typetree()
                    if tree.get("CollisionModule", {}).get("enabled"):
                        out["particle_collision"] += 1
                    if tree.get("ExternalForcesModule", {}).get("enabled"):
                        out["particle_forces"] += 1
                except Exception:
                    pass
    except Exception as e:
        out["error"] = f"{type(e).__name__}: {e}"
    return out


def family_of(name):
    for p in FAMILIES:
        if name.startswith(p):
            return p.rstrip("_")
    return "other"


def report():
    path = os.path.join(OUT, "scan.jsonl")
    rows = [json.loads(l) for l in open(path, encoding="utf-8")]
    fam = collections.OrderedDict()
    for r in rows:
        f = fam.setdefault(family_of(r["bundle"]), dict(bundles=0, errors=0, physics=0, types=collections.Counter(), scripts=collections.Counter(),
                                                        physics_scripts=collections.Counter(), with_physics=[], particles=0,
                                                        particle_collision=0, particle_forces=0, animators=0, clips=0))
        f["bundles"] += 1
        f["errors"] += "error" in r
        f["types"].update(r["types"])
        f["scripts"].update(r["scripts"])
        f["physics_scripts"].update(r["physics_scripts"])
        for k in ("particles", "particle_collision", "particle_forces", "animators", "clips"):
            f[k] += r[k]
        simulating = {k: v for k, v in r["physics_scripts"].items() if not NOT_SIMULATION.search(k)}
        if r["types"] or simulating or r["particle_collision"] or r["particle_forces"]:
            f["physics"] += 1
            f["with_physics"].append(r["bundle"])
    for name, f in fam.items():
        print(f"\n== {name}: {f['bundles']} bundles ({f['errors']} unreadable), {f['physics']} with anything physical; "
              f"{f['animators']} Animators, {f['clips']} clips, {f['particles']} particle systems "
              f"({f['particle_collision']} colliding, {f['particle_forces']} with external forces)")
        if f["types"]:
            print("   Unity physics components: " + ", ".join(f"{k} {v}" for k, v in f["types"].most_common()))
        sim = [(k, v) for k, v in f["physics_scripts"].most_common() if not NOT_SIMULATION.search(k)]
        other = [(k, v) for k, v in f["physics_scripts"].most_common() if NOT_SIMULATION.search(k)]
        if sim:
            print("   physics-like scripts: " + ", ".join(f"{k} {v}" for k, v in sim))
        if other:
            print("   named like physics, simulate nothing: " + ", ".join(f"{k} {v}" for k, v in other))
        top = [k for k, _ in f["scripts"].most_common(12)]
        if top:
            print("   most common scripts: " + ", ".join(f"{k} {f['scripts'][k]}" for k in top))
        if f["with_physics"]:
            print("   bundles: " + ", ".join(f["with_physics"][:12]) + (" ..." if len(f["with_physics"]) > 12 else ""))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("families", nargs="*")
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--limit-minutes", type=float, default=20)
    ap.add_argument("--report", action="store_true")
    a = ap.parse_args()
    if a.report:
        report()
        return
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "scan.jsonl")
    done = set()
    if os.path.exists(path):
        done = {json.loads(l)["bundle"] for l in open(path, encoding="utf-8")}
    todo = [p for p in bundles(a.families or FAMILIES) if os.path.basename(p) not in done]
    print(f"{len(todo)} bundles to scan ({len(done)} done before)", flush=True)
    t0 = time.time()
    with mp.Pool(a.workers) as pool, open(path, "a", encoding="utf-8") as fh:
        for k, r in enumerate(pool.imap_unordered(scan, todo, chunksize=2)):
            fh.write(json.dumps(r, ensure_ascii=False) + "\n")
            fh.flush()
            if (k + 1) % 100 == 0:
                print(f"  {k + 1}/{len(todo)} in {time.time() - t0:.0f} s", flush=True)
            if time.time() - t0 > a.limit_minutes * 60:
                print("time limit - run again to go on", flush=True)
                pool.terminate()
                break
    print(f"done in {time.time() - t0:.0f} s", flush=True)


if __name__ == "__main__":
    main()
