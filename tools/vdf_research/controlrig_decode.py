"""Read the constants of a cooked UE 5.3 Control Rig (Vindictus: Defying Fate, no .usmap): the property list of its
RigVMMemory_Literal class and the values in that class's default object.

    python controlrig_decode.py <dir with Rig_proc_ControlRig.uasset + .zen.json from zen_dump.py> [out.json]

Layout (worked out from the bytes, UE 5.3 cooked, editor-only data filtered):
  class export:  unversioned header (none), UField.Next, UStruct.SuperStruct, Children (0), ChildProperties: count, then per
                 property its class FName and FField/FProperty fields (Name, Flags, ArrayDim, ElementSize, PropertyFlags u64,
                 RepIndex u16, RepNotifyFunc, BlueprintReplicationCondition u8) + the type's own (array: inner property,
                 struct/byte/object: package index, bool: 6 bytes, enum: package index + underlying property).
  default object: unversioned header over that property list, then the values.
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kawaii_decode import R, header  # noqa: E402


class Pkg:
    def __init__(self, folder, base="Rig_proc_ControlRig"):
        self.z = json.load(open(os.path.join(folder, base + ".zen.json"), encoding="utf-8"))
        self.data = open(os.path.join(folder, base + ".uasset"), "rb").read()
        self.names = self.z["names"]
        self.exports = {e["name"]: e for e in self.z["exports"]}

    def blob(self, export):
        e = self.exports[export]
        o = self.z["header_size"] + e["serial_offset"]
        return self.data[o:o + e["serial_size"]]

    def fname(self, r):
        i, n = r.u32(), r.u32()
        s = self.names[i] if i < len(self.names) else f"?{i}"
        return s if n == 0 else f"{s}_{n - 1}"

    def ref(self, index):
        if index < 0:
            return self.z["imports"][-index - 1]["name"]
        if index > 0:
            return "export:" + self.z["exports"][index - 1]["name"]
        return None


def read_field(pkg, r):
    """One FProperty as its class name and fields."""
    cls = pkg.fname(r)
    p = {"type": cls, "name": pkg.fname(r), "flags": r.u32(), "dim": r.i32(), "size": r.i32()}
    p["pflags"] = struct.unpack_from("<Q", r.d, r.p)[0]
    r.p += 8
    r.u16()                     # RepIndex
    pkg.fname(r)                # RepNotifyFunc
    r.u8()                      # BlueprintReplicationCondition
    if cls == "ArrayProperty":
        p["inner"] = read_field(pkg, r)
    elif cls in ("StructProperty", "ObjectProperty", "ClassProperty", "SoftObjectProperty", "WeakObjectProperty", "InterfaceProperty"):
        p["ref"] = pkg.ref(r.i32())
        if cls == "ClassProperty":
            p["meta"] = pkg.ref(r.i32())
    elif cls == "ByteProperty":
        p["enum"] = pkg.ref(r.i32())
    elif cls == "EnumProperty":
        p["enum"] = pkg.ref(r.i32())
        p["underlying"] = read_field(pkg, r)
    elif cls == "BoolProperty":
        p["bool"] = list(r.d[r.p:r.p + 6])
        r.p += 6
    elif cls in ("SetProperty",):
        p["inner"] = read_field(pkg, r)
    elif cls in ("MapProperty",):
        p["key"] = read_field(pkg, r)
        p["value"] = read_field(pkg, r)
    return p


def class_properties(pkg, export="RigVMMemory_Literal"):
    r = R(pkg.blob(export))
    header(r)                   # the class object's own properties: none
    r.i32()                     # UField.Next
    p_super = pkg.ref(r.i32())
    children = r.i32()
    assert children == 0, children
    count = r.i32()
    props = [read_field(pkg, r) for _ in range(count)]
    return p_super, props, r.p


# ---- values

def read_value(pkg, r, p, top=True):
    t = p["type"]
    if t == "ArrayProperty":
        n = r.i32()
        return [read_value(pkg, r, p["inner"], False) for _ in range(n)]
    if t == "NameProperty":
        return pkg.fname(r)
    if t == "BoolProperty":
        return bool(r.u8())
    if t == "IntProperty":
        return r.i32()
    if t == "FloatProperty":
        return r.f32()
    if t == "DoubleProperty":
        return r.f64()[0]
    if t == "ByteProperty":
        return r.u8()
    if t == "EnumProperty":
        return read_value(pkg, r, p["underlying"], False)
    if t == "StructProperty":
        return read_struct(pkg, r, p["ref"])
    raise ValueError(f"no reader for {t} ({p['name']})")


# native layouts of the structs the rig uses (doubles: UE5 large world coordinates)
NATIVE = {
    "/Script/CoreUObject.Vector": lambda r: r.f64(3),
    "/Script/CoreUObject.Quat": lambda r: r.f64(4),
    "/Script/CoreUObject.Rotator": lambda r: r.f64(3),
}
# property lists of the tagged structs, in their unversioned order
FIELDS = {
    "/Script/CoreUObject.Transform": [("Rotation", "/Script/CoreUObject.Quat"), ("Translation", "/Script/CoreUObject.Vector"),
                                      ("Scale3D", "/Script/CoreUObject.Vector")],
    "/Script/ControlRig.RigElementKey": [("Type", "byte"), ("Name", "name")],
    "/Script/ControlRig.CachedRigElement": [("Key", "/Script/ControlRig.RigElementKey"), ("Index", "u16"), ("ContainerVersion", "i32")],
}


def read_struct(pkg, r, ref):
    if ref in NATIVE:
        return NATIVE[ref](r)
    if ref in FIELDS:
        fields = FIELDS[ref]
        out = {}
        for idx, zero in header(r):
            name, kind = fields[idx] if idx < len(fields) else (f"p{idx}", "?")
            if zero:
                out[name] = 0
                continue
            if kind == "byte":
                out[name] = r.u8()
            elif kind == "name":
                out[name] = pkg.fname(r)
            elif kind == "u16":
                out[name] = r.u16()
            elif kind == "i32":
                out[name] = r.i32()
            elif kind.startswith("/Script/"):
                out[name] = read_struct(pkg, r, kind)
            else:
                raise ValueError(f"{ref}.{name}: unknown kind {kind}")
        return out
    raise ValueError(f"no layout for struct {ref}")


def defaults(pkg, props, export="Default__RigVMMemory_Literal"):
    r = R(pkg.blob(export))
    out = {}
    for idx, zero in header(r):
        p = props[idx]
        if zero:
            out[p["name"]] = 0
            continue
        start = r.p
        try:
            out[p["name"]] = read_value(pkg, r, p)
        except Exception as e:      # stop at the first property we cannot read: the rest would be misaligned
            out[p["name"]] = f"<unread: {e} at {start}>"
            break
    return out, r.p, len(r.d)


# ---- the program (URigVM's byte code)

OPS = {101: "Execute", 65: "Zero", 66: "BoolFalse", 67: "BoolTrue", 68: "Copy", 69: "Increment", 70: "Decrement",
       71: "Equals", 72: "NotEquals", 73: "JumpAbsolute", 74: "JumpForward", 75: "JumpBackward", 76: "JumpAbsoluteIf",
       77: "JumpForwardIf", 78: "JumpBackwardIf", 79: "ChangeType", 80: "Exit", 81: "BeginBlock", 82: "EndBlock",
       99: "InvokeEntry", 100: "JumpToBranch"}
MEM = {0: "W", 1: "L", 2: "X", 3: "D"}


def operand(r):
    mem, reg, off = r.u8(), r.u16(), r.u16()
    return (MEM.get(mem, f"M{mem}"), reg, None if off == 0xFFFF else off)


def program(pkg):
    """The function names and the instructions, as far as they read."""
    r = R(pkg.blob("VM"))
    r.p += 8                    # a hash of the byte code
    functions = [pkg.fname(r) for _ in range(r.i32())]
    count = r.i32()
    code = []
    for i in range(count):
        at = r.p
        op = r.u8()
        name = OPS.get(op)
        if name in ("EndBlock", "Exit"):              # no operands: the code alone
            code.append((i, name, None, []))
            continue
        if name is not None and r.d[r.p] != op:      # every other op repeats its code first; anything else: stop and show the bytes
            code.append((i, f"op{op}?", r.d[at:at + 48].hex(" "), []))
            break
        if name == "Execute":
            assert r.u8() == op
            fn, argc, pred0, predn = r.u16(), r.u16(), r.u16(), r.u16()
            code.append((i, name, functions[fn], [operand(r) for _ in range(argc)]))
        elif name in ("Zero", "BoolFalse", "BoolTrue", "Increment", "Decrement"):
            assert r.u8() == op
            code.append((i, name, None, [operand(r)]))
        elif name == "Copy":
            assert r.u8() == op
            src, dst = operand(r), operand(r)
            r.u16()                 # number of bytes (0: the whole register)
            r.u8()                  # copy type
            code.append((i, name, None, [src, dst]))
        elif name in ("Equals", "NotEquals"):
            assert r.u8() == op
            code.append((i, name, None, [operand(r), operand(r), operand(r)]))
        elif name in ("JumpAbsolute", "JumpForward", "JumpBackward"):
            assert r.u8() == op
            code.append((i, name, r.i32(), []))
        elif name in ("JumpAbsoluteIf", "JumpForwardIf", "JumpBackwardIf"):
            assert r.u8() == op
            arg = operand(r)
            code.append((i, name, (r.i32(), bool(r.u8())), [arg]))
        elif name in ("BeginBlock",):
            assert r.u8() == op
            code.append((i, name, None, [operand(r), operand(r)]))
        elif name in ("EndBlock", "Exit"):
            assert r.u8() == op
            code.append((i, name, None, []))
        elif name == "JumpToBranch":
            assert r.u8() == op
            arg = operand(r)
            code.append((i, name, r.i32(), [arg]))
        elif name == "InvokeEntry":
            assert r.u8() == op
            code.append((i, name, pkg.fname(r), []))
        else:
            code.append((i, f"op{op}?", r.d[at:at + 48].hex(" "), []))
            break
    return functions, code, r.p, len(r.d)


def main():
    folder = sys.argv[1]
    pkg = Pkg(folder)
    if len(sys.argv) > 2 and sys.argv[2] == "--code":
        _, lit, _ = class_properties(pkg, "RigVMMemory_Literal")
        _, work, _ = class_properties(pkg, "RigVMMemory_Work")
        lit_values, _, _ = defaults(pkg, lit)
        functions, code, used, total = program(pkg)
        print(f"{len(functions)} functions, {len(code)} instructions read, {used} of {total} bytes")

        def show(o):
            mem, reg, off = o
            table = lit if mem == "L" else work if mem == "W" else None
            name = table[reg]["name"] if table is not None and reg < len(table) else f"{mem}{reg}"
            if mem == "L":
                val = lit_values.get(name, "?")
                if isinstance(val, dict) and "Name" in val:
                    val = val["Name"]
                if isinstance(val, list) and len(val) > 4:
                    val = f"[{len(val)}]"
                name = f"{name}={val}"
            return f"{mem}:{name}" + (f"+{off}" if off is not None else "")
        for i, name, extra, ops in code:
            fn = extra.split("::")[0].replace("FRigVMFunction_", "").replace("FRigUnit_", "").replace("DISPATCH_RigVMDispatch_", "") \
                if isinstance(extra, str) else extra
            print(f"{i:4d} {name:12s} {str(fn)[:40]:40s} " + ", ".join(show(o) for o in ops[:12]) + (" ..." if len(ops) > 12 else ""))
        return
    pkg = Pkg(folder)
    sup, props, end = class_properties(pkg)
    print(f"RigVMMemory_Literal: super {sup}, {len(props)} properties (class data read to byte {end})")
    types = {}
    for p in props:
        key = p["type"] + ("<" + p["inner"]["type"] + (":" + str(p["inner"].get("ref")) if "ref" in p["inner"] else "") + ">" if "inner" in p else
                           (":" + str(p.get("ref")) if "ref" in p else ""))
        types[key] = types.get(key, 0) + 1
    for k, v in sorted(types.items(), key=lambda kv: -kv[1]):
        print(f"  {v:4d}  {k}")
    values, used, total = defaults(pkg, props)
    print(f"default object: {len(values)} values, {used} of {total} bytes read")
    if len(sys.argv) > 2:
        json.dump({"properties": props, "values": values}, open(sys.argv[2], "w", encoding="utf-8"), indent=1, default=str)
        print("->", sys.argv[2])


if __name__ == "__main__":
    main()
