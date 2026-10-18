# Which of the game's battle stages has room for a 3D fight?  Reads the scene bundles directly
# (UnityPy), puts every visible mesh into a top-down grid around the battle centre and measures
# the largest circle of flat, free floor:
#   floor    a near-horizontal triangle within 0.2 m of the battle centre's height
#   blocked  any triangle between 0.25 and 2.2 m above it (walls, props, steps, railings)
# Results: _work/stage_survey/survey.json (one entry per scene) and a map per scene
# (<scene>.png: grey free floor, red blocked, black no floor; blue = battle centre,
# green circle = the largest free circle near it, yellow = the game's own camera).
#
#   python tools/stage_survey.py [name filters ...] [--all-variants] [--limit-minutes 20]
# Resumable: scenes already in survey.json are skipped.  Day/dusk/night variants of one stage
# share the geometry, so only the first is measured unless --all-variants.
import argparse
import io
import json
import math
import os
import re
import sys
import time

import numpy as np
import UnityPy
from PIL import Image, ImageDraw
from UnityPy.helpers.MeshHelper import MeshHandler

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'
OUT = os.path.join(PROJECT, '_work', 'stage_survey')

CELL = 0.25          # grid cell, metres
EXTENT = 20.0        # grid half size around the battle centre
BAND = (0.25, 2.2)   # heights above the floor that block a fighter
FLOOR_TOL = 0.2      # floor = within this of the centre's height
SAMPLE = 0.15        # sample spacing on triangles
SEARCH = 4.0         # look for the best circle centre this far from the battle centre

# Unity's built-in meshes ("unity default resources"), by path id
BUILTIN = {10202: 'Cube', 10206: 'Cylinder', 10207: 'Sphere', 10208: 'Capsule', 10209: 'Plane', 10210: 'Quad'}
VARIANT_RE = re.compile(r'_(day|dawn|dusk|night|rain)$')


def builtin_mesh(kind):
    """Rough stand-ins for Unity's built-in meshes: vertices and triangles."""
    if kind == 'Plane':
        v = np.array([[-5, 0, -5], [5, 0, -5], [5, 0, 5], [-5, 0, 5]], float)
        return v, np.array([[0, 1, 2], [0, 2, 3]])
    if kind == 'Quad':
        v = np.array([[-.5, -.5, 0], [.5, -.5, 0], [.5, .5, 0], [-.5, .5, 0]], float)
        return v, np.array([[0, 1, 2], [0, 2, 3]])
    # cube-like box for the rest
    v = np.array([[x, y, z] for x in (-.5, .5) for y in (-.5, .5) for z in (-.5, .5)], float)
    f = [[0, 1, 3], [0, 3, 2], [4, 6, 7], [4, 7, 5], [0, 4, 5], [0, 5, 1], [2, 3, 7], [2, 7, 6], [0, 2, 6], [0, 6, 4], [1, 5, 7], [1, 7, 3]]
    return v, np.array(f)


def trs(t):
    p, q, s = t.m_LocalPosition, t.m_LocalRotation, t.m_LocalScale
    x, y, z, w = q.x, q.y, q.z, q.w
    r = np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.array([s.x, s.y, s.z])
    m[:3, 3] = [p.x, p.y, p.z]
    return m


