"""Fiona's nude base for a Vindictus outfit (the fighter's clothes burst, Editor/Burst/fio005.json; user 10-06: "fiona的爆衣先做这个吧，
对战的是inase"), from the game's own base body:

  SM_Fiona_Body01 - Vindictus ships her new-rig base mesh as a STATIC mesh (Character/Player/BaseBody_PCF/Model; the archive's
  lister only takes SK_* parts, so it was never exported: tools/vdf_nude_export.ps1). Its body (MI_PCF_Body01) and hands and
  feet (MI_PCF_HandFoot01) are the old prework body (the archive's Fiona_BaseBody) re-textured as a whole nude: the "B" skin of
  her outfits PCF_002..012. PCF_005 wears the "A" skin (Upper01 / Lower01 / Hand01), cut away under the dress and its other
  A outfits too (none shows the nipples), so the B body is the only whole one. Here it is:
   1. moved onto the old body (6.4 cm in y, median 2 mm after) and skinned with its Bip001 weights (nearest face);
   2. posed: each Bip001 bone turned so it points where the outfit's UE bone points (spine, neck, clavicles, arms, the hand by
      its middle finger and palm width, the fingers' first two segments - the last joints' imported tails run along nothing in
      either rig, so they follow their parents - thighs, calves, feet; the rig moved pelvis on pelvis). FK keeps the old
      body's lengths: the joints still miss by up to 1.1 cm;
   3. baked and laid on the outfit's own skin: each vertex to the nearest point of it within SNAP_REACH (hands on its hands,
      SNAP_REACH_HANDS) facing the same way (SNAP_FACING) and not off to the side (at a patch's edge the vertex beyond it
      would be pulled onto the edge), else on the skin of her other A outfits (--others: under the dress), the rest moved
      with its neighbours (the moves spread over the mesh, LAPLACE_ITERS passes, then SMOOTH_PASSES of the whole field: it
      blends the body's own breasts into the outfit's dress-pressed skin round them, and pulls the laid vertices up to 5 mm
      off it - so near the --inside pieces (the lace cuffs: 1-2 mm over the outfit's forearm, a net the body is seen through)
      they go back onto the skin, within BACK_NEAR wholly, blended out by BACK_FAR; else the body came out through the net);
      the nails are shells of their own: each nail vertex moves as the body under it (NAIL_NEAREST nearest body vertices);
   4. under the face's bib (UNDER_FACE, by nothing at its edge), and the neck's open edge stitched onto the bib's lower edge
      (it ended ~1 cm short: a crack line round the neck) and NECK_OVERLAP on under the face, NECK_UNDER deep there and flush
      with the bib at its edge (as the outfit's skin is: 1.5 mm under the edge itself left a step whose shadow was a dark
      dashed line in Unity), the vertices within NECK_SPREAD moved along and laid again;
   5. on the outfit's rig with the outfit's skin weights (barycentric at the nearest point of its skin; the burst does not use
      them - RoeNudeBody copies the dressed fighter's own - but the .blend poses right);
   6. the B materials of a B outfit's .blend (--b-from), with her face's skin tint: every outfit tints the face (multiply
      1 / 0.905 / 0.905, value x0.93), the B bodies not, and untinted both textures are the same colour where they meet at
      the bib - so with the face's tint the seam does not show (it was a faint tone line).
  Normals: the body's own, but along the face's bib the outfit's skin's (matched to the bib by the game's artists), blended
  back to its own over BIB_NORMALS - its own up to the bib drew a shading line there; the outfit's everywhere was blotchy.
  Result for PCF_005: on its skin median 0.3 mm, p90 2.6 mm (2.7 cm at most, under the dress where it has no skin); the bib's
  edge on the body 0.25 mm (median), as on PCF_005's own skin 0.
  Writes <out>/<name>.blend (the outfit's rig + the body; the B outfit's build.log next to it for vdf_fbx.py's material kinds)
  and check pictures <out>/<name>_*.png. Then:
    blender -b --factory-startup <out>/fio_nude.blend --python tools/vdf_fbx.py -- Assets/VDF/fio_nude fio_nude
    python tools/vdf_textures.py Assets/VDF/fio_nude
    Unity: -executeMethod RoeFighter.EditorTools.VdfFighter.BuildBase [-roeVdf fio_nude]

    blender -b --factory-startup <archive>/Fiona_BaseBody/Fiona_BaseBody.blend --python tools/vdf_nude.py -- <out>
        [--outfit PCF_005] [--name fio_nude] [--pskx <SM_Fiona_Body01.pskx>] [--archive E:/game_export/Vindictus/Fiona/blend]
        [--b-from PCF_007] [--others Fiona,PCF_001,PCF_008] [--inside Hand] [--set SNAP_REACH=3.0 ...]
  Game data: personal use only; the .blend and everything made from it stay out of git (Assets/VDF is ignored).
"""
import math
import os
import shutil
import sys
import addon_utils
import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

