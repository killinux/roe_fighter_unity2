# Her joints with each twist rig side by side (VdfAnims.RigStills): one row per moment, one column per rig.
#   python vdf_rig_sheet.py [stills dir] [out.jpg]
#   python vdf_rig_sheet.py _work\vdf\rig out\fio005_rig.jpg
import argparse
import csv
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FONT = r'C:\Windows\Fonts\msyh.ttc'
MODES = [('Off', '不驱动（都在绑定姿势）'), ('ByPosition', '上一版：扭转骨按位置分'), ('Game', '游戏的程序化骨骼')]
JOINTS = {'elbow': '肘', 'knee': '膝', 'forearm': '前臂', 'shoulder': '肩'}
CLIPS = {'Guard_Counter': '举盾反击', 'Attack01': '转身斩', 'HeavyStander_During': '重装防御架势', 'Attack_Strong04': '踢加斩',
         'Attack_Strong03': '盾后突刺', 'Cheering_Evy': '胜利欢呼', 'battle_idle': '战斗站架', 'Battle_Idle': '战斗站架',
         'Walk_Loop': '前进', 'Damage_Strong_Back_Begin': '重受击（从背后）', 'skill_01': '技能 1', 'skill_02': '技能 2',
         'skill_03': '技能 3', 'die': '被 KO 倒地'}


def label(clip, time, joint):
    name = next((v for k, v in CLIPS.items() if k in clip), clip)
    side = '左' if joint.endswith('_l') else '右'
    return f'{name}\n{float(time):.2f} 秒 {side}{JOINTS.get(joint.split("_")[0], joint)}'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('folder', nargs='?', default=os.path.join(PROJECT, '_work', 'vdf', 'rig'))
    ap.add_argument('out', nargs='?', default=os.path.join(PROJECT, 'out', 'fio005_rig.jpg'))
    ap.add_argument('--tile', type=int, default=330)
    a = ap.parse_args()
    rows = list(csv.DictReader(open(os.path.join(a.folder, 'shots.tsv'), encoding='utf-8'), delimiter='\t'))
    modes = [m for m in MODES if os.path.exists(os.path.join(a.folder, f'rig_{rows[0]["n"]}_{m[0]}.png'))]
    t, head, side = a.tile, 60, 170
    sheet = Image.new('RGB', (side + t * len(modes), head + t * len(rows)), (24, 24, 24))
    d = ImageDraw.Draw(sheet)
    big = ImageFont.truetype(FONT, 22)
    small = ImageFont.truetype(FONT, 18)
    for c, (_, title) in enumerate(modes):
        d.text((side + c * t + 8, 18), title, font=small, fill=(255, 220, 90))
    for r, row in enumerate(rows):
        d.multiline_text((10, head + r * t + t // 2 - 30), label(row['clip'], row['time'], row['joint']), font=big, fill=(230, 230, 230))
        for c, (mode, _) in enumerate(modes):
            path = os.path.join(a.folder, f'rig_{row["n"]}_{mode}.png')
            if os.path.exists(path):
                im = Image.open(path).convert('RGB').resize((t, t))
                sheet.paste(im, (side + c * t, head + r * t))
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    sheet.save(a.out, quality=90)
    print(a.out, sheet.size)


if __name__ == '__main__':
    main()
