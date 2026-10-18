"""Stellar Blade's own physics for Eve, read out of the game (user 10-05: "物理能用剑星自己的就用自己的，次选项才是用magic cloth2"):

    python tools/sb_physics.py Assets/SB/<id> [--outfit CH_P_EVE_09] [--hair EVE_HR_01] [--dlc DLC_2] [--hair-dlc DLC_2]
    python tools/sb_physics.py Assets/SB/<id> --abp <package> --physics <package>      # a character of her own (Raven)

(--abp / --physics, 10-18: another character - Raven, Monster/CH_M_NA_53 - has one anim blueprint of her own
(CH_M_NA_53_AnimBP: spring bones, KawaiiPhysics nodes) and one physics asset (CH_M_NA_53_Physics), whose bodies the game
leaves non-kinematic simulate - her long hair, 130 bodies on the HBN_* strands - as the "hair" chain, colliding with the
asset's kinematic bodies.  Her control rigs are not ported.)

(--dlc: an outfit of a DLC lives under SB/Content/<dlc>/Art/Character/PC - the NIKKE collaboration's CH_P_EVE_Nikke_06,
10-17; her main blueprint stays the base game's.  --hair-dlc: the same for the hairstyle - Alice's EVE_HR_Nikke_06, whose
twin tails are all KawaiiPhysics (65 nodes in its blueprint) and which has no ponytail component: no rigid ponytail then.)

Everything comes from the game's cooked packages through the CUE4Parse CLI with the community .usmap (-f json, cached in
_work/sb/research/json; exported here when missing).  What the game runs on her (docs/stellar-blade-eve.md):
  - KawaiiPhysics nodes (the plugin, MIT - an older generation than Vindictus': TeleportDistance/Rotation thresholds,
    no simulation space or bone constraints): the ponytail's first two bones in her main anim blueprint
    (CH_P_EVE_01_AnimBP_New: root Ab-TL-HairB01, Ab-TL-HairB03 and below excluded), the four front locks and the two
    side locks in the hair's (EVE_HR_01_AnimBP, its collision capsules in KawaiiLimitsData_Head on the hair's root), the
    tie in the outfit's (CH_P_EVE_09_AnimBP, two nodes down the tie);
  - UE's AnimNode_SpringBone (27 in the main blueprint, SB's own version with bUseLocalSpace / AverageVelocityFrameCount):
    the ones whose bone her outfit has - breasts, the hips' Ab-*-Hip-Reg, the thighs' twist bones, the forearm bands;
  - PhysX rigid bodies (UE physics assets), simulated in the component's local space (bLocalSpaceSimulation on the
    body and the ponytail components): the ponytail from Ab-TL-HairB03 down (EVE_HR_01_Tail_PhysicsAsset: masses
    1.0 -> 0.04 kg, linear / angular damping 5 / 1, cone and twist limits) colliding with the outfit's ponytail set
    (CH_P_EVE_09_PonytailPhysicsAsset on the body bones SB's AdditiveMasterBoneArray names), and the back panels
    Ab_CapeL/R01-07 (CH_P_EVE_09_Physics: 20 kg each, damping 5 / 10, limits 10 degrees) colliding with the body's own
    kinematic bodies.
  - Control Rigs in the outfit's anim blueprint (UE 4.26 RigVM, tools/sb_controlrig.py): the pleated skirt's
    (CH_P_EVE_37_Skirt_CtlRig) turns each panel's root bone by how far the thigh under it is lifted - RoeRigVM runs the
    rig's own byte code.  BtoB_CtrlRig (every outfit: pushes the outfit's breast bones away from the arms) is left out.
Order: a blueprint's nodes run along their pose links (LinkID: an index into the class's anim nodes, in the order the
class default object lists them) from its input pose to its root - the Kawaii nodes and the rigs are written in that
order (Eve 37's outfit: BtoB, the skirt rig, then the skirt panels' Kawaii nodes D, O, E, Q, S, the tie, the strings).
Bone names: the Biped's hyphens become spaces (tools/sb_fbx.py renames her bones so); the hair blueprint's "Root" is the
hair mesh's root (Hair_Root in the merged armature).  Units stay the game's (cm, degrees, UE bone space): RoeSbPhysics
and RoeKawaiiPhysics turn them into Unity's.

Writes <dir>/kawaii.json (RoeKawaiiRig: one entry per Kawaii node, as tools/vdf_kawaii.py) and <dir>/sbphysics.json
(RoeSbRig: springs, bodies, joints, control rigs).
"""
import argparse
import json
import os
import subprocess

