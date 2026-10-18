"""Decode KawaiiPhysics settings out of Vindictus: Defying Fate cooked Zen packages (no .usmap).

Inputs: raw/<...>.uasset + .zen.json written by zen_dump.py.
What is decoded (layout worked out by hand from the bytes; unversioned order = derived struct first, then super;
nested structs are dense with a zero mask; deprecated UPROPERTYs are skipped):
  * KawaiiPhysicsLimitsDataAsset: Spherical/Capsule/Planar limit arrays (driving bone, offsets, radius, length).
  * CurveFloat: rich-curve keys.
  * Anim BP ExecuteUbergraph bytecode: K2Node_MakeStruct_KawaiiPhysicsSettings literals and which
    AnimGraphNode_KawaiiPhysics_N each struct is assigned to (= the PhysicsSettings the node actually uses).
  * Anim BP CDO: per node RootBone, ExcludeBones, CurveData keys / external curve, inline limits,
    LimitsDataAsset and a few scalars (indices whose names are uncertain are reported as p<index>).
"""
import glob
import json
import struct
import sys

UNK = object()


class R:
    def __init__(self, d, pos=0):
        self.d, self.p = d, pos

    def u8(self):
        v = self.d[self.p]; self.p += 1; return v

    def u16(self):
        v, = struct.unpack_from("<H", self.d, self.p); self.p += 2; return v

    def i32(self):
        v, = struct.unpack_from("<i", self.d, self.p); self.p += 4; return v

    def u32(self):
        v, = struct.unpack_from("<I", self.d, self.p); self.p += 4; return v

    def f32(self):
        v, = struct.unpack_from("<f", self.d, self.p); self.p += 4; return round(v, 6)

    def f64(self, n=1):
        v = struct.unpack_from("<%dd" % n, self.d, self.p); self.p += 8 * n; return [round(x, 5) for x in v]


def header(r):
    """Unversioned header -> list of (prop_index, is_zero)."""
    frags = []
    while True:
        p = r.u16()
        skip, hz, last, num = p & 0x7F, bool(p & 0x80), bool(p & 0x100), p >> 9
        frags.append((skip, hz, num))
        if last:
            break
    nz = sum(n for _, hz, n in frags if hz)
    mask = 0
    if nz:
        if nz <= 8:
            mask = r.u8()
        elif nz <= 16:
            mask = r.u16()
        else:
            words = (nz + 31) // 32
            for w in range(words):
                mask |= r.u32() << (32 * w)
    out, idx, bit = [], 0, 0
    for skip, hz, num in frags:
        idx += skip
        for _ in range(num):
            z = False
            if hz:
                z = bool(mask >> bit & 1)
                bit += 1
            out.append((idx, z))
            idx += 1
    return out


class Ctx:
    def __init__(self, z):
        self.names = z["names"]
        self.imports = z["imports"]
        self.exports = z["exports"]

    def name(self, r):
        i, n = r.u32(), r.u32()
        s = self.names[i] if i < len(self.names) else "?%d" % i
        return s + ("_%d" % (n - 1) if n else "")

    def obj(self, r):
        i = r.i32()
        if i < 0:
            imp = self.imports[-i - 1]
            return imp.get("package") or imp.get("name") or "import%d" % (-i - 1)
        if i > 0:
            return "export:" + self.exports[i - 1]["name"]
        return None


# ---- struct schemas: list of (name, reader) in unversioned (derived-first) order
def rd_struct(schema):
    def f(r, c):
        h = header(r)
        out = {}
        for idx, zero in h:
            nm, fn = schema[idx] if idx < len(schema) else ("p%d" % idx, None)
            if zero:
                out[nm] = 0
                continue
            if fn is None:
                raise ValueError("unknown non-zero field %s at %d" % (nm, r.p))
            out[nm] = fn(r, c)
        return out
    return f


def rd_arr(inner):
    return lambda r, c: [inner(r, c) for _ in range(r.i32())]


f32 = lambda r, c: r.f32()  # noqa: E731
i32 = lambda r, c: r.i32()  # noqa: E731
u8 = lambda r, c: r.u8()  # noqa: E731
vec = lambda r, c: r.f64(3)  # noqa: E731
quat = lambda r, c: r.f64(4)  # noqa: E731
plane = lambda r, c: r.f64(4)  # noqa: E731
fname = lambda r, c: c.name(r)  # noqa: E731
obj = lambda r, c: c.obj(r)  # noqa: E731
empty = lambda r, c: None  # noqa: E731

BoneRef = rd_struct([("BoneName", fname)])
LIMIT_BASE = [("DrivingBone", BoneRef), ("OffsetLocation", vec), ("OffsetRotation_PYR", vec), ("Location", vec),
              ("Rotation_xyzw", quat), ("bEnable", u8)]
