"""Read-only dumper for Vindictus: Defying Fate (UE 5.3 IoStore) Zen packages.

Uses ripper_tpose/scripts/firstdescendant/iostore.py (imported, not modified) for the container,
parses the UE 5.3 Zen summary itself (52-byte summary: ... DependencyBundleHeadersOffset,
DependencyBundleEntriesOffset, ImportedPackageNamesOffset) and resolves script-class hashes through
the global.utoc ScriptObjects chunk.

The AES key is read from the key file into a variable and never printed.

    python zen_dump.py --out <dir> <regex> [<regex> ...]      # dump headers (+ raw .uasset) of matching packages
    python zen_dump.py --list <regex>                          # list matching package paths only
"""
import argparse
import json
import os
import re
import struct
import sys

sys.path.insert(0, r"E:\code\othercode\ripper_tpose\scripts\firstdescendant")
from iostore import Oodle, Toc, read_name_batch  # noqa: E402

PAKS = r"E:\tools\vindictus\Vindictus\Content\Paks"
KEY_FILE = r"E:\tools\vindictus\_download\aes_key.txt"
OODLE = r"E:\tools\cue4parse_cli\oodle-data-shared.dll"


def load_key():
    k = open(KEY_FILE, encoding="utf-8").read().strip()
    return bytes.fromhex(k[2:] if k.lower().startswith("0x") else k)


def script_objects(oodle, key):
    """{global index u64: '/Script/Pkg.Object'} from global.utoc ScriptObjects chunk (chunk type 5)."""
    g = Toc(os.path.join(PAKS, "global.utoc"), key)
    idx = None
    for i, cid in enumerate(g.chunk_ids):
        if cid[11] == 5:
            idx = i
            break
    if idx is None:
        return {}
    buf = g.read_chunk(idx, oodle)
    names, pos = read_name_batch(buf, 0)
    n, = struct.unpack_from("<i", buf, pos)
    pos += 4
    entries = []
    for i in range(n):
        ni, nn, gidx, outer, cdo = struct.unpack_from("<IIQQQ", buf, pos + 32 * i)
        nm = names[ni & 0x3FFFFFFF] + ("_%d" % (nn - 1) if nn else "")
        entries.append((gidx, outer, nm))
    by = {e[0]: e for e in entries}

    def full(gidx, depth=0):
        e = by.get(gidx)
        if e is None or depth > 10:
            return "?"
        if e[1] == 0xFFFFFFFFFFFFFFFF or e[1] not in by:
            return e[2]
        return full(e[1], depth + 1) + "." + e[2]

    return {gidx: full(gidx) for gidx in by}


def kind(v):
    if v == 0xFFFFFFFFFFFFFFFF:
        return "null"
    return ["export", "script", "package", "null"][v >> 62]


def parse_zen53(buf, scripts):
    has_ver, hdr_size = struct.unpack_from("<II", buf, 0)
    name_idx, name_num = struct.unpack_from("<II", buf, 8)
    pkg_flags, cooked_hdr = struct.unpack_from("<II", buf, 16)
    (pub_hash_off, import_off, export_off, bundle_off, dep_hdr_off, dep_ent_off,
     imp_pkg_names_off) = struct.unpack_from("<7i", buf, 24)
    pos = 52
    if has_ver:
        pos += 16
        ncv, = struct.unpack_from("<i", buf, pos)
        pos += 4 + 20 * ncv
    names, pos = read_name_batch(buf, pos)
    imp_pkgs = []
    if imp_pkg_names_off > 0:
        pn, p2 = read_name_batch(buf, imp_pkg_names_off)
        nums = [struct.unpack_from("<i", buf, p2 + 4 * i)[0] for i in range(len(pn))]
        imp_pkgs = [n + ("_%d" % (k - 1) if k else "") for n, k in zip(pn, nums)]
    n_imports = (export_off - import_off) // 8
    imports = []
    for i in range(n_imports):
        v, = struct.unpack_from("<Q", buf, import_off + 8 * i)
        k = kind(v)
        if k == "script":
            imports.append({"kind": k, "name": scripts.get(v, "?script %016x" % v)})
        elif k == "package":
            pk = (v >> 32) & 0x3FFFFFFF
            imports.append({"kind": k, "package": imp_pkgs[pk] if pk < len(imp_pkgs) else "?", "hash_index": v & 0xFFFFFFFF})
        else:
            imports.append({"kind": k})

    def resolve(v):
        k = kind(v)
        if k == "script":
            return scripts.get(v, "?script")
        if k == "package":
            pk = (v >> 32) & 0x3FFFFFFF
            return "pkg:" + (imp_pkgs[pk] if pk < len(imp_pkgs) else "?")
        if k == "export":
            return "export:%d" % v
        return None

    n_exports = (bundle_off - export_off) // 72
    exports = []
    for i in range(n_exports):
        o = export_off + 72 * i
        cso, css = struct.unpack_from("<QQ", buf, o)
        oni, onn = struct.unpack_from("<II", buf, o + 16)
        outer, cls, sup, tmpl = struct.unpack_from("<4Q", buf, o + 24)
        flags, = struct.unpack_from("<I", buf, o + 64)
        nm = names[oni & 0x3FFFFFFF] + ("_%d" % (onn - 1) if onn else "")
        exports.append({"index": i, "name": nm, "class": resolve(cls), "super": resolve(sup),
                        "template": resolve(tmpl), "outer": resolve(outer),
                        "serial_offset": cso, "serial_size": css, "flags": "0x%x" % flags})
    return {"package": names[name_idx & 0x3FFFFFFF] if names else "?", "flags": "0x%x" % pkg_flags,
            "unversioned": bool(pkg_flags & 0x2000), "has_versioning_info": bool(has_ver),
            "header_size": hdr_size, "cooked_header_size": cooked_hdr,
            "imported_packages": imp_pkgs, "imports": imports, "exports": exports, "names": names}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("patterns", nargs="+")
    ap.add_argument("--out", default="")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--no-raw", action="store_true")
    a = ap.parse_args()
    key = load_key()
    toc = Toc(os.path.join(PAKS, "Vindictus-Windows.utoc"), key)
    paths = toc.paths()
    rx = [re.compile(p, re.I) for p in a.patterns]
    hits = sorted(p for p in paths if p.endswith(".uasset") and any(r.search(p) for r in rx))
    if a.list:
        for p in hits:
            print(p)
        print("#", len(hits))
        return
    oodle = Oodle(OODLE)
    scripts = script_objects(oodle, key)
    os.makedirs(a.out, exist_ok=True)
    summary = []
    for p in hits:
        try:
            buf = toc.read_chunk(paths[p], oodle)
        except Exception as e:  # noqa: BLE001
            print("FAIL", p, e)
            continue
        rel = p.split("/Content/", 1)[-1]
        base = os.path.join(a.out, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(base), exist_ok=True)
        if not a.no_raw:
            open(base, "wb").write(buf)
        z = parse_zen53(buf, scripts)
        z["path"] = p
        z["size"] = len(buf)
        json.dump(z, open(base[:-7] + ".zen.json", "w", encoding="utf-8"), indent=1)
        summary.append((p, [(e["name"], e["class"]) for e in z["exports"]], z["imported_packages"]))
        print("==", rel, "bytes", len(buf), "unversioned", z["unversioned"])
        for e in z["exports"]:
            print("   export %-50s class=%s size=%d" % (e["name"], e["class"], e["serial_size"]))
        for ip in z["imported_packages"]:
            print("   imports", ip)


if __name__ == "__main__":
    main()
