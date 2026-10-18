# One "skill sheet" per unit: everything the game knows about how a battle action is shown.
#   python skill_sheet.py suit_ines_8 a08 [suit_luf_4 g04 ...]
# Reads gameplay_prefab_<unit>.ab (and what it refers to) and writes
#   Assets/ROE/<id>/skill_sheet.json
# For every action (idle, skill1..3, hurt, die ...):
#   animation  clip name, start, end on the action's timeline
#   hits       when damage numbers appear and how the damage is split  (CharacterSkillView)
#   moves      when the unit slides to its attack spot and back         (SkillTransformSettings)
#   turns      when it turns to its target / back                       (SkillRotationSettings)
#   shakes     camera shakes
#   cameras    the game's own skill camera: which rig node it rides on, lens, aim
#   effects    effect prefab, start, end, speed, and the anchor it is parented to
#              (path below the unit root, local position / rotation / scale of every node on the way)
#   spawns     effects the game spawns at a target (hit effects)
#   sounds     sound clip + start; voices = the names of the voice lines the game picks from
import json
import math
import os
import sys

from timeline_dump import Game, describe_timeline, object_path, script_name

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP = ('m_ObjectHideFlags', 'm_CorrespondingSourceObject', 'm_PrefabInstance', 'm_PrefabAsset', 'm_GameObject', 'm_Enabled',
        'm_EditorHideFlags', 'm_Script', 'm_Name', 'm_EditorClassIdentifier')


class Prefab:
    """The GameObject hierarchy of one serialized file."""

    def __init__(self, game, sf):
        self.game, self.sf = game, sf
        self.go = {}
        self.tr = {}
        self.tr_of_go = {}
        self.components = {}
        for pid, obj in sf.objects.items():
            if obj.type.name == 'GameObject':
                self.go[pid] = obj.read_typetree()
            elif obj.type.name in ('Transform', 'RectTransform'):
                t = obj.read_typetree()
                self.tr[pid] = t
                self.tr_of_go[t['m_GameObject']['m_PathID']] = pid
        for pid, g in self.go.items():
            self.components[pid] = [c['component']['m_PathID'] for c in g['m_Component']]

    def roots(self):
        return [pid for pid, t in self.tr.items() if not t['m_Father'].get('m_PathID')]

    def name_of_tr(self, tr_id):
        return self.go[self.tr[tr_id]['m_GameObject']['m_PathID']]['m_Name']

    def chain(self, tr_id):
        """Transforms from the root down to tr_id."""
        chain = []
        while tr_id:
            chain.append(tr_id)
            tr_id = self.tr[tr_id]['m_Father'].get('m_PathID')
        return list(reversed(chain))

    def path(self, tr_id, below=1):
        return '/'.join(self.name_of_tr(t) for t in self.chain(tr_id)[below:])

    def local(self, tr_id):
        t = self.tr[tr_id]
        p, r, s = t['m_LocalPosition'], t['m_LocalRotation'], t['m_LocalScale']
        return {'name': self.name_of_tr(tr_id), 'position': [p['x'], p['y'], p['z']], 'rotation': [r['x'], r['y'], r['z'], r['w']],
                'scale': [s['x'], s['y'], s['z']]}

    def node(self, tr_id):
        """How to find / rebuild this node below the unit root: every node on the way with its local transform."""
        chain = self.chain(tr_id)[1:]
        return {'path': '/'.join(self.name_of_tr(t) for t in chain), 'nodes': [self.local(t) for t in chain]}

    def tr_of_component(self, comp_id):
        obj = self.sf.objects.get(comp_id)
        if obj is None:
            return None
        if obj.type.name in ('Transform', 'RectTransform'):
            return comp_id
        t = obj.read_typetree()
        return self.tr_of_go.get(t.get('m_GameObject', {}).get('m_PathID'))

    def scripts_on(self, go_id):
        out = []
        for cid in self.components.get(go_id, []):
            obj = self.sf.objects.get(cid)
            if obj is not None and obj.type.name == 'MonoBehaviour':
                t = obj.read_typetree()
                out.append((script_name(self.game, self.sf, t), t, cid))
        return out


