# Download one big file over many parallel range requests.
# Why: on this machine a direct connection to Unity's download servers is redirected to the
# China site (404 for international builds), and the proxy from HTTP(S)_PROXY gives only about
# 30 KB/s per connection.  Many connections add up (64 -> about 1.8 MB/s).
#
#   python fetch_parallel.py <url> <out file> [--threads 64] [--chunk-mb 8] [--md5 <hex>]
#
# Resumable: finished chunks are recorded in <out file>.parts.json; run the same command again.
import argparse
import hashlib
import json
import os
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed


def opener():
    return urllib.request.build_opener(urllib.request.ProxyHandler())  # proxies from the environment


def head(url):
    req = urllib.request.Request(url, method='HEAD')
    with opener().open(req, timeout=60) as r:
        return int(r.headers['Content-Length']), (r.headers.get('ETag') or '').strip('"')


def fetch_chunk(url, path, start, end, tries=30):
    want = end - start + 1
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers={'Range': f'bytes={start}-{end}'})
            with opener().open(req, timeout=60) as r:
                if r.status != 206:
                    raise IOError(f'status {r.status}')
                data = r.read()
            if len(data) != want:
                raise IOError(f'short read {len(data)}/{want}')
            with open(path, 'r+b') as f:
                f.seek(start)
                f.write(data)
            return want
        except Exception as e:  # retry on anything; the proxy drops connections now and then
            last = e
            time.sleep(min(30, 2 + attempt * 2))
    raise IOError(f'chunk {start}-{end} failed after {tries} tries: {last!r}')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('url')
    ap.add_argument('out')
    ap.add_argument('--threads', type=int, default=64)
    ap.add_argument('--chunk-mb', type=int, default=8)
    ap.add_argument('--md5', default='')
    a = ap.parse_args()

    size, etag = head(a.url)
    md5 = a.md5 or (etag if len(etag) == 32 else '')
    chunk = a.chunk_mb * 1024 * 1024
    chunks = [(s, min(s + chunk, size) - 1) for s in range(0, size, chunk)]
    state_path = a.out + '.parts.json'
    done = set()
    if os.path.exists(state_path) and os.path.exists(a.out) and os.path.getsize(a.out) == size:
        st = json.load(open(state_path))
        if st.get('size') == size and st.get('chunk') == chunk:
            done = set(st['done'])
    else:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, 'wb') as f:
            f.truncate(size)
    todo = [c for c in chunks if c[0] not in done]
    print(f'{size / 1e6:.0f} MB, {len(chunks)} chunks, {len(done)} already done, {a.threads} connections', flush=True)

    lock = threading.Lock()
    got = sum(e - s + 1 for s, e in chunks if s in done)
    t0 = time.time()
    session = 0
    last_print = 0.0
    with ThreadPoolExecutor(a.threads) as ex:
        futs = {ex.submit(fetch_chunk, a.url, a.out, s, e): s for s, e in todo}
        for fu in as_completed(futs):
            n = fu.result()
            with lock:
                done.add(futs[fu])
                got += n
                session += n
                now = time.time()
                if now - last_print > 15 or got == size:
                    last_print = now
                    json.dump({'size': size, 'chunk': chunk, 'done': sorted(done)}, open(state_path, 'w'))
                    speed = session / max(1e-6, now - t0)
                    eta = (size - got) / max(1.0, speed)
                    print(f'{got / size * 100:5.1f}%  {got / 1e6:7.0f} MB  {speed / 1e6:5.2f} MB/s  eta {eta / 60:5.1f} min',
                          flush=True)
    json.dump({'size': size, 'chunk': chunk, 'done': sorted(done)}, open(state_path, 'w'))
    if md5:
        h = hashlib.md5()
        with open(a.out, 'rb') as f:
            for block in iter(lambda: f.read(1 << 22), b''):
                h.update(block)
        ok = h.hexdigest() == md5.lower()
        print('md5', h.hexdigest(), 'OK' if ok else f'MISMATCH (want {md5})', flush=True)
        if not ok:
            sys.exit(2)
    os.remove(state_path)
    print('done', a.out, flush=True)


if __name__ == '__main__':
    main()
