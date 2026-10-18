"""A Vindictus: Defying Fate character for the fighter (user 10-04: "再加入一个角色吧，vindictus里的fiona，用 PCF_005 这个版本"):
the assembled .blend of ripper_tpose scripts/vindictus (build_blend.py: the game's UE5 skeleton, all parts on one rig,
materials rebuilt from the game's material instances) -> one FBX in metres + a materials sidecar, into the Unity project
(Assets/VDF/<id>/, gitignored - game data, personal use only).

    blender -b --factory-startup <PCF_005.blend> --python tools/vdf_fbx.py -- <out dir> [<id>]
    python tools/vdf_textures.py <out dir>          # then: the maps Unity uses, from the sidecar

  - materials.json says, per material, what build_blend wired into it - read back from the node tree, so the sidecar
    follows whatever the .blend has: the colour map and its Hue/Saturation/Value and multiply tint, the DirectX normal
    map (green flipped by a Math node), the ARM map (R occlusion, G roughness, B metallic), alpha (the colour map's own,
    an ODI map's red, or a constant) with the blend mode and clip threshold, the hair's root / mid / tip colour ramp over
    the FR map's blue, constant colours and roughness; plus the kind build_blend gave it (its build.log report);
  - the eyeballs' iris is procedural (build_eye): baked with Cycles to <id>_eye_albedo.png (colour only, no light);
  - the .blend's units are centimetres (the UE5 rig as imported): exported at 0.01, scale applied, so the FBX holds a
    1.7 m character with unscaled bones; facing -Y in Blender (axis_forward -Z, as tools/doa6_fbx.py);
  - the .blend itself is never saved.
"""
import bpy
import json
import os
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = os.path.abspath(argv[0]) if argv else os.path.abspath("vdf_out")
cid = argv[1] if len(argv) > 1 else os.path.splitext(os.path.basename(bpy.data.filepath))[0].lower()
os.makedirs(out_dir, exist_ok=True)
blend_dir = os.path.dirname(bpy.data.filepath)


# ---- build_blend's report (material kinds), when the build.log is next to the .blend
def read_report():
    path = os.path.join(blend_dir, "build.log")
    if not os.path.exists(path):
        return {}
    raw = open(path, "rb").read()
    for enc in ("utf-16", "utf-8"):
        try:
            text = raw.decode(enc)
        except UnicodeDecodeError:
            continue
        m = re.search(r"VINDICTUS_REPORT=(\{.*\})", text)
        if m:
            try:
                return json.loads(m.group(1))
            except ValueError:
                pass
    return {}


report = read_report()
kinds = {name: info.get("kind") for name, info in (report.get("materials") or {}).items()} if isinstance(report.get("materials"), dict) else {}


def kind_by_name(name):
    low = name.lower()
    if "eyeball" in low:
        return "eye"
    if "eyeshdow" in low or "eyeshadow" in low:
        return "eye-occlusion"
    if "lacrimal" in low or "eyereflection" in low:
        return "eye-wet"
    if "eyebrow" in low or "eyelash" in low:
        return "brow"
    if "hair" in low:
        return "hair"
    if re.search(r"mi_pc[fm]_(upper|lower|hand|foot|body|handfoot)01|face01_$|teeth", low):
        return "skin"
    return "pbr"


# ---- materials: read back what the node tree does
def image_file(node):
    im = node.image
    path = bpy.path.abspath(im.filepath, library=im.library) if im.filepath else ""
    if not path or not os.path.exists(path):
        cand = os.path.join(blend_dir, "textures", os.path.basename(im.filepath or im.name))
        path = cand if os.path.exists(cand) else path
    return os.path.normpath(path) if path else None


def upstream(socket):
    """The node and output socket linked into an input socket (None if unlinked)."""
    if not socket.is_linked:
        return None, None
    link = socket.links[0]
    return link.from_node, link.from_socket


def color4(v):
    return [round(float(x), 5) for x in v][:4]


