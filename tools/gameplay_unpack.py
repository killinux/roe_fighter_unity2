# Unpack Rise of Eros' gameplay data (skill definitions, unit stats, stage scripts):
#   %USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\GameplayAssets\gameplayAsset.data
#   python gameplay_unpack.py [out dir]          (default: <project>/_work/gameplay)
#
# Layout (version 2): u8 version, u64 data_end, u64 file_count, u64 tail_size (data_end + tail_size
# = file size).  From offset 25 the files follow back to back; each one is an LZ4 stream:
# [u32 block_size] then blocks of [u32 packed_size][LZ4 block], every block decoding to block_size
# bytes except the last, and LINKED - a block may copy from the previous 64 KB of its own stream.
# The tail (from data_end) is one more such stream holding the index:
# [u32 name_len][name][u64 raw_size][u64 packed_size] per file, in file order.
import os
import struct
import sys

import lz4.block

SRC = os.path.expandvars(r'%USERPROFILE%\AppData\LocalLow\Pinkcore\Rise of Eros\GameplayAssets\gameplayAsset.data')
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def lz4_prefixed(src, prefix):
    """Plain LZ4 block decode with a prefix dictionary, for a last block of unknown size."""
    out = bytearray(prefix)
    i, n = 0, len(src)
    while i < n:
        token = src[i]
        i += 1
        lit = token >> 4
        if lit == 15:
            while True:
                b = src[i]
                i += 1
                lit += b
                if b != 255:
                    break
        out += src[i:i + lit]
        i += lit
        if i >= n:
            break
        off = src[i] | (src[i + 1] << 8)
        i += 2
        ml = token & 15
        if ml == 15:
            while True:
                b = src[i]
                i += 1
                ml += b
                if b != 255:
                    break
        ml += 4
        start = len(out) - off
        for k in range(ml):
            out.append(out[start + k])
    return bytes(out[len(prefix):])


def read_stream(data, pos, end=None, raw_size=None):
    """One LZ4 stream starting at pos; stops at `end` (file offset) or after raw_size bytes."""
    block_size, = struct.unpack_from('<I', data, pos)
    pos += 4
    out = bytearray()
    while (end is None or pos < end) and (raw_size is None or len(out) < raw_size):
        packed, = struct.unpack_from('<I', data, pos)
        pos += 4
        block = data[pos:pos + packed]
        pos += packed
        prefix = bytes(out[-65536:])
        want = block_size if raw_size is None else min(block_size, raw_size - len(out))
        try:
            chunk = lz4.block.decompress(block, uncompressed_size=want, dict=prefix)
        except lz4.block.LZ4BlockError:
            chunk = lz4_prefixed(block, prefix)
        out += chunk
    return bytes(out), pos


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJECT, '_work', 'gameplay')
    data = open(SRC, 'rb').read()
    version = data[0]
    data_end, count, tail = struct.unpack_from('<QQQ', data, 1)
    print(f'version {version}, data_end {data_end}, count {count}, tail {tail}, file size {len(data)}')
    index_raw, _ = read_stream(data, data_end, end=len(data))
    entries = []
    p = 0
    while p < len(index_raw):
        n, = struct.unpack_from('<I', index_raw, p)
        p += 4
        name = index_raw[p:p + n].decode('utf-8')
        p += n
        raw, packed = struct.unpack_from('<QQ', index_raw, p)
        p += 16
        entries.append((name, raw, packed))
    print(f'{len(entries)} files in the index')

    pos = 25
    bad = 0
    for name, raw, packed in entries:
        start = pos
        content, pos = read_stream(data, pos, raw_size=raw)
        if len(content) != raw or pos - start != packed:
            bad += 1
            if bad <= 5:
                print(f'  size mismatch in {name}: raw {len(content)} / {raw}, packed {pos - start} / {packed}')
            pos = start + packed
        path = os.path.join(out_dir, *name.replace('\\', '/').split('/'))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(content)
    print(f'wrote {len(entries)} files to {out_dir}; {bad} with a size mismatch; ended at {pos} (data_end {data_end})')


if __name__ == '__main__':
    main()