# tunables (cm unless said), each with --set NAME=value
T = {
    "SNAP_REACH": 3.0,          # the outfit's skin this close (and facing the same way) is where the body goes
    "SNAP_REACH_HANDS": 2.0,    # ... for the hands, on its hands
    "SNAP_FACING": 0.4,         # cos: the skin's normal and the body's at least this alike (either way round)
    "LAPLACE_ITERS": 400,       # passes spreading the laid vertices' moves over the rest
    "SMOOTH_PASSES": 3,         # light smoothing of the whole move field (overlapping outfit layers differ a little)
    "NAIL_NEAREST": 6,          # a nail vertex moves as this many nearest body vertices (inverse distance)
    "UNDER_FACE": 0.25,         # how deep under the face's bib the body lies (none at the bib's edge, all 1.5 cm inside)
    "NECK_UNDER": 0.05,         # the neck's edge this far under the face, NECK_OVERLAP up from the bib's lower edge (the body
    "NECK_OVERLAP": 1.0,        #   flush with the bib at its edge, as the outfit's skin is, sinking in to NECK_UNDER by here)
    "BIB_NORMALS": 3.0,         # below the bib's lower edge the outfit's skin's normals, blended back to the body's own by here
    "NECK_SPREAD": 4.0,         # the vertices within this of the neck's edge move along (smoothstep)
    "BACK_NEAR": 1.0,           # the laid vertices this close to an --inside piece go back onto the skin after the smoothing
    "BACK_FAR": 3.0,            #   blended back to the smoothed place by here
}
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
opts = {"outfit": "PCF_005", "name": "fio_nude", "archive": r"E:\game_export\Vindictus\Fiona\blend",
        "pskx": r"E:\tools\vindictus\_scratch_export\fiona_body01\VindictusRoot\Character\Player\BaseBody_PCF\Model\SM_Fiona_Body01.pskx",
        "b-from": "PCF_007", "others": "Fiona,PCF_001,PCF_008", "inside": "Hand"}
out_dir = None
k = 0
while k < len(argv):
    a = argv[k]
    if a == "--set":
        key, val = argv[k + 1].split("=", 1)
        assert key in T, f"--set: no tunable {key} ({', '.join(T)})"
        T[key] = type(T[key])(float(val)) if isinstance(T[key], float) else int(val)
        k += 2
    elif a.startswith("--"):
        assert a[2:] in opts, f"unknown option {a}"
        opts[a[2:]] = argv[k + 1]
        k += 2
    else:
        out_dir = a
        k += 1
assert out_dir, "usage: ... --python tools/vdf_nude.py -- <out dir> [options]"
os.makedirs(out_dir, exist_ok=True)
ROOT = opts["archive"]
PSKX = opts["pskx"]
OUTFIT = opts["outfit"]
B_FROM = opts["b-from"]
NAME = opts["name"]
SNAP_REACH, SNAP_REACH_HANDS, SNAP_FACING = T["SNAP_REACH"], T["SNAP_REACH_HANDS"], T["SNAP_FACING"]
INSIDE = tuple(x for x in opts["inside"].split(",") if x)       # the outfit objects (name parts) the body must stay inside exactly
SKIN_A = ("MI_PCF_Upper01", "MI_PCF_Lower01", "MI_PCF_Hand01")


def base(n):
    return n.split(".")[0]


def log(*a):
    print("[fit]", *a)


scene = bpy.context.scene
old_rig = bpy.data.objects["Fiona_BaseBody_rig"]
old_body = bpy.data.objects["Fiona_BaseBody_BaseBody"]
for o in list(bpy.data.objects):
    if o.type == "MESH" and o is not old_body:
        bpy.data.objects.remove(o)

# ---- 1. the game's base mesh, its body faces, onto the old body (best translation), the old body's Bip001 weights
addon_utils.enable("io_scene_psk_psa", default_set=True)
before = set(bpy.data.objects)
bpy.ops.import_scene.psk(filepath=PSKX, should_import_vertex_colors=False, should_import_vertex_normals=True, should_import_extra_uvs=False,
                         should_import_mesh=True, should_import_materials=True, should_reuse_materials=False, should_import_skeleton=True,
                         should_import_shape_keys=False, bone_length=1.0)
created = [o for o in bpy.data.objects if o not in before]
body = next(o for o in created if o.type == "MESH")
for o in created:
    if o is not body:
        bpy.data.objects.remove(o)
body.parent = None
bm = bmesh.new()
bm.from_mesh(body.data)
keep = {i for i, m in enumerate(body.data.materials) if m and base(m.name) in ("MI_PCF_Body01", "MI_PCF_HandFoot01")}
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index not in keep], context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(body.data)
bm.free()
# drop the unused material slots
body_mats = [base(m.name) if m else None for m in body.data.materials]
log("body", len(body.data.vertices), "verts", len(body.data.polygons), "faces")

deps = bpy.context.evaluated_depsgraph_get()
ob_eval = old_body.evaluated_get(deps)
old_mesh = ob_eval.to_mesh()
obm = bmesh.new()
obm.from_mesh(old_mesh)
obm.transform(old_body.matrix_world)
keep_old = {i for i, m in enumerate(old_mesh.materials) if m and ("female_body05" in m.name or "female_handfoot05" in m.name)}
bmesh.ops.delete(obm, geom=[f for f in obm.faces if f.material_index not in keep_old], context="FACES")
old_tree = BVHTree.FromBMesh(obm)
ob_eval.to_mesh_clear()
shift = Vector((0, 0, 0))
for it in range(6):
    acc = Vector()
    n = 0
    for v in list(body.data.vertices)[::7]:
        p = body.matrix_world @ v.co + shift
        hit = old_tree.find_nearest(p, 20.0)
        if hit[0] is not None:
            acc += hit[0] - p
            n += 1
    shift += acc / max(n, 1)
body.location += shift
bpy.context.view_layer.update()
ds = sorted((old_tree.find_nearest(body.matrix_world @ v.co, 20.0)[0] - body.matrix_world @ v.co).length for v in body.data.vertices)
log(f"onto the old body: shift {tuple(round(x, 2) for x in shift)}, distance median {ds[len(ds) // 2]:.3f} cm, p99 {ds[int(0.99 * len(ds))]:.3f}, max {ds[-1]:.2f}")
obm.free()
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for g in old_body.vertex_groups:
    if g.name.startswith("Bip001") and body.vertex_groups.get(g.name) is None:
        body.vertex_groups.new(name=g.name)
