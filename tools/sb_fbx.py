"""Stellar Blade's Eve for the fighter (user 10-05: "另外把剑星里的eve的也加个角色进来，物理能用剑星自己的就用自己的，
次选项才是用magic cloth2"): the assembled outfit .blend of ripper_tpose scripts/stellarblade (export_outfit.ps1 /
validate_eve.py: the outfit body + Face_003 head + hair + ponytails on one armature, Eve_Armature, archived under
E:\\game_export\\StellarBlade\\Eve\\blend\\<package>\\) -> one FBX in metres + a materials sidecar, into the Unity project
(Assets/SB/<id>/, gitignored - game data, personal use only).

    blender -b --factory-startup <Eve_CH_P_EVE_09.blend> --python tools/sb_fbx.py -- <out dir> [<id>]
    python tools/sb_textures.py <out dir> --extra <the .blend's textures/extra>   # then: the maps Unity uses

  - the game's Biped bones are written with hyphens (Bip001-L-Clavicle); they are renamed to the spaced form the
    fighter's humanoid builder reads for the ROE characters (Bip001 L Clavicle) - their vertex groups follow; the game's
    helper bones (Ab-*, Ab_*) keep their names, which the physics data uses (tools/sb_physics.py renames the Biped ones
    in it the same way);
  - the .blend stands her facing +X in centimetres (as UE Viewer's PSK); she is turned to face -Y (what the FBX settings
    below, the same as tools/vdf_fbx.py, make Unity's forward) and exported at 0.01: a 1.75 m character;
  - materials.json says, per material, the maps the .blend wires (validate_eve.py: the colour map, alpha) - read back
    from the node trees as tools/vdf_fbx.py does; normal / ORM maps are not wired in the .blend: sb_textures.py finds
    them in textures/extra by the colour map's name;
  - the .blend itself is never saved.
"""
import bpy
import json
import math
import os
import re
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = os.path.abspath(argv[0]) if argv else os.path.abspath("sb_out")
cid = argv[1] if len(argv) > 1 else "eve09"
os.makedirs(out_dir, exist_ok=True)
blend_dir = os.path.dirname(bpy.data.filepath)


def kind_by_name(name):
    low = name.lower()
    if "eyerefractive" in low:
        return "eye"
    if "eyeshadow" in low or "occlusion" in low or "occulusion" in low or "eyeblend" in low:
        return "eye-occlusion"
    # the other characters' names (ripper_tpose scripts/stellarblade/npc, 10-18): MI_CH_M_NA_53_Tearline, MI_NPC_Enya_fluid,
    # MI_LacimalFluid; MI_CH_M_NA_53_Eyes, MI_NPC_EnyaEyeball; MI_CH_NPC_Kara_Lashes; MI_CH_NPC_Raven_01_BaseBody
    if "lacrimal" in low or "eyelight" in low or "tearline" in low or "_fluid" in low or "lacimal" in low:
        return "eye-wet"
    if "eyebrow" in low or "eyelash" in low or "lashes" in low:
        return "brow"
    if re.search(r"_eyes?$|eyeball", low):
        return "eye"
    if "hair" in low or "ponytail" in low:
        return "hair"
    if "teeth" in low or "mouthinner" in low:
        return "mouth"
    if "head" in low or "skin" in low or "basebody" in low:
        return "skin"
    return "pbr"


def image_file(node):
    im = node.image
    if im is None:
        return None
    path = bpy.path.abspath(im.filepath, library=im.library) if im.filepath else ""
    if not path or not os.path.exists(path):
        cand = os.path.join(blend_dir, "textures", os.path.basename(im.filepath or im.name))
        path = cand if os.path.exists(cand) else path
    return os.path.normpath(path) if path else None


def upstream(socket):
    if not socket.is_linked:
        return None, None
    link = socket.links[0]
    return link.from_node, link.from_socket


def color4(v):
    return [round(float(x), 5) for x in v][:4]


def find_image(node, depth=0):
    """The first image texture upstream of a node (through mixes, hue/sat, separate/combine), and the chain's kinds."""
    if node is None or depth > 6:
        return None
    if node.type == 'TEX_IMAGE':
        return node
    for s in node.inputs:
        n, _ = upstream(s)
        hit = find_image(n, depth + 1)
        if hit is not None:
            return hit
    return None