def describe(m):
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    d = {"kind": kinds.get(m.name) or kind_by_name(m.name), "blend": m.blend_method,
         "clip": round(m.alpha_threshold, 4), "double_sided": not m.use_backface_culling}
    if bsdf is None:
        return d
    for key in ("Roughness", "Metallic", "Specular", "Subsurface"):
        s = bsdf.inputs.get(key)
        if s is not None and not s.is_linked:
            d[key.lower()] = round(float(s.default_value), 4)

    # base colour
    node, out = upstream(bsdf.inputs["Base Color"])
    if node is None:
        d["color"] = color4(bsdf.inputs["Base Color"].default_value)
    elif node.type == 'VALTORGB':
        # hair: ramp over a map channel
        src, src_out = upstream(node.inputs["Fac"])
        if src is not None and src.type in ('SEPRGB', 'SEPARATE_COLOR'):
            img, _ = upstream(src.inputs[0])
            d["ramp_map"] = image_file(img) if img is not None and img.type == 'TEX_IMAGE' else None
            d["ramp_channel"] = src_out.name
        else:
            d["ramp_fac"] = round(float(node.inputs["Fac"].default_value), 4)
        d["ramp"] = [[round(e.position, 4), color4(e.color)] for e in node.color_ramp.elements]
    elif node.type == 'TEX_IMAGE':
        d["albedo"] = image_file(node)
    elif node.type in ('MIX_RGB', 'HUE_SAT') and not (node.type == 'MIX_RGB' and node.blend_type != 'MULTIPLY'):
        n = node
        tint = hsv = None
        if n.type == 'MIX_RGB':
            tint = color4(n.inputs["Color2"].default_value)
            n, _ = upstream(n.inputs["Color1"])
        if n is not None and n.type == 'HUE_SAT':
            hsv = [round(float(n.inputs[k].default_value), 4) for k in ("Hue", "Saturation", "Value")]
            n, _ = upstream(n.inputs["Color"])
        if n is not None and n.type == 'TEX_IMAGE':
            d["albedo"] = image_file(n)
            if tint:
                d["tint"] = tint
            if hsv:
                d["hsv"] = hsv
        else:
            d["bake"] = True        # something else behind the tint: baked below
    else:
        d["bake"] = True            # a procedural colour (the eyes' iris, pupil, sclera mix): baked below

    # alpha
    node, out = upstream(bsdf.inputs["Alpha"])
    if node is None:
        d["alpha"] = round(float(bsdf.inputs["Alpha"].default_value), 4)
    else:
        gain = 1.0
        if node.type == 'MATH' and node.operation == 'MULTIPLY':
            gain = float(node.inputs[1].default_value) if node.inputs[0].is_linked else float(node.inputs[0].default_value)
            node, out = upstream(node.inputs[0] if node.inputs[0].is_linked else node.inputs[1])
        if node is not None and node.type == 'TEX_IMAGE':
            d["alpha_map"] = image_file(node)
            d["alpha_channel"] = "A"
        elif node is not None and node.type in ('SEPRGB', 'SEPARATE_COLOR'):
            img, _ = upstream(node.inputs[0])
            d["alpha_map"] = image_file(img) if img is not None else None
            d["alpha_channel"] = out.name
        d["alpha_gain"] = round(gain, 4)

    # normal map (build_blend flips the DirectX green with 1 - G before Blender's Normal Map node)
    node, _ = upstream(bsdf.inputs["Normal"])
    if node is not None and node.type == 'NORMAL_MAP':
        d["normal_strength"] = round(float(node.inputs["Strength"].default_value), 4)
        src, _ = upstream(node.inputs["Color"])
        flip = False
        if src is not None and src.type in ('COMBRGB', 'COMBINE_COLOR'):
            g, _ = upstream(src.inputs[1])
            flip = g is not None and g.type == 'MATH'
            sep, _ = upstream(src.inputs[0])
            src, _ = upstream(sep.inputs[0]) if sep is not None else (None, None)
        if src is not None and src.type == 'TEX_IMAGE':
            d["normal"] = image_file(src)
            d["normal_flip_green"] = flip

    # ARM / ORM: roughness from G, metallic from B (R = occlusion)
    node, out = upstream(bsdf.inputs["Roughness"])
    if node is not None and node.type in ('SEPRGB', 'SEPARATE_COLOR'):
        img, _ = upstream(node.inputs[0])
        if img is not None and img.type == 'TEX_IMAGE':
            d["arm"] = image_file(img)
    return d