mod = body.modifiers.new("w", "DATA_TRANSFER")
mod.object = old_body
mod.use_vert_data = True
mod.data_types_verts = {"VGROUP_WEIGHTS"}
mod.vert_mapping = "POLYINTERP_NEAREST"
mod.layers_vgroup_select_src = "ALL"
mod.layers_vgroup_select_dst = "NAME"
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
am = body.modifiers.new("Armature", "ARMATURE")
am.object = old_rig
body.parent = old_rig
body.matrix_parent_inverse = old_rig.matrix_world.inverted()
none = sum(1 for v in body.data.vertices if not any(g.weight > 1e-4 for g in v.groups))
log("Bip001 weights:", len(body.vertex_groups), "groups,", none, "vertices without")

# ---- 2. PCF_005: its rig (bind pose) and its skin; Fiona / PCF_001 / PCF_008: their skin (for under the dress)
def load_objects(path, want):
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if want(n)]
    objs = []
    for o in dst.objects:
        if o is not None:
            scene.collection.objects.link(o)
            objs.append(o)
    return objs


a_objs = load_objects(os.path.join(ROOT, OUTFIT, OUTFIT + ".blend"), lambda n: True)
a_rig = next(o for o in a_objs if o.type == "ARMATURE")
log(OUTFIT, "rig", a_rig.name, len(a_rig.data.bones))


def skin_bmesh(objs, mats):
    bmx = bmesh.new()
    deps = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.type != "MESH":
            continue
        me = o.evaluated_get(deps).to_mesh()
        keep = {i for i, m in enumerate(me.materials) if m and base(m.name) in mats}
        if keep:
            tmp = bmesh.new()
            tmp.from_mesh(me)
            bmesh.ops.delete(tmp, geom=[f for f in tmp.faces if f.material_index not in keep], context="FACES")
            tmp.transform(o.matrix_world)
            for f in tmp.faces:
                f.material_index = sorted(keep).index(f.material_index) if False else f.material_index
            mesh_tmp = bpy.data.meshes.new("tmp")
            tmp.to_mesh(mesh_tmp)
            bmx.from_mesh(mesh_tmp)
            bpy.data.meshes.remove(mesh_tmp)
            tmp.free()
        o.evaluated_get(deps).to_mesh_clear()
    return bmx


a_skin = skin_bmesh(a_objs, SKIN_A)
a_hand = skin_bmesh(a_objs, ("MI_PCF_Hand01",))
log(OUTFIT, "skin faces", len(a_skin.faces), "hands", len(a_hand.faces))
others = []
for name in [x for x in opts["others"].split(",") if x and x != OUTFIT]:
    others += load_objects(os.path.join(ROOT, name, name + ".blend"), lambda n: True)
o_skin = skin_bmesh(others, ("MI_PCF_Upper01", "MI_PCF_Lower01"))
log("other outfits' skin faces (no hands)", len(o_skin.faces))
for o in others:
    if o.type == "ARMATURE":
        bpy.data.objects.remove(o)
for o in list(bpy.data.objects):
    if o in others:
        try:
            bpy.data.objects.remove(o)
        except ReferenceError:
            pass

# ---- 3. the Bip001 bones onto PCF_005's bones (FK: each bone turned so it points where the matching UE bone points)
A = lambda n: a_rig.matrix_world @ a_rig.data.bones[n].head_local
At = lambda n: a_rig.matrix_world @ a_rig.data.bones[n].tail_local
bpy.context.view_layer.objects.active = old_rig
bpy.ops.object.mode_set(mode="POSE")
PB = old_rig.pose.bones
M = old_rig.matrix_world


def head(n):
    return M @ PB[n].head


def tail(n):
    return M @ PB[n].tail


def turn(n, rot, pivot):
    pb = PB[n]
    W = M @ pb.matrix
    T = Matrix.Translation(pivot) @ rot.to_matrix().to_4x4() @ Matrix.Translation(-pivot)
    pb.matrix = M.inverted() @ T @ W
    bpy.context.view_layer.update()


def aim(n, cur_to, target_from, target_to, limit=75.0):
    cur = cur_to - head(n)
    tgt = target_to - target_from
    if cur.length < 1e-6 or tgt.length < 1e-6:
        return 0.0
    rot = cur.rotation_difference(tgt)
    ang = math.degrees(rot.angle)
    if ang > limit:
        log(f"  {n}: {ang:.0f} deg - more than {limit}, not turned")
        return ang
    turn(n, rot, head(n))
    return ang


def frame(primary, secondary):
    x = primary.normalized()
    z = x.cross(secondary).normalized()
    y = z.cross(x)
    return Matrix((x, y, z)).transposed()


def align2(n, cur_p, cur_s, tgt_p, tgt_s):
    F0 = frame(cur_p, cur_s)
    F1 = frame(tgt_p, tgt_s)
    rot = (F1 @ F0.transposed()).to_quaternion()
    turn(n, rot, head(n))
    return math.degrees(rot.angle)


# the whole rig over: Bip001_Pelvis on pelvis
old_rig.location += A("pelvis") - head("Bip001_Pelvis")
bpy.context.view_layer.update()
turned = []
chain = [("Bip001_Spine", "Bip001_Spine1", "spine_02", "spine_03"), ("Bip001_Spine1", "Bip001_Spine2", "spine_03", "spine_04"),
         ("Bip001_Spine2", "Bip001_Spine3", "spine_04", "spine_05"), ("Bip001_Spine3", "Bip001_Neck", "spine_05", "neck_01"),
         ("Bip001_Neck", "Bip001_Head", "neck_01", "head")]
