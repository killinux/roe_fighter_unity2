# The game plays every battle action of a unit (idle, the three skills, hurt, die ...) as a
# Unity Timeline: which animation clip, which effects are switched on when, which sounds, which
# markers (hit events), which camera.  This reads those timelines straight from the bundles.
#   python timeline_dump.py suit_ines_8 [--json out.json] [--root suit_Ines_8]
# The unit bundle is gameplay_prefab_<unit>.ab; everything it refers to is loaded through the
# CAB index (_work/cab_index.json, see cab_index.py).
import json
import os
import sys

import UnityPy

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'
# the game's hot updates: newer copies of some bundles, which the game loads instead (10-17: b14's outfit1 unit there moves
# the hits of her super by 0.1 s against the one in the install)
LOCAL = os.path.expandvars(r'%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\AssetBundles')


def bundle_path(name):
    """A bundle as the game loads it: the hot-updated copy if there is one."""
    local = os.path.join(LOCAL, name)
    return local if os.path.exists(local) else os.path.join(AB, name)


class Game:
    """Bundles loaded on demand, references resolved across them."""

    def __init__(self):
        self.index = json.load(open(os.path.join(PROJECT, '_work', 'cab_index.json'), encoding='utf-8'))
        self.files = {}         # cab name (lower case) -> SerializedFile
        self.bundle_of = {}     # cab name -> bundle file name

    def load_bundle(self, name):
        env = UnityPy.load(bundle_path(name))
        for bundle in env.files.values():
            for cab, sf in getattr(bundle, 'files', {}).items():
                if hasattr(sf, 'objects'):
                    self.files[cab.lower()] = sf
                    self.bundle_of[cab.lower()] = name
        return env

    def file(self, cab):
        cab = cab.lower()
        if cab not in self.files:
            bundle = self.index.get(cab)
            if bundle is None or not os.path.exists(bundle_path(bundle)):
                self.files[cab] = None
            else:
                self.load_bundle(bundle)
        return self.files.get(cab)

    def resolve(self, sf, ptr):
        """(SerializedFile, ObjectReader) for a PPtr dict read from an object of file sf."""
        if not ptr or not ptr.get('m_PathID'):
            return None, None
        fid = ptr.get('m_FileID', 0)
        if fid:
            path = sf.externals[fid - 1].path
            cab = path.rsplit('/', 1)[-1]
            sf = self.file(cab)
            if sf is None:
                return None, None
        return sf, sf.objects.get(ptr['m_PathID'])

    def name(self, sf, ptr):
        """A readable name for whatever a PPtr points at."""
        if not ptr or not ptr.get('m_PathID'):
            return None
        tsf, obj = self.resolve(sf, ptr)
        if obj is None:
            return f"<missing {ptr.get('m_FileID')}:{ptr.get('m_PathID')}>"
        try:
            tree = obj.read_typetree()
        except Exception:
            return f'<{obj.type.name}>'
        label = tree.get('m_Name')
        if not label and 'm_GameObject' in tree:
            _, go = self.resolve(tsf, tree['m_GameObject'])
            label = go.read_typetree().get('m_Name') if go is not None else ''
        bundle = self.bundle_of.get(next((c for c, f in self.files.items() if f is tsf), ''), '')
        return f'{label} [{obj.type.name}' + (f' in {bundle}' if tsf is not sf else '') + ']'


def object_path(game, sf, go_ptr):
    """Hierarchy path of a GameObject."""
    _, go = game.resolve(sf, go_ptr)
    if go is None:
        return None
    tree = go.read_typetree()
    parts = [tree['m_Name']]
    tr = None
    for c in tree['m_Component']:
        _, comp = game.resolve(sf, c['component'])
        if comp is not None and comp.type.name in ('Transform', 'RectTransform'):
            tr = comp.read_typetree()
            break
    while tr is not None and tr['m_Father'].get('m_PathID'):
        _, father = game.resolve(sf, tr['m_Father'])
        tr = father.read_typetree()
        _, fgo = game.resolve(sf, tr['m_GameObject'])
        parts.append(fgo.read_typetree()['m_Name'])
    return '/'.join(reversed(parts))


