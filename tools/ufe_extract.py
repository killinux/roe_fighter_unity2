"""Copy assets out of a .unitypackage, with everything they reference inside the package (by GUID), keeping their paths
and their .meta (GUIDs) - e.g. UFE 2's hit effects, which the project did not take when it took UFE's animations.
The package is read only; the files go under the project (Assets/UFE is gitignored: Asset Store EULA, personal use).

    python tools/ufe_extract.py <package.unitypackage> <project dir> <asset path or prefix> [...] [--list]
    python tools/ufe_extract.py "E:/Downloads/Universal Fighting Engine 2 Source v2.7.0a.unitypackage" . Assets/UFE/Demos/Shared_Assets/Particles/

A .unitypackage is a gzipped tar: one folder per asset, named by its GUID, with "pathname" (the project path), "asset"
(the file) and "asset.meta".  References are "guid: <32 hex>" in the YAML of prefabs, materials, controllers; a GUID
that is not in the package (Unity's built-in resources, e.g. its default particle material) is reported and left out.
"""
import os
import re
import sys
import tarfile

GUID = re.compile(rb"guid: ([0-9a-f]{32})")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    listing = "--list" in sys.argv
    package, project, wanted = args[0], args[1], args[2:]
    entries = {}
    with tarfile.open(package, "r:gz") as t:
        for m in t:
            if not m.isfile():
                continue
            guid, _, part = m.name.partition("/")
            if part in ("pathname", "asset", "asset.meta"):
                entries.setdefault(guid, {})[part] = t.extractfile(m).read()
    path_of = {g: e["pathname"].decode("utf-8", "replace").splitlines()[0] for g, e in entries.items() if "pathname" in e}
    start = [g for g, p in path_of.items() if any(p == w or p.startswith(w) for w in wanted) and "asset" in entries[g]]
    todo, done, outside = list(start), set(), set()
    while todo:
        g = todo.pop()
        if g in done:
            continue
        done.add(g)
        data = entries[g].get("asset", b"")
        if path_of[g].endswith((".prefab", ".mat", ".controller", ".asset", ".anim", ".overrideController", ".physicMaterial")):
            for ref in GUID.findall(data):
                r = ref.decode()
                if r in entries and "asset" in entries[r]:
                    todo.append(r)
                elif r not in entries:
                    outside.add(r)
    for g in sorted(done, key=lambda x: path_of[x]):
        p = path_of[g]
        print(("  " if g in start else "  + ") + p)
        if listing:
            continue
        dst = os.path.join(project, p)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "wb") as f:
            f.write(entries[g]["asset"])
        if "asset.meta" in entries[g]:
            with open(dst + ".meta", "wb") as f:
                f.write(entries[g]["asset.meta"])
    print(f"{len(start)} asked, {len(done)} with what they use{' (listed only)' if listing else ' written'}; "
          f"{len(outside)} references outside the package (built-in): {', '.join(sorted(outside)) or '-'}")


if __name__ == "__main__":
    main()