for n, c, u, uc in chain:
    turned.append((n, aim(n, head(c), A(u), A(uc))))
for s, us in (("L", "l"), ("R", "r")):
    for n, c, u, uc in ((f"Bip001_{s}_Clavicle", f"Bip001_{s}_UpperArm", f"clavicle_{us}", f"upperarm_{us}"),
                        (f"Bip001_{s}_UpperArm", f"Bip001_{s}_Forearm", f"upperarm_{us}", f"lowerarm_{us}"),
                        (f"Bip001_{s}_Forearm", f"Bip001_{s}_Hand", f"lowerarm_{us}", f"hand_{us}")):
        turned.append((n, aim(n, head(c), A(u), A(uc), limit=100.0)))
    # the hand: towards the middle finger, the palm's width from index to little finger
    n = f"Bip001_{s}_Hand"
    turned.append((n, align2(n, head(f"Bip001_{s}_Finger2") - head(n), head(f"Bip001_{s}_Finger4") - head(f"Bip001_{s}_Finger1"),
                             A(f"middle_01_{us}") - A(f"hand_{us}"), A(f"pinky_01_{us}") - A(f"index_01_{us}"))))
    for f, uf in (("0", "thumb"), ("1", "index"), ("2", "middle"), ("3", "ring"), ("4", "pinky")):
        names = [f"Bip001_{s}_Finger{f}", f"Bip001_{s}_Finger{f}1", f"Bip001_{s}_Finger{f}2"]
        unames = [f"{uf}_01_{us}", f"{uf}_02_{us}", f"{uf}_03_{us}"]
        # FK keeps the old body's lengths, only directions; the last joint has no child, and the imported tails do not run
        # along the bones (either rig): it is not turned, it follows its parent (the laying on the skin does the rest)
        for k in range(2):
            turned.append((names[k], aim(names[k], head(names[k + 1]), A(unames[k]), A(unames[k + 1]), limit=120.0)))
    for n, c, u, uc in ((f"Bip001_{s}_Thigh", f"Bip001_{s}_Calf", f"thigh_{us}", f"calf_{us}"),
                        (f"Bip001_{s}_Calf", f"Bip001_{s}_Foot", f"calf_{us}", f"foot_{us}"),
                        (f"Bip001_{s}_Foot", f"Bip001_{s}_Toe0", f"foot_{us}", f"ball_{us}")):
        turned.append((n, aim(n, head(c), A(u), A(uc))))
log("turned (deg):", ", ".join(f"{n.replace('Bip001_', '')} {a:.0f}" for n, a in turned))
# joints that still miss their UE joint (FK keeps the old body's lengths)
miss = []
for b, u in (("Bip001_L_UpperArm", "upperarm_l"), ("Bip001_L_Forearm", "lowerarm_l"), ("Bip001_L_Hand", "hand_l"), ("Bip001_L_Finger21", "middle_02_l"),
             ("Bip001_L_Calf", "calf_l"), ("Bip001_L_Foot", "foot_l"), ("Bip001_L_Toe0", "ball_l"), ("Bip001_Neck", "neck_01"), ("Bip001_Head", "head")):
    miss.append(f"{b.replace('Bip001_', '')} {(head(b) - A(u)).length:.1f}")
log("joint misses (cm):", ", ".join(miss))
bpy.ops.object.mode_set(mode="OBJECT")

# ---- 4. the posed body baked
deps = bpy.context.evaluated_depsgraph_get()
posed = body.evaluated_get(deps).to_mesh()
P = [body.matrix_world @ v.co for v in posed.vertices]
Nrm = [(body.matrix_world.to_3x3() @ v.normal).normalized() for v in posed.vertices]
body.evaluated_get(deps).to_mesh_clear()
a_tree = BVHTree.FromBMesh(a_skin)
h_tree = BVHTree.FromBMesh(a_hand)
o_tree = BVHTree.FromBMesh(o_skin)
hand_vert = set()
for poly in body.data.polygons:
    if body_mats[poly.material_index] == "MI_PCF_HandFoot01":
        hand_vert.update(poly.vertices)


def dist_stats(label, tree, idx):
    ds = sorted((tree.find_nearest(P[i], 50.0)[0] - P[i]).length if tree.find_nearest(P[i], 50.0)[0] is not None else 50.0 for i in idx)
    if ds:
        log(f"{label}: n {len(ds)}, median {ds[len(ds) // 2]:.2f} cm, p90 {ds[int(0.9 * (len(ds) - 1))]:.2f}, max {ds[-1]:.2f}")


dist_stats(f"posed body -> {OUTFIT} skin (all)", a_tree, range(len(P)))
dist_stats(f"posed hands/feet -> {OUTFIT} skin", a_tree, sorted(hand_vert))

# ---- 5. laid on PCF_005's skin (hands on its hands); under the face's bib 2.5 mm under the bib (the face covers the
# body's neck: its lower edge must not be covered); else the other outfits' skin; the rest moved with its neighbours.
# A nearest point off to the side (at the edge of a skin patch, the vertex beyond it) is not taken: the vertex would be
# pulled onto the patch's edge.
face_obj = next(o for o in a_objs if o.type == "MESH" and "Face" in o.name)
f_skin = skin_bmesh([face_obj], ("MI_Fiona_Face01_",))
f_tree = BVHTree.FromBMesh(f_skin)
log("face skin faces", len(f_skin.faces))
UNDER_FACE = T["UNDER_FACE"]
target = [None] * len(P)
source = [0] * len(P)
for i, p in enumerate(P):
    trees = (((h_tree, SNAP_REACH_HANDS, 1, 0.0), (a_tree, SNAP_REACH, 1, 0.0)) if i in hand_vert else
             ((a_tree, SNAP_REACH, 1, 0.0), (f_tree, SNAP_REACH, 3, UNDER_FACE), (o_tree, SNAP_REACH, 2, 0.0)))
    for tree, reach, src, under in trees:
        q, n, fi, d = tree.find_nearest(p, reach)
        if q is None:
            continue
        if Nrm[i].dot(n) < SNAP_FACING and Nrm[i].dot(-n) < SNAP_FACING:
            continue
        off = q - p
        if off.length > 0.1 and abs(off.normalized().dot(n)) < 0.8:
            continue
        nn = n if Nrm[i].dot(n) >= 0 else -n
        target[i] = q - nn * under
        source[i] = src
        break
