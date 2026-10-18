"""Read a Universal Fighting Engine 2 .unitypackage without importing it into Unity.

UFE 2 is a paid Asset Store package: it stays where the user put it, nothing of it goes into this repo.
A .unitypackage is a gzip'd tar of <guid>/pathname + <guid>/asset + <guid>/asset.meta; this tool only reads it.

    python tools/ufe_package.py list    <package> [--grep REGEX]            what is in it (path, size)
    python tools/ufe_package.py extract <package> <out_root> REGEX ...      copy entries (asset + .meta, parent folders'
                                                                            .meta too, so Unity keeps the package's GUIDs)
    python tools/ufe_package.py moves   <package> [--out moves.tsv]         the demo characters' move lists: for every
                                                                            move set, its basic moves (idle, walks, blocks,
                                                                            hits, falls, stand-ups) and attack moves with
                                                                            their clip, frame data, input and hits

Example (the fight's UFE motion packs, README 动作包 > UFE 2 的演示角色):
    python tools/ufe_package.py extract "E:/Downloads/Universal Fighting Engine 2 Source v2.7.0a.unitypackage" . ^Assets/UFE/Demos/Shared_Assets/Characters/(Robot_Kyle|Ethan|Mecanim_Bot|Mike)/(Animations|Model)/
"""
import argparse
import os
import re
import sys
import tarfile

FIX = 4294967296.0  # UFE's FPLibrary Fix64: raw 32.32 fixed point

BUTTONS = ["Forward", "Back", "Up", "Down", "Button1", "Button2", "Button3", "Button4", "Button5", "Button6", "Button7",
           "Button8", "Button9", "Button10", "Button11", "Button12", "DownBack", "DownForward", "UpForward", "UpBack", "Start"]
HIT_TYPES = ["Mid", "Low", "High", "Launcher", "HighKnockdown", "MidKnockdown", "KnockBack", "Sweep", "MidRight", "MidLeft"]
HIT_STRENGTH = ["Weak", "Medium", "Heavy", "Crumple", "Custom1", "Custom2", "Custom3", "Custom4", "Custom5", "Custom6"]


def entries(pkg):
    """guid -> (path, asset bytes or None) for the whole package (two passes: the tar is streamed)."""
    paths, data = {}, {}
    with tarfile.open(pkg, "r:gz") as tf:
        for m in tf:
            name = m.name[2:] if m.name.startswith("./") else m.name
            parts = name.split("/")
            if len(parts) != 2:
                continue
            if parts[1] == "pathname":
                paths[parts[0]] = tf.extractfile(m).read().decode("utf-8", "replace").splitlines()[0].strip()
    return paths


def read_assets(pkg, wanted):
    """guid -> asset bytes for the guids in wanted."""
    out = {}
    with tarfile.open(pkg, "r:gz") as tf:
        for m in tf:
            name = m.name[2:] if m.name.startswith("./") else m.name
            parts = name.split("/")
            if len(parts) == 2 and parts[1] == "asset" and parts[0] in wanted:
                out[parts[0]] = tf.extractfile(m).read()
    return out


def cmd_list(a):
    paths = entries(a.package)
    rx = re.compile(a.grep) if a.grep else None
    for g, p in sorted(paths.items(), key=lambda kv: kv[1]):
        if rx is None or rx.search(p):
            print(p)