Sphere = rd_struct([("Radius", f32), ("LimitType", u8)] + LIMIT_BASE)
Capsule = rd_struct([("Radius", f32), ("Length", f32)] + LIMIT_BASE)
Planar = rd_struct([("Plane", plane)] + LIMIT_BASE)


def rich_key(r, c):
    im, tm, tw = r.u8(), r.u8(), r.u8()
    t, v, at, atw, lt, ltw = (r.f32() for _ in range(6))
    return {"interp": ["linear", "constant", "cubic", "none"][im] if im < 4 else im, "time": t, "value": v,
            "arrive": at, "leave": lt}


RichCurve = rd_struct([("Keys", rd_arr(rich_key)), ("DefaultValue", f32), ("PreInfinityExtrap", u8),
                       ("PostInfinityExtrap", u8), ("KeyHandlesToIndices", empty)])
RuntimeCurve = rd_struct([("EditorCurveData", RichCurve), ("ExternalCurve", obj)])
Settings = rd_struct([("Damping", f32), ("WorldDampingLocation", f32), ("WorldDampingRotation", f32),
                      ("Stiffness", f32), ("Radius", f32), ("LimitAngle", f32)])

# FAnimNode_KawaiiPhysics own properties 0..45 (index -> name, reader); names marked ? are inferred
NODE = [None] * 46
NODE[0] = ("RootBone", BoneRef)
NODE[1] = ("ExcludeBones", rd_arr(BoneRef))
NODE[2] = ("TargetFramerate?", i32)
NODE[3] = ("p3_OverrideTargetFramerate?", u8)
NODE[4] = ("p4_DummyBoneLength?", f32)
NODE[5] = ("p5_BoneForwardAxis?", u8)
NODE[6] = ("PhysicsSettings(CDO default)", Settings)
for k, nm in enumerate(["DampingCurve", "WorldDampingLocationCurve", "WorldDampingRotationCurve", "StiffnessCurve",
                        "RadiusCurve", "LimitAngleCurve"]):
    NODE[7 + k] = (nm + "(deprecated ptr)", obj)
    NODE[13 + k] = (nm + "Data", RuntimeCurve)
NODE[19] = ("p19_bool", u8)
NODE[24] = ("SphericalLimits", rd_arr(Sphere))
NODE[25] = ("CapsuleLimits", rd_arr(Capsule))
NODE[26] = ("PlanarLimits", rd_arr(Planar))
NODE[27] = ("LimitsDataAsset", obj)
NODE[28] = ("SphericalLimitsData", rd_arr(Sphere))
NODE[29] = ("CapsuleLimitsData", rd_arr(Capsule))
NODE[30] = ("PlanarLimitsData", rd_arr(Planar))
NODE[33] = ("Gravity", vec)
NODE[34] = ("p34_bool(bEnableWind?)", u8)
NODE[35] = ("p35_float(WindScale?)", f32)
NODE[36] = ("bAllowWorldCollision?", u8)
NODE[37] = ("bOverrideCollisionParams?", u8)
NODE[38] = ("CollisionChannelSettings(FBodyInstance)", None)


def decode_node(r, c):
    h = header(r)
    out, raw_unknown = {}, []
    for idx, zero in h:
        ent = NODE[idx] if idx < len(NODE) else None
        nm = ent[0] if ent else "p%d" % idx
        if zero:
            out[nm] = 0
            continue
        if idx >= 38 or ent is None or ent[1] is None:
            # stop: unknown type / FBodyInstance; report which indices are present (non-zero) after this
            rest = [i for i, zz in h if i >= idx and not zz]
            out["_stopped_at_index"] = idx
            out["_nonzero_indices_from_here"] = rest
            out["_next_bytes"] = r.d[r.p:r.p + 48].hex(" ")
            break
        out[nm] = ent[1](r, c)
    return out


def class_props(c, d):
    """Own properties of a BlueprintGeneratedClass export: [(name, type, elem_size)] in stored order."""
    tn = {i for i, n in enumerate(c.names) if n.endswith("Property")}
    props, i = [], 0
    while i + 28 <= len(d):
        ti, tnum = struct.unpack_from("<II", d, i)
        if ti in tn and tnum == 0:
            ni, nn = struct.unpack_from("<II", d, i + 8)
            arr, el = struct.unpack_from("<ii", d, i + 20)
            if ni < len(c.names) and arr == 1 and 0 < el < 100000:
                props.append((c.names[ni] + ("_%d" % (nn - 1) if nn else ""), c.names[ti], el))
                i += 28
                continue
        i += 1
    return props


