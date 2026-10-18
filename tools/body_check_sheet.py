# One sheet from RoeBodyCheck: per fighter a row - the chest dressed (at rest, both breasts turned down), the same with
# every outfit piece that can come off taken off (fighters with a clothes burst), and two maps from straight in front of
# her: how far each vertex of the body (skin / nude body) and of what she wears moved with the breasts turned
# (grey: did not move; blue to red: 0.1 to 3 cm and more).  Movement away from the breasts is a weight leak.
#   python tools/body_check_sheet.py [dir] [--chars a08,g04] [--out sheet.jpg]
import argparse
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FONT = r'C:\Windows\Fonts\msyh.ttc'
BODY = ('skin', 'nude')
WORN = ('cloth', 'piece')
SPAN = 0.30          # the maps show this far round the middle of the chest (m)


def ramp(v):
    """cm moved -> colour: grey under 0.1, then blue, cyan, green, yellow, red at 3 cm and more."""
    if v < 0.1:
        return (175, 175, 175)
    stops = [(0.1, (40, 60, 220)), (0.5, (0, 190, 230)), (1.0, (40, 200, 60)), (2.0, (240, 220, 0)), (3.0, (230, 30, 20))]
    if v >= stops[-1][0]:
        return stops[-1][1]
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if v <= b:
            t = (v - a) / (b - a)
            return tuple(int(ca[k] + (cb[k] - ca[k]) * t) for k in range(3))
    return stops[-1][1]


def read_table(path):
    rows = []
    with open(path, encoding='utf-8') as f:
        next(f)
        for line in f:
            p = line.rstrip('\n').split('\t')
            if len(p) < 7:
                continue
            rows.append((p[1], float(p[3]), float(p[4]), float(p[5]), float(p[6])))
    return rows


def heat(rows, roles, size, title, font):
    img = Image.new('RGB', (size, size), (250, 250, 250))
    dr = ImageDraw.Draw(img)
    pts = [r for r in rows if r[0] in roles]
    # painter's order: the back first, so what is in front of her is drawn last
    pts.sort(key=lambda r: r[3])
    s = size / (2 * SPAN)
    for role, x, y, z, v in pts:
        # seen from in front of her: her right is on the left of the picture
        u = (-x + SPAN) * s
        w = (SPAN - y) * s
        if 0 <= u < size and 0 <= w < size:
            dr.rectangle([u - 1, w - 1, u + 1, w + 1], fill=ramp(v))
    dr.text((8, 6), title, fill=(0, 0, 0), font=font)
    if not pts:
        dr.text((8, size // 2), '（没有）', fill=(120, 120, 120), font=font)
    return img


def legend(width, font):
    img = Image.new('RGB', (width, 40), (255, 255, 255))
    dr = ImageDraw.Draw(img)
    x = 10
    for v, label in [(0.0, '没动'), (0.2, '0.2 cm'), (0.5, '0.5'), (1.0, '1'), (2.0, '2'), (3.0, '3 cm 以上')]:
        dr.rectangle([x, 12, x + 22, 30], fill=ramp(v))
        dr.text((x + 28, 8), label, fill=(0, 0, 0), font=font)
        x += 40 + int(dr.textlength(label, font=font))
    dr.text((x + 20, 8), '胸骨往下转 20° 时每个顶点动了多少（从她正前方看，胸口中间 ±30 cm）；离胸远的地方在动 = 权重漏了',
            fill=(60, 60, 60), font=font)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir', nargs='?', default=os.path.join(PROJECT, '_work', 'body_check'))
    ap.add_argument('--chars', default='')
    ap.add_argument('--out', default='')
    ap.add_argument('--tile', type=int, default=330)
    a = ap.parse_args()
    font = ImageFont.truetype(FONT, 20)
    small = ImageFont.truetype(FONT, 17)
    ids = [c for c in a.chars.split(',') if c] or sorted(
        f[:-len('_chest.tsv')] for f in os.listdir(a.dir) if f.endswith('_chest.tsv'))
    names = {}
    report = os.path.join(a.dir, 'report.txt')
    if os.path.exists(report):
        for line in open(report, encoding='utf-8'):
            if line.startswith('== '):
                p = line[3:].strip().split(' ', 1)
                names[p[0]] = p[1] if len(p) > 1 else ''
    t = a.tile
    head = 30
    cols = 6
    rows_img = []
    for cid in ids:
        row = Image.new('RGB', (cols * t, t + head + 34), (255, 255, 255))
        dr = ImageDraw.Draw(row)
        dr.text((8, 4), f'{cid}  {names.get(cid, "")}', fill=(0, 0, 0), font=font)
        tiles = [('dressed_rest', '穿着，原样'), ('dressed_turned', '穿着，胸骨往下转'),
                 ('nude_rest', '爆衣后（裸体），原样'), ('nude_turned', '爆衣后，胸骨往下转')]
        for k, (name, title) in enumerate(tiles):
            path = os.path.join(a.dir, f'{cid}_{name}.jpg')
            x0 = k * t
            if os.path.exists(path):
                img = Image.open(path).convert('RGB').resize((t, t), Image.LANCZOS)
                row.paste(img, (x0, head + 34))
                dr.text((x0 + 8, head + 4), title, fill=(0, 0, 0), font=small)
            else:
                dr.rectangle([x0 + 4, head + 34, x0 + t - 4, head + 34 + t - 4], outline=(200, 200, 200))
                dr.text((x0 + 8, head + 4), title, fill=(150, 150, 150), font=small)
                dr.text((x0 + 20, head + 34 + t // 2 - 10), '没有爆衣' if name.startswith('nude') else '（没有图）',
                        fill=(150, 150, 150), font=font)
        table = os.path.join(a.dir, f'{cid}_chest.tsv')
        rows = read_table(table) if os.path.exists(table) else []
        row.paste(heat(rows, BODY, t, '身体（皮肤 / 裸体）', small), (4 * t, head + 34))
        row.paste(heat(rows, WORN, t, '衣服', small), (5 * t, head + 34))
        rows_img.append(row)
    if not rows_img:
        print('nothing to put on the sheet')
        return
    lg = legend(cols * t, small)
    sheet = Image.new('RGB', (cols * t, lg.height + sum(r.height for r in rows_img)), (255, 255, 255))
    sheet.paste(lg, (0, 0))
    y = lg.height
    for r in rows_img:
        sheet.paste(r, (0, y))
        y += r.height
    out = a.out or os.path.join(a.dir, 'body_check_sheet.jpg')
    sheet.save(out, quality=88)
    print(out, sheet.size)


if __name__ == '__main__':
    main()
