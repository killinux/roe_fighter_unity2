"""A Stellar Blade character built from several meshes (ripper_tpose scripts/stellarblade/npc: body, face, hair each a
mesh of its own on an armature of its own, the extra armatures parented to the body's) -> one armature, for
tools/sb_fbx.py.  Runs before it in the same Blender session (the .blend is never saved):

    blender -b --factory-startup <Raven.blend> --python tools/sb_parts.py --python tools/sb_fbx.py -- <out dir> <id>

The parts share the body's skeleton (the game's skinned meshes of one character): a bone the body's armature has is
used as it is (its rest against the part's own copy is reported - 0 when they agree); a bone it lacks (the hair's own
strands) is added with the part's rest and parent; every part's mesh then deforms with the body's armature and the
parts' armatures go.  Bones are matched by name before tools/sb_fbx.py renames the Biped's (hyphens -> spaces).
"""
import bpy

main = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o.parent is None)
others = [o for o in bpy.data.objects if o.type == 'ARMATURE' and o is not main]
if not others:
    print(f"[sb_parts] {main.name}: one armature already - nothing to merge")
else:
    inv = main.matrix_world.inverted()
    bpy.ops.object.select_all(action='DESELECT')
    main.select_set(True)
    bpy.context.view_layer.objects.active = main
    bpy.ops.object.mode_set(mode='EDIT')
    eb = main.data.edit_bones
    added, worst = [], (0.0, "")
    for arm in others:
        parents = {}
        for b in arm.data.bones:                     # parents before children (bones are stored in hierarchy order)
            m = inv @ arm.matrix_world @ b.matrix_local
            if b.name in eb:
                d = (eb[b.name].head - m.translation).length
                if d > worst[0]:
                    worst = (d, f"{b.name} ({arm.name})")
                continue
            nb = eb.new(b.name)
            nb.head = inv @ arm.matrix_world @ b.head_local
            nb.tail = inv @ arm.matrix_world @ b.tail_local
            nb.matrix = m
            parents[b.name] = b.parent.name if b.parent else None
            added.append(b.name)
        for name, parent in parents.items():
            if parent and parent in eb:
                eb[name].parent = eb[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    moved = []
    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        for md in o.modifiers:
            if md.type == 'ARMATURE' and md.object in others:
                md.object = main
                moved.append(o.name)
        if o.parent in others:
            mw = o.matrix_world.copy()
            o.parent = main
            o.matrix_world = mw
    for arm in others:
        bpy.data.objects.remove(arm)
    print(f"[sb_parts] {main.name}: {len(added)} bones added from the parts ({', '.join(added[:8])}{' ...' if len(added) > 8 else ''}); "
          f"meshes now on it: {', '.join(moved)}; shared bones' rests differ by at most {worst[0]:.3f} units ({worst[1]})")