def describe_query(prefab, ptr):
    """A position / rotation query of the game, in plain words."""
    if not ptr or not ptr.get('m_PathID'):
        return None
    obj = prefab.sf.objects.get(ptr['m_PathID'])
    if obj is None:
        return None
    t = obj.read_typetree()
    kind = script_name(prefab.game, prefab.sf, t)
    go_name = prefab.go[t['m_GameObject']['m_PathID']]['m_Name']
    out = {'query': kind, 'node': go_name}
    for k, v in t.items():
        if k in SKIP:
            continue
        if isinstance(v, dict) and set(v) == {'m_FileID', 'm_PathID'}:
            if not v.get('m_PathID'):
                out[k.lstrip('_')] = None
                continue
            target = prefab.sf.objects.get(v['m_PathID']) if not v.get('m_FileID') else None
            if target is None:
                out[k.lstrip('_')] = '<external>'
            elif target.type.name in ('Transform', 'RectTransform'):
                out[k.lstrip('_')] = prefab.path(v['m_PathID'])
            elif target.type.name == 'MonoBehaviour':
                out[k.lstrip('_')] = describe_query(prefab, v)
            else:
                out[k.lstrip('_')] = target.type.name
        else:
            out[k.lstrip('_')] = v
    return out


def describe_camera(prefab, ptr):
    obj = prefab.sf.objects.get(ptr.get('m_PathID')) if ptr else None
    if obj is None:
        return None
    t = obj.read_typetree()
    go_id = t['m_GameObject']['m_PathID']
    tr_id = prefab.tr_of_go[go_id]

    def target(p):
        if not p or not p.get('m_PathID') or p.get('m_FileID'):
            return None
        return prefab.node(p['m_PathID'])

    out = {'node': prefab.node(tr_id), 'lens': t.get('m_Lens'), 'follow': target(t.get('m_Follow')), 'look_at': target(t.get('m_LookAt')),
           'pipeline': []}
    # the body / aim components sit on a hidden child ("cm")
    for child in prefab.tr[tr_id].get('m_Children') or []:
        cgo = prefab.tr[child['m_PathID']]['m_GameObject']['m_PathID']
        for kind, data, _ in prefab.scripts_on(cgo):
            if kind == 'CinemachinePipeline':
                continue
            out['pipeline'].append({'type': kind, **{k: v for k, v in data.items() if k not in SKIP and not isinstance(v, dict)
                                                     or (isinstance(v, dict) and set(v) <= {'x', 'y', 'z', 'w'})}})
    return out


def position_kind(q):
    """The game's position queries, reduced to what a two-fighter game needs."""
    if not q:
        return {'kind': 'none'}
    k = q.get('query')
    if k == 'SkillTargetRuntimePositionQuery':        # in front of the target, attack radius + the target's receive radius away
        return {'kind': 'attack', 'radius': q.get('attackRadius') or 0.0}
    if k == 'BasicPositionQuery':                     # a node of the unit itself: its root (= where it stood) or its moving pivot
        return {'kind': 'return' if not q.get('targetPos') else 'self'}
    if k == 'BattlefieldAnchorPositionQuery':         # a formation node of the stage: SelfAll, OppositeAll, PlayerAll ...
        return {'kind': 'anchor', 'name': q.get('anchorName') or ''}
    if k == 'SkillTargetAnchorPositionQuery':         # a named anchor on the target: hurt, center, mark, status
        return {'kind': 'target', 'name': (q.get('info') or {}).get('AnchorName') or ''}
    return {'kind': k or 'none'}


def rotation_kind(q):
    if not q:
        return {'kind': 'none'}
    if q.get('query') == 'TargetRotationQuery':       # look from one position towards another
        end = position_kind(q.get('endPositionQuery'))
        return {'kind': 'towards', 'to': end['kind'], 'name': end.get('name', '')}
    if q.get('query') == 'BasicRotationQuery':        # back to the rotation of the unit's root
        return {'kind': 'origin'}
    return {'kind': q.get('query') or 'none'}