def script_name(game, sf, tree):
    _, s = game.resolve(sf, tree.get('m_Script'))
    if s is None:
        return '?'
    d = s.read_typetree()
    return d.get('m_ClassName', '?')


def describe_timeline(game, sf, timeline_ptr, exposed, bindings, prefab_sf):
    tsf, tobj = game.resolve(sf, timeline_ptr)
    if tobj is None:
        return None
    t = tobj.read_typetree()
    result = {'name': t.get('m_Name'), 'duration_mode': t.get('m_DurationMode'), 'fixed_duration': t.get('m_FixedDuration'),
              'frame_rate': (t.get('m_EditorSettings') or {}).get('m_Framerate'), 'tracks': []}

    def track(ptr, depth=0):
        _, obj = game.resolve(tsf, ptr)
        if obj is None:
            return
        d = obj.read_typetree()
        kind = script_name(game, tsf, d)
        bound = bindings.get((id(tsf), ptr['m_PathID']))
        info = {'name': d.get('m_Name'), 'type': kind, 'muted': bool(d.get('m_Muted')), 'bound_to': bound, 'clips': [], 'markers': []}
        for c in d.get('m_Clips') or []:
            clip = {'start': c['m_Start'], 'duration': c['m_Duration'], 'clip_in': c.get('m_ClipIn', 0), 'time_scale': c.get('m_TimeScale', 1),
                    'name': c.get('m_DisplayName')}
            _, aobj = game.resolve(tsf, c.get('m_Asset'))
            if aobj is not None:
                a = aobj.read_typetree()
                akind = script_name(game, tsf, a)
                clip['asset'] = akind
                if akind == 'AnimationPlayableAsset':
                    clip['animation'] = game.name(tsf, a.get('m_Clip'))
                    clip['loop'] = a.get('m_Loop')
                elif akind == 'AudioPlayableAsset':
                    clip['audio'] = game.name(tsf, a.get('m_Clip'))
                    clip['volume'] = (a.get('m_ClipProperties') or {}).get('volume')
                elif akind == 'ControlPlayableAsset':
                    src = a.get('sourceGameObject') or {}
                    key = src.get('exposedName')
                    clip['object'] = exposed.get(key) or game.name(tsf, src.get('defaultValue'))
                    clip['prefab'] = game.name(tsf, a.get('prefabGameObject'))
                    clip['control'] = {k: a.get(k) for k in ('updateParticle', 'updateDirector', 'updateITimeControl', 'active', 'postPlayback',
                                                             'searchHierarchy')}
                else:
                    clip['data'] = {k: v for k, v in a.items() if not k.startswith('m_') or k in ('m_Name',)}
            info['clips'].append(clip)
        for m in (d.get('m_Markers') or {}).get('m_Objects') or []:
            _, mobj = game.resolve(tsf, m)
            if mobj is None:
                continue
            md = mobj.read_typetree()
            marker = {'time': md.get('m_Time'), 'type': script_name(game, tsf, md)}
            if md.get('m_Asset'):
                marker['signal'] = game.name(tsf, md.get('m_Asset'))
            for k, v in md.items():
                if k not in ('m_ObjectHideFlags', 'm_CorrespondingSourceObject', 'm_PrefabInstance', 'm_PrefabAsset', 'm_GameObject',
                             'm_Enabled', 'm_EditorHideFlags', 'm_Script', 'm_Name', 'm_EditorClassIdentifier', 'm_Parent', 'm_Time', 'm_Asset'):
                    marker[k] = v
            info['markers'].append(marker)
        info['clips'].sort(key=lambda c: c['start'])
        info['markers'].sort(key=lambda m: m['time'] or 0)
        info['depth'] = depth
        result['tracks'].append(info)
        for child in d.get('m_Children') or []:
            track(child, depth + 1)

    if t.get('m_MarkerTrack', {}).get('m_PathID'):
        track(t['m_MarkerTrack'])
    for ptr in t.get('m_Tracks') or []:
        track(ptr)
    end = 0.0
    for tr in result['tracks']:
        for c in tr['clips']:
            end = max(end, c['start'] + c['duration'])
    result['duration'] = end if not result['duration_mode'] else result['fixed_duration']
    return result