import sb_controlrig

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
CLI = r"E:\tools\cue4parse_cli_ff7\cue4parse.exe"
PAKS = r"D:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB\Content\Paks"
USMAP = r"E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap"
JSON_DIR = os.path.join(PROJECT, "_work", "sb", "research", "json")
PC = "SB/Content/Art/Character/PC"


def game_json(package):
    """A package's exports (CUE4Parse JSON), exported first when not cached.  package: SB/Content/... without extension."""
    path = os.path.join(JSON_DIR, package.replace("/", os.sep) + ".json")
    if not os.path.exists(path):
        subprocess.run([CLI, "-i", PAKS, "-g", "GAME_StellarBlade", "-m", USMAP, "-f", "json", "-o", JSON_DIR, "-y",
                        "-p", package + ".*"], capture_output=True)
    if not os.path.exists(path):
        raise SystemExit(f"could not export {package}")
    return json.load(open(path, encoding="utf-8-sig"))


def exists(package):
    """Whether the game has that package (exported, as game_json does, when not cached)."""
    path = os.path.join(JSON_DIR, package.replace("/", os.sep) + ".json")
    if not os.path.exists(path):
        subprocess.run([CLI, "-i", PAKS, "-g", "GAME_StellarBlade", "-m", USMAP, "-f", "json", "-o", JSON_DIR, "-y",
                        "-p", package + ".*"], capture_output=True)
    return os.path.exists(path)


def cdo(package):
    return next(e for e in game_json(package) if e.get("Name", "").startswith("Default__"))["Properties"]


def bone_name(name, root_alias=None):
    if name is None:
        return None
    if name == "Root" and root_alias:
        return root_alias
    return name.replace("-", " ") if name.startswith("Bip001-") else name


def vec(d, keys="XYZ"):
    return [round(float(d.get(k, 0.0)), 6) for k in keys]


def curve(node, field):
    """A Kawaii curve as flat [t0, v0, t1, v1, ...]: the node's own keys, else its external CurveFloat asset's."""
    c = node.get(field) or {}
    keys = (c.get("EditorCurveData") or {}).get("Keys") or []
    if not keys and c.get("ExternalCurve"):
        path = c["ExternalCurve"]["ObjectPath"].split(".")[0]
        asset = game_json("SB/Content/" + path[len("/Game/"):])
        fc = next(e for e in asset if e.get("Type") == "CurveFloat")["Properties"]
        keys = (fc.get("FloatCurve") or {}).get("Keys") or []
    out = []
    for k in keys:
        out += [round(float(k.get("Time", 0.0)), 6), round(float(k.get("Value", 0.0)), 6)]
    return out


def capsules_of(node, root_alias):
    caps = list(node.get("CapsuleLimits") or [])
    caps += node.get("CapsuleLimitsData") or []
    lda = node.get("LimitsDataAsset")
    if lda and not node.get("CapsuleLimitsData"):
        path = lda["ObjectPath"].split(".")[0]
        asset = game_json("SB/Content/" + path[len("/Game/"):])
        props = next(e for e in asset if e.get("Type", "").startswith("KawaiiPhysicsLimitsDataAsset"))["Properties"]
        caps += props.get("CapsuleLimitsData") or []
    out = []
    for c in caps:
        if not c.get("bEnable", True):
            continue
        r = c.get("OffsetRotation") or {}
        out.append({"bone": bone_name(c["DrivingBone"]["BoneName"], root_alias), "radius": round(float(c["Radius"]), 4),
                    "length": round(float(c["Length"]), 4), "offset": vec(c.get("OffsetLocation") or {}),
                    "rotation": [round(float(r.get(k, 0.0)), 4) for k in ("Pitch", "Yaw", "Roll")]})
    return out