def cmd_extract(a):
    rx = [re.compile(r) for r in a.regex]
    paths = entries(a.package)
    want = {g: p for g, p in paths.items() if any(r.search(p) for r in rx)}
    by_path = {p: g for g, p in paths.items()}
    for p in list(want.values()):
        d = p.rsplit("/", 1)[0]
        while "/" in d:
            if d in by_path:
                want.setdefault(by_path[d], d)
            d = d.rsplit("/", 1)[0]
    n = 0
    with tarfile.open(a.package, "r:gz") as tf:
        for m in tf:
            name = m.name[2:] if m.name.startswith("./") else m.name
            parts = name.split("/")
            if len(parts) != 2 or parts[0] not in want or parts[1] not in ("asset", "asset.meta"):
                continue
            dst = os.path.join(a.out_root, *want[parts[0]].split("/")) + (".meta" if parts[1] == "asset.meta" else "")
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, "wb") as f:
                f.write(tf.extractfile(m).read())
            n += 1
    for p in want.values():
        d = os.path.join(a.out_root, *p.split("/"))
        if not os.path.splitext(p)[1]:
            os.makedirs(d, exist_ok=True)
    print(f"{n} files ({len(want)} entries) -> {a.out_root}")


# ---- move lists

def fix(text, key):
    """The first Fix64 field `key:` + `_serializedValue: n` after it, as a float (None when missing)."""
    m = re.search(r"\n\s*" + re.escape(key) + r":\s*\n\s*_serializedValue: (-?\d+)", text)
    return int(m.group(1)) / FIX if m else None


def field(text, key, default=None):
    m = re.search(r"\n  " + re.escape(key) + r": ?(.*)", text)
    return m.group(1).strip() if m else default


def guid_of(ref):
    m = re.search(r"guid: ([0-9a-f]{32})", ref or "")
    return m.group(1) if m else None


def buttons(hexs):
    """UFE stores a ButtonPress list as little-endian int32s in hex."""
    hexs = (hexs or "").strip()
    out = []
    for i in range(0, len(hexs) - 7, 8):
        v = int.from_bytes(bytes.fromhex(hexs[i:i + 8]), "little")
        out.append(BUTTONS[v] if v < len(BUTTONS) else str(v))
    return out


def basic_moves(text):
    """basicMoves: name -> ([clip guids], speed)."""
    block = "\n" + text.split("\n  basicMoves:\n", 1)[1].split("\n  attackMoves:", 1)[0]
    out = {}
    for m in re.finditer(r"\n    (\w+):\n(.*?)(?=\n    \w+:\n|\Z)", block, re.S):
        name, body = m.group(1), m.group(2)
        clips = [g for g in re.findall(r"clip: \{fileID: \d+, guid: ([0-9a-f]{32})", body)]
        speed = fix(body, "_animationSpeed")
        out[name] = (clips, speed)
    return out


def attack_moves(text):
    block = text.split("\n  attackMoves:", 1)[1]
    block = re.split(r"\n  [a-zA-Z_]+:", block, 1)[0]
    return re.findall(r"guid: ([0-9a-f]{32})", block)


def move_info(text):
    hits = []
    hb = text.split("\n  hits:\n", 1)
    if len(hb) == 2:
        body = re.split(r"\n  [a-zA-Z_]+:", hb[1], 1)[0]
        for h in re.split(r"\n  - activeFramesBegin: ", "\n" + body)[1:]:
            begin = int(h.split("\n", 1)[0])
            end = int(re.search(r"activeFramesEnds: (-?\d+)", h).group(1))
            ht = re.search(r"\n    hitType: (\d+)", h)
            hs = re.search(r"\n    hitStrength: (\d+)", h)
            dmg = re.search(r"deltaAdjustment:\s*\n\s*_serializedValue: (-?\d+)", h)
            stun = re.search(r"hitStunOnHit:\s*\n\s*_serializedValue: (-?\d+)", h)
            bstun = re.search(r"hitStunOnBlock:\s*\n\s*_serializedValue: (-?\d+)", h)
            hits.append({
                "frames": f"{begin}-{end}",
                "type": HIT_TYPES[int(ht.group(1))] if ht else "",
                "strength": HIT_STRENGTH[int(hs.group(1))] if hs else "",
                "damage": round(int(dmg.group(1)) / FIX, 1) if dmg else None,
                "hitstun": round(int(stun.group(1)) / FIX, 1) if stun else None,
                "blockstun": round(int(bstun.group(1)) / FIX, 1) if bstun else None,
            })
    inputs = text.split("\n  defaultInputs:\n", 1)[1].split("\n  altInputs:", 1)[0]
    seq = re.search(r"buttonSequence: ?(\w*)", inputs)
    exe = re.search(r"buttonExecution: ?(\w*)", inputs)
    return {
        "name": field(text, "moveName", ""),
        "clip": guid_of(text.split("\n  animData:", 1)[1][:300]) if "\n  animData:" in text else None,
        "speed": fix(text, "_animationSpeed"),
        "total": field(text, "totalFrames"), "startup": field(text, "startUpFrames"),
        "active": field(text, "activeFrames"), "recovery": field(text, "recoveryFrames"),
        "sequence": buttons(seq.group(1) if seq else ""), "execution": buttons(exe.group(1) if exe else ""),
        "hits": hits,
    }


