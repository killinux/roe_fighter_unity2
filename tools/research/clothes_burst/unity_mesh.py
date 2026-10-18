"""Read-only decoder for AssetRipper-exported Unity Mesh YAML (.asset): positions, normals, uv0,
colors, bone weights/indices, index buffer, submeshes.  Only numpy."""
import re
import numpy as np

FMT = {0: ('f4', 4), 1: ('f2', 2), 2: ('u1', 1), 3: ('i1', 1), 4: ('u2', 2), 5: ('i2', 2),
       6: ('u1', 1), 7: ('i1', 1), 8: ('u2', 2), 9: ('i2', 2), 10: ('u4', 4), 11: ('i4', 4)}
CH = ['pos', 'nrm', 'tan', 'col', 'uv0', 'uv1', 'uv2', 'uv3', 'uv4', 'uv5', 'uv6', 'uv7', 'bw', 'bi']


def _hex_after(txt, key):
    m = re.search(r'^\s*' + re.escape(key) + r'\s*(\S*)\s*$', txt, flags=re.M)
    return bytes.fromhex(m.group(1)) if m and m.group(1) else b''


def load(path):
    txt = open(path, 'r', encoding='utf-8', errors='ignore').read()
    name = re.search(r'm_Name: (.*)', txt).group(1).strip()
    subs = []
    for m in re.finditer(r'firstByte: (\d+)\s+indexCount: (\d+)\s+topology: (\d+)\s+baseVertex: (\d+)\s+'
                         r'firstVertex: (\d+)\s+vertexCount: (\d+)', txt):
        subs.append(dict(firstByte=int(m.group(1)), indexCount=int(m.group(2)), baseVertex=int(m.group(4)),
                         firstVertex=int(m.group(5)), vertexCount=int(m.group(6))))
    ifmt = int(re.search(r'm_IndexFormat: (\d+)', txt).group(1))
    ib = _hex_after(txt, 'm_IndexBuffer:')
    idx = np.frombuffer(ib, dtype='<u2' if ifmt == 0 else '<u4').astype(np.int64)
    vd = txt.split('m_VertexData:', 1)[1]
    nv = int(re.search(r'm_VertexCount: (\d+)', vd).group(1))
    chans = [tuple(int(x) for x in m.groups()) for m in
             re.finditer(r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', vd)]
    data = _hex_after(vd, '_typelessdata:')
    # stream strides
    strides = {}
    for s, off, f, d in chans:
        if d == 0:
            continue
        size = FMT[f][1] * (d & 0xF)
        strides[s] = max(strides.get(s, 0), off + size)
    for s in strides:
        strides[s] = (strides[s] + 3) // 4 * 4
    starts, pos = {}, 0
    for s in sorted(strides):
        pos = (pos + 15) // 16 * 16
        starts[s] = pos
        pos += strides[s] * nv
    out = {'name': name, 'subs': subs, 'idx': idx, 'nv': nv, 'readable': re.search(r'm_IsReadable: (\d)', txt).group(1)}
    for ci, (s, off, f, d) in enumerate(chans):
        d &= 0xF
        if d == 0 or ci >= len(CH):
            continue
        dt, sz = FMT[f]
        st = strides[s]
        raw = np.frombuffer(data, dtype=np.uint8, count=st * nv, offset=starts[s]).reshape(nv, st)
        col = raw[:, off:off + sz * d].copy().view('<' + dt).reshape(nv, d)
        out[CH[ci]] = col.astype(np.float64) if dt.startswith('f') else col
    out['bindposes'] = parse_bindposes(txt)
    return out


def parse_bindposes(txt):
    seg = txt.split('m_BindPose:', 1)[1].split('m_BoneNameHashes', 1)[0]
    vals = re.findall(r'e(\d)(\d): (\S+)', seg)
    mats, cur = [], {}
    for r, c, v in vals:
        cur[(int(r), int(c))] = float(v)
        if len(cur) == 16:
            mats.append(np.array([[cur[(i, j)] for j in range(4)] for i in range(4)]))
            cur = {}
    return mats


def tris(mesh, si):
    s = mesh['subs'][si]
    a = s['firstByte'] // (2 if mesh['idx'].dtype == np.int64 and max(mesh['idx'].max(), 0) < 65536 else 4)
    return None


def submesh_tris(mesh, si, index_size):
    s = mesh['subs'][si]
    a = s['firstByte'] // index_size
    t = mesh['idx'][a:a + s['indexCount']].reshape(-1, 3) + s['baseVertex']
    return t


def tri_area(P, T):
    return 0.5 * np.linalg.norm(np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]]), axis=1)


def weld(P, tol=1e-5):
    """map each vertex to a welded id by rounded position (UV seams split verts)."""
    key = np.round(P / tol).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    return inv.reshape(-1)


def boundary_stats(P, T):
    w = weld(P)
    WT = w[T]
    e = np.concatenate([WT[:, [0, 1]], WT[:, [1, 2]], WT[:, [2, 0]]])
    e = np.sort(e, axis=1)
    u, cnt = np.unique(e, axis=0, return_counts=True)
    b = u[cnt == 1]
    blen = np.linalg.norm(P[np.searchsorted(w, 0) * 0 + 0] - P[0]) if False else None
    # boundary length using a representative position per welded id
    rep = np.zeros((w.max() + 1, 3))
    rep[w] = P
    L = np.linalg.norm(rep[b[:, 0]] - rep[b[:, 1]], axis=1).sum() if len(b) else 0.0
    # loops (connected components of boundary edges)
    parent = {}

    def find(x):
        while parent.get(x, x) != x:
            parent[x] = parent.get(parent[x], parent[x])
            x = parent[x]
        return x
    for a_, b_ in b:
        ra, rb = find(a_), find(b_)
        if ra != rb:
            parent[ra] = rb
    roots = {find(x) for x in np.unique(b)} if len(b) else set()
    return len(b), L, len(roots)
