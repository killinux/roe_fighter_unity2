"""Put takes from several demo runs (RoeClothDemo / RoeClothPlayDemo folders) into one folder for tools/cloth_demo_video.py,
under new names - columns filmed in different runs side by side.

    python tools/merge_takes.py <out dir> <src dir>:<id>_<variant>=<new variant> ...
    python tools/merge_takes.py _work/a08_skills_all _work/a08_skills_keys:a08_off=keys _work/a08_skills_mc2:a08_magica=mc2_nokeys
"""
import os
import shutil
import sys

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
lines = []
script = None
for spec in sys.argv[2:]:
    src, rest = spec.rsplit(":", 1)
    take, new = rest.split("=")
    cid, variant = take.split("_", 1)
    dst = os.path.join(out, f"{cid}_{new}")
    if os.path.exists(dst):
        shutil.rmtree(dst)
    shutil.copytree(os.path.join(src, take), dst)
    for line in open(os.path.join(src, "takes.txt"), encoding="utf-8").read().splitlines():
        parts = line.split("\t")
        if len(parts) >= 2 and parts[0] == cid and parts[1] == variant:
            parts[1] = new
            lines.append("\t".join(parts))
    if script is None and os.path.exists(os.path.join(src, "script.txt")):
        script = open(os.path.join(src, "script.txt"), encoding="utf-8").read()
open(os.path.join(out, "takes.txt"), "w", encoding="utf-8").write("\n".join(lines))
if script:
    open(os.path.join(out, "script.txt"), "w", encoding="utf-8").write(script)
print(out, len(lines), "takes")