class Assets:
    """Resolves references across bundles: CAB name -> loaded serialized file."""

    def __init__(self):
        self.index = json.load(open(os.path.join(PROJECT, '_work', 'cab_index.json'), encoding='utf-8'))
        self.files = {}
        self.loaded = set()
        self.meshes = {}
        self.shaders = {}

    def load(self, bundle):
        if bundle in self.loaded:
            return
        self.loaded.add(bundle)
        env = UnityPy.load(os.path.join(AB, bundle))
        for bf in env.files.values():
            for name, f in getattr(bf, 'files', {}).items():
                if type(f).__name__ == 'SerializedFile':
                    self.files[name.lower()] = f

    def deref(self, sf, pptr):
        if pptr is None or pptr.m_PathID == 0:
            return None, None
        if pptr.m_FileID == 0:
            target = sf
        else:
            ext = sf.externals[pptr.m_FileID - 1]
            name = ext.path.replace('\\', '/').split('/')[-1].lower()
            if name not in self.files:
                bundle = self.index.get(name)
                if bundle is None:
                    return ext.path, None
                self.load(bundle)
            target = self.files.get(name)
            if target is None:
                return ext.path, None
        return getattr(target, 'name', ''), target.objects.get(pptr.m_PathID)

    def mesh(self, sf, pptr):
        where, obj = self.deref(sf, pptr)
        if obj is None:
            if where and 'unity default resources' in where.lower() and pptr.m_PathID in BUILTIN:
                kind = BUILTIN[pptr.m_PathID]
                return kind, builtin_mesh(kind)
            return None, None
        key = (id(obj.assets_file), obj.path_id)
        if key not in self.meshes:
            data = obj.read()
            handler = MeshHandler(data)
            handler.process()
            verts = np.asarray(handler.m_Vertices, float).reshape(-1, 3)
            tris = []
            for sub in handler.get_triangles():
                if len(sub):
                    tris.append(np.asarray(sub, np.int64).reshape(-1, 3))
            tris = np.concatenate(tris) if tris else np.zeros((0, 3), np.int64)
            self.meshes[key] = (data.m_Name, (verts, tris))
        return self.meshes[key]

    def shader_name(self, sf, material_pptr):
        where, mat = self.deref(sf, material_pptr)
        if mat is None:
            return ''
        key = (id(mat.assets_file), mat.path_id)
        if key not in self.shaders:
            name = ''
            try:
                m = mat.read()
                _w, sh = self.deref(mat.assets_file, m.m_Shader)
                if sh is not None:
                    s = sh.read()
                    pf = getattr(s, 'm_ParsedForm', None)
                    name = getattr(pf, 'm_Name', '') if pf is not None else getattr(s, 'm_Name', '')
            except Exception as e:      # an unreadable shader just counts as solid
                name = f'?{type(e).__name__}'
            self.shaders[key] = name
        return self.shaders[key]


def is_see_through(shader):
    s = shader.lower()
    return any(k in s for k in ('particles', 'unlit', 'transparent', 'fog', 'skybox', 'lightshaft', 'decal'))


