"""Stellar Blade (UE 4.26) Control Rigs from their CUE4Parse JSON exports: a readable listing, or a program RoeRigVM runs.

    python tools/sb_controlrig.py <ControlRig.json> [out.txt]           # disassemble
    python tools/sb_controlrig.py <ControlRig.json> --json program.json  # the program (sb_physics.py puts it in sbphysics.json)
    python tools/sb_controlrig.py _work/sb/research/json/SB/Content/Art/Character/PC/CH_P_EVE_37/CH_P_EVE_37_Skirt_CtlRig.json _work/sb/research/skirt_ctlrig.txt

The RigVM export (CUE4Parse reads it whole): FunctionNamesStorage (the rig units), ByteCodeStorage.Instructions (OpCode 101 =
Execute: a function and its arguments; 68 = Copy; 80 = Exit; the others are named below), and three memories - work
(MemoryType 0), literal (1) and external (2: the rig's own variables, the class's ChildProperties) - whose registers are
named "<node>.<pin>" and carry their value in "View" (structs as text, plain values as base64).  The work memory holds what
the rig computed the last time the editor ran it (every register agrees with its inputs), so a port can replay the rig on
those inputs and compare.  A register argument with RegisterOffset 65535 is the whole register; other offsets index
RegisterOffsets (a member path such as "Rotation" or "Translation.Z" inside the register's struct).  The class default
object holds the rig's own copy of the skeleton (Hierarchy.BoneHierarchy: each bone's local transform at its reference
pose).  Disassembly: each line the step, the unit, and per pin "name = value" for literals, "name" for work registers.

The program (RoeRigVM.Program): every register flattened to floats (a transform is rotation x y z w, translation x y z,
scale x y z; a quaternion 4; a rotator pitch yaw roll; a vector 3; a float or bool 1; a bone key none - its bone name),
work registers first, then the literals, then the variables; each instruction a unit and its arguments as triples
(register, first float, count; count -1 = the whole register); Copy's are the source's then the target's.
"""
import base64
import json
import re
import struct
import sys

OPS = {0: "Copy?", 68: "Copy", 101: "Execute", 102: "Zero", 103: "BoolFalse", 104: "BoolTrue", 105: "Increment", 106: "Decrement",
       107: "Equals", 108: "NotEquals", 109: "JumpAbsolute", 110: "JumpForward", 111: "JumpBackward", 112: "JumpAbsoluteIf",
       113: "JumpForwardIf", 114: "JumpBackwardIf", 115: "ChangeType", 116: "Exit"}

# struct kinds: their size in floats and their members (first float, count)
KINDS = {
    "Transform": ("transform", 10, {"Rotation": (0, 4), "Translation": (4, 3), "Scale3D": (7, 3)}),
    "Quat": ("quat", 4, {"X": (0, 1), "Y": (1, 1), "Z": (2, 1), "W": (3, 1)}),
    "Rotator": ("rotator", 3, {"Pitch": (0, 1), "Yaw": (1, 1), "Roll": (2, 1)}),
    "Vector": ("vector", 3, {"X": (0, 1), "Y": (1, 1), "Z": (2, 1)}),
    "RigElementKey": ("key", 0, {}),
    "CachedRigElement": ("cache", 0, {}),
    "ControlRigExecuteContext": ("context", 0, {}),
}
MEMBER_KIND = {"Rotation": "Quat", "Translation": "Vector", "Scale3D": "Vector"}


def value(reg):
    v = reg.get("View")
    if isinstance(v, list):
        return v[0] if len(v) == 1 else v
    if isinstance(v, str):
        try:
            raw = base64.b64decode(v)
        except Exception:
            return v
        if reg.get("ElementSize") == 4 and len(raw) == 4:
            f = struct.unpack("<f", raw)[0]
            i = struct.unpack("<i", raw)[0]
            return round(f, 6) if abs(f) < 1e7 and (f == 0 or abs(f) > 1e-12) else i
        if reg.get("ElementSize") == 1 and len(raw) == 1:
            return bool(raw[0])
        return raw.hex()
    return v


def load(src):
    d = json.load(open(src, encoding="utf-8-sig"))
    vm = next(e for e in d if e.get("Type") == "RigVM")
    cls = next((e for e in d if e.get("Type") == "ControlRigBlueprintGeneratedClass"), {})
    cdo = next((e for e in d if e.get("Name", "").startswith("Default__")), {})
    return d, vm, cls, cdo