def graph_order(props):
    """The anim nodes' names in the order the blueprint runs them: from the input pose along the pose links to the root
    (each node's ComponentPose / LocalPose / Source / Result link holds its input's index among the class's anim nodes)."""
    names = [k for k, v in props.items() if k.startswith("AnimGraphNode_") and isinstance(v, dict)]
    inputs = {}
    for i, k in enumerate(names):
        links = [v["LinkID"] for v in props[k].values() if isinstance(v, dict) and "LinkID" in v]
        inputs[i] = links
    order, seen = [], set()

    def visit(i):
        if i in seen or i < 0 or i >= len(names):
            return
        seen.add(i)
        for j in inputs[i]:
            visit(j)
        order.append(names[i])
    roots = [i for i, k in enumerate(names) if k.startswith("AnimGraphNode_Root")]
    for r in roots:
        visit(r)
    # nodes no link reaches (none in Eve's blueprints) keep the listing's order, after the rest
    return order + [k for k in names if k not in order]


def kawaii_nodes(package, abp, kinds, root_alias=None):
    props = cdo(package)
    nodes = []
    for key in graph_order(props):
        n = props[key]
        if not key.startswith("AnimGraphNode_KawaiiPhysics") or not isinstance(n, dict) or not n.get("bEnabled", True):
            continue
        root = n["RootBone"]["BoneName"]
        ps = n.get("PhysicsSettings") or {}
        g = n.get("Gravity") or {}
        nodes.append({
            "name": key, "abp": abp, "kind": kinds(root), "root": bone_name(root, root_alias),
            "exclude": [bone_name(b["BoneName"], root_alias) for b in n.get("ExcludeBones") or []],
            "damping": ps.get("Damping", 0.1), "stiffness": ps.get("Stiffness", 0.05),
            "worldDampingLocation": ps.get("WorldDampingLocation", 0.8), "worldDampingRotation": ps.get("WorldDampingRotation", 0.8),
            "radius": ps.get("Radius", 3.0), "limitAngle": ps.get("LimitAngle", 0.0),
            "gravity": vec(g), "targetFramerate": float(n.get("TargetFramerate", 60)),
            "dampingCurve": curve(n, "DampingCurveData"), "stiffnessCurve": curve(n, "StiffnessCurveData"),
            "radiusCurve": curve(n, "RadiusCurveData"), "limitAngleCurve": curve(n, "LimitAngleCurveData"),
            "capsules": capsules_of(n, root_alias),
            "game": {"DummyBoneLength": n.get("DummyBoneLength"), "BoneForwardAxis": n.get("BoneForwardAxis"),
                     "WindScale": n.get("WindScale"), "bEnableWind": n.get("bEnableWind"),
                     "TeleportDistanceThreshold": n.get("TeleportDistanceThreshold"),
                     "TeleportRotationThreshold": n.get("TeleportRotationThreshold"),
                     "OverrideTargetFramerate": n.get("OverrideTargetFramerate")},
        })
    return nodes


# the game's control rigs left out, and why
RIGS_LEFT_OUT = {"BtoB_CtrlRig": "pushes the outfit's breast bones away from the upper arms, forearms and hands - "
                                 "not ported: in the merged skeleton the outfit's breasts are the body's"}
# bones a rig writes that it may not here, and why
RIG_SKIP = {"Bip001 Pelvis": "the outfit's own pelvis in the game (its outfit is a mesh of its own): moving it here would "
                             "move her whole body (the skirt rig shifts it along its local Y by 1.2 x how far the thigh "
                             "points sit below the thigh twist bones)"}


