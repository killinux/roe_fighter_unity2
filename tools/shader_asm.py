# What a game shader really does: properties, render state, keywords, and the DirectX 11
# program of every keyword variant as disassembly (via Windows' own d3dcompiler_47.dll), with
# the constant-buffer layout so that cb offsets can be read as material properties.
#   python shader_asm.py "Pinkcore/Particles/Default" [more names or substrings ...] [--out dir] [--bundle heros_shader_collection.ab]
# Writes one .txt per shader (default dir: _work/shader_asm).
import ctypes
import os
import struct
import sys

import lz4.block
import UnityPy

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AB = r'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'
D3D11 = 4
PROGRAM_TYPES = {15: 'DX11 vertex SM4', 16: 'DX11 vertex SM5', 17: 'DX11 pixel SM4', 18: 'DX11 pixel SM5'}
BLEND = {0: 'Zero', 1: 'One', 2: 'DstColor', 3: 'SrcColor', 4: 'OneMinusDstColor', 5: 'SrcAlpha', 6: 'OneMinusSrcColor', 7: 'DstAlpha',
         8: 'OneMinusDstAlpha', 9: 'SrcAlphaSaturate', 10: 'OneMinusSrcAlpha'}
COMPARE = {0: 'Disabled', 1: 'Never', 2: 'Less', 3: 'Equal', 4: 'LEqual', 5: 'Greater', 6: 'NotEqual', 7: 'GEqual', 8: 'Always'}
CULL = {0: 'Off', 1: 'Front', 2: 'Back'}

_d3d = None


def disassemble(dxbc):
    global _d3d
    if _d3d is None:
        _d3d = ctypes.WinDLL('d3dcompiler_47.dll')
        _d3d.D3DDisassemble.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
        _d3d.D3DDisassemble.restype = ctypes.c_long
    blob = ctypes.c_void_p()
    hr = _d3d.D3DDisassemble(dxbc, len(dxbc), 0, None, ctypes.byref(blob))
    if hr != 0 or not blob.value:
        return f'<D3DDisassemble failed: 0x{hr & 0xffffffff:08x}>'
    vtbl = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    get_ptr = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vtbl[3])
    get_size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vtbl[4])
    release = ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(vtbl[2])
    text = ctypes.string_at(get_ptr(blob), get_size(blob)).decode('ascii', 'replace').rstrip('\0')
    release(blob)
    return text


def value(v, table=None):
    """A SerializedShaderFloatValue: a constant, or the name of the material property that sets it."""
    name = v.get('name')
    if name and name != '<noninit>':
        return f'[{name}]'
    x = v.get('val')
    if table is not None and int(x) in table:
        return table[int(x)]
    return f'{x:g}'


def state_text(st):
    b = st['rtBlend0']
    out = [f"Blend {value(b['srcBlend'], BLEND)} {value(b['destBlend'], BLEND)}, {value(b['srcBlendAlpha'], BLEND)} {value(b['destBlendAlpha'], BLEND)}"
           f"  (op {value(b['blendOp'])}/{value(b['blendOpAlpha'])}, ColorMask {value(b['colMask'])})",
           f"ZWrite {value(st['zWrite'])}  ZTest {value(st['zTest'], COMPARE)}  Cull {value(st['culling'], CULL)}"
           f"  Offset {value(st['offsetFactor'])}, {value(st['offsetUnits'])}  AlphaToMask {value(st['alphaToMask'])}"]
    stencil = st.get('stencilOp') or {}
    if st.get('stencilRef') and (st['stencilRef'].get('val') or st['stencilRef'].get('name') not in (None, '<noninit>')):
        out.append(f"Stencil ref {value(st['stencilRef'])} read {value(st['stencilReadMask'])} write {value(st['stencilWriteMask'])} "
                   f"comp {value(stencil.get('comp', {'val': 0}), COMPARE)} pass {value(stencil.get('pass', {'val': 0}))}")
    return out