def bytecode_settings(c, d):
    """MakeStruct literal assignments + struct -> node assignment from ExecuteUbergraph bytecode."""
    makes, assign = {}, {}
    i = 0
    while i < len(d) - 40:
        if d[i] == 0x0F and d[i + 1:i + 5] == b"\x01\x00\x00\x00" and d[i + 17] == 0x42:
            r = R(d, i + 5)
            member = c.name(r); r.i32()
            r.p += 1 + 4
            c.name(r); r.i32()          # member again (struct member context)
            op = r.u8()
            if op in (0x00, 0x01, 0x48) and d[r.p:r.p + 4] == b"\x01\x00\x00\x00":
                r.p += 4
                var = c.name(r); r.i32()
                vop = r.u8()
                val = UNK
                if vop == 0x1E:
                    val = r.f32()
                elif vop == 0x37:
                    val = r.f64()[0]
                elif vop == 0x27:
                    val = True
                elif vop == 0x28:
                    val = False
                elif vop == 0x24:
                    val = r.u8()
                elif vop == 0x1D:
                    val = r.i32()
                elif vop == 0x23:
                    val = r.f64(3)
                elif vop in (0x00, 0x01) and d[r.p:r.p + 4] == b"\x01\x00\x00\x00":
                    r.p += 4
                    src = c.name(r); r.i32()
                    if var.startswith("AnimGraphNode_"):
                        assign.setdefault(var, {})[member] = src
                    else:
                        makes.setdefault(var, {})[member] = "var:" + src
                    i = r.p
                    continue
                if val is not UNK:
                    makes.setdefault(var, {})[member] = val
                else:
                    makes.setdefault(var, {})[member] = "expr opcode 0x%02x" % vop
                i = r.p
                continue
        i += 1
    return makes, assign


def load(pattern):
    f = glob.glob("raw/**/%s.zen.json" % pattern, recursive=True)[0]
    z = json.load(open(f, encoding="utf-8"))
    buf = open(f[:-9] + ".uasset", "rb").read()
    return z, buf


def export_data(z, buf, name):
    e = [e for e in z["exports"] if e["name"] == name][0]
    s = z["header_size"] + e["serial_offset"]
    return buf[s:s + e["serial_size"]]


def decode_da(name):
    z, buf = load(name)
    c = Ctx(z)
    r = R(export_data(z, buf, name))
    out = {}
    for idx, zero in header(r):
        nm = ["SphericalLimitsData", "CapsuleLimitsData", "PlanarLimitsData"][idx] if idx < 3 else "p%d" % idx
        out[nm] = [] if zero else rd_arr([Sphere, Capsule, Planar][idx])(r, c)
    return out


def decode_curve(name):
    z, buf = load(name)
    c = Ctx(z)
    r = R(export_data(z, buf, name))
    out = {}
    for idx, zero in header(r):
        if idx == 0 and not zero:
            out["FloatCurve"] = RichCurve(r, c)
        else:
            out["p%d" % idx] = 0 if zero else "?"
    return out


def find_nodes(c, d):
    """Locate FAnimNode_KawaiiPhysics blocks in a CDO by pattern: a header whose first values are
    RootBone (FBoneReference: 00 03 + FName) and PhysicsSettings at index 6."""
    hits = []
    for p in range(len(d) - 40):
        if d[p] == 0 and d[p + 1] == 0:      # an empty non-last fragment in front of the real header
            continue
        try:
            r = R(d, p)
            h = header(r)
        except Exception:  # noqa: BLE001
            continue
        idxs = [i for i, _ in h]
        if len(h) < 40 or idxs[0] != 0 or 6 not in idxs or not all(i in idxs for i in range(13, 19)) or max(idxs) > 60:
            continue
        if d[r.p:r.p + 2] != b"\x00\x03":
            continue
        ni = struct.unpack_from("<I", d, r.p + 2)[0]
        if ni >= len(c.names):
            continue
        hits.append(p)
    return hits


def decode_abp(name):
    z, buf = load(name)
    c = Ctx(z)
    cls = export_data(z, buf, name + "_C")
    props = class_props(c, cls)
    cdo = export_data(z, buf, "Default__" + name + "_C")
    nodes = []
    for p in find_nodes(c, cdo):
        r = R(cdo, p)
        n = decode_node(r, c)
        n["_cdo_offset"] = p
        nodes.append(n)
    ub = [e["name"] for e in z["exports"] if e["name"].startswith("ExecuteUbergraph")][0]
    makes, assign = bytecode_settings(c, export_data(z, buf, ub))
    return props, nodes, makes, assign


def skip_generic(r):
    """Skip a struct whose fields are all PoseLinks / simple small values (Root, Local<->Component)."""
    h = header(r)
    for idx, zero in h:
        if zero:
            continue
        # FPoseLink / FComponentSpacePoseLink: header + int32
        h2 = header(r)
        for _i, z2 in h2:
            if not z2:
                r.i32()


if __name__ == "__main__":
    print(json.dumps(decode_da(sys.argv[1]) if sys.argv[1].startswith(("DA_", "Fiona_Hair_Coll")) else
                     decode_curve(sys.argv[1]), indent=1))
