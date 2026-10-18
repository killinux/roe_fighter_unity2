# One picture of a pack's strikes from RoeMotionPacks.Sheet: a row per strike (8 frames across it) with what was
# measured - the hand or foot that hits, how far it reaches, when it can hit.  --pack: the pack's JSON names each row by
# its "who" / "zh" / "speed" (a candidates pack: tools/motionpacks/ufe_normals.json).
#   python strike_sheet.py <frames dir (out\motion_sheets\<pack>)> [out.png] [--cols 8] [--tile 210] [--pack x.json]
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

FONT = r'C:\Windows\Fonts\msyh.ttc'
BONES = {'LeftHand': '左手', 'RightHand': '右手', 'LeftFoot': '左脚', 'RightFoot': '右脚'}
LEVELS = {'High': '上段', 'Mid': '中段', 'Low': '下段'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir')
    ap.add_argument('out', nargs='?')
    ap.add_argument('--cols', type=int, default=8)
    ap.add_argument('--tile', type=int, default=210)
    ap.add_argument('--pack', help="the pack's JSON: rows named by its who / zh / speed")
    a = ap.parse_args()
    spec = {x['name']: x for x in json.load(open(a.pack, encoding='utf-8'))['strikes']} if a.pack else {}
    rows = []
    for line in open(os.path.join(a.dir, 'strikes.tsv'), encoding='utf-8').read().splitlines()[1:]:
        c = line.split('\t')
        if len(c) >= 8:
            rows.append(c)
    font = ImageFont.truetype(FONT, 20)
    label_w = 300
    first = Image.open(os.path.join(a.dir, f'{rows[0][0]}_0.jpg'))
    th = round(first.height * a.tile / first.width)
    sheet = Image.new('RGB', (label_w + a.cols * a.tile, len(rows) * th), (255, 255, 255))
    dr = ImageDraw.Draw(sheet)
    for r, (clip, button, bone, reach, level, h0, h1, length) in enumerate(rows):
        y = r * th
        for i in range(a.cols):
            path = os.path.join(a.dir, f'{clip}_{i}.jpg')
            if os.path.exists(path):
                sheet.paste(Image.open(path).convert('RGB').resize((a.tile, th)), (label_w + i * a.tile, y))
        x = spec.get(clip)
        name = f"{x.get('who', '')} {x.get('zh') or clip} ×{x.get('speed', 1):g}".strip() if x else clip
        text = (f'{name}\n{BONES.get(bone, bone)}，够到 {float(reach):.2f} 米\n{LEVELS.get(level, level)}，'
                f'{float(h0):.2f}–{float(h1):.2f} 秒能打中\n全长 {float(length):.2f} 秒')
        dr.multiline_text((10, y + 10), text, fill=(0, 0, 0), font=font, spacing=6)
    out = a.out or os.path.join(a.dir, 'sheet.png')
    sheet.save(out)
    print(out, sheet.size)


if __name__ == '__main__':
    main()
