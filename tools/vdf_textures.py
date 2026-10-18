"""The Unity maps of a Vindictus character exported by tools/vdf_fbx.py, from its materials.json (what each Blender
material does) - so URP Lit shows what the .blend shows:

    python tools/vdf_textures.py Assets/VDF/<id> [--size 2048]

  - albedo: the colour map with the material's Hue/Saturation/Value and multiply tint applied the way Blender's nodes
    do it (linear colour; HSV saturation clamped to 0..1), or the hair's root / mid / tip ramp over the FR map's blue,
    or a constant colour; alpha from where the material takes it (the colour map's own, an ODI map's red, times a gain);
  - normal: the game's DirectX normal map with green flipped (Unity wants OpenGL), when the .blend flips it;
  - mask: URP Lit's metallic map from the ARM map - R metallic (ARM blue), G occlusion (ARM red, URP's occlusion map
    reads green), A smoothness (1 - ARM green);
  - unity.json: one flat entry per material (JsonUtility reads lists, not dictionaries) for VdfFighter.Build:
    surface (opaque / cutout / transparent), cutoff, cull, smoothness, metallic, colour, the maps.
Maps larger than --size are scaled down (Lanczos).  Source maps are read where materials.json points (the archived
.blend's textures folder) - nothing is written there.
"""
import argparse
import json
import os

import numpy as np
from PIL import Image


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055)


