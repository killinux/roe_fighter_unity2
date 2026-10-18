"""
Eve's own animations from Stellar Blade (user 10-17: "用 eve里自己的招式") as ActorX .psa files for SbAnims (Unity):

    python tools/sb_anims.py export [--only REGEX] [--force]
        the clips below (or the names of CH_P_EVE_01/Animation matching REGEX) exported by the CUE4Parse CLI with the
        community .usmap into _work/sb/anim/<package path>.psa (cached; game data, personal use only, never committed);
    python tools/sb_anims.py list [--only REGEX]
        the exported clips: frames, length, how far Root and Bip001 travel and turn.
    --folder SB/Content/Art/Character/Monster/CH_M_NA_53/Animation   (10-18: another character's clips - Raven's; with
        --only "." all of them)

Her clips play on the game's whole PC skeleton (3267 bones: every outfit's and hairstyle's helpers): Root (where the
game moves her: the _Root versions and the RootMotionData ones carry the travel), Bip001 (3ds Max Biped's centre of
mass), the Biped, Bip001-Prop1 (the sword's bone, under Bip001: keyed in every sword clip) and the helpers.  The PSA's
rate is CUE4Parse's frames / length (91 keys over 3.0 s read 30.33): the keys are 1 / 30 s apart.
Not exported: the clips compressed with ACL (Social/, the character screen's UIStudio poses: SBAnimBoneCompressionSettings)
- this CLI build has no CUE4Parse-Natives library ("DllNotFoundException" in ACLNative); her battle clips are not ACL.
"""
import argparse
import math
import os
import re
import struct
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "_work", "sb", "anim")
CLI = r"E:\tools\cue4parse_cli_ff7\cue4parse.exe"
PAKS = r"D:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB\Content\Paks"
USMAP = r"E:\game_export\StellarBlade\_meta\mappings\StellarBlade_1.1.0.usmap"
EVE_ANIM = "SB/Content/Art/Character/PC/CH_P_EVE_01/Animation"
ANIM = EVE_ANIM

