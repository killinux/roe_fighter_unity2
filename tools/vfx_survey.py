# What do a unit's skill effects need?  Walks the effect prefabs that the unit's timelines use
# and lists renderers, materials, shaders (with keywords), textures, meshes and the bundles
# they live in.
#   python vfx_survey.py suit_ines_8 [suit_luf_4 ...]
import json
import os
import sys

from timeline_dump import Game, AB


def walk_prefab(game, sf, go_obj, found, depth=0):
    """Collect every component under a GameObject."""
    tree = go_obj.read_typetree()
    tr = None
    for c in tree['m_Component']:
        csf, comp = game.resolve(sf, c['component'])
        if comp is None:
            continue
        if comp.type.name in ('Transform', 'RectTransform'):
            tr = comp.read_typetree()
        else:
            found.append((sf, comp, tree['m_Name']))
    if tr is not None:
        for child in tr.get('m_Children') or []:
            _, ctr = game.resolve(sf, child)
            if ctr is None:
                continue
            _, cgo = game.resolve(sf, ctr.read_typetree()['m_GameObject'])
            if cgo is not None:
                walk_prefab(game, sf, cgo, found, depth + 1)


def bundle_name(game, sf):
    for cab, f in game.files.items():
        if f is sf:
            return game.bundle_of.get(cab, '?')
    return '?'


def main():
    game = Game()
    shaders = {}
    component_types = {}
    bundles = set()
    textures = {}
    for unit in sys.argv[1:]:
        timelines = json.load(open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), '_work', 'timelines', unit + '.json'),
                                   encoding='utf-8'))
        env = game.load_bundle(f'gameplay_prefab_{unit}.ab')
        # every prefab a control clip instantiates
        prefabs = {}
        for obj in env.objects:
            if obj.type.name != 'MonoBehaviour':
                continue
            try:
                tree = obj.read_typetree()
            except Exception:
                continue
            if 'prefabGameObject' in tree and tree['prefabGameObject'].get('m_PathID'):
                psf, pobj = game.resolve(obj.assets_file, tree['prefabGameObject'])
                if pobj is not None:
                    prefabs[(id(psf), pobj.path_id)] = (psf, pobj)
        # the timelines live in the _prelude bundle; pull the control clips from there as well
        for cab, sf in list(game.files.items()):
            if sf is None:
                continue
            for obj in list(sf.objects.values()):
                if obj.type.name != 'MonoBehaviour':
                    continue
                try:
                    tree = obj.read_typetree()
                except Exception:
                    continue
                if 'prefabGameObject' in tree and tree['prefabGameObject'].get('m_PathID'):
                    psf, pobj = game.resolve(sf, tree['prefabGameObject'])
                    if pobj is not None:
                        prefabs[(id(psf), pobj.path_id)] = (psf, pobj)
        print(f'\n===== {unit}: {len(prefabs)} effect prefabs')
        for psf, pobj in prefabs.values():
            found = []
            walk_prefab(game, psf, pobj, found)
            name = pobj.read_typetree()['m_Name']
            kinds = {}
            for sf, comp, go_name in found:
                kinds[comp.type.name] = kinds.get(comp.type.name, 0) + 1
                component_types[comp.type.name] = component_types.get(comp.type.name, 0) + 1
                if comp.type.name == 'MonoBehaviour':
                    t = comp.read_typetree()
                    _, s = game.resolve(sf, t.get('m_Script'))
                    sname = s.read_typetree().get('m_ClassName') if s is not None else '?'
                    component_types['script:' + sname] = component_types.get('script:' + sname, 0) + 1
                if not comp.type.name.endswith('Renderer'):
                    continue
                t = comp.read_typetree()
                for m in t.get('m_Materials') or []:
                    msf, mobj = game.resolve(sf, m)
                    if mobj is None:
                        continue
                    mt = mobj.read_typetree()
                    ssf, sobj = game.resolve(msf, mt.get('m_Shader'))
                    sname = '?'
                    if sobj is not None:
                        st = sobj.read_typetree()
                        sname = (st.get('m_ParsedForm') or {}).get('m_Name') or st.get('m_Name') or '?'
                        bundles.add(bundle_name(game, ssf))
                    kw = mt.get('m_ValidKeywords') or mt.get('m_ShaderKeywords') or []
                    if isinstance(kw, str):
                        kw = kw.split()
                    entry = shaders.setdefault(sname, {'materials': set(), 'keywords': {}, 'renderers': {}, 'props': {}})
                    entry['materials'].add(mt.get('m_Name'))
                    entry['renderers'][comp.type.name] = entry['renderers'].get(comp.type.name, 0) + 1
                    for k in kw:
                        entry['keywords'][k] = entry['keywords'].get(k, 0) + 1
                    bundles.add(bundle_name(game, msf))
                    saved = mt.get('m_SavedProperties') or {}
                    for pair in saved.get('m_TexEnvs') or []:
                        key, val = (pair[0], pair[1]) if isinstance(pair, (list, tuple)) else (pair.get('first'), pair.get('second'))
                        tsf, tobj = game.resolve(msf, val.get('m_Texture'))
                        if tobj is not None:
                            entry['props'][key] = entry['props'].get(key, 0) + 1
                            textures[(id(tsf), tobj.path_id)] = bundle_name(game, tsf)
                            bundles.add(bundle_name(game, tsf))
                mesh = t.get('m_Mesh')
                if mesh and mesh.get('m_PathID'):
                    xsf, xobj = game.resolve(sf, mesh)
                    if xobj is not None:
                        bundles.add(bundle_name(game, xsf))
            print(f"  {name}: {kinds}")
    print('\n===== component types')
    for k, v in sorted(component_types.items(), key=lambda kv: -kv[1]):
        print(f'  {v:5} {k}')
    print('\n===== shaders')
    for name, e in sorted(shaders.items(), key=lambda kv: -len(kv[1]['materials'])):
        print(f"  {name}: {len(e['materials'])} materials, renderers {e['renderers']}")
        print(f"      keywords {dict(sorted(e['keywords'].items(), key=lambda kv: -kv[1]))}")
        print(f"      textures {e['props']}")
    print(f'\n===== {len(textures)} textures, {len(bundles)} bundles')
    for b in sorted(bundles):
        size = os.path.getsize(os.path.join(AB, b)) if os.path.exists(os.path.join(AB, b)) else 0
        print(f'  {size / 1e3:9.0f} KB  {b}')


if __name__ == '__main__':
    main()