def to_unity(sheet):
    """The same sheet shaped for Unity's JsonUtility: lists, fixed fields, no dictionaries."""
    out = {'unit': sheet['unit'], 'id': sheet['id'], 'actions': [],
           'receiveRadius': next((r['radius'] for r in sheet.get('receive') or [] if r['key'] == 'enemyAttackPos'), 1.0),
           'model': (sheet.get('model') or {}).get('path', ''),
           'anchors': [{'name': k, 'node': node_unity(v)} for k, v in (sheet.get('anchors') or {}).items()]}
    for name, a in sheet['actions'].items():
        numbers = [{'time': n['time'], 'ratio': n['ratio'], 'what': h['what']} for h in a.get('hits') or [] for n in h['numbers']]
        if not numbers:     # no damage numbers listed: the moment the game shows the effect on the target
            numbers = [{'time': h['time'], 'ratio': 1.0, 'what': h['what']} for h in a.get('hits') or []]
        out['actions'].append({
            'name': name, 'timeline': a.get('timeline') or '', 'duration': a.get('duration') or 0.0, 'loop': bool(a.get('loop')),
            'clips': [{'clip': c['clip'], 'start': c['start'], 'end': c['end'], 'clipIn': c['clip_in'], 'speed': c['speed'],
                       'muted': bool(c.get('muted')), 'target': c.get('target') or ''} for c in a.get('animation') or []],
            'hits': sorted(numbers, key=lambda n: n['time']),
            'moves': [{'start': m['start'], 'end': m['end'], **position_kind(m['to'])} for m in a.get('moves') or []],
            'turns': [{'start': m['start'], 'end': m['end'], **rotation_kind(m['to'])} for m in a.get('turns') or []],
            'shakes': a.get('shakes') or [],
            'effects': [{'start': e['start'], 'end': e['end'], 'clipIn': e['clip_in'], 'speed': e['speed'], 'muted': bool(e.get('muted')),
                         'prefab': e.get('prefab') or '', 'bundle': e.get('bundle') or '', 'track': e.get('track') or '',
                         'anchor': node_unity(e.get('anchor'))} for e in a.get('effects') or []],
            'spawns': [{'time': s['time'], 'prefab': s['prefab'], 'bundle': s.get('bundle') or '', **position_kind(s['at'])}
                       for s in a.get('spawns') or []],
            'sounds': [{'start': s['start'], 'clip': s['clip'], 'volume': s.get('volume') if s.get('volume') is not None else 1.0,
                        'muted': bool(s.get('muted'))} for s in a.get('sounds') or []],
            'voices': [{'start': v['start'], 'name': v.get('name') or '', 'muted': bool(v.get('muted')),
                        'clips': [{'language': lang, 'clip': clip} for lang, clip in ((a.get('voice_lines') or {}).get(v.get('name')) or {}).items()]}
                       for v in a.get('voices') or []],
            'cameras': [{'start': c['start'], 'end': c['end'], 'fov': ((c.get('camera') or {}).get('lens') or {}).get('FieldOfView') or 0.0,
                         'node': node_unity((c.get('camera') or {}).get('node')), 'follow': node_unity((c.get('camera') or {}).get('follow')),
                         'lookAt': node_unity((c.get('camera') or {}).get('look_at'))} for c in a.get('cameras') or []],
        })
    return out


def node_unity(node):
    if not node:
        return {'path': '', 'nodes': []}
    return {'path': node['path'], 'nodes': [{'name': n['name'], 'position': dict(zip('xyz', n['position'])),
                                             'rotation': dict(zip('xyzw', n['rotation'])), 'scale': dict(zip('xyz', n['scale']))}
                                            for n in node['nodes']]}


