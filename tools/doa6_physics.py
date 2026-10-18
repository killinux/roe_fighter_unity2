"""A Dead or Alive 6 character's physics for the fighter: the JSON of ripper_tpose scripts/doa6/g1m_physics.py (one per
model part: costume, hair) -> one compact Assets/DOA/<id>/physics.json that Unity's JsonUtility reads (DoaFighter.BuildPhysics
maps it onto the fighter prefab).  Game data: the output stays in the gitignored Assets/DOA.

    python tools/doa6_physics.py <out physics.json> <part.json> [<part.json> ...]
    python tools/doa6_physics.py Assets/DOA/kas/physics.json E:/game_export/DOA6/Kasumi/physics/KAS_COS_001.json E:/game_export/DOA6/Kasumi/physics/KAS_HAIR_001.json

Everything stays in G1M model space (cm, +Y up, faces +Z, +X = her left); bones by global id.  What goes in:
  - colliders: every group of every part, named <part>:<group> (capsules type 5: a radius, b half-length along local Y;
    ellipsoids type 6: radii a b c on local X Y Z), with their model-space pose;
  - chains (NUNO4): bones, rest positions, the 17 parameter words, collider groups.  The kind for the physics setups:
    hair (on the head), ribbon (long sash tails, 50 cm and more), chain (shorter cords and tassels);
  - swings (.swg): bone, parent, strand group, the 15 floats (f0-f3 angle limits in degrees);
  - softs (SOFT): nodes (model position, flags, skin bones + weights, w_self, the 6 lattice neighbours), hull triangles,
    header parameters by name, collider groups; kind breast (body class 3) or body (1, hips);
  - softMeshes: per soft-body mesh (the part's submesh number = the Blender object <part>_sm<n>) every vertex's rest
    position, UV, 8 lattice nodes + weights and its blend back to plain skinning;
  - attachments: bones placed from the soft body (565/566 at the cleavage);
  - twists: the RF_ twist helpers the game's rig script drives (twists()), from the costume's skeleton;
  - grids (NUNO3 grid cloth: skirts, sleeves, flaps): control points (model position, left/right/up/down links, rest distances),
    the skinned top rows (bones + weights), the simulated ranges, extra constraints, the 46 parameter words, collider groups;
  - gridMeshes: per cloth surface mesh (type 1) its rest positions and UVs, and for each rebuilt vertex the 16 control
    points of its 4x4 patch with the weights w_h, w_v, dw_h, dw_v, the depth and the normal / tangent coefficients
    (the other vertices are rigid: the imported mesh's own skinning).
Hair parts reference the costume's collider group 0 (the body colliders for hair, ripper_tpose doa6_physics_findings).
"""
import json
import re
import os
import sys


TWIST_CHAIN = re.compile(r"^RF_([LR])_(Arm|UpLeg)_(\d\d)$")


def twists(skeleton):
    """The twist helpers the game's rig script drives (the clips key only bones 1-61): RF_<side>_Arm_00..02 along the
    upper arm and RF_<side>_UpLeg_00..02 along the thigh take 0, 1/2 and all of their limb bone's own twist (guess: the
    usual twist split, 00 at the joint, 02 at the far end); RF_*NoTwist bones none of their parent's.  Each: the helper,
    the limb bone whose twist it shares (source), the bone that limb points at (toward: the twist axis) and the share."""
    by_id = {b["id"]: b for b in skeleton}
    kids = {}
    for b in skeleton:
        kids.setdefault(b["parent_id"], []).append(b)

    def limb_of(b):
        p = by_id.get(b["parent_id"])
        while p is not None and not (p.get("name") or "").startswith("SK_"):
            p = by_id.get(p["parent_id"])
        return p

    def toward(src):
        sk_kids = [k for k in kids.get(src["id"], []) if (k.get("name") or "").startswith("SK_")]
        return sk_kids[0] if sk_kids else None

    chains = {}
    out = []
    for b in skeleton:
        name = b.get("name") or ""
        m = TWIST_CHAIN.match(name)
        if m:
            chains.setdefault((m.group(1), m.group(2)), []).append((int(m.group(3)), b))
        elif name.startswith("RF_") and name.endswith("NoTwist"):
            src = by_id.get(b["parent_id"])
            if src is not None and toward(src) is not None:
                out.append({"bone": b["id"], "source": src["id"], "toward": toward(src)["id"], "share": 0.0, "name": name})
    for (side, limb), items in chains.items():
        items.sort(key=lambda x: x[0])
        last = max(1, items[-1][0])
        for n, b in items:
            src = limb_of(b)
            if src is not None and toward(src) is not None:
                out.append({"bone": b["id"], "source": src["id"], "toward": toward(src)["id"], "share": n / last, "name": b["name"]})
    # parents before children
    depth = {}

    def d(i):
        if i not in depth:
            p = by_id[i]["parent_id"]
            depth[i] = 0 if p not in by_id else d(p) + 1
        return depth[i]
    out.sort(key=lambda t: d(t["bone"]))
    return out


