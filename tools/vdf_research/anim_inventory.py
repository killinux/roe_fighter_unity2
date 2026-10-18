"""Build Fiona's animation inventory from the zen headers dumped by zen_dump.py (anim_hdr/)."""
import collections
import glob
import json
import os
import re

ROOT = "anim_hdr"
CATS = [
    ("facial", r"/Facial/|facial|_face"),
    ("interaction / traversal", r"interactive|ladder|door|lever|narrow|climb|potion|drink|item|pickup|loot|campsite"),
    ("death", r"_die_|death|dead"),
    ("emote/dance", r"emo_|dance|cheering|gesture|followme"),
    ("get-up", r"getup|get_up|standup|stand_up|wakeup|rise"),
    ("hit reaction / damage", r"damage|_hit|hit_|stagger|heavystander|flinch|stun|knockback|airborne|jump_damage"),
    ("guard / parry / counter", r"guard|parry|counter|block"),
    ("attack / skill", r"attack|smash|skill|finish|charging|combo|strike|slash|speedy_move"),
    ("dodge / evade", r"dodge|evade|roll|step|avoid"),
    ("locomotion", r"walk|run|sprint|jump|fall|land|pivot|turn|slope|idle2|2idle|start|stop|move"),
    ("idle / rest", r"idle|rest|stand|wait|campsite"),
    ("interaction / traversal", r"interactive|ladder|door|lever|narrow|climb|potion|drink|item|pickup|loot"),
]


def category(path):
    low = path.lower()
    for name, rx in CATS:
        if re.search(rx, low):
            return name
    return "other"


def main():
    rows = []
    for f in sorted(glob.glob(os.path.join(ROOT, "**", "*.zen.json"), recursive=True)):
        z = json.load(open(f, encoding="utf-8"))
        path = z["path"].split("/Content/", 1)[-1][:-7]
        main_exp = [e for e in z["exports"] if e["name"] == os.path.basename(path)]
        cls = (main_exp[0]["class"] if main_exp else (z["exports"][0]["class"] if z["exports"] else "?")) or "?"
        cls = cls.split(".")[-1]
        if not ("/Fiona/" in path or "/Common/Animation" in path or "/Public/Animation" in path):
            continue
        refs = [p.split("/")[-1] for p in z["imported_packages"] if "/Animation/" in p or "/Sequences/" in p]
        skel = [p.split("/")[-1] for p in z["imported_packages"] if p.endswith("Skeleton")]
        slots = [n for n in z["names"] if n.lower().startswith(("defaultslot", "upperbody", "fullbody", "slot")) or "Slot" in n]
        rows.append({"path": path, "folder": path.split("/")[-2], "name": path.split("/")[-1], "class": cls,
                     "category": category(path), "size": z["size"], "skeleton": skel,
                     "montage_uses": refs if cls == "AnimMontage" else [],
                     "slots_or_names": slots[:6]})
    json.dump(rows, open("fiona_animations.json", "w", encoding="utf-8"), indent=1)
    by = collections.defaultdict(list)
    for r in rows:
        by[(r["folder"], r["class"])].append(r)
    with open("fiona_animations.txt", "w", encoding="utf-8") as fh:
        fh.write("Fiona animation inventory (Vindictus: Defying Fate 2024-03 pre-alpha), from cooked package headers\n")
        fh.write("Columns: category | class | name | (montage -> sequences it imports)\n\n")
        cnt = collections.Counter((r["folder"], r["class"]) for r in rows)
        for k, v in sorted(cnt.items()):
            fh.write("  %-12s %-16s %d\n" % (k[0], k[1], v))
        fh.write("\nBy category:\n")
        cc = collections.Counter(r["category"] for r in rows if r["folder"] != "Facial")
        for k, v in cc.most_common():
            fh.write("  %-26s %d\n" % (k, v))
        for (folder, cls), rs in sorted(by.items()):
            fh.write("\n== %s / %s (%d)\n" % (folder, cls, len(rs)))
            for r in sorted(rs, key=lambda r: (r["category"], r["name"].lower())):
                extra = (" -> " + ", ".join(r["montage_uses"])) if r["montage_uses"] else ""
                fh.write("  %-24s %s%s\n" % (r["category"], r["name"], extra))
    print(len(rows), "rows")
    for k, v in sorted(collections.Counter((r["folder"], r["class"]) for r in rows).items()):
        print(k, v)
    print(collections.Counter(r["category"] for r in rows if r["folder"] != "Facial").most_common())


if __name__ == "__main__":
    main()