def describe(m):
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    d = {"kind": kind_by_name(m.name), "blend": m.blend_method, "clip": round(m.alpha_threshold, 4),
         "double_sided": not m.use_backface_culling}
    if bsdf is None:
        img = next((n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image is not None), None)
        if img is not None:
            d["albedo"] = image_file(img)
        d["no_bsdf"] = True
        return d
    for key in ("Roughness", "Metallic", "Specular", "Subsurface"):
        s = bsdf.inputs.get(key)
        if s is not None and not s.is_linked:
            d[key.lower()] = round(float(s.default_value), 4)
    node, _ = upstream(bsdf.inputs["Base Color"])
    if node is None:
        d["color"] = color4(bsdf.inputs["Base Color"].default_value)
    elif node.type == 'TEX_IMAGE':
        d["albedo"] = image_file(node)
    else:
        img = find_image(node)
        if img is not None:
            d["albedo"] = image_file(img)
            d["albedo_via"] = node.type
            if node.type == 'MIX_RGB' and node.blend_type == 'MULTIPLY':
                d["tint"] = color4(node.inputs["Color2"].default_value)
        else:
            d["bake"] = True
    node, out = upstream(bsdf.inputs["Alpha"])
    if node is None:
        d["alpha"] = round(float(bsdf.inputs["Alpha"].default_value), 4)
    else:
        img = node if node.type == 'TEX_IMAGE' else find_image(node)
        if img is not None:
            d["alpha_map"] = image_file(img)
            d["alpha_channel"] = out.name if node.type in ('SEPRGB', 'SEPARATE_COLOR') else "A"
    node, _ = upstream(bsdf.inputs["Normal"])
    if node is not None and node.type == 'NORMAL_MAP':
        img = find_image(node)
        if img is not None:
            d["normal"] = image_file(img)
    return d


materials = {}
for m in bpy.data.materials:
    if m.use_nodes and m.users:
        materials[m.name] = describe(m)
print(f"[sb_fbx] {len(materials)} materials read")

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and (o.parent is arm or any(md.type == 'ARMATURE' and md.object is arm for md in o.modifiers))]
# the .blend carries both of her ponytails; the game shows one (the long one, unless "IsShortPonyTail")
SKIP = re.compile(os.environ.get("SB_FBX_SKIP", r"HairTailShort"))
skipped = [o.name for o in meshes if SKIP.search(o.name)]
meshes = [o for o in meshes if not SKIP.search(o.name)]
print(f"[sb_fbx] left out: {', '.join(skipped) or 'nothing'}")
# only the materials of what goes out (the nude base for the clothes burst is her body alone: SB_FBX_SKIP="Hair|Head")
used = {s.material.name for o in meshes for s in o.material_slots if s.material}
materials = {k: v for k, v in materials.items() if k in used}

# ---- the Biped bones in the spaced form (vertex groups are renamed with them)
renamed = 0
for b in arm.data.bones:
    if b.name.startswith("Bip001-"):
        new = b.name.replace("-", " ")
        for o in meshes:
            g = o.vertex_groups.get(b.name)
            if g is not None:
                g.name = new
        b.name = new
        renamed += 1
print(f"[sb_fbx] {renamed} Biped bones renamed to the spaced form")

# ---- facing: the front of the chest (both breast bones, else the toes) tells which way she faces
deps = bpy.context.evaluated_depsgraph_get()
def head_of(name):
    b = arm.data.bones.get(name)
    return arm.matrix_world @ b.head_local if b else None
front = None
for a, b in (("Ab-L-Breast", "Ab-R-Breast"), ("Bip001 L Toe0", "Bip001 R Toe0")):
    pa, pb, pelvis = head_of(a), head_of(b), head_of("Bip001 Pelvis")
    if pa is not None and pb is not None and pelvis is not None:
        v = (pa + pb) / 2 - pelvis
        v.z = 0
        if v.length > 1e-3:
            front = v.normalized()
            break
yaw_now = math.atan2(front.y, front.x) if front is not None else 0.0
turn = -math.pi / 2 - yaw_now        # to face -Y
if os.environ.get("SB_FBX_TURN"):    # for checks: another turn (degrees)
    turn = math.radians(float(os.environ["SB_FBX_TURN"]))
