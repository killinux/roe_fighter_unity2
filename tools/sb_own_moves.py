"""
Eve's own Stellar Blade moves for another of her outfits (user 10-17: "换" - eve09 and eve37 as eve_nikke06 got them):

    python tools/sb_own_moves.py eve09 eve37

eve_nikke06's set is the template: tools/motionpacks/sb_eve.json (her four sword strikes, her lock-on stance, walks and run)
and the game clips and specials of tools/sb/eve_nikke06.json.  Per outfit this writes
  - tools/motionpacks/sb_<id>.json: the same pack on the outfit's own clips (Assets/SB/<id>/anims) - each needs its own copies:
    her clips key the sword's holder by its path from the fighter, which starts with her armature's name (the outfit's id);
  - tools/sb/<id>.json: that pack, her own stance (no motion pack stance), feet as her clips have them, the same game clips
    (Assets/SB/<id>/anims/inplace/...) and specials; name, outfit, physics tunes and portrait stay hers;
and prints the -roeOnly expression of the clips SbAnims.Import has to make for them (the pack's and the game clips).
Then: tools/sb_weapons.py Assets/SB/<id>, SbFighter.Build, SbAnims.Import -roeOnly ..., RoeMotionPacks.Import of the pack.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE_ID = "eve_nikke06"
PACK = os.path.join(HERE, "motionpacks", "sb_eve.json")


def main():
    ids = sys.argv[1:]
    if not ids:
        raise SystemExit(__doc__)
    pack = json.load(open(PACK, encoding="utf-8"))
    template = json.load(open(os.path.join(HERE, "sb", TEMPLATE_ID + ".json"), encoding="utf-8"))
    src = f"Assets/SB/{TEMPLATE_ID}/"
    for cid in ids:
        dst = f"Assets/SB/{cid}/"
        p = json.loads(json.dumps(pack).replace(src, dst))
        p["name"] = f"sb_{cid}"
        p["measureOn"] = cid
        p["note"] = (f"tools/sb_own_moves.py: eve_nikke06's pack (tools/motionpacks/sb_eve.json) on {cid}'s own clips "
                     f"(the sword holder's path starts with her armature's name). " + pack["note"])
        json.dump(p, open(os.path.join(HERE, "motionpacks", f"sb_{cid}.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
        path = os.path.join(HERE, "sb", cid + ".json")
        d = json.load(open(path, encoding="utf-8"))
        d["pack"] = p["name"]
        d["stance"] = ""
        d["feet"] = ""
        d["game"] = json.loads(json.dumps(template["game"]).replace(src, dst))
        d["skills"] = template["skills"]
        json.dump(d, open(path, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
        clips = sorted({os.path.splitext(os.path.basename(c))[0] for c in
                        [s["clip"] for s in p["strikes"]] + list(p["roles"].values()) + [g["clip"] for g in d["game"]]})
        print(f"{cid}: tools/motionpacks/sb_{cid}.json, tools/sb/{cid}.json; {len(clips)} clips")
    print("-roeOnly '^(" + "|".join(re.escape(c) for c in clips) + ")$'")


if __name__ == "__main__":
    main()