log(f"targets: {OUTFIT} skin {sum(1 for s in source if s == 1)}, under the face {sum(1 for s in source if s == 3)}, "
    f"other outfits {sum(1 for s in source if s == 2)}, none {sum(1 for s in source if s == 0)}")
neighbours = [[] for _ in P]
for e in body.data.edges:
    a, b = e.vertices
    neighbours[a].append(b)
    neighbours[b].append(a)
disp = [(target[i] - P[i]) if target[i] is not None else None for i in range(len(P))]
free = [i for i in range(len(P)) if disp[i] is None]
for i in free:
    disp[i] = Vector()
for it in range(T["LAPLACE_ITERS"]):
    for i in free:
        nb = neighbours[i]
        if nb:
            disp[i] = sum((disp[j] for j in nb), Vector()) / len(nb)
# a light smoothing of the whole field (the targets of neighbouring vertices on overlapping outfit layers differ a little)
for it in range(T["SMOOTH_PASSES"]):
    new = []
    for i in range(len(P)):
        nb = neighbours[i]
        new.append(disp[i] * 0.5 + (sum((disp[j] for j in nb), Vector()) / len(nb)) * 0.5 if nb else disp[i])
    disp = new
# the vertices laid on the outfit's skin back on it near the --inside pieces: the smoothing pulled them off (up to 5 mm) - out
# through PCF_005's lace cuffs, which lie 1-2 mm over its own forearm. Not everywhere: back on the skin round her breasts (the
# dress-pressed skin the bodice shows) creased them where they meet her own.
inside_bm = skin_bmesh([o for o in a_objs if any(k in o.name for k in INSIDE)],
                       {base(s_.material.name) for o in a_objs if any(k in o.name for k in INSIDE)
                        for s_ in o.material_slots if s_.material and base(s_.material.name) not in SKIN_A})
inside_tree = BVHTree.FromBMesh(inside_bm) if inside_bm.faces else None
back_on = 0
for i in range(len(P)):
    if source[i] != 1 or inside_tree is None:
        continue
    hit = inside_tree.find_nearest(P[i] + disp[i], T["BACK_FAR"])
    if hit[0] is None:
        continue
    w = 1.0 - max(0.0, min(1.0, (hit[3] - T["BACK_NEAR"]) / max(1e-6, T["BACK_FAR"] - T["BACK_NEAR"])))
    for tree in ((h_tree, a_tree) if i in hand_vert else (a_tree,)):
        q = tree.find_nearest(P[i] + disp[i], 1.0)[0]
        if q is not None:
            disp[i] = disp[i].lerp(q - P[i], w)
            back_on += 1
            break
inside_bm.free()
log(f"back on the skin near the {'/'.join(INSIDE)} pieces: {back_on} vertices")
moves = sorted(d.length for d in disp)
log(f"laid on: {OUTFIT} {sum(1 for s in source if s == 1)}, other outfits {sum(1 for s in source if s == 2)}, moved with neighbours {len(free)} "
    f"of {len(P)}; move median {moves[len(moves) // 2]:.2f} cm, p90 {moves[int(0.9 * len(moves))]:.2f}, max {moves[-1]:.2f}")
# the nails: separate shells (10 finger-, 10 toenails) - the moves above stay inside a shell, so a nail went by its own few laid
# vertices or not at all and stood off its finger (thin spikes at the fingertips, specks through the shoes' toes). Each nail
# vertex moves as the body under it does: inverse-distance mix of the nearest body vertices' moves, in the posed shape.
from mathutils.kdtree import KDTree  # noqa: E402
lab = [-1] * len(P)
n_isl = 0
for s0 in range(len(P)):
    if lab[s0] >= 0:
        continue
    stack = [s0]
    lab[s0] = n_isl
    while stack:
        i = stack.pop()
        for j in neighbours[i]:
            if lab[j] < 0:
                lab[j] = n_isl
                stack.append(j)
    n_isl += 1
sizes = [0] * n_isl
for l in lab:
    sizes[l] += 1
main = max(range(n_isl), key=lambda l: sizes[l])
body_idx = [i for i in range(len(P)) if lab[i] == main]
kd_main = KDTree(len(body_idx))
for k, i in enumerate(body_idx):
    kd_main.insert(P[i], k)
kd_main.balance()
shell = 0
for i in range(len(P)):
    if lab[i] == main:
        continue
    near = kd_main.find_n(P[i], T["NAIL_NEAREST"])
    ws = [1.0 / (d + 0.05) for _, _, d in near]
    disp[i] = sum((disp[body_idx[k]] * w for (_, k, _), w in zip(near, ws)), Vector()) / sum(ws)
    shell += 1
