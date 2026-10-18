"""Minimal read-only binary FBX (7.x) reader: node tree + typed properties (arrays zlib-decoded)."""
import struct, zlib
import numpy as np


class Node:
    __slots__ = ('name', 'props', 'children')

    def __init__(self, name, props, children):
        self.name, self.props, self.children = name, props, children

    def find(self, name):
        return [c for c in self.children if c.name == name]

    def first(self, name):
        for c in self.children:
            if c.name == name:
                return c
        return None


def _read_prop(buf, p):
    t = chr(buf[p]); p += 1
    if t == 'Y': return struct.unpack_from('<h', buf, p)[0], p + 2
    if t == 'C': return bool(buf[p]), p + 1
    if t == 'I': return struct.unpack_from('<i', buf, p)[0], p + 4
    if t == 'F': return struct.unpack_from('<f', buf, p)[0], p + 4
    if t == 'D': return struct.unpack_from('<d', buf, p)[0], p + 8
    if t == 'L': return struct.unpack_from('<q', buf, p)[0], p + 8
    if t in 'SR':
        n = struct.unpack_from('<I', buf, p)[0]; p += 4
        raw = bytes(buf[p:p + n]); p += n
        return (raw.decode('utf-8', 'replace') if t == 'S' else raw), p
    if t in 'fdlib':
        n, enc, clen = struct.unpack_from('<III', buf, p); p += 12
        raw = bytes(buf[p:p + clen]); p += clen
        if enc == 1:
            raw = zlib.decompress(raw)
        dt = {'f': '<f4', 'd': '<f8', 'l': '<i8', 'i': '<i4', 'b': 'u1'}[t]
        return np.frombuffer(raw, dtype=dt, count=n), p
    raise ValueError('unknown prop type %r at %d' % (t, p - 1))


def _read_node(buf, p, v64):
    if v64:
        end, nprops, plen = struct.unpack_from('<QQQ', buf, p); p += 24
    else:
        end, nprops, plen = struct.unpack_from('<III', buf, p); p += 12
    nlen = buf[p]; p += 1
    name = bytes(buf[p:p + nlen]).decode('ascii', 'replace'); p += nlen
    if end == 0:
        return None, p
    props = []
    for _ in range(nprops):
        v, p = _read_prop(buf, p)
        props.append(v)
    children = []
    sentinel = 25 if v64 else 13
    while p < end:
        if end - p == sentinel:
            p = end
            break
        ch, p = _read_node(buf, p, v64)
        if ch is None:
            break
        children.append(ch)
    return Node(name, props, children), end


def load(path):
    buf = memoryview(open(path, 'rb').read())
    assert bytes(buf[:18]) == b'Kaydara FBX Binary'
    ver = struct.unpack_from('<I', buf, 23)[0]
    v64 = ver >= 7500
    p = 27
    roots = []
    while p < len(buf) - 200:
        n, p = _read_node(buf, p, v64)
        if n is None:
            break
        roots.append(n)
    return Node('root', [], roots), ver


def geometries(root):
    """yield dict(name, id, verts (N,3), polys (list of index arrays), mat_per_poly) for each Mesh geometry"""
    objs = root.first('Objects')
    conns = root.first('Connections')
    models = {}
    for c in objs.children:
        if c.name == 'Model':
            models[c.props[0]] = c.props[1].split('\x00')[0]
    mats = {c.props[0]: c.props[1].split('\x00')[0] for c in objs.children if c.name == 'Material'}
    links = [(c.props[1], c.props[2]) for c in conns.children if c.name == 'C']  # (child, parent)
    out = []
    for g in objs.children:
        if g.name != 'Geometry' or len(g.props) < 3 or g.props[2] != 'Mesh':
            continue
        V = g.first('Vertices').props[0].reshape(-1, 3)
        pvi = g.first('PolygonVertexIndex').props[0]
        polys, cur = [], []
        for x in pvi:
            if x < 0:
                cur.append(~x); polys.append(cur); cur = []
            else:
                cur.append(x)
        lem = g.first('LayerElementMaterial')
        mpp = lem.first('Materials').props[0] if lem is not None and lem.first('Materials') is not None else None
        model = [models.get(par) for ch, par in links if ch == g.props[0] and par in models]
        mname = model[0] if model else None
        model_id = [par for ch, par in links if ch == g.props[0] and par in models]
        mat_names = [mats[ch] for ch, par in links if model_id and par == model_id[0] and ch in mats]
        out.append(dict(name=g.props[1].split('\x00')[0], model=mname, verts=V, polys=polys, mat_per_poly=mpp, materials=mat_names))
    return out
