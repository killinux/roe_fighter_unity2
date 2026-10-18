"""
Eve's sword from Stellar Blade (CH_W_Sword_01, the Blood Edge) for the fight (user 10-17: "用 eve里自己的招式" - her moves
swing it): the maps URP Lit wants and a list for VdfFighter.AttachWeapons (SbFighter.Build), which reads the mesh from the
game's .psk itself and hangs it in her right hand.

    python tools/sb_weapons.py Assets/SB/<id>
    python tools/sb_weapons.py Assets/SB/raven --weapon SB/Content/Art/Character/Monster/CH_M_NA_53/CH_M_NA_53_Weapon \
        --maps SB/Content/Art/Character/Monster/CH_M_NA_53/Textures/CH_M_NA_53_Weapon_{A,N,ORM} --keep-all --socket-rotator -90,180,-180
        (10-18: Raven's long sword: the game hangs it on her skeleton's socket SC_Weapon - Bip001-Prop1 turned Pitch -90
        Yaw 180 Roll -180, which lays the blade (its root's z) along the prop bone's x, as Eve's; one slot, nothing dropped)

  - the mesh: CH_W_Sword_01.psk (CUE4Parse CLI, ActorX; exported here when missing into _work/sb/weapon), unfolded as the
    game shows it in a fight; its three slots: Blade_Body twice and Blade_Edge, the glowing edge the game keeps cut out
    (its instance's Opacity 0, Masked) until a skill lights it - left out;
  - where it hangs: the game keys her sword in every clip on Bip001-Prop1 (under Bip001, beside the hand: it follows the
    hand 6 cm off in her strikes and leaves it when a skill throws the sword), with the sword's own root bone (Weapon_Root,
    a quarter turn) on it - its blade lies along the prop bone's x.  Checked on the game's poses (FK of her clips): so it
    points ahead in her lock-on walk and her battle idle, grip 5-6 cm from her palm; the sword's component on the prop bone
    instead would point it into the floor.  A humanoid clip has no prop bone, so the sword hangs on a holder under her right
    hand where Bip001 Prop1 stands in her bind pose, and her own clips (SbAnims) move the holder as the game moves the prop
    bone against the hand;
  - maps (the master material CH_W_Sword_02's own, its instances override none): colour CH_W_Sword_02_D, normal _N
    (DirectX: green flipped), URP's mask from _M read as ORM (R occlusion 1, G roughness ~0.63, B metallic ~0.92).
Game data: personal use only, never committed (Assets/SB and _work are ignored).
"""
import argparse
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = r"E:\tools\cue4parse_cli_ff7\cue4parse.exe"
PAKS = r"D:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB\Content\Paks"
USMAP = r"E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap"
WEAPON_DIR = os.path.join(ROOT, "_work", "sb", "weapon")
TEX_DIR = os.path.join(ROOT, "_work", "sb", "tex")
SWORD = "SB/Content/Art/Character/Weapon/Sword/CH_W_Sword_01"


def cli(pattern, out):
    subprocess.run([CLI, "-i", PAKS, "-g", "GAME_StellarBlade", "-m", USMAP, "-o", out, "-y", "-p", pattern], capture_output=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("folder", help="Assets/SB/<id>")
    ap.add_argument("--weapon", default=f"{SWORD}/CH_W_Sword_01", help="the weapon's mesh package (default Eve's sword)")
    ap.add_argument("--maps", nargs=3, default=None, metavar=("COLOUR", "NORMAL", "ORM"),
                    help="its colour, normal and ORM maps' packages (default the sword's CH_W_Sword_02_D/_N/_M)")
    ap.add_argument("--keep-all", action="store_true", help="drop no material slot (the sword drops its edge, slot 1)")
    ap.add_argument("--socket-rotator", default="", help="pitch,yaw,roll of the game's socket on the prop bone")
    a = ap.parse_args()
    psk = os.path.join(WEAPON_DIR, *a.weapon.split("/")) + ".psk"
    if not os.path.exists(psk):
        cli(f"{a.weapon}.*", WEAPON_DIR)
    if a.maps:
        maps = dict(zip("DNM", (os.path.join(TEX_DIR, *m.split("/")) + ".png" for m in a.maps)))
        for m, path in zip(a.maps, maps.values()):
            if not os.path.exists(path):
                cli(f"{m}.*", TEX_DIR)
    else:
        tex = os.path.join(TEX_DIR, *SWORD.split("/"), "Textures")
        maps = {k: os.path.join(tex, f"CH_W_Sword_02_{k}.png") for k in ("D", "N", "M")}
        if not all(os.path.exists(p) for p in maps.values()):
            cli(f"{SWORD}/Textures/CH_W_Sword_02_*", TEX_DIR)
    missing = [p for p in [psk, *maps.values()] if not os.path.exists(p)]
    if missing:
        raise SystemExit(f"not exported: {missing}")
    out = os.path.join(a.folder, "weapons")
    os.makedirs(out, exist_ok=True)
    Image.open(maps["D"]).convert("RGB").save(os.path.join(out, "sword_albedo.png"))
    n = np.asarray(Image.open(maps["N"]).convert("RGB")).copy()
    n[..., 1] = 255 - n[..., 1]
    Image.fromarray(n, "RGB").save(os.path.join(out, "sword_normal.png"))
    orm = np.asarray(Image.open(maps["M"]).convert("RGB"), np.float32) / 255.0
    mask = np.stack([orm[..., 2], orm[..., 0], np.zeros_like(orm[..., 0]), 1.0 - orm[..., 1]], -1)
    Image.fromarray((mask * 255.0 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(out, "sword_mask.png"))
    entry = {
        "name": "sword", "socket": "Bip001 R Hand", "holder": "SB_Sword", "prop": "Bip001 Prop1", "rootTurn": not a.socket_rotator,
        "drop": [] if a.keep_all else [1], "psk": psk.replace("\\", "/"),
        "albedo": "sword_albedo.png", "normal": "sword_normal.png", "mask": "sword_mask.png"}
    if a.socket_rotator:
        entry["socketRotator"] = [float(x) for x in a.socket_rotator.split(",")]
    listing = {"weapons": [entry]}
    json.dump(listing, open(os.path.join(out, "weapons.json"), "w", encoding="utf-8"), indent=1)
    print(f"sword: roughness {orm[..., 1].mean():.2f}, metallic {orm[..., 2].mean():.2f} -> {out}")


if __name__ == "__main__":
    sys.exit(main())
