"""
Fiona's sword and shield from Vindictus: Defying Fate for the fight: the maps Unity's URP Lit wants and a list for
VdfFighter (which reads the meshes from the .psk itself and hangs them on her weapon_r / shield_l bones).

    python tools/vdf_weapons.py [--id fio005] [--export]

  --export  first exports the two weapons with UE Viewer (spiritovod's UE5 build) into _work/vdf/weapons (the pak key is
            handed over as a file and never printed);
  then per weapon (SK_Longsword01 on weapon_r, SK_Shield01 on shield_l - the newer meshes; longsword / shield are the old
  Source-engine versions with ValveBiped attachment bones):
  - albedo: the BaseColor map (UE Viewer writes it as Radiance .hdr: BC6H) with the material's Basecolor Saturation /
    Brightness / Contrast (M_Mob_Base's scalar parameters) applied in linear colour, saved sRGB;
  - normal: the DirectX normal map with green flipped;
  - mask: URP Lit's metallic map from ARM - R metallic (ARM blue x Metallic Intensity), G occlusion (ARM red),
    A smoothness (1 - roughness, roughness = ARM green remapped to the material's Roughness Min..Max);
  - Assets/VDF/<id>/weapons/weapons.json for VdfFighter.AttachWeapons.
Game data: personal use only, never committed (Assets/VDF and _work are ignored).
"""
import argparse
import json
import os
import re
import subprocess
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "_work", "vdf", "weapons")
UMODEL = r"E:\tools\umodel_specific\materials\umodel_materials_ue5.exe"
PAKS = r"E:\tools\vindictus\Vindictus\Content\Paks"
KEYFILE = r"E:\tools\vindictus\_download\aes_key.txt"
WEAPONS = [
    {"name": "longsword", "package": "VindictusRoot/Character/Player/Fiona/Weapon/Longsword/SK_Longsword01", "socket": "weapon_r",
     "material": "VindictusRoot/Character/Player/Fiona/Weapon/Model/MI_LongSword_001"},
    {"name": "shield", "package": "VindictusRoot/Character/Player/Fiona/Weapon/Shield/SK_Shield01", "socket": "shield_l",
     "material": "VindictusRoot/Character/Player/Fiona/Weapon/Model/MI_Shield_001"},
]


def read_hdr(path):
    """Radiance RGBE (.hdr), flat or new-style run-length encoded scanlines -> float32 [h, w, 3] (linear)."""
    data = open(path, "rb").read()
    pos = 0
    while True:
        end = data.index(b"\n", pos)
        line = data[pos:end].decode("ascii", "replace")
        pos = end + 1
        if line.startswith("-Y") or line.startswith("+Y"):
            parts = line.split()
            h, w = int(parts[1]), int(parts[3])
            flip = line.startswith("+Y")
            break
    rgbe = np.zeros((h, w, 4), dtype=np.uint8)
    for y in range(h):
        if w >= 8 and w < 32768 and data[pos] == 2 and data[pos + 1] == 2 and (data[pos + 2] << 8 | data[pos + 3]) == w:
            pos += 4
            for c in range(4):
                x = 0
                while x < w:
                    n = data[pos]
                    pos += 1
                    if n > 128:
                        n -= 128
                        rgbe[y, x:x + n, c] = data[pos]
                        pos += 1
                    else:
                        rgbe[y, x:x + n, c] = np.frombuffer(data, dtype=np.uint8, count=n, offset=pos)
                        pos += n
                    x += n
        else:
            rgbe[y] = np.frombuffer(data, dtype=np.uint8, count=w * 4, offset=pos).reshape(w, 4)
            pos += w * 4
    if flip:
        rgbe = rgbe[::-1]
    e = rgbe[..., 3].astype(np.int32)
    scale = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0)
    return rgbe[..., :3].astype(np.float32) * scale[..., None].astype(np.float32)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055)


def scalars(props_path):
    """ScalarParameterValues of a UE Viewer .props.txt: name -> value."""
    out = {}
    text = open(props_path, encoding="utf-8", errors="replace").read()
    for m in re.finditer(r"Name=([^}]+?) \}\s*\n\s*ParameterValue = (-?[\d.eE+-]+)", text):
        out[m.group(1).strip()] = float(m.group(2))
    return out


