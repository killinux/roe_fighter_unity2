# One sheet per character from RoeBurstBuilder.Check: the stages with the game's materials (front, back) in the
# first row, the groups in colours with their legend in the second.
#   python burst_sheet.py [pictures dir] [--chars a08,g04]
#   -> <dir>/<id>_sheet.png
import argparse
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FONT = r'C:\Windows\Fonts\msyh.ttc'
STAGE_NAMES = ['原样', '第一段掉了', '第二段掉了', '第三段掉了', '第四段掉了', '第五段掉了']


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir', nargs='?', default=os.path.join(PROJECT, 'out', 'clothes_burst', 'unity'))
    ap.add_argument('--chars', default='a08,g04')
    ap.add_argument('--tile', type=int, default=420, help='tile width (px)')
    a = ap.parse_args()
    font = ImageFont.truetype(FONT, 22)
    small = ImageFont.truetype(FONT, 19)
    for cid in a.chars.split(','):
        stages = []
        while os.path.exists(os.path.join(a.dir, f'{cid}_stage{len(stages)}_front.png')):
            stages.append(len(stages))
        if not stages:
            print(cid, 'no pictures')
            continue
        first = Image.open(os.path.join(a.dir, f'{cid}_stage0_front.png'))
        tw = a.tile
        th = round(first.height * tw / first.width)
        head = 34
        cols = max(2 * len(stages), 4)
        legend = []
        path = os.path.join(a.dir, f'{cid}_legend.txt')
        if os.path.exists(path):
            for line in open(path, encoding='utf-8'):
                p = line.rstrip('\n').split('\t')
                if len(p) >= 5:
                    legend.append(p)
        sheet = Image.new('RGB', (cols * tw, 2 * (th + head)), (255, 255, 255))
        dr = ImageDraw.Draw(sheet)

        def put(name, col, row, title):
            img = Image.open(os.path.join(a.dir, name)).convert('RGB').resize((tw, th), Image.LANCZOS)
            sheet.paste(img, (col * tw, row * (th + head) + head))
            dr.text((col * tw + 8, row * (th + head) + 5), title, fill=(0, 0, 0), font=font)

        for s in stages:
            put(f'{cid}_stage{s}_front.png', 2 * s, 0, f'{STAGE_NAMES[min(s, len(STAGE_NAMES) - 1)]} · 正')
            put(f'{cid}_stage{s}_back.png', 2 * s + 1, 0, '背')
        put(f'{cid}_groups_front.png', 0, 1, '分组（灰 = 不掉）· 正')
        put(f'{cid}_groups_back.png', 1, 1, '背')
        x, y = 2 * tw + 20, (th + head) + head + 10
        dr.text((x, y), '会掉的部件（第几段、怎么飞、三角形数）', fill=(0, 0, 0), font=font)
        y += 40
        for name, colour, stage, style, tris in legend:
            c = tuple(int(colour[i:i + 2], 16) for i in (1, 3, 5))
            dr.rectangle([x, y + 3, x + 26, y + 25], fill=c)
            dr.text((x + 36, y), f'{name}    第 {stage} 段，{"布料：落下摊平" if style == "cloth" else "护甲：飞出去翻滚"}，{int(tris):,}',
                    fill=(0, 0, 0), font=small)
            y += 32
        # split vs as the prefab has it, same pose and camera: should be the same picture
        y += 20
        for view in ('front', 'back'):
            u, s0 = (os.path.join(a.dir, f'{cid}_{k}_{view}.png') for k in ('unsplit', 'stage0'))
            if not (os.path.exists(u) and os.path.exists(s0)):
                continue
            d = np.abs(np.asarray(Image.open(u).convert('RGB'), dtype=np.int16) - np.asarray(Image.open(s0).convert('RGB'), dtype=np.int16))
            line = (f'拆分前后对比（{"正" if view == "front" else "背"}）：最大差 {int(d.max())}/255，'
                    f'差超过 2/255 的像素 {100.0 * (d.max(axis=2) > 2).mean():.3f}%')
            print(cid, view, line)
            dr.text((x, y), line, fill=(0, 0, 0), font=small)
            y += 30
        out = os.path.join(a.dir, f'{cid}_sheet.png')
        sheet.save(out)
        print(out, sheet.size)


if __name__ == '__main__':
    main()
