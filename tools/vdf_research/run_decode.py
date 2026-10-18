"""Run kawaii_decode on every relevant package and write kawaii_params.json (compact)."""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kawaii_decode as k  # noqa: E402

ABPS = {
    "PCF_005 Onepiece (skirt + spine05 feathers + breasts)": "ABP_PCF_005_Onepiece01_master",
    "PCF_005 Head (tiara feathers)": "ABP_PCF_005_Head01_master",
    "PCF_005 Hand (arm-band feathers)": "ABP_PCF_005_Hand01_master",
    "Fiona hair SK_Fiona_Hair01": "ABP_Fiona_Hair01",
    "Base body corrective (breasts, nude/base body)": "ABP_PCF_Corrective",
    "LEGACY prework hair (SK_female_base, not used by BP_PlayerFiona)": "ABP_Fiona_Hair_Kawaii",
}
DAS = ["DA_Skirt", "DA_Feather", "DA_Hand", "DA_Head", "DA_Breast", "DA_Fiona_Hair01_ABC", "DA_Fiona_Hair01_D",
       "Fiona_Hair_Collision", "Fiona_Hair_Collision1"]
CURVES = ["Curve_005_11", "Curve_PCF_BP_LimitAngle"]


def compact_curve(cd):
    if not isinstance(cd, dict):
        return None
    keys = cd.get("EditorCurveData", {}).get("Keys") if isinstance(cd.get("EditorCurveData"), dict) else None
    ext = cd.get("ExternalCurve")
    if not keys and not ext:
        return None
    out = {}
    if keys:
        out["keys(t,v)"] = [(kk["time"], kk["value"], kk["interp"]) for kk in keys]
    if ext:
        out["external"] = ext
    return out


def compact_node(n):
    o = {}
    for key, v in n.items():
        if key.endswith("CurveData"):
            c = compact_curve(v)
            if c:
                o[key] = c
        elif key.startswith("_next_bytes"):
            continue
        elif isinstance(v, list) and not v:
            continue
        elif key.endswith("Limits") or key.endswith("LimitsData"):
            o[key] = [{"bone": e["DrivingBone"]["BoneName"] if isinstance(e.get("DrivingBone"), dict) else e.get("DrivingBone"),
                       **{kk: vv for kk, vv in e.items() if kk not in ("DrivingBone",)}} for e in v]
        else:
            o[key] = v
    return o


def main():
    res = {"_doc": "Vindictus: Defying Fate (2024-03 pre-alpha) KawaiiPhysics data decoded from cooked packages. "
                   "Units: cm, degrees, UE axes (X fwd, Y right, Z up). Curves are evaluated over the normalised "
                   "bone position along the chain (0 = root, 1 = tip) and MULTIPLY the base value. "
                   "PhysicsSettings_effective = the K2Node_MakeStruct literals assigned to the node at runtime "
                   "(bytecode); 'PhysicsSettings(CDO default)' is only the stored default. Keys with '?' or p<N> "
                   "have an uncertain property name (index in FAnimNode_KawaiiPhysics).",
           "anim_blueprints": {}, "limits_data_assets": {}, "curves": {}}
    for label, abp in ABPS.items():
        try:
            props, nodes, makes, assign = k.decode_abp(abp)
        except Exception as e:  # noqa: BLE001
            res["anim_blueprints"][abp] = {"label": label, "error": repr(e)}
            continue
        kprops = [p[0] for p in props if p[0].startswith("AnimGraphNode_KawaiiPhysics")]
        # CDO nodes are in class-property order (same order as kprops)
        out_nodes = []
        for i, n in enumerate(nodes):
            nm = kprops[i] if i < len(kprops) else "node%d" % i
            c = compact_node(n)
            src = assign.get(nm, {}).get("PhysicsSettings")
            if src:
                c["PhysicsSettings_effective"] = makes.get(src, {})
                c["PhysicsSettings_source"] = src
            out_nodes.append({"node": nm, **c})
        res["anim_blueprints"][abp] = {"label": label, "class_properties": [p[0] for p in props],
                                       "kawaii_node_count_in_class": len(kprops), "nodes_decoded": len(nodes),
                                       "nodes": out_nodes,
                                       "bytecode_other_assignments": {kk: vv for kk, vv in assign.items()
                                                                      if set(vv) != {"PhysicsSettings"}}}
    for da in DAS:
        try:
            d = k.decode_da(da)
            res["limits_data_assets"][da] = {kk: [{"bone": e["DrivingBone"]["BoneName"], **{a: b for a, b in e.items() if a != "DrivingBone"}} for e in v]
                                             for kk, v in d.items() if v}
        except Exception as e:  # noqa: BLE001
            res["limits_data_assets"][da] = {"error": repr(e)}
    for cv in CURVES:
        try:
            d = k.decode_curve(cv)
            res["curves"][cv] = [(kk["time"], kk["value"], kk["interp"]) for kk in d["FloatCurve"]["Keys"]]
        except Exception as e:  # noqa: BLE001
            res["curves"][cv] = {"error": repr(e)}
    json.dump(res, open("kawaii_params.json", "w", encoding="utf-8"), indent=1)
    # short console summary
    for abp, a in res["anim_blueprints"].items():
        print("==", abp, a.get("label"), "class nodes", a.get("kawaii_node_count_in_class"), "decoded", a.get("nodes_decoded"), a.get("error", ""))
        for n in a.get("nodes", []):
            print("  %-34s root=%-32s DA=%s eff=%s stop=%s" % (
                n["node"], n.get("RootBone", {}).get("BoneName") if isinstance(n.get("RootBone"), dict) else n.get("RootBone"),
                (n.get("LimitsDataAsset") or "").split("/")[-1], n.get("PhysicsSettings_effective"), n.get("_stopped_at_index")))
    for da, d in res["limits_data_assets"].items():
        print("DA", da, {kk: len(v) for kk, v in d.items()} if "error" not in d else d)
    print("curves", res["curves"])


if __name__ == "__main__":
    main()