def main():
    out, parts = sys.argv[1], sys.argv[2:]
    data = {"source": [], "colliders": [], "groups": [], "chains": [], "swings": [], "softs": [], "softMeshes": [], "attachments": [],
            "twists": [], "grids": [], "gridMeshes": []}
    group_index = {}
    costume = None
    for path in parts:
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        part = os.path.splitext(os.path.basename(path))[0]
        is_hair = "_HAIR_" in part
        if "_COS_" in part:
            costume = part
        data["source"].append(part)
        # colliders, by group
        for g in d["colliders"]["groups"]:
            name = f"{part}:{g['index']}"
            idx = []
            for c in g["colliders"]:
                q = c["model_rot"]
                data["colliders"].append({"type": c["type"], "bone": c["bone"]["id"], "a": c["size_abc"][0], "b": c["size_abc"][1],
                                          "c": c["size_abc"][2], "pos": c["model_pos"], "rot": q})
                idx.append(len(data["colliders"]) - 1)
            group_index[name] = len(data["groups"])
            data["groups"].append({"name": name, "colliders": idx})

        def groups_of(owner_groups):
            names = [f"{part}:{g}" for g in owner_groups]
            # hair: group 0 of the hair part has neck and head only; the costume's group 0 is the body for the hair
            if is_hair and costume is not None and 0 in owner_groups:
                names.append(f"{costume}:0")
            return names

        # chains (NUNO4)
        for ch in d["chains"]:
            pts = ch["points"]
            length = sum(p["rest_len"] for p in pts)
            kind = "hair" if is_hair else ("ribbon" if length >= 50.0 else "chain")
            params = []
            for p in ch["params"]:
                v = p.get("value")
                params.append(float(v["int"]) if isinstance(v, dict) else float(v or 0))
            data["chains"].append({"name": f"{part} chain {ch['index']}", "kind": kind, "parent": ch["parent"]["id"],
                                   "bones": [p["bone"]["id"] for p in pts], "rest": [c for p in pts for c in p["model_pos"]],
                                   "restLen": [p["rest_len"] for p in pts], "t": [p["t"] for p in pts],
                                   "params": params, "groups": groups_of(ch["collision_groups"]), "length": length})
        # swing bones (.swg)
        sw = d.get("swing_bones") or {}
        for e in sw.get("entries", []):
            vals = [float(p.get("value") or 0) for p in e["params"]]
            data["swings"].append({"bone": e["bone"]["id"], "parent": e["parent_id"], "group": e["group"], "pos": e["model_pos"],
                                   "params": vals, "kind": "hair"})
        # soft bodies (SOFT)
        for sb in d["soft_bodies"]:
            hp = {}
            for p in sb["header_params"]:
                if p.get("name"):
                    v = p["value"]
                    hp[p["name"].split(" ")[0]] = float(v["int"]) if isinstance(v, dict) else float(v)
            kind = "breast" if sb["header_words"][9] == 3 else "body"
            nodes = []
            for n in sb["nodes"]:
                nodes.append({"pos": n["model_pos"], "flags": n["flags"], "bones": [s["bone"]["id"] for s in n["skin"]],
                              "weights": [s["weight"] for s in n["skin"]], "wSelf": n["w_self"], "n6": n["n6"]})
            tris = [i for t in sb["hull"]["triangles"] for i in t]
            data["softs"].append({"name": f"{part} soft {sb['index']}", "kind": kind, "parent": sb["parent"]["id"],
                                  "gravity": hp.get("gravity", -9.8), "damping": hp.get("damping", 0.75), "stiffness": hp.get("stiffness", 270.0),
                                  "scale": hp.get("scale", 1.0), "perAxis": [hp.get("per_axis_x", 0), hp.get("per_axis_y", 0), hp.get("per_axis_z", 0)],
                                  "coef": [hp.get("pairA_coef", 1), hp.get("pairB_coef", 1), hp.get("pairC_coef", 1), hp.get("pairD_coef", 1)],
                                  "axis": [hp.get("axis_x", 0), hp.get("axis_y", 1), hp.get("axis_z", 0)],
                                  "groups": [f"{part}:{g}" for g in sb["collision_groups"]], "nodes": nodes, "hull": tris,
                                  "restVolume": abs(sb["hull"]["signed_volume_cm3"])})
        soft_base = len(data["softs"]) - len(d["soft_bodies"])
        for m in d["meshes"]:
            if m["type"] != 4:
                continue
            sv = m["soft_vertices"]       # node_ids / node_w: 8 per vertex, flat
            verts = []
            for i in range(len(m["rest_model_pos"])):
                verts.append({"pos": m["rest_model_pos"][i], "uv": m["uv"][i], "nodes": sv["node_ids"][8 * i:8 * i + 8],
                              "w": sv["node_w"][8 * i:8 * i + 8], "blend": sv["blend"][i]})
            data["softMeshes"].append({"mesh": f"{part}_sm{m['submesh']}", "body": soft_base + m["driver"]["body_index"], "vertices": verts})
        for a in d.get("soft_attachments", []):
            data["attachments"].append({"bone": a["bone"]["id"],
                                        "refs": [{"body": soft_base + r["body"], "weight": r["weight"], "nodes": r["nodes"], "w": r["node_weights"]} for r in a["refs"]]})
        if "_COS_" in part:
            data["twists"] = twists(d["skeleton"])
        # grid cloth (NUNO3: the one the meshes use; NUNO1 / NUNV1 are the same grid with untuned words)
        grid_base = len(data["grids"])
        for cl in d.get("cloth", []):
            cps = cl["control_points"]
            n3 = cl.get("nuno3") or {}
            params, iterations = [], []
            for p in n3.get("params", []):
                v = p.get("value")
                if isinstance(v, dict) and "bytes" in v:
                    params.append(0.0)
                    if p["i"] == 37:
                        iterations = v["bytes"]
                else:
                    params.append(float(v["int"]) if isinstance(v, dict) else float(v or 0))
            skin = [{"cp": s["cp"], "bones": [b["id"] for b in s["bones"]], "weights": s["weights"]} for s in cl["skin"]]
            data["grids"].append({
                "name": f"{part} cloth {cl['index']}", "kind": "skirt", "parent": cl["parent"]["id"],
                "cols": cl["cols"], "rows": cl["rows"], "ring": bool(cl["ring"]),
                "pos": [c for p in cps for c in p["model_pos"]],
                "links": [k for p in cps for k in (p["left"], p["right"], p["up"], p["down"])],
                "restRight": [p["rest_right"] for p in cps], "restDown": [p["rest_down"] for p in cps],
                "skin": skin, "simRanges": [x for r in n3.get("sim_ranges", []) for x in r],
                "extra": [x for e in n3.get("extra_constraints", []) for x in (e["a"], e["b"], e["rest"])] if n3.get("extra_constraints") else [],
                "params": params, "iterations": iterations, "groups": [f"{part}:{g}" for g in cl["collision_groups"]]})
        for m in d["meshes"]:
            if m["type"] != 1:
                continue
            cv = m["cloth_vertices"]
            data["gridMeshes"].append({
                "mesh": f"{part}_sm{m['submesh']}", "grid": grid_base + m["driver"]["cloth_index"], "count": m["vertex_count"],
                "pos": [c for p in m["rest_model_pos"] for c in p], "uv": [c for p in m["uv"] for c in p],
                "cloth": cv["index"], "cp": cv["cp"], "wh": cv["w_h"], "wv": cv["w_v"], "dwh": cv["dw_h"], "dwv": cv["dw_v"],
                "depth": cv["depth"], "normalCoef": cv["normal_coef"], "tangentCoef": cv["tangent_coef"]})
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f)
    print(f"{out}: {len(data['groups'])} collider groups ({len(data['colliders'])} colliders), {len(data['chains'])} chains "
          f"({', '.join(c['kind'] + ' ' + str(len(c['bones'])) for c in data['chains'])}), {len(data['swings'])} swing bones, "
          f"{len(data['softs'])} soft bodies ({', '.join(s['kind'] + ' ' + str(len(s['nodes'])) for s in data['softs'])}), "
          f"{len(data['softMeshes'])} soft meshes ({sum(len(m['vertices']) for m in data['softMeshes'])} vertices), {len(data['attachments'])} attachments, "
          f"{len(data['twists'])} twist helpers, {len(data['grids'])} cloth grids ({', '.join(f"{g['cols']}x{g['rows']}" for g in data['grids'])}), "
          f"{len(data['gridMeshes'])} cloth surfaces ({sum(m['count'] for m in data['gridMeshes'])} vertices)")


if __name__ == "__main__":
    main()