log(f"shells off the body (nails): {n_isl - 1}, {shell} vertices moved with the body under them")
final = [P[i] + disp[i] for i in range(len(P))]
P = final
# under the face's bib: the body goes under it, by nothing at the bib's lower edge (the body meets the edge there, as PCF_005's
# skin does) to 2.5 mm 1.5 cm inside (the face must cover the body's neck, and no crack shows under the edge)
from mathutils.kdtree import KDTree  # noqa: E402
edge_pts = []
for e in f_skin.edges:
    if len(e.link_faces) == 1 and min(v.co.z for v in e.verts) < 152:
        a_, b_ = (v.co for v in e.verts)
        for t in (0.0, 0.25, 0.5, 0.75):
            edge_pts.append(a_.lerp(b_, t))
kd = KDTree(len(edge_pts))
for k, p in enumerate(edge_pts):
    kd.insert(p, k)
kd.balance()
pushed = 0
for i, p in enumerate(P):
    q, n, fi, d = f_tree.find_nearest(p, 1.5)
    if q is None:
        continue
    off = p - q
    if off.length > 0.05 and abs(off.normalized().dot(n)) < 0.8:
        continue                                    # off to the side of the face: not under it
    nn = n if Nrm[i].dot(n) >= 0 else -n
    edge_d = kd.find(q)[2]
    ramp = min(1.0, edge_d / 1.5)
    ramp = ramp * ramp * (3 - 2 * ramp)
    depth = UNDER_FACE * ramp
    if off.dot(nn) > -depth:
        P[i] = q - nn * depth
        pushed += 1
log("under the face's bib:", pushed, "vertices")
# the body's neck edge under the bib: after the posing it ended ~1 cm short of the bib's lower edge (a crack round the neck where
# PCF_005's skin meets the bib exactly). Each edge vertex to the nearest point of the bib edge, then NECK_OVERLAP up the face
# from there and NECK_UNDER under it; the vertices within NECK_SPREAD moved along (inverse-distance mix of the nearest edge
# moves), then laid on the skin again, or under the face: by nothing at the bib's edge, NECK_UNDER by NECK_OVERLAP. The bib's
# edge thus lies on the body as on PCF_005's skin - stitched 1.5 mm under the edge itself, the step it left drew a dark dashed
# line (its shadow) along the edge across her upper chest in Unity.
NECK_UNDER = T["NECK_UNDER"]
NECK_SPREAD = T["NECK_SPREAD"]
NECK_OVERLAP = T["NECK_OVERLAP"]
bmb = bmesh.new()
bmb.from_mesh(body.data)
neck_b = sorted({v.index for e in bmb.edges if len(e.link_faces) == 1 for v in e.verts if P[v.index].z > 135})
bmb.free()
segs = [(e.verts[0].co.copy(), e.verts[1].co.copy()) for e in f_skin.edges
        if len(e.link_faces) == 1 and min(v.co.z for v in e.verts) < 152]


def on_bib_edge(p):
    best = None
    for a_, b_ in segs:
        ab = b_ - a_
        t = max(0.0, min(1.0, (p - a_).dot(ab) / ab.length_squared)) if ab.length_squared > 0 else 0.0
        q = a_ + ab * t
        d = (q - p).length
        if best is None or d < best[0]:
            best = (d, q)
    return best


edge_move = {}
gaps = []
for i in neck_b:
    d, q = on_bib_edge(P[i])
    gaps.append(d)
    hit = f_tree.find_nearest(q, 1.0)
    nn = hit[1] if hit[0] is not None else Nrm[i]
    if nn.dot(Nrm[i]) < 0:
        nn = -nn
    up = Vector((0, 0, 1)) - nn * nn.z                  # up the neck, along the face's surface
    hit = f_tree.find_nearest(q + up.normalized() * NECK_OVERLAP, 1.0) if up.length > 1e-6 else (None,)
    if hit[0] is not None:
        q, nn = hit[0], (hit[1] if hit[1].dot(Nrm[i]) >= 0 else -hit[1])
    edge_move[i] = (q - nn * NECK_UNDER) - P[i]
gaps.sort()
log(f"neck edge: {len(neck_b)} vertices, gap to the bib edge median {gaps[len(gaps) // 2]:.2f} cm, max {gaps[-1]:.2f}")
kd_b = KDTree(len(neck_b))
for k, i in enumerate(neck_b):
    kd_b.insert(P[i], k)
kd_b.balance()
moved = []
newP = list(P)
for i, p in enumerate(P):
    if i in edge_move:
        newP[i] = p + edge_move[i]
        continue
    near = kd_b.find_range(p, NECK_SPREAD)
    if not near:
        continue
    near.sort(key=lambda x: x[2])
    near = near[:4]
    dmin = near[0][2]
    w = 1.0 - dmin / NECK_SPREAD
    w = w * w * (3 - 2 * w)
    ws = [1.0 / (x[2] + 0.3) for x in near]
    mv = sum((edge_move[neck_b[x[1]]] * wk for x, wk in zip(near, ws)), Vector()) / sum(ws)
    newP[i] = p + mv * w
    moved.append(i)
P = newP
resnap_skin = resnap_face = 0
for i in moved:
    p = P[i]
    q, n, fi, d = a_tree.find_nearest(p, 1.0)
    if q is not None and ((q - p).length < 0.05 or abs((q - p).normalized().dot(n)) >= 0.8):
        P[i] = q
        resnap_skin += 1
        continue
    q, n, fi, d = f_tree.find_nearest(p, 1.0)
    if q is not None and ((q - p).length < 0.05 or abs((q - p).normalized().dot(n)) >= 0.8):
        nn = n if Nrm[i].dot(n) >= 0 else -n
        P[i] = q - nn * NECK_UNDER * min(1.0, on_bib_edge(q)[0] / NECK_OVERLAP)
        resnap_face += 1
log(f"neck: {len(moved)} vertices moved along, back on the skin {resnap_skin}, under the face {resnap_face}")
dist_stats(f"final body -> {OUTFIT} skin (all)", a_tree, range(len(P)))