def textures(props_path):
    """texture parameters: name -> texture file stem"""
    out = {}
    text = open(props_path, encoding="utf-8", errors="replace").read()
    for m in re.finditer(r"Name=([^}]+?) \}\s*\n\s*ParameterValue = Texture2D'[^']*\.([A-Za-z0-9_]+)'", text):
        out[m.group(1).strip()] = m.group(2)
    return out


def find(stem):
    for dirpath, _, files in os.walk(SRC):
        for f in files:
            if os.path.splitext(f)[0] == stem and f.lower().endswith((".png", ".hdr", ".tga")):
                return os.path.join(dirpath, f)
    raise SystemExit(f"no exported texture {stem} under {SRC} - run with --export")


def export():
    for w in WEAPONS:
        cmd = [UMODEL, "-game=ue5.3", f"-path={PAKS}", f"-aes=@{KEYFILE}", "-export", "-png", f"-out={SRC}", w["package"]]
        run = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
        print(w["name"], "exported" if "Exported" in run.stdout else "FAILED")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--id", default="fio005")
    ap.add_argument("--export", action="store_true")
    a = ap.parse_args()
    if a.export:
        export()
    out_dir = os.path.join(ROOT, "Assets", "VDF", a.id, "weapons")
    os.makedirs(out_dir, exist_ok=True)
    listing = []
    for w in WEAPONS:
        psk = os.path.join(SRC, *w["package"].split("/")) + ".psk"
        props = os.path.join(SRC, *w["material"].split("/")) + ".props.txt"
        if not os.path.exists(psk) or not os.path.exists(props):
            raise SystemExit(f"{w['name']}: no {psk} / {props} - run with --export")
        s = scalars(props)
        t = textures(props)
        # albedo (linear), Saturation / Brightness / Contrast of M_Mob_Base
        d_path = find(t["BaseColor / Opacity"])
        color = read_hdr(d_path) if d_path.lower().endswith(".hdr") else \
            ((np.asarray(Image.open(d_path).convert("RGB"), dtype=np.float32) / 255.0) ** 2.2)
        luma = (color * np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)).sum(-1, keepdims=True)
        color = luma + (color - luma) * s.get("Basecolor Saturation", 1.0)
        contrast = s.get("Basecolor Contrast", 1.0)
        if contrast != 1.0:
            color = np.power(np.clip(color, 0.0, None), contrast)
        color = color * s.get("Basecolor Brightness", 1.0)
        Image.fromarray((linear_to_srgb(color) * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(out_dir, f"{w['name']}_albedo.png"))
        # normal: DirectX -> OpenGL
        n = np.asarray(Image.open(find(t["Normal Map"])).convert("RGB")).copy()
        n[..., 1] = 255 - n[..., 1]
        Image.fromarray(n, "RGB").save(os.path.join(out_dir, f"{w['name']}_normal.png"))
        # mask from ARM
        arm = np.asarray(Image.open(find(t["ARM / E"])).convert("RGB"), dtype=np.float32) / 255.0
        lo, hi = s.get("Roughness Min", 0.0), s.get("Roughness Max", 1.0)
        rough = np.clip(lo + (hi - lo) * arm[..., 1], 0.0, 1.0)
        metal = np.clip(arm[..., 2] * s.get("Metallic Intensity", 1.0), 0.0, 1.0)
        mask = np.stack([metal, arm[..., 0], np.zeros_like(metal), 1.0 - rough], -1)
        Image.fromarray((mask * 255.0 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(out_dir, f"{w['name']}_mask.png"))
        listing.append({"name": w["name"], "socket": w["socket"], "psk": psk.replace("\\", "/"),
                        "albedo": f"{w['name']}_albedo.png", "normal": f"{w['name']}_normal.png", "mask": f"{w['name']}_mask.png",
                        "roughness": [round(lo, 3), round(hi, 3)], "saturation": s.get("Basecolor Saturation", 1.0),
                        "brightness": s.get("Basecolor Brightness", 1.0)})
        print(f"{w['name']}: {w['socket']}, albedo {color.shape[1]}x{color.shape[0]} mean sRGB "
              f"{(linear_to_srgb(color).reshape(-1, 3).mean(0) * 255).round().tolist()}, roughness {lo:.2f}..{hi:.2f}, "
              f"metallic mean {metal.mean():.2f}")
    with open(os.path.join(out_dir, "weapons.json"), "w", encoding="utf-8") as f:
        json.dump({"weapons": listing}, f, indent=1)
    print("->", out_dir)


if __name__ == "__main__":
    sys.exit(main())
