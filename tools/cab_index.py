# Which game bundle holds a given CAB?  Reads only each bundle's header and directory
# (the CAB name is NOT a hash of the file name in this game), and caches the result.
#   python cab_index.py build <index.json>             scan all bundles (about a minute)
#   python cab_index.py find <index.json> <cab> [...]  look up bundles by CAB (prefix optional)
import io
import json
import os
import struct
import sys

from UnityPy.helpers import CompressionHelper

AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'


def cstr(f):
    out = bytearray()
    while True:
        c = f.read(1)
        if not c or c == b'\0':
            return out.decode('utf-8', 'replace')
        out += c


def nodes_of(path):
    """Names of the files stored in a UnityFS bundle (the CAB-... entries)."""
    with open(path, 'rb') as f:
        if cstr(f) != 'UnityFS':
            return None
        version = struct.unpack('>I', f.read(4))[0]
        cstr(f)
        cstr(f)
        size, csize, usize, flags = struct.unpack('>qIII', f.read(20))
        if version >= 7:
            f.seek((f.tell() + 15) // 16 * 16)
        if flags & 0x80:
            f.seek(size - csize)
        raw = f.read(csize)
    comp = flags & 0x3f
    if comp == 0:
        info = raw
    elif comp == 1:
        info = CompressionHelper.decompress_lzma(raw)
    else:
        info = CompressionHelper.decompress_lz4(raw, usize)
    r = io.BytesIO(info)
    r.read(16)
    nblocks = struct.unpack('>i', r.read(4))[0]
    r.read(10 * nblocks)
    nnodes = struct.unpack('>i', r.read(4))[0]
    names = []
    for _ in range(nnodes):
        r.read(20)
        names.append(cstr(r))
    return names


def build(out):
    idx = {}
    bad = []
    names = sorted(os.listdir(AB))
    for i, n in enumerate(names):
        try:
            ns = nodes_of(os.path.join(AB, n))
        except Exception as e:  # keep going, report at the end
            bad.append((n, repr(e)))
            continue
        if ns is None:
            bad.append((n, 'not UnityFS'))
            continue
        for node in ns:
            if node.lower().endswith(('.ress', '.resource')):
                continue
            idx[node.lower()] = n
    json.dump(idx, open(out, 'w', encoding='utf-8'), ensure_ascii=False)
    print(f'{len(names)} bundles, {len(idx)} serialized files, {len(bad)} unreadable -> {out}')
    for b in bad[:10]:
        print('  unreadable:', b)


if __name__ == '__main__':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    if sys.argv[1] == 'build':
        build(sys.argv[2])
    else:
        idx = json.load(open(sys.argv[2], encoding='utf-8'))
        for cab in sys.argv[3:]:
            key = cab.lower()
            if not key.startswith('cab-'):
                key = 'cab-' + key
            print(key, '->', idx.get(key, 'NOT FOUND'))