# ---- 6. a new object: the final shape on PCF_005's rig, with PCF_005's skin weights (nearest face, interpolated)
nm = body.data.copy()
nm.name = "Fiona_Nude_Body"
for i, v in enumerate(nm.vertices):
    v.co = P[i]
nude = bpy.data.objects.new("Fiona_Nude_Body", nm)
scene.collection.objects.link(nude)
nude.vertex_groups.clear()
# PCF_005's skin weights: per vertex, the nearest point of its skin (any of its parts), the corners' weights by barycentric share
src = []
for o in a_objs:
    if o.type != "MESH" or not any(s.material and base(s.material.name) in SKIN_A for s in o.material_slots):
        continue
    me_ = o.data
    if hasattr(me_, "calc_normals_split"):
        me_.calc_normals_split()
    vn = [Vector() for _ in me_.vertices]
    for lp in me_.loops:
        vn[lp.vertex_index] += lp.normal
    nmat = o.matrix_world.to_3x3()
    vnw = [(nmat @ v).normalized() if v.length > 0 else Vector((0, 0, 1)) for v in vn]
    b2 = bmesh.new()
    b2.from_mesh(me_)
    ol = b2.verts.layers.int.new("orig")
    for v in b2.verts:
        v[ol] = v.index
    k2 = {i for i, s in enumerate(o.material_slots) if s.material and base(s.material.name) in SKIN_A}
    bmesh.ops.delete(b2, geom=[f for f in b2.faces if f.material_index not in k2], context="FACES")
    bmesh.ops.triangulate(b2, faces=b2.faces[:])
    b2.transform(o.matrix_world)
    b2.faces.ensure_lookup_table()
    names = {g.index: g.name for g in o.vertex_groups}
    src.append((BVHTree.FromBMesh(b2), b2, b2.verts.layers.deform.active, names, ol, vnw))
from mathutils.geometry import barycentric_transform  # noqa: E402
weights = []
skin_normal = []            # PCF_005's own (custom) normal at the nearest point of its skin, and how far that is
for i, p in enumerate(P):
    best = None
    for tree, b2, dl, names, ol, vnw in src:
        q, n, fi, d = tree.find_nearest(p, 100.0)
        if q is not None and (best is None or d < best[0]):
            best = (d, q, b2.faces[fi], dl, names, ol, vnw)
    acc = {}
    skin_normal.append(None)
    if best is not None:
        d, q, f, dl, names, ol, vnw = best
        a, b, c = (v.co for v in f.verts)
        # barycentric coordinates of q in abc
        v0, v1, v2 = b - a, c - a, q - a
        d00, d01, d11, d20, d21 = v0.dot(v0), v0.dot(v1), v1.dot(v1), v2.dot(v0), v2.dot(v1)
        den = d00 * d11 - d01 * d01
        wb = (d11 * d20 - d01 * d21) / den if den else 1 / 3
        wc = (d00 * d21 - d01 * d20) / den if den else 1 / 3
        wa = 1 - wb - wc
        nsum = Vector()
        for v, s in zip(f.verts, (wa, wb, wc)):
            nsum += vnw[v[ol]] * max(0.0, s)
            if dl is None:
                continue
            for gi, w in v[dl].items():
                nm_ = names.get(gi)
                if nm_:
                    acc[nm_] = acc.get(nm_, 0.0) + max(0.0, s) * w
        if nsum.length > 0:
            skin_normal[-1] = (nsum.normalized(), d)
    top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
    tot = sum(w for _, w in top)
    weights.append([(n_, w / tot) for n_, w in top] if tot > 0 else [("pelvis", 1.0)])
groups = {}
for i, ws in enumerate(weights):
    for n_, w in ws:
        g = groups.get(n_) or nude.vertex_groups.new(name=n_)
        groups[n_] = g
        g.add([i], w, "REPLACE")
for tree, b2, dl, names, ol, vnw in src:
    b2.free()
log(OUTFIT, "weights on the nude body:", len(nude.vertex_groups), "groups")
# normals: the body's own (as the game's mesh has them, moved with it) - PCF_005's own (custom, the artists matched them to the
# face's bib) everywhere made the shading blotchy, a patchwork with the body's - except along the face's bib: within
# BIB_NORMALS below its lower edge PCF_005's (where the body lies on its skin and the two agree), blended back to the body's own
# by there. With its own normals up to the bib, the bib's edge showed as a shading line across her upper chest in Unity.
BIB_NORMALS = T["BIB_NORMALS"]
if hasattr(nm, "calc_normals_split"):
    nm.calc_normals_split()
own = [Vector() for _ in nm.vertices]
for lp in nm.loops:
    own[lp.vertex_index] += lp.normal
own = [v.normalized() if v.length > 0 else Vector((0, 0, 1)) for v in own]
out_n = []
taken = 0
for i in range(len(P)):
    sn = skin_normal[i]
    e = on_bib_edge(P[i])[0] if sn is not None and P[i].z > 120 else BIB_NORMALS
    if sn is None or e >= BIB_NORMALS:
        out_n.append(own[i])
        continue
    n_, d = sn
    if n_.dot(own[i]) < 0:
        n_ = -n_
    if d >= 0.05 or n_.dot(own[i]) <= 0.7:
        out_n.append(own[i])
        continue
    w = 1.0 - e / BIB_NORMALS
    w = w * w * (3 - 2 * w)
    out_n.append((own[i] * (1 - w) + n_ * w).normalized())
    taken += 1
