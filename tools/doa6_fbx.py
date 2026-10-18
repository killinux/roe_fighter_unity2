"""A Dead or Alive 6 character for the fighter: the assembled .blend of ripper_tpose scripts/doa6 (export_full.ps1:
costume + hair + face, each on its own armature, textures packed) -> one FBX on one skeleton + its textures + a
materials sidecar, into the Unity project (Assets/DOA/<id>/, gitignored - game data, personal use only).

    blender -b <KAS_Kasumi.blend> --python tools/doa6_fbx.py -- <out dir> [<id>]

  - the three armatures are joined into the costume's: bones of the same name (bone_11 neck, bone_12 head: the global
    bone ids every DOA6 part shares) become one, their children and vertex groups move over;
  - the model is lifted so the lowest vertex stands on the floor (the G1M rest has the hips at the origin);
  - packed textures are written as they are (PNG bytes), materials.json says which colour / normal map each material
    uses and whether its colour map has alpha (hair, lashes: names with _BLEND);
  - the .blend itself is never saved.
"""
import bpy
import json
import os
import sys

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = os.path.abspath(argv[0]) if argv else os.path.abspath("doa6_out")
cid = argv[1] if len(argv) > 1 else os.path.splitext(os.path.basename(bpy.data.filepath))[0]
tex_dir = os.path.join(out_dir, "textures")
os.makedirs(tex_dir, exist_ok=True)

# ---- textures, as packed
written = {}
for im in bpy.data.images:
    if im.packed_file is None:
        continue
    name = os.path.basename(im.filepath) or (im.name if im.name.lower().endswith(".png") else im.name + ".png")
    path = os.path.join(tex_dir, name)
    with open(path, "wb") as f:
        f.write(im.packed_file.data)
    written[im.name] = name
print(f"[doa6_fbx] {len(written)} textures -> {tex_dir}")

# ---- materials: colour / normal map per material
materials = {}
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    alb = nmh = None
    for n in m.node_tree.nodes:
        if n.type != 'TEX_IMAGE' or not n.image:
            continue
        nm = n.image.name.lower()
        if "kidsalb" in nm:
            alb = written.get(n.image.name)
        elif "kidsnmh" in nm:
            nmh = written.get(n.image.name)
    if alb or nmh:
        materials[m.name] = {"albedo": alb, "normal": nmh, "alpha": bool(alb and "_blend_" in alb.lower())}

# ---- one skeleton
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
base = next((a for a in arms if "_COS_" in a.name), arms[0])
others = [a for a in arms if a is not base]
base_names = {b.name for b in base.data.bones}
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in others:
    if o.matrix_world != base.matrix_world:
        raise SystemExit(f"{o.name}: another armature space than {base.name}")
bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
bpy.ops.object.select_all(action='DESELECT')
for o in [base] + others:
    o.select_set(True)
bpy.context.view_layer.objects.active = base
bpy.ops.object.join()
# duplicates came in as <name>.001 (or .002 for the third part): fold them into the original
bpy.ops.object.mode_set(mode='EDIT')
eb = base.data.edit_bones
folded = 0
for b in list(eb):
    stem, dot, num = b.name.rpartition(".")
    if dot and num.isdigit() and stem in base_names and stem in eb:
        keep = eb[stem]
        for c in list(b.children):
            c.parent = keep
        eb.remove(b)
        folded += 1
bpy.ops.object.mode_set(mode='OBJECT')
renamed = 0
for o in meshes:
    for vg in o.vertex_groups:
        stem, dot, num = vg.name.rpartition(".")
        if dot and num.isdigit() and stem in base.data.bones and vg.name not in base.data.bones:
            if o.vertex_groups.get(stem) is None:
                vg.name = stem
                renamed += 1
    for md in o.modifiers:
        if md.type == 'ARMATURE':
            md.object = base
    if o.parent is not base:
        mw = o.matrix_world.copy()
        o.parent = base
        o.matrix_world = mw
print(f"[doa6_fbx] joined {len(others)} armatures into {base.name}: {folded} duplicate bones folded, {renamed} vertex groups renamed, "
      f"{len(base.data.bones)} bones")

# ---- on the floor
deps = bpy.context.evaluated_depsgraph_get()
low = min((o.evaluated_get(deps).matrix_world @ v.co).z for o in meshes for v in o.evaluated_get(deps).data.vertices)
base.location.z -= low
bpy.context.view_layer.update()
print(f"[doa6_fbx] lifted {-low * 100:.1f} cm so the soles stand on the floor")

# ---- export
base.name = cid
bpy.ops.object.select_all(action='DESELECT')
base.select_set(True)
for o in meshes:
    o.select_set(True)
fbx = os.path.join(out_dir, cid + ".fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                         add_leaf_bones=False, use_armature_deform_only=False, mesh_smooth_type='FACE', use_tspace=True,
                         path_mode='STRIP', embed_textures=False, bake_anim=False)
mesh_info = {o.name: {"verts": len(o.data.vertices), "materials": [m.name for m in o.data.materials if m],
                      "groups": [vg.name for vg in o.vertex_groups][:64]} for o in meshes}
with open(os.path.join(out_dir, "materials.json"), "w", encoding="utf-8") as f:
    json.dump({"id": cid, "fbx": os.path.basename(fbx), "materials": materials, "meshes": mesh_info}, f, indent=1)
print(f"[doa6_fbx] {fbx}: {len(meshes)} meshes, {len(materials)} materials")
