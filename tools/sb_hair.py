"""Another hairstyle of Stellar Blade's for Eve (user 10-17: "用 Alice 的专属发型"): run before tools/sb_fbx.py in the same
Blender session, so her archived .blend is never saved and sb_fbx.py still finds its textures next to it:

    blender -b --factory-startup <Eve_CH_P_EVE_Nikke_06.blend> --python tools/sb_hair.py --python tools/sb_fbx.py -- \
        <out dir> <id> --hair <the hairstyle's .psk>

  - the hairstyle: its skeletal mesh as ActorX (.psk) from the game's packages (CUE4Parse CLI --mesh-format ActorX: the
    NIKKE collaboration's EVE_HR_Nikke_06 under SB/Content/DLC_2/Art/Character/PC/00_HR); imported by io_scene_psk_psa
    (5.0.6, the Blender 3.6 user add-on ripper_tpose validate_eve.py imports with, so its axes are the archive's);
  - out go the .blend's own hair meshes (the cap Eve_Hair_Mesh_01, both ponytails) and the bones only they are weighted
    to (Hair_Root and its locks, the ponytail chains Ab-TL-HairB*, the nape strands Ab_SHair_*): the new hairstyle has
    its own bones of the same names (the front locks Ab-HairVF*, the side locks Ab_Hair_ex*) in other places;
  - its skeleton's root is the head (Bip001-Head): the whole piece is moved so that bone's rest matrix is the body's (as
    validate_eve.py hangs the nape strands), joined into Eve_Armature, and its own head bone dropped (its vertex group
    then means the body's head);
  - its mesh becomes Eve_Hair_Mesh_01 with the game's material names (the hairstyle's MI_EVE_HR_Nikke_06, the ties in
    the outfit's MI_CH_P_EVE_Nikke_06_UV2 - the importer reuses the .blend's material of that name).
tools/sb_textures.py then reads the hair's material instance (--mi-dir), tools/sb_physics.py its anim blueprint's
KawaiiPhysics nodes (--hair, --hair-dlc).
"""
import re
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if "--hair" not in argv:
    raise SystemExit("sb_hair.py: --hair <the hairstyle's .psk> after --")
psk = argv[argv.index("--hair") + 1]
HAIR_MESHES = re.compile(r"^Eve_Hair(Tail|TailShort)?_Mesh_\d+$")
HEAD = "Bip001-Head"

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
old = [o for o in bpy.data.objects if o.type == 'MESH' and HAIR_MESHES.match(o.name)]
keep = [o for o in bpy.data.objects if o.type == 'MESH' and o not in old]


def weighted(objs):
    out = set()
    for o in objs:
        names = {g.index: g.name for g in o.vertex_groups}
        for v in o.data.vertices:
            for g in v.groups:
                if g.weight > 0.0:
                    out.add(names[g.group])
    return out


theirs, ours = weighted(old), weighted(keep)
# the bones only the old hair is weighted to, and every bone below them nothing else is weighted to
drop = set()
for b in arm.data.bones:
    chain, p = [], b
    while p is not None:
        chain.append(p.name)
        p = p.parent
    if b.name in ours or b.name == HEAD:
        continue
    if any(n in theirs and n not in ours for n in chain):
        drop.add(b.name)
assert not any(n in ours for n in drop)
print(f"[sb_hair] out: {', '.join(o.name for o in old)} and {len(drop)} bones only they use")
for o in old:
    mesh = o.data
    bpy.data.objects.remove(o)
    if mesh.users == 0:
        bpy.data.meshes.remove(mesh)

bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for name in drop:
    eb = arm.data.edit_bones.get(name)
    if eb is not None:
        arm.data.edit_bones.remove(eb)
bpy.ops.object.mode_set(mode='OBJECT')

# ---- the new hairstyle
import addon_utils  # noqa: E402
addon_utils.enable("io_scene_psk_psa", default_set=False)
before = set(bpy.data.objects)
# the add-on's operator: import_scene.psk in 5.0.6, psk.import_file in later versions (bpy.ops hands out any name, so ask
# the operator for its type)
try:
    bpy.ops.import_scene.psk.get_rna_type()
    op = bpy.ops.import_scene.psk
except (AttributeError, KeyError, RuntimeError):
    op = bpy.ops.psk.import_file
if "FINISHED" not in op(filepath=psk):
    raise SystemExit(f"sb_hair.py: the import of {psk} did not finish")
new = [o for o in bpy.data.objects if o not in before]
hair_arm = next(o for o in new if o.type == 'ARMATURE')
hair = next(o for o in new if o.type == 'MESH')
if hair_arm.data.bones[0].name != HEAD or hair_arm.data.bones[0].parent is not None:
    raise SystemExit(f"sb_hair.py: the hairstyle's root is {hair_arm.data.bones[0].name}, not {HEAD}")
clash = [b.name for b in hair_arm.data.bones if b.name != HEAD and b.name in arm.data.bones]
if clash:
    raise SystemExit(f"sb_hair.py: bones of the hairstyle the body still has: {clash}")

# its head on the body's head (full rest matrix): the importer parents the mesh to its armature, so moving the armature
# moves both
body_head = arm.matrix_world @ arm.data.bones[HEAD].matrix_local
its_head = hair_arm.matrix_world @ hair_arm.data.bones[HEAD].matrix_local
hair_arm.matrix_world = body_head @ its_head.inverted() @ hair_arm.matrix_world
bpy.context.view_layer.update()
err = ((hair_arm.matrix_world @ hair_arm.data.bones[HEAD].matrix_local).translation - body_head.translation).length
print(f"[sb_hair] {hair.name}: {len(hair.data.vertices)} vertices, {len(hair_arm.data.bones)} bones; head on head ({err:.6f} off)")

for p in hair.data.polygons:
    p.use_smooth = True
if hasattr(hair.data, "use_auto_smooth"):
    hair.data.use_auto_smooth = False          # as validate_eve.py imports the archive's hair
world = hair.matrix_world.copy()
hair.parent = None
hair.matrix_world = world
bpy.ops.object.select_all(action='DESELECT')
hair_arm.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.join()
bpy.ops.object.mode_set(mode='EDIT')
ebs = arm.data.edit_bones
dup = next(b for b in ebs if b.name.startswith(HEAD + ".") and b.name[len(HEAD) + 1:].isdigit())
for c in list(dup.children):
    c.parent = ebs[HEAD]
ebs.remove(dup)
bpy.ops.object.mode_set(mode='OBJECT')
hair.parent = arm
hair.matrix_world = world
for md in hair.modifiers:
    if md.type == 'ARMATURE':
        md.object = arm
hair.name = "Eve_Hair_Mesh_01"
hair.data.name = "Eve_Hair_Mesh_01"
bpy.context.view_layer.update()
print(f"[sb_hair] {arm.name}: {len(arm.data.bones)} bones; materials {[s.material.name for s in hair.material_slots if s.material]}")
