"""Vindictus' KawaiiPhysics settings for one character, in the shape the fighter's solver reads (RoeKawaiiPhysics):

    python tools/vdf_kawaii.py <kawaii_params.json> Assets/VDF/<id>/kawaii.json [--abp ABP_A,ABP_B,...]

kawaii_params.json is what tools/vdf_research/run_decode.py reads out of the game's cooked anim blueprints (every
AnimNode_KawaiiPhysics: root bone, excluded bones, the PhysicsSettings the blueprint really feeds it, curves, gravity,
the limits data asset's capsules).  This keeps the anim blueprints of the outfit's and hair's skeletal meshes (default:
PCF_005's one-piece, head piece, gloves, and Fiona's hair; not ABP_PCF_Corrective - the base body's own, which the
one-piece's breast nodes replace - nor the old prework hair) and writes one entry per node:

  name, abp, kind (skirt / hair / breast: what the fight's physics setups call it; feathers and earrings count as hair),
  root, exclude, damping, stiffness, worldDampingLocation, worldDampingRotation, radius, limitAngle (as the game's
  units: cm, degrees), gravity (UE world vector, cm/s2), targetFramerate, the curves over the chain as flat
  [t0, v0, t1, v1, ...] lists (dampingCurve, stiffnessCurve, radiusCurve, limitAngleCurve; empty = none), and capsules:
  bone, radius, length (cm), offset (cm, UE bone space) and rotation (pitch, yaw, roll degrees, UE).
The axes stay UE's: RoeKawaiiPhysics turns them into Unity's (see there).  Wind is left out (no wind in the arena).
"""
import argparse
import json
import os

DEFAULT_ABPS = ["ABP_PCF_005_Onepiece01_master", "ABP_PCF_005_Head01_master", "ABP_PCF_005_Hand01_master", "ABP_Fiona_Hair01"]


def kind_of(root):
    low = root.lower()
    if "skirt" in low:
        return "skirt"
    if low.startswith("breast"):
        return "breast"
    return "hair"       # hair, feathers, the tiara's earrings


def curve_keys(name, curves):
    keys = curves.get(name)
    if not keys:
        return []
    out = []
    for t, v, *_ in keys:
        out += [float(t), float(v)]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("params")
    ap.add_argument("out")
    ap.add_argument("--abp", default=",".join(DEFAULT_ABPS))
    a = ap.parse_args()
    d = json.load(open(a.params, encoding="utf-8"))
    curves = d.get("curves", {})
    limits = d.get("limits_data_assets", {})
    nodes = []
    for abp_name in a.abp.split(","):
        abp = d["anim_blueprints"].get(abp_name)
        if abp is None:
            raise SystemExit(f"no anim blueprint {abp_name} in {a.params}")
        # curves the blueprint's bytecode assigns to a node ("Radius Curve Data" = its Curve_005_11 variable)
        assigned = abp.get("bytecode_other_assignments", {})
        for n in abp["nodes"]:
            s = n.get("PhysicsSettings_effective") or n.get("PhysicsSettings(CDO default)") or {}
            root = n["RootBone"]["BoneName"]
            e = {
                "name": n["node"], "abp": abp_name, "kind": kind_of(root), "root": root,
                "exclude": [x["BoneName"] for x in n.get("ExcludeBones", [])],
                "damping": float(s.get("Damping", 0.1)), "stiffness": float(s.get("Stiffness", 0.05)),
                "worldDampingLocation": float(s.get("WorldDampingLocation", 0.8)),
                "worldDampingRotation": float(s.get("WorldDampingRotation", 0.8)),
                "radius": float(s.get("Radius", 3.0)), "limitAngle": float(s.get("LimitAngle", 0.0) or 0.0),
                "gravity": [float(x) for x in n["Gravity"]] if isinstance(n.get("Gravity"), list) else [0.0, 0.0, 0.0],
                "targetFramerate": float(n.get("TargetFramerate?", 60) or 60),
                "dampingCurve": [], "stiffnessCurve": [], "radiusCurve": [], "limitAngleCurve": [],
                "capsules": [],
            }
            for field in ("Damping", "Stiffness", "Radius", "LimitAngle", "WorldDampingLocation", "WorldDampingRotation"):
                key = field + "CurveData"
                ref = n.get(key)
                name = None
                if isinstance(ref, dict) and ref.get("external"):
                    name = ref["external"].rsplit("/", 1)[-1]
                elif key in assigned.get(n["node"], {}):
                    name = "Curve_005_11"       # the blueprint's "Radius Curve Data" variable (its default)
                if name and field[0].lower() + field[1:] + "Curve" in e:
                    e[field[0].lower() + field[1:] + "Curve"] = curve_keys(name, curves)
            da = n.get("LimitsDataAsset", "")
            caps = limits.get(da.rsplit("/", 1)[-1], {}).get("CapsuleLimitsData", []) if da else []
            caps = caps or n.get("CapsuleLimitsData", [])
            for c in caps:
                if not c.get("bEnable", 1):
                    continue
                off = c.get("OffsetLocation") or [0.0, 0.0, 0.0]
                rot = c.get("OffsetRotation_PYR") or [0.0, 0.0, 0.0]
                e["capsules"].append({"bone": c["bone"], "radius": float(c["Radius"]), "length": float(c["Length"]),
                                      "offset": [float(x) for x in off], "rotation": [float(x) for x in rot]})
            nodes.append(e)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"source": d.get("_doc", ""), "nodes": nodes}, f, indent=1)
    for e in nodes:
        curves_used = [k for k in ("dampingCurve", "stiffnessCurve", "radiusCurve", "limitAngleCurve") if e[k]]
        print(f"{e['abp']:32s} {e['kind']:6s} {e['root']:34s} d {e['damping']} k {e['stiffness']} r {e['radius']} "
              f"lim {e['limitAngle']} g {e['gravity'][2]} curves {curves_used} capsules {len(e['capsules'])} exclude {e['exclude']}")
    print(f"{len(nodes)} nodes -> {a.out}")


if __name__ == "__main__":
    main()