def disassemble(src, out=None):
    d, vm, cls, _ = load(src)
    funcs = vm["FunctionNamesStorage"]
    mems = {0: vm["WorkMemoryStorage"], 1: vm["LiteralMemoryStorageOld"]}
    paths = {m: [o.get("CachedSegmentPath") for o in mem.get("RegisterOffsets", [])] for m, mem in mems.items()}
    # external memory (2): the rig's own variables, in the order the class declares them
    variables = [p["Name"] for p in cls.get("ChildProperties", [])]

    def arg(a):
        if a["MemoryType"] == 2:
            i = a["RegisterIndex"]
            return f"var[{variables[i] if i < len(variables) else i}]"
        mem = mems[a["MemoryType"]]
        reg = mem["Registers"][a["RegisterIndex"]]
        name = reg["Name"]
        off = a.get("RegisterOffset", 65535)
        if off != 65535 and off < len(paths[a["MemoryType"]]):
            name += "." + str(paths[a["MemoryType"]][off])
        if a["MemoryType"] == 1:
            return f"{name} = {value(reg)}"
        return name

    lines = []
    for k, ins in enumerate(vm["ByteCodeStorage"]["Instructions"]):
        op = ins.get("OpCode")
        if op == 101:
            f = funcs[ins["FunctionIndex"]].replace("FRigUnit_", "").replace("::Execute", "")
            lines.append(f"{k:4d} {f}(" + ", ".join(arg(a) for a in ins.get("Arguments", [])) + ")")
        elif op == 68:
            lines.append(f"{k:4d}   {arg(ins['Target'])} <- {arg(ins['Source'])}")
        else:
            lines.append(f"{k:4d} {OPS.get(op, op)} " + json.dumps({x: y for x, y in ins.items() if x != 'OpCode'}))
    text = "\n".join(lines)
    print(f"{len(funcs)} units, {len(lines)} instructions; work registers {len(mems[0]['Registers'])}, literals {len(mems[1]['Registers'])}")
    if out:
        open(out, "w", encoding="utf-8").write(text + "\n")
        print("->", out)
    else:
        print(text)


# ---- the program

def parse_struct(text):
    """UE's exported struct text "(A=1.0,B=(X=1,Y=2),Name=\"x\")" as nested dicts (numbers as floats)."""
    pos = 0

    def parse():
        nonlocal pos
        assert text[pos] == "("
        pos += 1
        out = {}
        while text[pos] != ")":
            m = re.match(r"\s*([A-Za-z0-9_]+)=", text[pos:])
            key = m.group(1)
            pos += m.end()
            if text[pos] == "(":
                out[key] = parse()
            elif text[pos] == '"':
                end = text.index('"', pos + 1)
                out[key] = text[pos + 1:end]
                pos = end + 1
            else:
                m = re.match(r"[^,)]*", text[pos:])
                raw = m.group(0)
                pos += m.end()
                try:
                    out[key] = float(raw)
                except ValueError:
                    out[key] = raw
            if text[pos] == ",":
                pos += 1
        pos += 1
        return out

    return parse() if isinstance(text, str) and text.startswith("(") else {}


def floats(kind, v):
    """A struct's value (parsed text, or a CUE4Parse property dict) as the program's floats."""
    def vec(x, default=0.0, keys="XYZ"):
        x = x or {}
        return [float(x.get(k, default)) for k in keys]
    if kind == "transform":
        return vec(v.get("Rotation"), 0.0, "XYZ") + [float((v.get("Rotation") or {}).get("W", 1.0))] + \
            vec(v.get("Translation")) + vec(v.get("Scale3D"), 1.0)
    if kind == "quat":
        return vec(v, 0.0, "XYZ") + [float(v.get("W", 1.0))]
    if kind == "rotator":
        return [float(v.get("Pitch", 0.0)), float(v.get("Yaw", 0.0)), float(v.get("Roll", 0.0))]
    if kind == "vector":
        return vec(v)
    return []


def register(reg, struct_paths, bone_name):
    out = {"name": reg["Name"]}
    if reg["Type"] == 3:
        struct_name = struct_paths[reg["ScriptStructIndex"]].rsplit(".", 1)[-1]
        if struct_name not in KINDS:
            raise SystemExit(f"register {reg['Name']}: struct {struct_name} not supported")
        kind = KINDS[struct_name][0]
        v = parse_struct(value(reg) or "")
        out["kind"] = kind
        if kind == "key":
            if v.get("Type") != "Bone":
                raise SystemExit(f"register {reg['Name']}: {v.get('Type')} elements not supported (bones only)")
            out["bone"] = bone_name(v.get("Name"))
        out["value"] = floats(kind, v)
    elif reg["Type"] == 0 and reg["ElementSize"] in (1, 4) and reg.get("ElementCount", 1) == 1:
        v = value(reg)
        out["kind"] = "bool" if reg["ElementSize"] == 1 else "float"
        out["value"] = [float(v) if not isinstance(v, str) else 0.0]
    else:
        raise SystemExit(f"register {reg['Name']}: type {reg['Type']} size {reg['ElementSize']} not supported")
    return out


def segment(o):
    """A member path (RegisterOffsets entry) as (first float, count) inside its register."""
    parent = (o.get("ParentScriptStruct") or {}).get("ObjectName", "").replace("Class'", "").rstrip("'")
    kind, first, count = parent, 0, None
    for p in o["CachedSegmentPath"].split("."):
        if kind not in KINDS or p not in KINDS[kind][2]:
            raise SystemExit(f"member {o['CachedSegmentPath']} of {parent} not supported")
        f, c = KINDS[kind][2][p]
        first += f
        count = c
        kind = MEMBER_KIND.get(p, "")
    return first, count