def params_text(p, names):
    """SerializedProgramParameters (type tree, names by index) or a parsed parameter blob (names inline) as text."""
    def nm(x):
        return x['name'] if 'name' in x else names.get(x['m_NameIndex'], str(x['m_NameIndex']))

    out = []
    for cb in p.get('m_ConstantBuffers') or []:
        out.append(f"  cbuffer {nm(cb)} ({cb['m_Size']} bytes)")
        members = [(v['m_Index'], f"float{v.get('m_Dim', 4)}" + (f"[{v['m_ArraySize']}]" if v.get('m_ArraySize') else ''), nm(v))
                   for v in cb.get('m_VectorParams') or []]
        members += [(m['m_Index'], f"float{m.get('m_RowCount', 4)}x4" + (f"[{m['m_ArraySize']}]" if m.get('m_ArraySize') else ''), nm(m))
                    for m in cb.get('m_MatrixParams') or []]
        for off, kind, name in sorted(members):
            out.append(f"    +{off:4d}  = cb[{off // 16}].{'xyzw'[(off % 16) // 4]}  {kind:10} {name}")
    for b in p.get('m_ConstantBufferBindings') or []:
        out.append(f"  cbuffer binding: {nm(b)} -> cb{b['m_Index']}")
    for t in p.get('m_TextureParams') or []:
        out.append(f"  texture t{t['m_Index']} (sampler s{t.get('m_SamplerIndex')}, dim {t.get('m_Dim')}): {nm(t)}")
    for v in p.get('m_VectorParams') or []:
        out.append(f"  loose vector {nm(v)} at {v['m_Index']}")
    for b in p.get('m_BufferParams') or []:
        out.append(f"  buffer {nm(b)} at {b['m_Index']}")
    return out


class Reader:
    def __init__(self, data):
        self.d, self.p = data, 0

    def i32(self):
        v, = struct.unpack_from('<i', self.d, self.p)
        self.p += 4
        return v

    def string(self):
        n = self.i32()
        s = self.d[self.p:self.p + n].decode('utf-8', 'replace')
        self.p = (self.p + n + 3) & ~3
        return s


def parse_parameter_blob(data):
    """
    The per-variant parameter blob of a 2022.3 shader: version, then "groups" of named
    parameters (group 0 = loose globals, the others = constant buffers with the byte offset
    of every member), then the bindings (which register a cbuffer / texture / buffer sits in).
    """
    r = Reader(data)
    r.i32()                                     # version, 202012090
    out = {'m_VectorParams': [], 'm_ConstantBuffers': [], 'm_ConstantBufferBindings': [], 'm_TextureParams': [], 'm_BufferParams': []}

    def members(count):
        vectors, matrices = [], []
        for _ in range(count):
            name = r.string()
            r.i32()                             # type
            rows, cols, is_matrix, array, index = r.i32(), r.i32(), r.i32(), r.i32(), r.i32()
            if is_matrix:
                matrices.append({'name': name, 'm_Index': index, 'm_ArraySize': array, 'm_RowCount': rows})
            else:
                vectors.append({'name': name, 'm_Index': index, 'm_ArraySize': array, 'm_Dim': cols})
        return vectors, matrices

    for g in range(r.i32()):
        name = r.string()
        size = r.i32()
        vectors, matrices = members(r.i32())
        for _ in range(r.i32()):                # struct parameters
            sname = r.string()
            sindex, sarray, ssize = r.i32(), r.i32(), r.i32()
            sv, sm = members(r.i32())
            vectors += [{**v, 'name': f"{sname}.{v['name']}", 'm_Index': sindex + v['m_Index']} for v in sv]
            matrices += [{**m, 'name': f"{sname}.{m['name']}", 'm_Index': sindex + m['m_Index']} for m in sm]
        if g == 0:
            out['m_VectorParams'] = vectors + matrices
        else:
            out['m_ConstantBuffers'].append({'name': name, 'm_Size': size, 'm_VectorParams': vectors, 'm_MatrixParams': matrices})
    for _ in range(r.i32()):
        name = r.string()
        kind, index, extra = r.i32(), r.i32(), r.i32()
        if kind == 0:
            r.i32()                             # multisampled
            out['m_TextureParams'].append({'name': name, 'm_Index': index, 'm_SamplerIndex': extra >> 8, 'm_Dim': extra & 0xff})
        elif kind == 1:
            out['m_ConstantBufferBindings'].append({'name': name, 'm_Index': index})
        else:
            out['m_BufferParams'].append({'name': f'{name} (kind {kind})', 'm_Index': index})
    return out


