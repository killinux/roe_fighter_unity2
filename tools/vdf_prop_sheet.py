# Her weapons before and after they follow the game's animation (VdfAnims.PropStills run twice, -roeTag before /
# after): one row per moment, from her front-left and from her front, before | after side by side.
#   python vdf_prop_sheet.py [stills dir] [out.jpg] [--names "battle stance,shield counter,..."]
#   python vdf_prop_sheet.py _work\vdf\props out\fio005_props.jpg
import argparse
import glob
import os
import re

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FONT = r'C:\Windows\Fonts\msyh.ttc'
NAMES = '战斗站架,举盾反击（B）,重装防御架势,盾后突刺（A）,胜利欢呼'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('folder', nargs='?', default=os.path.join(PROJECT, '_work', 'vdf', 'props'))
    ap.add_argument('out', nargs='?', default=os.path.join(PROJECT, 'out', 'fio005_props.jpg'))
    ap.add_argument('--names', default=NAMES)
    ap.add_argument('--tile', type=int, default=300)
    a = ap.parse_args()
    names = a.names.split(',')
    rows = sorted({int(m.group(1)) for f in glob.glob(os.path.join(a.folder, 'props_after_*_left.png'))
                   for m in [re.search(r'props_after_(\d+)_left', f)] if m})
    tw, th = a.tile, int(a.tile * 8 / 7)
    head, side = 70, 150
    cols = [('before', 'left'), ('after', 'left'), ('before', 'front'), ('after', 'front')]
    sheet = Image.new('RGB', (side + tw * len(cols), head + th * len(rows)), (24, 24, 24))
    d = ImageDraw.Draw(sheet)
    big = ImageFont.truetype(FONT, 22)
    small = ImageFont.truetype(FONT, 18)
    titles = {('before', 'left'): '之前：盾不动（左前方）', ('after', 'left'): '之后：照游戏的动作（左前方）',
              ('before', 'front'): '之前（正面）', ('after', 'front'): '之后（正面）'}
    for c, key in enumerate(cols):
        d.text((side + c * tw + 8, 20), titles[key], font=small, fill=(255, 220, 90))
    for r, n in enumerate(rows):
        label = names[n] if n < len(names) else f'#{n}'
        d.text((10, head + r * th + th // 2 - 14), label, font=big, fill=(230, 230, 230))
        for c, (tag, view) in enumerate(cols):
            path = os.path.join(a.folder, f'props_{tag}_{n}_{view}.png')
            if not os.path.exists(path):
                continue
            im = Image.open(path).convert('RGB')
            im.thumbnail((tw, th))
            sheet.paste(im, (side + c * tw + (tw - im.width) // 2, head + r * th + (th - im.height) // 2))
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    sheet.save(a.out, quality=90)
    print(a.out, sheet.size)


if __name__ == '__main__':
    main()
