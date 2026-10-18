# Tiny client for the local AssetRipper server (headless, 127.0.0.1:5599).
#   python ar.py get /openapi.json
#   python ar.py post /LoadFolder path=E:/x
#   python ar.py api            list endpoints
#   python ar.py settings       list the settings form fields
import html
import io
import json
import re
import sys
import urllib.parse
import urllib.request

BASE = 'http://127.0.0.1:5599'
_opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def get(path, timeout=600):
    with _opener.open(BASE + path, timeout=timeout) as r:
        return r.status, r.read().decode('utf-8', 'replace')


def post(path, form, timeout=3600):
    data = urllib.parse.urlencode(form).encode()
    req = urllib.request.Request(BASE + path, data=data, method='POST')
    req.add_header('Content-Type', 'application/x-www-form-urlencoded')
    try:
        with _opener.open(req, timeout=timeout) as r:
            return r.status, r.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'replace')


def api():
    _, t = get('/openapi.json')
    d = json.loads(t)
    print(d.get('info'))
    for p, v in d.get('paths', {}).items():
        for m, spec in v.items():
            params = [(x.get('name'), x.get('in')) for x in spec.get('parameters', [])]
            body = []
            for ct, c in spec.get('requestBody', {}).get('content', {}).items():
                sch = c.get('schema', {})
                body.append((ct, list(sch.get('properties', {}).keys()) or sch.get('$ref')))
            print(m.upper(), p, '| params', params, '| body', body, '|', spec.get('summary') or '')


def settings():
    _, t = get('/Settings/Edit')
    print('len', len(t))
    for m in re.finditer(r'<input[^>]*>', t):
        tag = m.group(0)
        name = re.search(r'name="([^"]+)"', tag)
        if not name:
            continue
        val = re.search(r'value="([^"]*)"', tag)
        typ = re.search(r'type="([^"]*)"', tag)
        print(' input', name.group(1), '| type', typ.group(1) if typ else None,
              '| value', html.unescape(val.group(1)) if val else None, '| checked' if 'checked' in tag else '')
    for m in re.finditer(r'<select[^>]*name="([^"]+)"[^>]*>(.*?)</select>', t, re.S):
        opts = re.findall(r'<option([^>]*)>([^<]*)</option>', m.group(2))
        print(' select', m.group(1))
        for o in opts:
            v = re.search(r'value="([^"]*)"', o[0])
            print('     ', '*' if 'selected' in o[0] else ' ', v.group(1) if v else None, '=', html.unescape(o[1]).strip())


if __name__ == '__main__':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    cmd = sys.argv[1]
    if cmd == 'api':
        api()
    elif cmd == 'settings':
        settings()
    elif cmd == 'get':
        s, t = get(sys.argv[2])
        print(s)
        print(t[:int(sys.argv[3]) if len(sys.argv) > 3 else 4000])
    elif cmd == 'post':
        form = dict(a.split('=', 1) for a in sys.argv[3:])
        s, t = post(sys.argv[2], form)
        print(s)
        print(t[:3000])