materials = {}
for m in bpy.data.materials:
    if m.use_nodes and m.users:
        materials[m.name] = describe(m)
print(f"[vdf_fbx] {len(materials)} materials read")

# ---- the rig and its meshes
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and (o.parent is arm or any(md.type == 'ARMATURE' and md.object is arm for md in o.modifiers))]
deps = bpy.context.evaluated_depsgraph_get()
zs = [(o.evaluated_get(deps).matrix_world @ v.co).z for o in meshes for v in o.evaluated_get(deps).data.vertices]
low, high = min(zs), max(zs)
scale = 0.01 if high - low > 20.0 else 1.0
print(f"[vdf_fbx] {arm.name}: {len(arm.data.bones)} bones, {len(meshes)} meshes, height {high - low:.1f} units -> scale {scale}, "
      f"lowest {low:.2f}")

# ---- the eyes' procedural iris -> a texture (Cycles, colour only)
def bake_colour(mat_name, size=1024):
    mat = bpy.data.materials[mat_name]
    owner = next((o for o in meshes if any(s.material is mat for s in o.material_slots)), None)
    if owner is None:
        return None
    # a copy of the mesh with only this material's faces, so no other material needs a bake target
    tmp = owner.copy()
    tmp.data = owner.data.copy()
    tmp.modifiers.clear()
    bpy.context.scene.collection.objects.link(tmp)
    idx = [i for i, s in enumerate(tmp.material_slots) if s.material is mat][0]
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(tmp.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != idx], context='FACES')
    bm.to_mesh(tmp.data)
    bm.free()
    for i in range(len(tmp.data.materials)):      # every slot this material: no other one needs a bake target
        tmp.data.materials[i] = mat
    img = bpy.data.images.new(f"{cid}_bake_{mat_name}", size, size, alpha=False)
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    for n in mat.node_tree.nodes:
        n.select = False
    node.select = True
    mat.node_tree.nodes.active = node
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 8
    scene.cycles.device = 'CPU'
    bpy.ops.object.select_all(action='DESELECT')
    tmp.select_set(True)
    bpy.context.view_layer.objects.active = tmp
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8, use_clear=True)
    path = os.path.join(out_dir, f"{cid}_{re.sub(r'[^A-Za-z0-9_]+', '_', mat_name)}_baked.png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    mat.node_tree.nodes.remove(node)
    bpy.data.objects.remove(tmp)
    return path


for name, d in materials.items():
    if d.get("bake"):
        d["albedo"] = bake_colour(name)
        d["albedo_baked"] = True
        print(f"[vdf_fbx] baked {name} -> {d['albedo']}")

# ---- export (metres, scale applied)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = os.path.join(out_dir, cid + ".fbx")
arm_name = arm.name
arm.name = cid
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'}, global_scale=scale,
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         add_leaf_bones=False, use_armature_deform_only=False, mesh_smooth_type='FACE', use_tspace=True,
                         path_mode='STRIP', embed_textures=False, bake_anim=False)
arm.name = arm_name
mesh_info = {o.name: {"verts": len(o.data.vertices), "materials": [s.material.name for s in o.material_slots if s.material]} for o in meshes}
with open(os.path.join(out_dir, "materials.json"), "w", encoding="utf-8") as f:
    json.dump({"id": cid, "fbx": os.path.basename(fbx), "source": bpy.data.filepath, "scale": scale,
               "materials": materials, "meshes": mesh_info}, f, indent=1)
print(f"[vdf_fbx] {fbx}: {len(meshes)} meshes, {len(materials)} materials")
