# Finish a fetch_parallel.py download whose last chunks crawl: fetch every missing chunk as
# many small parallel pieces and mark it done.  Then run fetch_parallel.py again to verify.
#   python fetch_fill.py <url> <out file> [--pieces 32]
import argparse
import json
import urllib.request
from concurrent.futures import ThreadPoolExecutor


def piece(url, path, start, end):
    for attempt in range(40):
        try:
            op = urllib.request.build_opener(urllib.request.ProxyHandler())
            req = urllib.request.Request(url, headers={'Range': f'bytes={start}-{end}'})
            with op.open(req, timeout=60) as r:
                data = r.read()
            if len(data) != end - start + 1:
                raise IOError('short read')
            with open(path, 'r+b') as f:
                f.seek(start)
                f.write(data)
            return len(data)
        except Exception:
            continue
    raise IOError(f'piece {start}-{end} failed')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('url')
    ap.add_argument('out')
    ap.add_argument('--pieces', type=int, default=32)
    a = ap.parse_args()
    state_path = a.out + '.parts.json'
    st = json.load(open(state_path))
    size, chunk = st['size'], st['chunk']
    done = set(st['done'])
    missing = [s for s in range(0, size, chunk) if s not in done]
    print('missing chunks:', len(missing), flush=True)
    for s in missing:
        e = min(s + chunk, size) - 1
        step = max(1, (e - s + 1) // a.pieces)
        ranges = [(x, min(x + step, e + 1) - 1) for x in range(s, e + 1, step)]
        with ThreadPoolExecutor(len(ranges)) as ex:
            got = sum(ex.map(lambda r: piece(a.url, a.out, r[0], r[1]), ranges))
        done.add(s)
        json.dump({'size': size, 'chunk': chunk, 'done': sorted(done)}, open(state_path, 'w'))
        print(f'chunk at {s}: {got} bytes in {len(ranges)} pieces', flush=True)


if __name__ == '__main__':
    main()