print(f"[sb_fbx] she faces {math.degrees(yaw_now):.1f} deg (from +X); turned {math.degrees(turn):.1f} deg about Z to face -Y")
if abs(turn) > 1e-4:
    # turned in the data (bones' rest, vertices, shape keys), the objects stay where they are: no object rotation for
    # the FBX to carry into Unity
    from mathutils import Matrix
    rot = Matrix.Rotation(turn, 4, 'Z')
    arm.data.transform(arm.matrix_world.inverted() @ rot @ arm.matrix_world)
    for o in meshes:
        o.data.transform(o.matrix_world.inverted() @ rot @ o.matrix_world, shape_keys=True)
    # Armature.transform turns the rest bones but tags nothing: the pose (what the FBX exporter writes as the bones' nodes)
    # stayed the old one until the armature is re-evaluated - the first export came out unturned
    arm.data.update_tag()
    arm.update_tag()
    for o in meshes:
        o.data.update_tag()
    bpy.context.view_layer.update()
    toe = arm.pose.bones.get("Bip001-L-Toe0") or arm.pose.bones.get("Bip001 L Toe0")
    if toe is not None:
        print(f"[sb_fbx] posed left toe now at {tuple(round(x, 2) for x in (arm.matrix_world @ toe.head))} (rest {tuple(round(x, 2) for x in (arm.matrix_world @ toe.bone.head_local))})")

zs = [(o.evaluated_get(deps).matrix_world @ v.co).z for o in meshes for v in o.evaluated_get(deps).data.vertices]
low, high = min(zs), max(zs)


# ---- the eyes: validate_eve.py mixes the iris map into the sclera map in the node tree -> baked to one texture (Cycles,
# colour only), as tools/vdf_fbx.py bakes Fiona's procedural iris
def bake_colour(mat_name, size=1024):
    mat = bpy.data.materials[mat_name]
    owner = next((o for o in meshes if any(s.material is mat for s in o.material_slots)), None)
    if owner is None:
        return None
    tmp = owner.copy()
    tmp.data = owner.data.copy()
    tmp.modifiers.clear()
    if tmp.data.shape_keys:
        tmp.shape_key_clear()
    bpy.context.scene.collection.objects.link(tmp)
    idx = [i for i, s in enumerate(tmp.material_slots) if s.material is mat][0]
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(tmp.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != idx], context='FACES')
    bm.to_mesh(tmp.data)
    bm.free()
    for i in range(len(tmp.data.materials)):
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
    if d.get("kind") == "eye" and d.get("albedo_via") in ("MIX_RGB", "MIX"):
        d["albedo"] = bake_colour(name)
        d["albedo_baked"] = True
        print(f"[sb_fbx] baked {name} -> {d['albedo']}")
scale = 0.01 if high - low > 20.0 else 1.0
print(f"[sb_fbx] {arm.name}: {len(arm.data.bones)} bones, {len(meshes)} meshes, height {high - low:.1f} units -> scale {scale}, lowest {low:.2f}")

# ---- export (metres, scale and the turn applied)
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
                         path_mode='STRIP', embed_textures=False, bake_anim=False, use_mesh_modifiers=False)
arm.name = arm_name
mesh_info = {o.name: {"verts": len(o.data.vertices), "materials": [s.material.name for s in o.material_slots if s.material],
                      "shape_keys": len(o.data.shape_keys.key_blocks) if o.data.shape_keys else 0} for o in meshes}
# her bones as exported (tools/sb_physics.py keeps the game's physics on the bones she has); skin = vertices weighted to it
weighted = {}
for o in meshes:
    names = {g.index: g.name for g in o.vertex_groups}
    for v in o.data.vertices:
        for g in v.groups:
            if g.weight > 0.01:
                weighted[names[g.group]] = weighted.get(names[g.group], 0) + 1
with open(os.path.join(out_dir, "bones.json"), "w", encoding="utf-8") as f:
    json.dump({"bones": [{"name": b.name, "parent": b.parent.name if b.parent else None,
                          "head_m": [round(x * scale, 5) for x in (arm.matrix_world @ b.head_local)], "skin": weighted.get(b.name, 0)}
                         for b in arm.data.bones]}, f, indent=1)
with open(os.path.join(out_dir, "materials.json"), "w", encoding="utf-8") as f:
    json.dump({"id": cid, "fbx": os.path.basename(fbx), "source": bpy.data.filepath, "scale": scale,
               "turned_deg": round(math.degrees(turn), 2), "materials": materials, "meshes": mesh_info}, f, indent=1)
print(f"[sb_fbx] {fbx}: {len(meshes)} meshes, {len(materials)} materials")
