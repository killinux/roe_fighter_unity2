# Rise of Eros' own Magica Cloth 2 set-up on one of its prefabs, read from an AssetRipper export: every MagicaCloth
# component (cloth type, root bones by name, connection, the parameters as the game set them) and every Magica
# collider (the bone it hangs on, its place there, shape, size) - "the game's own physics" for a fighter (user 10-11
# "就完全遵从ROE本身的物理的角度再看看").  ROE has them only on its lobby prefabs (meta_armor_pc_<id>_hd): Luf's is one
# BoneCloth on her six hair chains; its battle models carry none (docs/roe-battle-physics.md).
#   python tools\roe_mc2_dump.py <prefab> [--out dump.json] [--fighter g04]
#     --fighter writes Assets\ROE\<id>\magica_lobby.json for the fight's "roe" cloth setup (RoeMagicaCloth.GameSetup):
#     each cloth's parameters as Magica Cloth 2's own preset JSON (the plugin's MC2_Preset_Skirt.json gives the field
#     types: the game's YAML has 0/1 where the JSON wants false/true), its roots and colliders by name.
#   python tools\roe_mc2_dump.py "Assets\ROE\g04\meta_armor_pc_g04_hd_ld\pc_g04_hd.prefab" --fighter g04
import argparse
import json
import os
import re

import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
TEMPLATE = os.path.join(PROJECT, 'Assets', 'MagicaCloth2', 'Res', 'Preset', 'MC2_Preset_Skirt.json')
# the class of a Magica script, by the m_Script guid: the export's Scripts folder (AssetRipper), else the field names
SIGNS = {'MagicaCloth': 'serializeData', 'MagicaCapsuleCollider': 'radiusSeparation',
         'MagicaSphereCollider': None, 'MagicaPlaneCollider': None}


def script_names(assets):
    """guid of every MonoScript in an export (Scripts/<assembly>/.../<Class>.cs.meta) -> class name."""
    names = {}
    for root, _, files in os.walk(os.path.join(assets, 'Scripts')):
        for f in files:
            if f.endswith('.cs.meta'):
                text = open(os.path.join(root, f), encoding='utf-8', errors='replace').read()
                m = re.search(r'guid: ([0-9a-f]{32})', text)
                if m:
                    names[m.group(1)] = f[:-len('.cs.meta')]
    return names


def documents(path):
    """{fileID: (type id, body dict)} of a Unity YAML file."""
    text = open(path, encoding='utf-8', errors='replace').read()
    docs = {}
    for part in re.split(r'^--- ', text, flags=re.M)[1:]:
        head, _, body = part.partition('\n')
        m = re.match(r'!u!(\d+) &(-?\d+)', head)
        if not m:
            continue
        data = yaml.safe_load(body)
        if isinstance(data, dict) and len(data) == 1:
            docs[int(m.group(2))] = (int(m.group(1)), next(iter(data.values())))
    return docs


def classify(body, scripts):
    """The Magica class of a MonoBehaviour: by its script's guid, else by its fields (an imported prefab whose script
    the project does not have)."""
    cls = scripts.get(body.get('m_Script', {}).get('guid'))
    if cls:
        return cls
    if 'serializeData' in body:
        return 'MagicaCloth'
    if 'radiusSeparation' in body and 'reverseDirection' in body:
        return 'MagicaCapsuleCollider'
    if 'size' in body and 'center' in body and 'symmetryMode' in body:
        return 'MagicaSphereCollider'
    return '?'