nm.use_auto_smooth = True
nm.normals_split_custom_set_from_vertices(out_n)
log(f"normals: {OUTFIT}'s skin's along the bib on {taken} vertices (within {BIB_NORMALS} cm of its edge), else the body's own")
nude.parent = a_rig
nude.matrix_parent_inverse = a_rig.matrix_world.inverted()
am2 = nude.modifiers.new("Armature", "ARMATURE")
am2.object = a_rig

# ---- 7. materials: PCF_007's built Body01 / HandFoot01
with bpy.data.libraries.load(os.path.join(ROOT, B_FROM, B_FROM + ".blend"), link=False) as (src, dst):
    dst.materials = [n for n in src.materials if base(n) in ("MI_PCF_Body01", "MI_PCF_HandFoot01")]
mats = {base(m.name): m for m in dst.materials if m}
# only the two used slots, in a fixed order (Body01 0, HandFoot01 1); the appended materials under their own names
order = ["MI_PCF_Body01", "MI_PCF_HandFoot01"]
old_index = [order.index(body_mats[p.material_index]) for p in nm.polygons]
for name in order:
    for m in list(bpy.data.materials):
        if base(m.name) == name and m is not mats[name]:
            m.name = m.name + "_import"
    mats[name].name = name
nm.materials.clear()
for name in order:
    nm.materials.append(mats[name])
for p, k in zip(nm.polygons, old_index):
    p.material_index = k
log("materials", [m.name for m in nm.materials], "faces", [old_index.count(0), old_index.count(1)])
# the face's skin tone on the body: every Fiona outfit tints her face (multiply (1, 0.905, 0.905), value x0.93), the B bodies
# (PCF_002..012) not, and the two textures are the same colour where they meet at the bib - untinted. Sampled at the seam
# (fio_seam_colour.py): face 186/148/129, this body 192/160/140 = the faint tone line at the bib; with the face's tint the
# body is 186/148/129 there too. Both materials (the wrist seam between them stays as the textures have it).
face_mat = next(s.material for o in a_objs if o.type == "MESH" for s in o.material_slots
                if s.material and base(s.material.name) == "MI_Fiona_Face01_")
f_mix = next(n for n in face_mat.node_tree.nodes if n.type == "MIX_RGB" and n.blend_type == "MULTIPLY")
f_hsv = next(n for n in face_mat.node_tree.nodes if n.type == "HUE_SAT")
for name in order:
    nt = mats[name].node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    colour = bsdf.inputs["Base Color"].links[0].from_socket
    hs = nt.nodes.new("ShaderNodeHueSaturation")
    for k in ("Hue", "Saturation", "Value", "Fac"):
        hs.inputs[k].default_value = f_hsv.inputs[k].default_value
    mix = nt.nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MULTIPLY"
    mix.inputs["Fac"].default_value = f_mix.inputs["Fac"].default_value
    mix.inputs["Color2"].default_value = f_mix.inputs["Color2"].default_value
    nt.links.new(colour, hs.inputs["Color"])
    nt.links.new(hs.outputs["Color"], mix.inputs["Color1"])
    nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
log("the face's tint on the body:", tuple(round(x, 3) for x in f_mix.inputs["Color2"].default_value[:3]), "fac", round(f_mix.inputs["Fac"].default_value, 3),
    "hsv", tuple(round(f_hsv.inputs[k].default_value, 3) for k in ("Hue", "Saturation", "Value", "Fac")))

# ---- 8. pictures: the nude body with PCF_005's face / hair / shoes, and PCF_005 dressed for comparison
for o in list(bpy.data.objects):
    if o.type == "MESH" and o not in (nude,) and o not in a_objs:
        bpy.data.objects.remove(o)
for o in a_objs:
    if o.type == "MESH":
        o.hide_render = not any(k in o.name for k in ("Face", "Hair"))
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
cd = bpy.data.cameras.new("cam")
cd.type = "ORTHO"
cam = bpy.data.objects.new("cam", cd)
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.resolution_x, scene.render.resolution_y = 800, 1100
for name, (loc, rot, scale) in {"front": ((0, -300, 88), (math.pi / 2, 0, 0), 185), "back": ((0, 300, 88), (math.pi / 2, 0, math.pi), 185),
                                "chest": ((0, -300, 125), (math.pi / 2, 0, 0), 55), "hands": ((0, -300, 105), (math.pi / 2, 0, 0), 100),
                                "neck_front": ((0, -300, 140), (math.pi / 2, 0, 0), 34), "neck_back": ((0, 300, 140), (math.pi / 2, 0, math.pi), 34),
                                "feet": ((0, -300, 10), (math.pi / 2.3, 0, 0), 40)}.items():
    cam.location, cam.rotation_euler, cd.ortho_scale = loc, rot, scale
    scene.render.filepath = os.path.join(out_dir, f"{NAME}_{name}.png")
    bpy.ops.render.render(write_still=True)
# dressed PCF_005 for comparison
for o in a_objs:
    if o.type == "MESH":
        o.hide_render = False
nude.hide_render = True
cam.location, cam.rotation_euler, cd.ortho_scale = (0, -300, 88), (math.pi / 2, 0, 0), 185
scene.render.filepath = os.path.join(out_dir, f"{NAME}_dressed_front.png")
bpy.ops.render.render(write_still=True)
nude.hide_render = False

# ---- 9. only PCF_005's rig and the nude body stay
for o in list(bpy.data.objects):
    if o not in (a_rig, nude):
        bpy.data.objects.remove(o)
for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.images, bpy.data.materials):
    for x in list(coll):
        if x.users == 0:
            coll.remove(x)
blend = os.path.join(out_dir, NAME + ".blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
shutil.copy(os.path.join(ROOT, B_FROM, "build.log"), os.path.join(out_dir, "build.log"))
log("saved", blend)