def survey(assets, scene):
    t0 = time.time()
    env = UnityPy.load(os.path.join(AB, scene))
    objs = {}
    sf = None
    for o in env.objects:
        objs.setdefault(o.type.name, []).append(o)
        sf = o.assets_file
    transforms = {o.path_id: o.read() for o in objs.get('Transform', [])}
    gos = {o.path_id: o.read() for o in objs.get('GameObject', [])}
    go_transform = {t.m_GameObject.m_PathID: pid for pid, t in transforms.items()}
    world = {}

    def world_of(pid):
        if pid in world:
            return world[pid]
        t = transforms[pid]
        m = trs(t)
        if t.m_Father.m_PathID and t.m_Father.m_PathID in transforms:
            m = world_of(t.m_Father.m_PathID) @ m
        world[pid] = m
        return m

    def active(pid):
        t = transforms[pid]
        go = gos.get(t.m_GameObject.m_PathID)
        if go is not None and not go.m_IsActive:
            return False
        if t.m_Father.m_PathID and t.m_Father.m_PathID in transforms:
            return active(t.m_Father.m_PathID)
        return True

    def path_of(pid):
        names = []
        while pid in transforms:
            go = gos.get(transforms[pid].m_GameObject.m_PathID)
            names.append(go.m_Name if go else '?')
            pid = transforms[pid].m_Father.m_PathID
        return '/'.join(reversed(names))

    centre = None
    for pid in transforms:
        p = path_of(pid)
        if p.endswith('FormationSetting/Center'):
            centre = world_of(pid)[:3, 3]
            break
    if centre is None:
        centre = np.zeros(3)
    camera = None
    for o in objs.get('Camera', []):
        c = o.read()
        tp = go_transform.get(c.m_GameObject.m_PathID)
        if tp is not None:
            m = world_of(tp)
            camera = (m[:3, 3], m[:3, 2])

    filters = {}
    for o in objs.get('MeshFilter', []):
        d = o.read()
        filters[d.m_GameObject.m_PathID] = d
    n = int(round(2 * EXTENT / CELL))
    blocked = np.zeros((n, n), bool)
    floor = np.zeros((n, n), bool)
    cx, cy, cz = centre
    stats = dict(renderers=0, skipped_see_through=0, triangles=0, missing=0)
    shader_count = {}
    for o in objs.get('MeshRenderer', []):
        r = o.read()
        if not r.m_Enabled:
            continue
        gp = r.m_GameObject.m_PathID
        tp = go_transform.get(gp)
        if tp is None or not active(tp) or gp not in filters:
            continue
        shader = assets.shader_name(sf, r.m_Materials[0]) if len(r.m_Materials) else ''
        shader_count[shader] = shader_count.get(shader, 0) + 1
        if is_see_through(shader):
            stats['skipped_see_through'] += 1
            continue
        kind, mesh = assets.mesh(sf, filters[gp].m_Mesh)
        if mesh is None:
            stats['missing'] += 1
            continue
        verts, tris = mesh
        if not len(tris):
            continue
        builtin_flat = kind in ('Plane', 'Quad')
        m = world_of(tp)
        w = verts @ m[:3, :3].T + m[:3, 3]
        a, b, c = w[tris[:, 0]], w[tris[:, 1]], w[tris[:, 2]]
        lo = np.minimum(np.minimum(a, b), c)
        hi = np.maximum(np.maximum(a, b), c)
        keep = ((hi[:, 0] >= cx - EXTENT) & (lo[:, 0] <= cx + EXTENT) & (hi[:, 2] >= cz - EXTENT) & (lo[:, 2] <= cz + EXTENT)
                & (lo[:, 1] <= cy + BAND[1]) & (hi[:, 1] >= cy - FLOOR_TOL))
        if not keep.any():
            continue
        a, b, c = a[keep], b[keep], c[keep]
        stats['renderers'] += 1
        stats['triangles'] += len(a)
        normal = np.cross(b - a, c - a)
        nlen = np.linalg.norm(normal, axis=1)
        flat = np.abs(normal[:, 1]) > 0.9 * np.maximum(nlen, 1e-12)
        edge = np.maximum(np.maximum(np.linalg.norm(b - a, axis=1), np.linalg.norm(c - b, axis=1)), np.linalg.norm(a - c, axis=1))
        steps = np.clip(np.ceil(edge / SAMPLE), 1, 400).astype(int)
        for k in np.unique(steps):
            sel = steps == k
            uu, vv = np.meshgrid(np.arange(k + 1), np.arange(k + 1), indexing='ij')
            ok = uu + vv <= k
            u = (uu[ok] / k)[None, :, None]
            v = (vv[ok] / k)[None, :, None]
            A, B, C = a[sel][:, None, :], b[sel][:, None, :], c[sel][:, None, :]
            pts = A + u * (B - A) + v * (C - A)
            isflat = np.broadcast_to(flat[sel][:, None], pts.shape[:2])
            pts = pts.reshape(-1, 3)
            isflat = isflat.reshape(-1)
            ix = np.floor((pts[:, 0] - cx + EXTENT) / CELL).astype(int)
            iz = np.floor((pts[:, 2] - cz + EXTENT) / CELL).astype(int)
            inside = (ix >= 0) & (ix < n) & (iz >= 0) & (iz < n)
            h = pts[:, 1] - cy
            band = inside & (h >= BAND[0]) & (h <= BAND[1])
            if not builtin_flat:
                blocked[ix[band], iz[band]] = True
            fl = inside & (np.abs(h) <= FLOOR_TOL) & isflat
            floor[ix[fl], iz[fl]] = True

    free = floor & ~blocked
    # largest free circle around the centre and near it
    obstacle = np.argwhere(~free)
    ocx = (obstacle[:, 0] + 0.5) * CELL - EXTENT
    ocz = (obstacle[:, 1] + 0.5) * CELL - EXTENT

    def radius(px, pz):
        if not len(obstacle):
            return EXTENT
        d = np.sqrt((ocx - px) ** 2 + (ocz - pz) ** 2).min() - CELL * 0.5
        return float(min(max(d, 0.0), EXTENT - max(abs(px), abs(pz))))

    r_centre = radius(0.0, 0.0)
    best = (r_centre, 0.0, 0.0)
    for px in np.arange(-SEARCH, SEARCH + 0.01, 0.5):
        for pz in np.arange(-SEARCH, SEARCH + 0.01, 0.5):
            if px * px + pz * pz > SEARCH * SEARCH:
                continue
            r = radius(px, pz)
            if r > best[0] + 1e-6:
                best = (r, float(px), float(pz))

    # map
    img = np.zeros((n, n, 3), np.uint8)
    img[floor] = (90, 90, 90)
    img[free] = (200, 200, 200)
    img[blocked] = (200, 40, 40)
    pic = Image.fromarray(np.transpose(img, (1, 0, 2))[::-1]).resize((n * 2, n * 2), Image.NEAREST)
    d = ImageDraw.Draw(pic)

    def px_of(x, z):
        return ((x + EXTENT) / CELL * 2, (EXTENT - z) / CELL * 2)

    r, bx, bz = best
    x0, y0 = px_of(bx - r, bz + r)
    x1, y1 = px_of(bx + r, bz - r)
    d.ellipse([x0, y0, x1, y1], outline=(40, 200, 40), width=2)
    c0 = px_of(0, 0)
    d.ellipse([c0[0] - 4, c0[1] - 4, c0[0] + 4, c0[1] + 4], fill=(40, 80, 255))
    if camera is not None:
        cp, cf = camera
        p0 = px_of(cp[0] - cx, cp[2] - cz)
        p1 = px_of(cp[0] - cx + cf[0] * 4, cp[2] - cz + cf[2] * 4)
        d.line([p0, p1], fill=(255, 220, 0), width=3)
    name = scene[len('scene_battlefield_'):-3]
    d.text((4, 4), f'{name}  centre r={r_centre:.1f} m  best r={r:.1f} m', fill=(255, 255, 0))
    os.makedirs(OUT, exist_ok=True)
    pic.save(os.path.join(OUT, name + '.png'))
    top = sorted(shader_count.items(), key=lambda kv: -kv[1])[:6]
    return dict(scene=name, centre=[round(float(x), 2) for x in centre], r_centre=round(r_centre, 2),
                r_best=round(best[0], 2), best_offset=[best[1], best[2]],
                free_area_10m=round(float(free[int((EXTENT - 10) / CELL):int((EXTENT + 10) / CELL),
                                               int((EXTENT - 10) / CELL):int((EXTENT + 10) / CELL)].sum()) * CELL * CELL, 1),
                seconds=round(time.time() - t0, 1), shaders=top, **stats)


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('filters', nargs='*')
    ap.add_argument('--all-variants', action='store_true')
    ap.add_argument('--limit-minutes', type=float, default=20)
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    out_json = os.path.join(OUT, 'survey.json')
    results = json.load(open(out_json, encoding='utf-8')) if os.path.exists(out_json) else {}
    scenes = sorted(f for f in os.listdir(AB) if f.startswith('scene_battlefield_') and f.endswith('.ab'))
    if a.filters:
        scenes = [s for s in scenes if any(f in s for f in a.filters)]
    if not a.all_variants:
        seen, picked = set(), []
        for s in scenes:
            base = VARIANT_RE.sub('', s[len('scene_battlefield_'):-3])
            if base not in seen:
                seen.add(base)
                picked.append(s)
        scenes = picked
    assets = Assets()
    t0 = time.time()
    todo = [s for s in scenes if s[len('scene_battlefield_'):-3] not in results]
    print(f'{len(scenes)} scenes, {len(todo)} to measure')
    for s in todo:
        if time.time() - t0 > a.limit_minutes * 60:
            print('time limit reached; run again to continue')
            break
        try:
            res = survey(assets, s)
        except Exception as e:
            res = dict(scene=s[len('scene_battlefield_'):-3], error=f'{type(e).__name__}: {e}')
        results[res['scene']] = res
        json.dump(results, open(out_json, 'w', encoding='utf-8'), indent=1)
        if 'error' in res:
            print(f'{res["scene"]:45s} ERROR {res["error"][:120]}')
        else:
            print(f'{res["scene"]:45s} centre r {res["r_centre"]:5.1f}  best r {res["r_best"]:5.1f} at {res["best_offset"]}  '
                  f'free 20x20 {res["free_area_10m"]:6.1f} m2  {res["renderers"]} meshes {res["triangles"]} tris '
                  f'({res["skipped_see_through"]} see-through, {res["missing"]} missing)  {res["seconds"]} s')
    done = [r for r in results.values() if 'error' not in r]
    print('\nlargest circles:')
    for r in sorted(done, key=lambda r: -r['r_best'])[:25]:
        print(f'  {r["scene"]:45s} best r {r["r_best"]:5.1f}  centre r {r["r_centre"]:5.1f}  free {r["free_area_10m"]:6.1f} m2')


if __name__ == '__main__':
    main()