def main():
    args = sys.argv[1:]
    game = Game()
    for unit, cid in zip(args[0::2], args[1::2]):
        env = game.load_bundle(f'gameplay_prefab_{unit}.ab')
        sf = next(o.assets_file for o in env.objects)
        prefab = Prefab(game, sf)
        # the unit's own root: named like the unit (the bundle also holds the enemy version and a leftover "_TMP")
        root_tr = None
        for r in prefab.roots():
            if prefab.name_of_tr(r).lower() == unit.lower():
                root_tr = r
        if root_tr is None:
            print(f'{unit}: no root called like the unit; roots are {[prefab.name_of_tr(r) for r in prefab.roots()]}')
            continue
        root_go = prefab.tr[root_tr]['m_GameObject']['m_PathID']
        under_root = {t for t in prefab.tr if prefab.chain(t)[0] == root_tr}

        # directors of this root, by path id
        directors = {}
        for pid, obj in sf.objects.items():
            if obj.type.name != 'PlayableDirector':
                continue
            d = obj.read_typetree()
            tr_id = prefab.tr_of_go.get(d['m_GameObject']['m_PathID'])
            if tr_id not in under_root:
                continue
            exposed = {}
            for ref in (d.get('m_ExposedReferences') or {}).get('m_References') or []:
                key, ptr = (ref[0], ref[1]) if isinstance(ref, (list, tuple)) else (ref.get('first'), ref.get('second'))
                exposed[key] = ptr
            bindings = {}
            for b in d.get('m_SceneBindings') or []:
                ksf, kobj = game.resolve(sf, b['key'])
                if kobj is None:
                    continue
                vt_id = prefab.tr_of_component(b['value'].get('m_PathID')) if not b['value'].get('m_FileID') else None
                bindings[(id(ksf), b['key']['m_PathID'])] = prefab.path(vt_id) if vt_id else None
            # exposed names -> anchor node (the timeline dump wants names; keep the transform ids on the side)
            exposed_nodes = {}
            exposed_names = {}
            for key, ptr in exposed.items():
                if ptr.get('m_FileID') or not ptr.get('m_PathID'):
                    continue
                tgt = sf.objects.get(ptr['m_PathID'])
                if tgt is None:
                    continue
                tid = prefab.tr_of_go.get(ptr['m_PathID']) if tgt.type.name == 'GameObject' else prefab.tr_of_component(ptr['m_PathID'])
                if tid:
                    exposed_nodes[key] = tid
                    exposed_names[key] = 'node:' + key
            timeline = describe_timeline(game, sf, d['m_PlayableAsset'], exposed_names, bindings, sf)
            voices = {}
            for kind, data, _ in prefab.scripts_on(d['m_GameObject']['m_PathID']):
                if kind == 'SerializedLocalizePlayable':
                    for m in data.get('_audioClipMappings') or []:
                        langs = {}
                        for lm in m.get('_localizationMappings') or []:
                            langs[str(lm.get('_voiceLocalizationLanguage'))] = (game.name(sf, lm.get('_audioClip')) or '').split(' [')[0]
                        voices[m.get('_playableClipName')] = langs
            directors[pid] = {'name': prefab.name_of_tr(tr_id), 'timeline': timeline, 'exposed_nodes': exposed_nodes, 'voices': voices,
                              'wrap': d.get('m_WrapMode')}

        def action_from_director(info):
            t = info['timeline']
            action = {'timeline': t['name'] if t else None, 'duration': t['duration'] if t else 0.0, 'loop': info['wrap'] == 1,
                      'animation': [], 'effects': [], 'sounds': [], 'voices': [], 'markers': []}
            if not t:
                return action
            for tr in t['tracks']:
                for c in tr['clips']:
                    base = {'start': c['start'], 'end': c['start'] + c['duration'], 'clip_in': c.get('clip_in', 0), 'speed': c.get('time_scale', 1)}
                    if tr['muted']:
                        base['muted'] = True
                    if c.get('asset') == 'AnimationPlayableAsset':
                        action['animation'].append({**base, 'clip': (c.get('animation') or '').split(' [')[0], 'target': tr.get('bound_to')})
                    elif c.get('asset') == 'AudioPlayableAsset':
                        if c.get('audio'):
                            action['sounds'].append({**base, 'clip': c['audio'].split(' [')[0], 'volume': c.get('volume'), 'track': tr['name']})
                        else:
                            action['voices'].append({**base, 'name': c.get('name'), 'track': tr['name']})
                    elif c.get('asset') == 'ControlPlayableAsset':
                        key = (c.get('object') or '')
                        node = None
                        if key.startswith('node:') and key[5:] in info['exposed_nodes']:
                            node = prefab.node(info['exposed_nodes'][key[5:]])
                        prefab_name = c.get('prefab')
                        action['effects'].append({**base, 'prefab': prefab_name.split(' [')[0] if prefab_name else None,
                                                  'bundle': prefab_name.split(' in ')[-1].rstrip(']') if prefab_name and ' in ' in prefab_name else None,
                                                  'anchor': node, 'control': c.get('control'), 'track': tr['name']})
                    else:
                        action.setdefault('other', []).append({**base, 'type': c.get('asset'), 'track': tr['name'], 'data': c.get('data')})
                for m in tr['markers']:
                    action['markers'].append(m)
            action['voice_lines'] = info['voices']
            for key in ('animation', 'effects', 'sounds', 'voices'):
                action[key].sort(key=lambda x: x['start'])
            return action

        sheet = {'unit': prefab.name_of_tr(root_tr), 'id': cid, 'actions': {}, 'anchors': {}, 'model': None}
        # the model the game animates, and the unit's named anchors (hurt / center / status / mark)
        for kind, data, _ in prefab.scripts_on(root_go):
            if kind == 'CharacterModelAnchorInfo':
                for a in data.get('_anchorSettings') or []:
                    if a['Transform'].get('m_PathID'):
                        sheet['anchors'][a['Name']] = prefab.node(a['Transform']['m_PathID'])
            if kind == 'CharacterPerformingInfo':
                sheet['receive'] = [{'key': s.get('Key'), 'radius': (s.get('ReceivePosInfo') or {}).get('_receiveRadius')}
                                    for s in data.get('_settings') or []]
        for pid, obj in sf.objects.items():
            if obj.type.name == 'Animator':
                tid = prefab.tr_of_component(pid)
                if tid in under_root:
                    sheet['model'] = prefab.node(tid)

        by_director = {pid: action_from_director(info) for pid, info in directors.items()}
        for pid, info in directors.items():
            sheet['actions'][info['name'].lower()] = by_director[pid]

        # what CharacterSkillView adds per skill
        for kind, data, _ in prefab.scripts_on(root_go):
            if kind != 'CharacterSkillView':
                continue
            for s in data.get('_skillViewInfoSettings') or []:
                name = s['SkillName']
                director = s['PlayableDirector'].get('m_PathID')
                base = by_director.get(director)
                action = dict(base) if base else {'timeline': None}
                action['skill'] = name
                action['director'] = directors[director]['name'] if director in directors else None
                hits = []
                for v in s.get('ViewInfoTimeSettings') or []:
                    entry = {'what': v['ViewInfoName'], 'time': v['Time'],
                             'numbers': [{'time': h['Time'], 'ratio': h['Ratio'], 'style': h.get('CustomStyle')} for h in v.get('HealthInfoTimeSettings') or []]}
                    hits.append(entry)
                action['hits'] = hits
                action['moves'] = [{'start': m['StartTime'], 'end': m['EndTime'], 'to': describe_query(prefab, m['PositionQuery'])}
                                   for m in s.get('SkillTransformSettings') or []]
                action['turns'] = [{'start': m['StartTime'], 'end': m['EndTime'], 'to': describe_query(prefab, m['RotationQuery'])}
                                   for m in s.get('SkillRotationSettings') or []]
                action['spawns'] = []
                for f in s.get('SkillFxSettings') or []:
                    label = game.name(sf, f.get('FxPrefab')) or ''
                    action['spawns'].append({'time': f['Time'], 'prefab': label.split(' [')[0],
                                             'bundle': label.split(' in ')[-1].rstrip(']') if ' in ' in label else None,
                                             'at': describe_query(prefab, f.get('PositionQuery'))})
                action['fx_moves'] = [{k: (describe_query(prefab, v) if isinstance(v, dict) and 'm_PathID' in v else v) for k, v in f.items()}
                                      for f in s.get('SkillFxTransformationSettings') or []]
                action['shakes'] = [{'time': c['StartTime'], **{k.lower(): v for k, v in c['Param'].items()}} for c in s.get('CameraShakeSettings') or []]
                action['cameras'] = [{'start': c['StartTime'], 'end': c['EndTime'], 'blend_in': c['BlendDefinition'].get('m_Time'),
                                      'camera': describe_camera(prefab, c['VirtualCamera'])} for c in s.get('VirtualCameraSettings') or []]
                action['camera_return_blend'] = (s.get('ReturnCameraBlendDefinition') or {}).get('m_Time')
                action['custom_duration'] = s.get('CustomDuration')
                sheet['actions'][name.lower()] = action

        dst = os.path.join(PROJECT, 'Assets', 'ROE', cid, 'skill_sheet.json')
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, 'w', encoding='utf-8') as f:
            json.dump(sheet, f, ensure_ascii=False, indent=1)
        with open(dst.replace('.json', '_unity.json'), 'w', encoding='utf-8') as f:
            json.dump(to_unity(sheet), f, ensure_ascii=False, indent=1)
        print(f"{unit} -> {dst}: {len(sheet['actions'])} actions")
        for name in ('skill1', 'skill2', 'skill3'):
            a = sheet['actions'].get(name)
            if not a:
                continue
            numbers = [f"{n['time']:.2f} x{n['ratio']:.1f}" for h in a['hits'] for n in h['numbers']] or [f"{h['time']:.2f} ({h['what']})" for h in a['hits']]
            print(f"  {name}: '{a['timeline']}' {a['duration']:.2f} s, clip {[x['clip'] for x in a['animation']]}, hits {numbers}, "
                  f"{len([e for e in a['effects'] if not e.get('muted')])} effects (+{len([e for e in a['effects'] if e.get('muted')])} muted), "
                  f"{len(a['spawns'])} spawned at targets, {len(a['sounds'])} sounds, {len(a['voices'])} voice slots, "
                  f"moves {[(round(m['start'], 2), (m['to'] or {}).get('node')) for m in a['moves']]}, "
                  f"shakes {[round(x['time'], 2) for x in a['shakes']]}, cameras {[(c['start'], c['end']) for c in a['cameras']]}")


if __name__ == '__main__':
    main()