def control_rigs(package, abp, have):
    """The outfit blueprint's Control Rig nodes, in its order, as RoeRigVM programs (tools/sb_controlrig.py)."""
    props = cdo(package)
    rigs = []
    for key in graph_order(props):
        n = props[key]
        if not key.startswith("AnimGraphNode_ControlRig") or not isinstance(n, dict) or not n.get("bExecute", True):
            continue
        path = n["ControlRigClass"]["ObjectPath"].rsplit(".", 1)[0]                 # /Game/Art/...
        name = path.rsplit("/", 1)[-1]
        if name in RIGS_LEFT_OUT:
            print(f"control rig {name} ({abp}) left out: {RIGS_LEFT_OUT[name]}")
            continue
        package_path = "SB/Content/" + path[len("/Game/"):]
        game_json(package_path)                                                     # exported when missing
        src = os.path.join(JSON_DIR, package_path.replace("/", os.sep) + ".json")
        variables = dict(zip(n.get("DestPropertyNames") or [], [None] * len(n.get("DestPropertyNames") or [])))
        if variables:
            raise SystemExit(f"control rig {name}: its variables come from the blueprint's pins - not read yet")
        p = sb_controlrig.program(src, bone_name=bone_name)
        bones = {r["bone"] for r in p["registers"] if r["kind"] == "key"}
        missing = sorted(b for b in bones if b not in have)
        if missing:
            print(f"control rig {name} ({abp}) left out: bones not on her rig {missing}")
            continue
        written = sorted({p["registers"][c["args"][0]]["bone"] for c in p["code"] if c["unit"] == "SetTransform"})
        skip = [b for b in written if b in RIG_SKIP]
        p.update({"abp": abp, "node": key, "alpha": float(n.get("Alpha", 1.0)),
                  "kind": "skirt" if any("Skirt" in b for b in written) else "chain",
                  "skip": skip, "skipWhy": [RIG_SKIP[b] for b in skip]})
        rigs.append(p)
        print(f"control rig {name} ({abp}, {key}): {len(p['code'])} steps, writes {written}; skips {skip}")
    return rigs


def springs(package, have):
    out = []
    for key, n in cdo(package).items():
        if not key.startswith("AnimGraphNode_SpringBone") or not isinstance(n, dict):
            continue
        bone = bone_name(n["SpringBone"]["BoneName"])
        if bone not in have:
            continue
        out.append({
            "name": key, "bone": bone, "kind": "breast" if "Breast" in bone else "chain" if "Acc" in bone else "body",
            "maxDisplacement": float(n.get("MaxDisplacement", 0.0)), "stiffness": float(n.get("SpringStiffness", 50.0)),
            "damping": float(n.get("SpringDamping", 4.0)), "errorResetThresh": float(n.get("ErrorResetThresh", 256.0)),
            "limitDisplacement": bool(n.get("bLimitDisplacement", False)),
            "translate": [bool(n.get("bTranslate" + a, True)) for a in "XYZ"],
            "rotate": [bool(n.get("bRotate" + a, False)) for a in "XYZ"],
            "localSpace": bool(n.get("bUseLocalSpace", False)), "alpha": float(n.get("Alpha", 1.0)),
            "averageVelocityFrames": int(n.get("AverageVelocityFrameCount", 0)),
        })
    return out


def shape_list(agg):
    out = []
    for s in agg.get("SphylElems") or []:
        out.append({"type": "capsule", "center": vec(s["Center"]), "rotation": [s["Rotation"].get(k, 0.0) for k in ("Pitch", "Yaw", "Roll")],
                    "radius": s["Radius"], "length": s["Length"]})
    for s in agg.get("TaperedCapsuleElems") or []:
        out.append({"type": "capsule", "center": vec(s["Center"]), "rotation": [s["Rotation"].get(k, 0.0) for k in ("Pitch", "Yaw", "Roll")],
                    "radius": 0.5 * (s["Radius0"] + s["Radius1"]), "length": s["Length"], "tapered": [s["Radius0"], s["Radius1"]]})
    for s in agg.get("SphereElems") or []:
        out.append({"type": "sphere", "center": vec(s["Center"]), "radius": s["Radius"]})
    for s in agg.get("BoxElems") or []:
        out.append({"type": "box", "center": vec(s["Center"]), "rotation": [s["Rotation"].get(k, 0.0) for k in ("Pitch", "Yaw", "Roll")],
                    "size": [s["X"], s["Y"], s["Z"]]})
    return out