def main():
    unit = sys.argv[1]
    want_root = sys.argv[sys.argv.index('--root') + 1] if '--root' in sys.argv else None
    game = Game()
    env = game.load_bundle(f'gameplay_prefab_{unit}.ab')
    directors = []
    for obj in env.objects:
        if obj.type.name != 'PlayableDirector':
            continue
        sf = obj.assets_file
        d = obj.read_typetree()
        path = object_path(game, sf, d['m_GameObject'])
        exposed = {}
        for ref in (d.get('m_ExposedReferences') or {}).get('m_References') or []:
            key, ptr = (ref[0], ref[1]) if isinstance(ref, (list, tuple)) else (ref.get('first'), ref.get('second'))
            exposed[key] = object_path(game, sf, ptr) or game.name(sf, ptr)
        bindings = {}
        for b in d.get('m_SceneBindings') or []:
            ksf, kobj = game.resolve(sf, b['key'])
            if kobj is None:
                continue
            vsf, vobj = game.resolve(sf, b['value'])
            target = None
            if vobj is not None:
                vt = vobj.read_typetree()
                target = (object_path(game, vsf, vt['m_GameObject']) if 'm_GameObject' in vt else vt.get('m_Name')) or ''
                target += f' [{vobj.type.name}]'
            bindings[(id(ksf), b['key']['m_PathID'])] = target
        timeline = describe_timeline(game, sf, d['m_PlayableAsset'], exposed, bindings, sf)
        directors.append({'director': path, 'wrap': d.get('m_WrapMode'), 'timeline': timeline})

    directors.sort(key=lambda x: x['director'] or '')
    if '--json' in sys.argv:
        dst = sys.argv[sys.argv.index('--json') + 1]
        os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
        with open(dst, 'w', encoding='utf-8') as f:
            json.dump(directors, f, ensure_ascii=False, indent=1)
        print(f'{len(directors)} directors -> {dst}')
    for d in directors:
        if want_root and not (d['director'] or '').startswith(want_root + '/'):
            continue
        t = d['timeline']
        if t is None:
            print(f"\n== {d['director']}: timeline missing")
            continue
        print(f"\n== {d['director']}: timeline '{t['name']}', {t['duration']:.3f} s, {len(t['tracks'])} tracks")
        for tr in t['tracks']:
            if not tr['clips'] and not tr['markers']:
                continue
            print(f"  {'  ' * tr['depth']}[{tr['type']}] {tr['name']}{' (muted)' if tr['muted'] else ''}"
                  + (f"  -> {tr['bound_to']}" if tr['bound_to'] else ''))
            for c in tr['clips']:
                what = c.get('animation') or c.get('audio') or c.get('object') or c.get('prefab') or c.get('asset') or ''
                extra = ''
                if c.get('prefab') and c.get('object'):
                    extra = f"  prefab {c['prefab']}"
                if c.get('clip_in'):
                    extra += f"  clip-in {c['clip_in']:.3f}"
                if c.get('time_scale', 1) != 1:
                    extra += f"  speed {c['time_scale']:.2f}"
                if 'data' in c:
                    extra += f"  {json.dumps(c['data'], ensure_ascii=False)[:300]}"
                print(f"  {'  ' * tr['depth']}  {c['start']:7.3f} .. {c['start'] + c['duration']:7.3f}  {what}{extra}")
            for m in tr['markers']:
                rest = {k: v for k, v in m.items() if k not in ('time', 'type')}
                print(f"  {'  ' * tr['depth']}  @ {m['time']:7.3f}  {m['type']}  {json.dumps(rest, ensure_ascii=False)[:300]}")


if __name__ == '__main__':
    main()