def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = np.max(rgb, axis=-1)
    mn = np.min(rgb, axis=-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-8
    rc = np.where(nz, (mx - r) / np.where(nz, d, 1), 0)
    gc = np.where(nz, (mx - g) / np.where(nz, d, 1), 0)
    bc = np.where(nz, (mx - b) / np.where(nz, d, 1), 0)
    h = np.where(r == mx, bc - gc, np.where(g == mx, 2.0 + rc - bc, 4.0 + gc - rc))
    h = np.where(nz, (h / 6.0) % 1.0, 0.0)
    s = np.where(mx > 1e-8, d / np.where(mx > 1e-8, mx, 1), 0.0)
    return np.stack([h, s, mx], axis=-1)


def hsv_to_rgb(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = v * (1.0 - s)
    q = v * (1.0 - s * f)
    t = v * (1.0 - s * (1.0 - f))
    i = i.astype(np.int64) % 6
    r = np.choose(i, [v, q, p, p, t, v])
    g = np.choose(i, [t, v, v, q, p, p])
    b = np.choose(i, [p, p, t, v, v, q])
    return np.stack([r, g, b], axis=-1)


def load(path, size, mode="RGB"):
    """The map scaled down to size.  mode RGB for data maps (ARM, normals, ODI, FR): their alpha must go first - Pillow
    resizes RGBA premultiplied, and the ARM maps' unused alpha is ~0, which wiped occlusion, roughness and metallic."""
    im = Image.open(path)
    im.load()
    im = im.convert(mode)
    if max(im.size) > size:
        im = im.resize((size, size) if im.size[0] == im.size[1] else
                       (round(im.size[0] * size / max(im.size)), round(im.size[1] * size / max(im.size))), Image.LANCZOS)
    return im


def channel(im, name):
    im = im.convert("RGBA")
    return np.asarray(im, dtype=np.float32)[..., "RGBA".index(name)] / 255.0


def save_rgba(rgb_srgb, alpha, path):
    a = np.ones(rgb_srgb.shape[:2], np.float32) if alpha is None else alpha
    if a.shape != rgb_srgb.shape[:2]:
        a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize(rgb_srgb.shape[1::-1], Image.LANCZOS), np.float32) / 255.0
    out = np.dstack([rgb_srgb, a])
    Image.fromarray(np.round(np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").save(path, optimize=False)


def ramp_eval(ramp, x):
    """Blender ColorRamp, linear interpolation in RGB (linear colour)."""
    pos = np.array([p for p, _ in ramp], np.float32)
    cols = np.array([c[:3] for _, c in ramp], np.float32)
    out = np.empty(x.shape + (3,), np.float32)
    for k in range(3):
        out[..., k] = np.interp(x, pos, cols[:, k])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--size", type=int, default=2048)
    a = ap.parse_args()
    side = json.load(open(os.path.join(a.folder, "materials.json"), encoding="utf-8"))
    tex = os.path.join(a.folder, "textures")
    os.makedirs(tex, exist_ok=True)
    entries = []
    for name, d in side["materials"].items():
        kind = d.get("kind", "pbr")
        e = {"name": name, "kind": kind, "albedo": "", "normal": "", "mask": "", "color": [1, 1, 1, 1],
             "smoothness": round(1.0 - float(d.get("roughness", 0.5)), 4), "metallic": float(d.get("metallic", 0.0)),
             "normalScale": float(d.get("normal_strength", 1.0)), "cutoff": float(d.get("clip", 0.5)),
             "surface": "opaque", "cull": "back"}
        stem = name.replace(" ", "_")
        alpha = None
        rgb = None
        if d.get("albedo") and os.path.exists(d["albedo"]):
            im = load(d["albedo"], a.size, "RGBA")
            arr = np.asarray(im, np.float32) / 255.0
            lin = srgb_to_linear(arr[..., :3])
            if d.get("hsv"):
                hue, sat, val = d["hsv"]
                hsv = rgb_to_hsv(lin)
                hsv[..., 0] = (hsv[..., 0] + hue + 0.5) % 1.0
                hsv[..., 1] = np.clip(hsv[..., 1] * sat, 0.0, 1.0)
                hsv[..., 2] = hsv[..., 2] * val
                lin = hsv_to_rgb(hsv)
            if d.get("tint"):
                lin = lin * np.array(d["tint"][:3], np.float32)
            rgb = linear_to_srgb(lin)
            if d.get("alpha_map") == d["albedo"] and d.get("alpha_channel") == "A":
                alpha = arr[..., 3] * float(d.get("alpha_gain", 1.0))
        elif d.get("ramp"):
            fr = load(d["ramp_map"], a.size) if d.get("ramp_map") else None
            x = channel(fr, d.get("ramp_channel", "B")) if fr is not None else np.full((8, 8), d.get("ramp_fac", 0.5), np.float32)
            rgb = linear_to_srgb(ramp_eval(d["ramp"], x))
        elif d.get("color") and d.get("alpha_map"):
            # a constant colour cut out by a map (brows, lashes, the reflection card)
            m = load(d["alpha_map"], a.size, "RGBA" if d.get("alpha_channel") == "A" else "RGB")
            alpha = channel(m, d.get("alpha_channel", "R")) * float(d.get("alpha_gain", 1.0))
            rgb = np.broadcast_to(linear_to_srgb(np.array(d["color"][:3], np.float32)), alpha.shape + (3,)).copy()
        if alpha is None and d.get("alpha_map") and os.path.exists(d["alpha_map"]) and rgb is not None:
            m = load(d["alpha_map"], a.size, "RGBA" if d.get("alpha_channel") == "A" else "RGB")
            alpha = channel(m, d.get("alpha_channel", "R")) * float(d.get("alpha_gain", 1.0))
        if rgb is not None:
            path = os.path.join(tex, f"{stem}_albedo.png")
            save_rgba(rgb, alpha, path)
            e["albedo"] = os.path.basename(path)
        elif d.get("color"):
            c = linear_to_srgb(np.array(d["color"][:3], np.float32))
            e["color"] = [round(float(x), 4) for x in c] + [float(d.get("alpha", 1.0))]
        if "alpha" in d and rgb is None:
            e["color"][3] = float(d["alpha"])
        if d.get("normal") and os.path.exists(d["normal"]):
            n = np.asarray(load(d["normal"], a.size).convert("RGB"), np.uint8).copy()
            if d.get("normal_flip_green"):
                n[..., 1] = 255 - n[..., 1]
            path = os.path.join(tex, f"{stem}_normal.png")
            Image.fromarray(n, "RGB").save(path)
            e["normal"] = os.path.basename(path)
        if d.get("arm") and os.path.exists(d["arm"]):
            arm = np.asarray(load(d["arm"], a.size).convert("RGB"), np.float32) / 255.0
            mask = np.dstack([arm[..., 2], arm[..., 0], np.zeros_like(arm[..., 0]), 1.0 - arm[..., 1]])
            path = os.path.join(tex, f"{stem}_mask.png")
            Image.fromarray(np.round(mask * 255).astype(np.uint8), "RGBA").save(path)
            e["mask"] = os.path.basename(path)
            e["smoothness"] = 1.0
            e["metallic"] = 1.0
        # how Unity draws it
        blend = d.get("blend", "OPAQUE")
        if kind in ("eye-occlusion", "eye-wet"):
            e["surface"] = "transparent"
        elif blend in ("CLIP", "HASHED") or kind in ("hair", "brow"):
            e["surface"] = "cutout"
        elif blend == "BLEND":
            e["surface"] = "transparent"
        if kind in ("hair", "brow"):
            e["cutoff"] = 0.35 if kind == "hair" else 0.3
        e["cull"] = "off" if kind in ("pbr", "hair", "brow") else "back"
        entries.append(e)
        print(f"{name}: {kind} {e['surface']} cull {e['cull']} albedo {e['albedo'] or e['color']} normal {e['normal'] or '-'} mask {e['mask'] or '-'}")
    with open(os.path.join(a.folder, "unity.json"), "w", encoding="utf-8") as f:
        json.dump({"id": side["id"], "fbx": side["fbx"], "materials": entries}, f, indent=1)
    print(f"{len(entries)} materials -> {os.path.join(a.folder, 'unity.json')}")


if __name__ == "__main__":
    main()