# the candidates: her sword combos and finishers, her Beta and Burst skills, the slot skills, getting hit, knocked down,
# lying, getting up, her battle idles and lock-on walks, and poses for an intro and a win
CLIPS = """
Proto_Sword_Lightattack_01 Proto_Sword_Lightattack_02 Proto_Sword_Lightattack_03 Proto_Sword_Lightattack_04 Proto_Sword_Lightattack_05
Proto_Sword_Lightattack_01_Root Proto_Sword_Lightattack_02_Root Proto_Sword_Lightattack_03_Root Proto_Sword_Lightattack_04_Root
Proto_Sword_Strongattack_01 Proto_Sword_Strongattack_02 Proto_Sword_Strongattack_03 Proto_Sword_Strongattack_04 Proto_Sword_Strongattack_01R
Proto_Sword_Strongattack_01_Root Proto_Sword_Strongattack_02_Root Proto_Sword_Strongattack_03_Root
Proto_Sword_Thrust Proto_Sword_Upper Proto_Sword_Parryattack_01
P_Eve_Sword_Normal_LightAttack5 P_Eve_Sword_Normal_LightAttack6 P_Eve_Sword_Normal_LightAttack7 P_Eve_Sword_Normal_LightAttack8
P_Eve_Sword_Normal_LIghtAttack2_2 P_Eve_Sword_Normal_LIghtAttack3_2 P_Eve_Sword_Normal_LIghtAttack4_2
P_Eve_Sword_Normal_StrongAttack3 P_Eve_Sword_Normal_StrongAttack4 P_Eve_Sword_Normal_StrongAttack5 P_Eve_Sword_Normal_StrongAttack7
P_Eve_Sword_Normal_StrongAttack8 P_Eve_Sword_Normal_StrongAttack9 P_Eve_Sword_Normal_StrongAttack10
P_Eve_Sword_Normal_DashAttackCombo2 P_Eve_Sword_Normal_MoveBackAttack1_S P_Eve_Sword_Normal_MoveBackAttack2_S
P_Eve_Sword_Normal_FlashBehindAttack_S P_Eve_Sword_Normal_FlashBehindAttack_E P_Eve_Sword_Normal_Buff1
Sword_Combo_Thrust_1 Sword_Combo_Thrust_2 Sword_Combo_Thrust_3 Sword_Combo_Thrust_4 Sword_Thrust_Combo
Sword_Finish_Attack_1 Sword_Finish_Attack_2 Sword_Finish_Attack_3 Sword_Finish_Attack_4 Sword_Down_Attack Sword_Ground_Upper
P_Eve_Sword_JustParry_LightAttack1_S P_Eve_Sword_JustParry_StrongAttack1 P_Eve_Sword_JustParry_ChargeSlash1
P_Eve_Sword_JustEvade_LightAttack1 P_Eve_Sword_JustEvade_StrongAttack1
P_Eve_Sword_Beta_ChainStab1_Ex P_Eve_Sword_Beta_ChainStabCombo1_Ex P_Eve_Sword_Beta_ChargeSlash1_Ex P_Eve_Sword_Beta_ChargeSlash2_Ex
P_Eve_Sword_Beta_ChargeSlashCombo1_Ex P_Eve_Sword_Beta_ShieldBreak1_Ex_S P_Eve_Sword_Beta_ShieldBreak1_Ex_E
P_Eve_Sword_Beta_ShieldBreak2_Ex_Charged_S P_Eve_Sword_Beta_SwordAura1 P_Eve_Sword_Beta_SwordAura1_Ex P_Eve_Sword_Beta_SwordAura2_Ex
P_Eve_Sword_Beta_SwordAuraCombo1_Ex P_Eve_Sword_Burst_SpinAttack1 P_Eve_Sword_Burst_SwordAura1 P_Eve_Sword_Burst_MeleeCombatBuff1
P_Eve_Sword_SlotNormal_ChainStab P_Eve_Sword_SlotNormal_ChargeSlash P_Eve_Sword_SlotNormal_SpinAttack
P_Eve_Sword_SlotNormal_ChargeSpinAttack1 P_Eve_Sword_SlotNormal_PushBackAttack1 P_Eve_Sword_SlotNormal_ShieldBreak1_S
P_Eve_Sword_SlotNormal_ShieldBreak1_E P_Eve_Sword_StanceBurst_JumpAttack1_1 P_Eve_Sword_StanceBurst_JumpAttack2_1
P_Eve_Sword_StanceBurst_S P_Eve_Sword_SuitNormal_MassiveAttack_S P_Eve_Sword_SuitNormal_MassiveAttack_E
Result_Hit_Stand_Light_Fw Result_Hit_Stand_Light_Bw Result_Hit_Stand_Strong_Fw Result_Hit_Stand_Strong_Bw
Result_State_KnockDown_Start_Fw Result_State_KnockDown_Start_Bw Result_State_KnockDown_Fall_Fw Result_State_KnockDown_Fall_Bw
Result_State_KnockDown_L Result_State_KnockDown_E Result_State_KnockDown_S_Fw Result_State_KnockDown_S_Bw
Result_State_DownFaceUp_E Result_State_DownFaceDown Result_State_DownFaceDown_E
Result_Knock_Down_Start Result_Knock_Down_Loop Result_Knock_Down_End Result_Knock_Down_Backward Result_Knock_Down_Forward
Result_Knock_Back Result_Stand_Dead Getup_Center Getup_Backward Getup_Forward GetupFaceDown_Center
Proto_Battle_Idle Proto_Battle_IdleR P_Eve_StanceBurst_Battle_Idle Proto_Lockon_Battle_Idle Proto_Guard_idle Proto_Guard
Proto_Lockon_Walk_Forward Proto_Lockon_Walk_Backward Proto_Lockon_Walk_Left Proto_Lockon_Walk_Right Proto_Lockon_Run_Forward
Proto_Idle P_Eve_Peaceful_Idle01 P_Eve_Peaceful_Idle02 P_Eve_Peaceful_Idle03 P_Eve_Peaceful_Idle04
""".split()