def physics_asset(package, have, chain, simulate_from, root_alias=None):
    """Bodies and joints of a physics asset: the bodies under simulate_from (and that bone) simulate, the rest are kinematic
    colliders following their bones; bones her rig lacks are left out."""
    exports = game_json(package)
    bodies, joints = [], []
    for e in exports:
        p = e.get("Properties", {})
        if e.get("Type") == "SkeletalBodySetup":
            bone = bone_name(p["BoneName"], root_alias)
            if bone not in have:
                continue
            di = p.get("DefaultInstance") or {}
            bodies.append({"bone": bone, "chain": chain, "simulate": False,
                           "kinematicInGame": "Kinematic" in p.get("PhysicsType", ""),
                           "mass": float(di.get("MassInKgOverride", 0.0)) if di.get("bOverrideMass") else 0.0,
                           "linearDamping": float(di.get("LinearDamping", 0.01)), "angularDamping": float(di.get("AngularDamping", 0.0)),
                           "shapes": shape_list(p.get("AggGeom") or {})})
        elif e.get("Type") == "PhysicsConstraintTemplate":
            ci = p["DefaultInstance"]
            prof = ci.get("ProfileInstance") or {}
            cone, twist = prof.get("ConeLimit") or {}, prof.get("TwistLimit") or {}
            child, parent = bone_name(ci["ConstraintBone1"], root_alias), bone_name(ci["ConstraintBone2"], root_alias)
            if child not in have or parent not in have:
                continue
            joints.append({"chain": chain, "child": child, "parent": parent,
                           "pos1": vec(ci.get("Pos1") or {}), "pri1": vec(ci.get("PriAxis1") or {"X": 1}), "sec1": vec(ci.get("SecAxis1") or {"Y": 1}),
                           "pos2": vec(ci.get("Pos2") or {}), "pri2": vec(ci.get("PriAxis2") or {"X": 1}), "sec2": vec(ci.get("SecAxis2") or {"Y": 1}),
                           # UE's defaults where the unversioned data leaves them out: 45 degrees, free
                           "swing1": float(cone.get("Swing1LimitDegrees", 45.0)), "swing2": float(cone.get("Swing2LimitDegrees", 45.0)),
                           "twist": float(twist.get("TwistLimitDegrees", 45.0)),
                           "swing1Motion": cone.get("Swing1Motion", "ACM_Free").split("::")[-1],
                           "swing2Motion": cone.get("Swing2Motion", "ACM_Free").split("::")[-1],
                           "twistMotion": twist.get("TwistMotion", "ACM_Free").split("::")[-1]})
    # simulated: every body the game does not make kinematic (simulate_from None: a character's own asset, whose
    # non-kinematic bodies are what her component simulates), else simulate_from and every body below it (by joints)
    if simulate_from is None:
        for b in bodies:
            b["simulate"] = not b["kinematicInGame"]
        return bodies, joints
    below = {simulate_from}
    grew = True
    while grew:
        grew = False
        for j in joints:
            if j["parent"] in below and j["child"] not in below:
                below.add(j["child"])
                grew = True
    for b in bodies:
        b["simulate"] = b["bone"] in below and not b["kinematicInGame"]
    return bodies, joints


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--outfit", default="CH_P_EVE_09")
    ap.add_argument("--hair", default="EVE_HR_01")
    ap.add_argument("--dlc", default="", help="the outfit's DLC folder (DLC_2: the NIKKE collaboration's outfits)")
    ap.add_argument("--hair-dlc", default="", help="the hairstyle's DLC folder (DLC_2: Alice's EVE_HR_Nikke_06)")
    ap.add_argument("--abp", default="", help="a character of her own: her anim blueprint package (not Eve's)")
    ap.add_argument("--physics", default="", help="with --abp: her physics asset package")
    a = ap.parse_args()
    if a.abp:
        own_character(a)
        return
    outfit_pc = f"SB/Content/{a.dlc}/Art/Character/PC" if a.dlc else PC    # where the outfit's own assets are
    hair_pc = f"SB/Content/{a.hair_dlc}/Art/Character/PC" if a.hair_dlc else PC
    # her bones, as tools/sb_fbx.py exported them
    have = {b["name"] for b in json.load(open(os.path.join(a.folder, "bones.json"), encoding="utf-8"))["bones"]}

    def hair_kind(root):
        return "hair"
    nodes = []
    nodes += kawaii_nodes(f"{PC}/CH_P_EVE_01/Blueprints/CH_P_EVE_01_AnimBP_New", "CH_P_EVE_01_AnimBP_New",
                          lambda r: "hair" if "Hair" in r else "ribbon")
    nodes += kawaii_nodes(f"{hair_pc}/00_HR/{a.hair}/{a.hair}_AnimBP", f"{a.hair}_AnimBP", hair_kind, root_alias="Hair_Root")
    nodes += kawaii_nodes(f"{outfit_pc}/{a.outfit}/Blueprints/{a.outfit}_AnimBP", f"{a.outfit}_AnimBP",
                          lambda r: "skirt" if "Skirt" in r else "ribbon" if "Neck" in r or "Tie" in r else "chain")
    missing = [n["root"] for n in nodes if n["root"] not in have]
    nodes = [n for n in nodes if n["root"] in have]
    kawaii = {"source": "Stellar Blade (1.4.1) KawaiiPhysics nodes read from the cooked anim blueprints (tools/sb_physics.py). "
                        "Units: cm, degrees, UE bone space. Curves multiply the base value over the chain (0 root, 1 tip).",
              "nodes": nodes}
    json.dump(kawaii, open(os.path.join(a.folder, "kawaii.json"), "w", encoding="utf-8"), indent=1)

    sp = springs(f"{PC}/CH_P_EVE_01/Blueprints/CH_P_EVE_01_AnimBP_New", have)
    # the ponytail component's skeleton has its own "Root" (where it hangs on the head socket): not a bone of hers, so its
    # bodies there (a head capsule; the outfit set's big flat box behind the head) are left out
    tail_root = "(ponytail root)"
    tail_asset = f"{hair_pc}/00_HR/{a.hair}/{a.hair}_Tail_PhysicsAsset"
    if exists(tail_asset):
        tail_bodies, tail_joints = physics_asset(tail_asset, have, "ponytail", "Ab-TL-HairB03", root_alias=tail_root)
        tail_coll, _ = physics_asset(f"{outfit_pc}/{a.outfit}/{a.outfit}_PonytailPhysicsAsset", have, "ponytail", "", root_alias=tail_root)
    else:
        # a hairstyle without a ponytail component (Alice's): nothing rigid, nor the outfit's colliders for it
        print(f"{a.hair}: no ponytail physics asset ({tail_asset}) - no rigid ponytail")
        tail_bodies, tail_joints, tail_coll = [], [], []
    body_bodies, body_joints = physics_asset(f"{outfit_pc}/{a.outfit}/{a.outfit}_Physics", have, "cape", "Ab_CapeL01")
    _, cape_r = physics_asset(f"{outfit_pc}/{a.outfit}/{a.outfit}_Physics", have, "cape", "Ab_CapeR01")
    capes = {b["bone"] for b in body_bodies if b["bone"].startswith("Ab_Cape")}
    for b in body_bodies:
        b["simulate"] = b["bone"] in capes
    # the cape set: its simulated panels and the body's kinematic bodies (the ponytail's bodies of this asset are the
    # tail component's business - left out here)
    cape_bodies = [b for b in body_bodies if b["simulate"] or not b["bone"].startswith("Ab-TL")]
    cape_joints = [j for j in body_joints if j["child"] in capes]
    # the ponytail set: the tail asset's own bodies (B01, B02 follow Kawaii's bones; B03 down simulate; its kinematic
    # "Root" is the head) and the outfit's collision set for it
    for b in tail_bodies:
        b["simulate"] = b["simulate"] and b["bone"] not in ("Ab-TL-HairB01", "Ab-TL-HairB02")
    for b in tail_coll:
        b["simulate"] = False
        b["chain"] = "ponytail"
    tail_joints = [j for j in tail_joints if j["child"] in {b["bone"] for b in tail_bodies if b["simulate"]}]
    data = {
        "source": f"Stellar Blade (1.4.1): springs from CH_P_EVE_01_AnimBP_New, rigid bodies from {a.hair}_Tail_PhysicsAsset (when it has one) + "
                  f"{a.outfit}_PonytailPhysicsAsset (ponytail) and {a.outfit}_Physics (back panels); UE units (cm, kg, degrees), "
                  "UE bone space; simulated in the fighter's local space as the game's components do (bLocalSpaceSimulation)",
        "springs": sp,
        "bodies": tail_bodies + tail_coll + cape_bodies,
        "joints": tail_joints + cape_joints,
        "rigs": control_rigs(f"{outfit_pc}/{a.outfit}/Blueprints/{a.outfit}_AnimBP", f"{a.outfit}_AnimBP", have),
    }
    json.dump(data, open(os.path.join(a.folder, "sbphysics.json"), "w", encoding="utf-8"), indent=1)
    print(f"kawaii: {len(nodes)} nodes ({', '.join(f'{n['root']} [{n['kind']}]' for n in nodes)}); not on her rig: {missing}")
    print(f"springs: {len(sp)} ({', '.join(s['bone'] for s in sp)})")
    for chain in ("ponytail", "cape"):
        bs = [b for b in data["bodies"] if b["chain"] == chain]
        print(f"{chain}: {sum(b['simulate'] for b in bs)} simulated ({', '.join(b['bone'] for b in bs if b['simulate'])}), "
              f"{sum(not b['simulate'] for b in bs)} colliders, {len([j for j in data['joints'] if j['chain'] == chain])} joints")