def q_mul(a, b):
    """Hamilton product of quaternions (x, y, z, w) - FQuat's operator*."""
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def q_rotate(q, v):
    x, y, z, w = q
    r = q_mul(q_mul(q, [v[0], v[1], v[2], 0.0]), [-x, -y, -z, w])
    return r[:3]


def ue_relative(a, b):
    """FTransform A * B.Inverse() for flat transforms (rotation xyzw, translation, scale): A in B's space."""
    if b is None:
        return a
    bq, bt, bs = b[:4], b[4:7], b[7:10]
    iq = [-bq[0], -bq[1], -bq[2], bq[3]]
    i_s = [1.0 / x if abs(x) > 1e-8 else 0.0 for x in bs]
    it = q_rotate(iq, [-i_s[k] * bt[k] for k in range(3)])
    aq, at, as_ = a[:4], a[4:7], a[7:10]
    q = q_mul(iq, aq)
    t = [x + y for x, y in zip(q_rotate(iq, [i_s[k] * at[k] for k in range(3)]), it)]
    return q + t + [as_[k] * i_s[k] for k in range(3)]


def program(src, bone_name=lambda n: n, variables=None):
    """The rig as RoeRigVM's program (see the module's notes).  variables: the anim node's values for the rig's variables
    (name -> float; the class default otherwise)."""
    d, vm, cls, cdo = load(src)
    mems = [(0, vm["WorkMemoryStorage"]), (1, vm["LiteralMemoryStorageOld"])]
    regs, base = [], {}
    for mt, mem in mems:
        base[mt] = len(regs)
        regs += [register(r, mem["ScriptStructPaths"], bone_name) for r in mem["Registers"]]
    base[2] = len(regs)
    defaults = cdo.get("Properties") or {}
    for p in cls.get("ChildProperties", []):
        if p["Type"] != "FloatProperty":
            raise SystemExit(f"variable {p['Name']}: {p['Type']} not supported")
        v = (variables or {}).get(p["Name"], defaults.get(p["Name"], 0.0))
        regs.append({"name": p["Name"], "kind": "float", "value": [float(v)]})
    segments = {mt: [segment(o) for o in mem.get("RegisterOffsets", [])] for mt, mem in mems}

    def ref(a):
        mt, off = a["MemoryType"], a.get("RegisterOffset", 65535)
        first, count = (0, -1) if off == 65535 else segments[mt][off]
        return [base[mt] + a["RegisterIndex"], first, count]

    funcs = [f.replace("FRigUnit_", "").replace("::Execute", "") for f in vm["FunctionNamesStorage"]]
    code = []
    for ins in vm["ByteCodeStorage"]["Instructions"]:
        op = ins.get("OpCode")
        if op == 101:
            code.append({"unit": funcs[ins["FunctionIndex"]], "args": [x for a in ins.get("Arguments", []) for x in ref(a)]})
        elif op == 68:
            code.append({"unit": "Copy", "args": ref(ins["Source"]) + ref(ins["Target"])})
        elif op == 80:
            break
        else:
            raise SystemExit(f"opcode {OPS.get(op, op)} not supported")

    # the rig's own skeleton at its reference pose: the global (InitialTransform) and local transforms of the bones it reads
    # or writes (a port checks its axes against them); the local one from the globals - the hierarchy's LocalTransform is the
    # pose as the editor's last run of the rig left it (its skirt roots turned and moved)
    touched = {r["bone"] for r in regs if r["kind"] == "key"}
    bones = ((cdo.get("Properties") or {}).get("Hierarchy") or {}).get("BoneHierarchy", {}).get("Bones", [])
    initial = {b["Name"]: floats("transform", b["InitialTransform"]) for b in bones}
    reference = [{"bone": bone_name(b["Name"]),
                  "local": ue_relative(initial[b["Name"]], initial.get(b.get("ParentName"))),
                  "global": initial[b["Name"]]}
                 for b in bones if bone_name(b["Name"]) in touched]
    name = cls.get("Name", "").removesuffix("_C") or src
    return {"name": name, "registers": regs, "code": code, "reference": reference}


def main():
    src = sys.argv[1]
    if "--json" in sys.argv:
        out = sys.argv[sys.argv.index("--json") + 1]
        p = program(src)
        json.dump(p, open(out, "w", encoding="utf-8"), indent=1)
        print(f"{p['name']}: {len(p['registers'])} registers, {len(p['code'])} instructions, units "
              f"{sorted({c['unit'] for c in p['code']})} -> {out}")
        return
    disassemble(src, sys.argv[2] if len(sys.argv) > 2 else None)


if __name__ == "__main__":
    main()