def package(name):
    return f"{ANIM}/{name}"


def psa_path(name):
    return os.path.join(OUT, *package(name).split("/")) + ".psa"


def names(only):
    if not only:
        return CLIPS
    # one listing per folder (Eve's keeps its old name)
    listing = os.path.join(OUT, "_listing.txt" if ANIM == EVE_ANIM else f"_listing_{ANIM.replace('/', '_')}.txt")
    if not os.path.exists(listing):
        r = subprocess.run([CLI, "-i", PAKS, "-g", "GAME_StellarBlade", "-m", USMAP, "-l", "-p", f"{ANIM}/*"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        os.makedirs(OUT, exist_ok=True)
        open(listing, "w", encoding="utf-8").write(r.stdout)
    # names under the folder, sub-folders included (Social/P_Eve_UIStudio_Weapon_Start)
    found = sorted({m.group(1) for m in re.finditer(re.escape(ANIM) + r"/([A-Za-z0-9_/]+)\.uasset", open(listing, encoding="utf-8").read())})
    return [n for n in found if re.search(only, n, re.I)]


def export(a):
    todo = [n for n in names(a.only) if a.force or not os.path.exists(psa_path(n))]
    for i in range(0, len(todo), 40):
        batch = todo[i:i + 40]
        args = [CLI, "-i", PAKS, "-g", "GAME_StellarBlade", "-m", USMAP, "-o", OUT, "-y"]
        for n in batch:
            args += ["-p", package(n) + ".*"]
        subprocess.run(args, capture_output=True)
    missing = [n for n in todo if not os.path.exists(psa_path(n))]
    print(f"{len(todo) - len(missing)} exported, {len(missing)} not found: {missing}")


def read(path):
    data = open(path, "rb").read()
    off, bones, info, keys = 0, [], None, None
    while off + 32 <= len(data):
        cid = data[off:off + 20].split(b"\0")[0].decode("ascii", "replace")
        _, size, count = struct.unpack_from("<iii", data, off + 20)
        body = off + 32
        if cid == "BONENAMES":
            bones = [data[body + i * size: body + i * size + 64].split(b"\0")[0].decode() for i in range(count)]
        elif cid == "ANIMINFO":
            info = (struct.unpack_from("<f", data, body + 152)[0], struct.unpack_from("<i", data, body + 164)[0])
        elif cid == "ANIMKEYS":
            keys = (data, body)
        off = body + size * count
    return bones, info, keys


def key(keys, nb, f, b):
    data, body = keys
    return struct.unpack_from("<3f4f", data, body + (f * nb + b) * 32)


def yaw(q):
    x, y, z, w = q
    return math.degrees(math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)))


def listing(a):
    for n in names(a.only):
        p = psa_path(n)
        if not os.path.exists(p):
            continue
        bones, (rate, frames), keys = read(p)
        nb = len(bones)
        step = frames / (rate * max(1, frames - 1))
        out = [f"{n}: {frames} keys {(frames - 1) * step:.2f} s"]
        for bone in ("Root", "Bip001"):
            b = bones.index(bone)
            p0, q0 = key(keys, nb, 0, b)[:3], key(keys, nb, 0, b)[3:]
            p1, q1 = key(keys, nb, frames - 1, b)[:3], key(keys, nb, frames - 1, b)[3:]
            out.append(f"{bone} x {p1[0] - p0[0]:+.0f} y {p1[1] - p0[1]:+.0f} z {p1[2] - p0[2]:+.0f} cm, yaw {yaw(q1) - yaw(q0):+.0f} deg")
        print("; ".join(out))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("what", choices=["export", "list"])
    ap.add_argument("--only", default="")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--folder", default=EVE_ANIM, help="the clips' folder in the game (default Eve's)")
    a = ap.parse_args()
    global ANIM
    ANIM = a.folder
    (export if a.what == "export" else listing)(a)


if __name__ == "__main__":
    main()