def as_template(value, template):
    """The game's value in the type the preset JSON has for that field (bools, refs, lists of refs)."""
    if isinstance(template, dict):
        if set(template) == {'instanceID'}:
            return {'instanceID': 0}
        if not isinstance(value, dict):
            return template
        # a field the game's version did not have is left out: the component keeps its own default
        return {k: as_template(value[k], t) for k, t in template.items() if k in value}
    if isinstance(template, bool):
        return bool(value)
    if isinstance(template, list):
        if template and isinstance(template[0], dict) and value and isinstance(value, list):
            return [as_template(v, template[0]) for v in value]
        return [] if any(isinstance(v, dict) and 'fileID' in v for v in (value or [])) else (value if isinstance(value, list) else template)
    if isinstance(template, str):
        return str(value)
    if isinstance(template, float) and isinstance(value, (int, float)):
        return float(value)
    return value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('prefab')
    ap.add_argument('--out')
    ap.add_argument('--fighter')
    a = ap.parse_args()
    path = os.path.normpath(os.path.abspath(a.prefab))
    marker = os.sep + 'AssetBundles'
    assets = path[:path.index(marker)] if marker in path else os.path.dirname(path)
    scripts = script_names(assets)
    docs = documents(path)
    go_name = {fid: body.get('m_Name') for fid, (t, body) in docs.items() if t == 1}
    tf = {fid: body for fid, (t, body) in docs.items() if t == 4}
    tf_of_go = {b['m_GameObject']['fileID']: fid for fid, b in tf.items()}

    def name_of(fid):
        b = tf.get(fid)
        return go_name.get(b['m_GameObject']['fileID']) if b else None

    def path_of(fid):
        names = []
        while fid in tf:
            names.append(name_of(fid))
            fid = tf[fid]['m_Father']['fileID']
        return '/'.join(reversed(names))

    def v3(d):
        return [d['x'], d['y'], d['z']]

    cloths, colliders = [], {}
    for fid, (t, body) in docs.items():
        if t != 114:
            continue
        cls = classify(body, scripts)
        go = body.get('m_GameObject', {}).get('fileID')
        tfid = tf_of_go.get(go)
        if cls == 'MagicaCloth':
            sd = body['serializeData']
            cloths.append({'object': path_of(tfid), 'enabled': body.get('m_Enabled'),
                           'roots': [name_of(r['fileID']) for r in sd.get('rootBones', [])],
                           'colliderIds': [c['fileID'] for c in sd.get('colliderCollisionConstraint', {}).get('colliderList', [])],
                           'serializeData': sd})
        elif cls.startswith('Magica') and cls.endswith('Collider'):
            t4 = tf[tfid]
            r = t4['m_LocalRotation']
            colliders[fid] = {
                'name': name_of(tfid), 'kind': cls.replace('Magica', '').replace('Collider', '').lower(),
                'bone': name_of(t4['m_Father']['fileID']),
                'localPosition': v3(t4['m_LocalPosition']), 'localRotation': [r['x'], r['y'], r['z'], r['w']],
                'localScale': v3(t4['m_LocalScale']),
                'size': v3(body.get('size', {'x': 0, 'y': 0, 'z': 0})), 'center': v3(body.get('center', {'x': 0, 'y': 0, 'z': 0})),
                'direction': body.get('direction', 0), 'radiusSeparation': bool(body.get('radiusSeparation', 0)),
                'alignedOnCenter': bool(body.get('alignedOnCenter', 1)), 'reverseDirection': bool(body.get('reverseDirection', 0)),
                'symmetryMode': body.get('symmetryMode', 0),
            }
    order = list(colliders)
    for c in cloths:
        c['colliders'] = [order.index(f) for f in c.pop('colliderIds') if f in colliders]

    for c in cloths:
        sd = c['serializeData']
        restore, limit, inertia = sd['angleRestorationConstraint'], sd['angleLimitConstraint'], sd['inertiaConstraint']
        curve = limit['limitAngle']['curve']['m_Curve']
        print(f"MagicaCloth {c['object']} (enabled {c['enabled']}): type {sd['clothType']} connection {sd['connectionMode']} roots {c['roots']}")
        print(f"  gravity {sd['gravity']} damping {sd['damping']['value']} radius {sd['radius']['value']} blendWeight {sd['blendWeight']} "
              f"poseRatio {sd['animationPoseRatio']} restore {restore['stiffness']['value']} (use {restore['useAngleRestoration']}) "
              f"limit {limit['limitAngle']['value']} x curve {[(k['time'], k['value']) for k in curve] if limit['limitAngle']['useCurve'] else 'off'} "
              f"inertia world {inertia['worldInertia']} local {inertia['localInertia']} particle limit {inertia['particleSpeedLimit']} "
              f"tether {sd['tetherConstraint']['distanceCompression']} collision mode {sd['colliderCollisionConstraint']['mode']} colliders {c['colliders']}")
    for i, f in enumerate(order):
        c = colliders[f]
        print(f"  collider {i}: {c['kind']} {c['name']} on {c['bone']} at {c['localPosition']} size {c['size']} dir {c['direction']} "
              f"separate {c['radiusSeparation']} centred {c['alignedOnCenter']} reversed {c['reverseDirection']}")

    result = {'prefab': os.path.basename(path), 'cloths': cloths, 'colliders': [colliders[f] for f in order]}
    if a.out:
        json.dump(result, open(a.out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1, default=str)
        print('->', a.out)
    if a.fighter:
        template = json.load(open(TEMPLATE, encoding='utf-8'))
        setup = {'source': os.path.basename(path), 'cloths': [], 'colliders': result['colliders']}
        for c in cloths:
            setup['cloths'].append({'roots': c['roots'], 'colliders': c['colliders'], 'enabled': bool(c['enabled']),
                                    'json': json.dumps(as_template(c['serializeData'], template), separators=(',', ':'))})
        out = os.path.join(PROJECT, 'Assets', 'ROE', a.fighter, 'magica_lobby.json')
        json.dump(setup, open(out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        print('->', out)


if __name__ == '__main__':
    main()