def own_character(a):
    """A character with one blueprint and one physics asset of her own (Raven): her blueprint's Kawaii nodes and spring
    bones; the asset's simulated bodies (chain "hair") with their joints and its kinematic bodies as their colliders."""
    have = {b["name"] for b in json.load(open(os.path.join(a.folder, "bones.json"), encoding="utf-8"))["bones"]}
    abp = a.abp.rsplit("/", 1)[1]
    nodes = kawaii_nodes(a.abp, abp, lambda r: "skirt" if "Suite" in r else "ribbon")
    missing = [n["root"] for n in nodes if n["root"] not in have]
    nodes = [n for n in nodes if n["root"] in have]
    json.dump({"source": f"Stellar Blade (1.4.1) KawaiiPhysics nodes read from {abp} (tools/sb_physics.py --abp). "
                         "Units: cm, degrees, UE bone space. Curves multiply the base value over the chain (0 root, 1 tip).",
               "nodes": nodes}, open(os.path.join(a.folder, "kawaii.json"), "w", encoding="utf-8"), indent=1)
    sp = springs(a.abp, have)
    bodies, joints = physics_asset(a.physics, have, "hair", None) if a.physics else ([], [])
    simulated = {b["bone"] for b in bodies if b["simulate"]}
    joints = [j for j in joints if j["child"] in simulated]
    data = {
        "source": f"Stellar Blade (1.4.1): springs and Kawaii nodes from {abp}, rigid bodies from "
                  f"{a.physics.rsplit('/', 1)[-1] if a.physics else '-'} (its non-kinematic bodies simulate, its kinematic "
                  "ones collide); UE units (cm, kg, degrees), UE bone space; simulated in the fighter's local space",
        "springs": sp,
        "bodies": bodies,
        "joints": joints,
        "rigs": [],
    }
    json.dump(data, open(os.path.join(a.folder, "sbphysics.json"), "w", encoding="utf-8"), indent=1)
    print(f"kawaii: {len(nodes)} nodes ({', '.join(f'{n['root']} [{n['kind']}]' for n in nodes)}); not on her rig: {missing}")
    print(f"springs: {len(sp)} ({', '.join(x['bone'] for x in sp)})")
    print(f"hair: {len(simulated)} simulated bodies, {sum(not b['simulate'] for b in bodies)} colliders, {len(joints)} joints")


if __name__ == "__main__":
    main()