def cmd_moves(a):
    paths = entries(a.package)
    sets = {g: p for g, p in paths.items() if re.search(r"/MoveSets/[^/]+\.asset$", p) and not p.endswith("_maps.asset")}
    data = read_assets(a.package, set(sets))
    moves_needed = set()
    parsed = {}
    for g, p in sets.items():
        text = data[g].decode("utf-8", "replace").replace("\r\n", "\n")
        if "\n  basicMoves:\n" not in text:
            print("not a move set:", p, file=sys.stderr)
            continue
        parsed[g] = (basic_moves(text), attack_moves(text))
        moves_needed.update(parsed[g][1])
    infos = {g: move_info(t.decode("utf-8", "replace").replace("\r\n", "\n")) for g, t in read_assets(a.package, moves_needed).items()}

    def clip_name(g):
        p = paths.get(g or "", "")
        return p.split("/Characters/", 1)[-1] if p else ""

    rows = ["set\tkind\tmove\tclip\tspeed\tframes total/startup/active/recovery\tinput\thits (frames type strength damage hitstun blockstun)"]
    for g, p in sorted(sets.items(), key=lambda kv: kv[1]):
        if g not in parsed:
            continue
        name = p.split("/Resources/", 1)[-1].replace("/MoveSets/", "/").rsplit(".", 1)[0]
        basic, attacks = parsed[g]
        for k, (clips, speed) in basic.items():
            if clips:
                rows.append(f"{name}\tbasic\t{k}\t{' | '.join(clip_name(c) for c in clips)}\t{speed if speed is not None else ''}\t\t\t")
        for mg in attacks:
            m = infos.get(mg)
            if m is None:
                continue
            frames = f"{m['total']}/{m['startup']}/{m['active']}/{m['recovery']}"
            inp = " ".join(m["sequence"]) + (" + " if m["sequence"] and m["execution"] else "") + "+".join(m["execution"])
            hits = "; ".join(f"{h['frames']} {h['type']} {h['strength']} {h['damage']} {h['hitstun']} {h['blockstun']}" for h in m["hits"])
            rows.append(f"{name}\tattack\t{m['name']}\t{clip_name(m['clip'])}\t{m['speed']}\t{frames}\t{inp}\t{hits}")
    text = "\n".join(rows) + "\n"
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, "w", encoding="utf-8") as f:
            f.write(text)
        print(f"{len(rows) - 1} rows -> {a.out}")
    else:
        sys.stdout.write(text)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("list")
    p.add_argument("package")
    p.add_argument("--grep")
    p.set_defaults(func=cmd_list)
    p = sub.add_parser("extract")
    p.add_argument("package")
    p.add_argument("out_root")
    p.add_argument("regex", nargs="+")
    p.set_defaults(func=cmd_extract)
    p = sub.add_parser("moves")
    p.add_argument("package")
    p.add_argument("--out")
    p.set_defaults(func=cmd_moves)
    a = ap.parse_args()
    a.func(a)


if __name__ == "__main__":
    main()