def dump_shader(t, out_dir):
    pf = t['m_ParsedForm']
    name = pf['m_Name']
    lines = [f'Shader "{name}"', '', 'Properties:']
    for p in pf['m_PropInfo']['m_Props']:
        default = [round(p[f'm_DefValue[{i}]'], 5) for i in range(4) if f'm_DefValue[{i}]' in p]
        if not default and 'm_DefValue' in p:
            default = p['m_DefValue']
        tex = (p.get('m_DefTexture') or {}).get('m_DefaultName')
        lines.append(f"  {p['m_Name']:28} type {p['m_Type']} flags {p['m_Flags']} default {default}{(' tex ' + repr(tex)) if tex else ''}"
                     f"  \"{p['m_Description']}\"  {p.get('m_Attributes') or ''}")
    keywords = pf.get('m_KeywordNames') or []
    lines += ['', 'Keywords: ' + ', '.join(f'{i}:{k}' for i, k in enumerate(keywords)), '']

    # the DirectX 11 blob
    blob_entries = None
    if D3D11 in t['platforms']:
        pi = t['platforms'].index(D3D11)
        raw = bytes(t['compressedBlob'])
        data = b''
        for off, clen, dlen in zip(t['offsets'][pi], t['compressedLengths'][pi], t['decompressedLengths'][pi]):
            data += lz4.block.decompress(raw[off:off + clen], uncompressed_size=dlen)
        count, = struct.unpack_from('<i', data, 0)
        blob_entries = []
        for i in range(count):
            off, length, segment = struct.unpack_from('<iii', data, 4 + i * 12)
            blob_entries.append(data[off:off + length])

    for si, ss in enumerate(pf['m_SubShaders']):
        lines.append(f"SubShader {si}: LOD {ss.get('m_LOD')} tags {dict((ss.get('m_Tags') or {}).get('tags') or [])}")
        for pi_, p in enumerate(ss['m_Passes']):
            st = p['m_State']
            names = {idx: nm for nm, idx in p.get('m_NameIndices') or []}
            lines.append(f"  Pass {pi_}: \"{st.get('m_Name')}\" type {p.get('m_Type')} tags {dict((st.get('m_Tags') or {}).get('tags') or [])}"
                         + (f"  UsePass {p.get('m_UseName')}" if p.get('m_UseName') else ''))
            if p.get('m_Type') != 0:
                continue
            for s in state_text(st):
                lines.append('    ' + s)
            for stage in ('progVertex', 'progFragment'):
                prog = p[stage]
                common = prog.get('m_CommonParameters') or {}
                variants = []
                for tier, subs in enumerate(prog.get('m_PlayerSubPrograms') or []):
                    blobs = (prog.get('m_ParameterBlobIndices') or [])[tier] if tier < len(prog.get('m_ParameterBlobIndices') or []) else []
                    for k, sp in enumerate(subs):
                        if sp['m_GpuProgramType'] in PROGRAM_TYPES:
                            variants.append((sp, blobs[k] if k < len(blobs) else None))
                for sp in prog.get('m_SubPrograms') or []:
                    if sp['m_GpuProgramType'] in PROGRAM_TYPES:
                        variants.append((sp, None))
                lines.append(f"    {stage}: {len(variants)} DirectX 11 variants")
                if common:
                    lines.append('      common parameters:')
                    lines += ['      ' + x for x in params_text(common, names)]
                for sp, pblob in variants:
                    kws = [keywords[i] if i < len(keywords) else str(i) for i in sp.get('m_KeywordIndices') or []]
                    lines.append('')
                    lines.append(f"    ---- {stage} [{' '.join(kws) or 'no keywords'}]  ({PROGRAM_TYPES[sp['m_GpuProgramType']]}, blob {sp['m_BlobIndex']})")
                    if blob_entries is None:
                        continue
                    if pblob is not None and 0 <= pblob < len(blob_entries):
                        try:
                            lines += ['      ' + x for x in params_text(parse_parameter_blob(blob_entries[pblob]), names)]
                        except Exception as e:
                            lines.append(f'      <parameter blob {pblob} not parsed: {e}>')
                    entry = blob_entries[sp['m_BlobIndex']]
                    at = entry.find(b'DXBC')
                    if at < 0:
                        lines.append('      <no DXBC in this blob>')
                        continue
                    size, = struct.unpack_from('<I', entry, at + 24)
                    asm = disassemble(entry[at:at + size])
                    lines += ['      ' + x for x in asm.splitlines()]
        lines.append('')
    os.makedirs(out_dir, exist_ok=True)
    dst = os.path.join(out_dir, name.replace('/', '_').replace(' ', '_') + '.txt')
    with open(dst, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    return dst, len(lines)


def main():
    args = [a for a in sys.argv[1:]]
    out_dir = os.path.join(PROJECT, '_work', 'shader_asm')
    bundle = 'heros_shader_collection.ab'
    wanted = []
    i = 0
    while i < len(args):
        if args[i] == '--out':
            out_dir = args[i + 1]
            i += 2
        elif args[i] == '--bundle':
            bundle = args[i + 1]
            i += 2
        else:
            wanted.append(args[i].lower())
            i += 1
    env = UnityPy.load(bundle if os.path.exists(bundle) else os.path.join(AB, bundle))
    for obj in env.objects:
        if obj.type.name != 'Shader':
            continue
        t = obj.read_typetree()
        name = t['m_ParsedForm']['m_Name']
        if not wanted:
            print(name)
            continue
        if any(w in name.lower() for w in wanted):
            dst, n = dump_shader(t, out_dir)
            print(f'{name} -> {dst} ({n} lines)')


if __name__ == '__main__':
    main()
