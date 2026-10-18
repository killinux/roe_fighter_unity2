# One picture of RoeClothDemo.StrikeTakes' takes: a row per strike, a frame every --step seconds from when the button was
# pressed (as the fight plays it, at the strike's speed), a red bar under the frames in which it can hit.  Made to pick
# strikes from (user 10-06: Inase's greatsword strikes; strike_sheet.py spreads 8 frames over a whole clip, which for
# Vindictus' 2.6 s attacks put one frame on the swing).  --pack: the pack's JSON names each row by its "zh" / "src".
#   python tools/takes_sheet.py <takes dir> <out.jpg> [--pack x.json] [--only a,b] [--cols 12] [--step 0.1] [--start 0]
#                               [--crop 0,0.25,0.85,1] [--tile 190]
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

FONT = r'C:\Windows\Fonts\msyh.ttc'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir')
    ap.add_argument('out')
    ap.add_argument('--pack')
    ap.add_argument('--only', help='these strikes, in this order (comma list)')
    ap.add_argument('--cols', type=int, default=12)
    ap.add_argument('--step', type=float, default=0.1, help='seconds between frames')
    ap.add_argument('--start', type=float, default=0.0, help='first frame, seconds after the press')
    ap.add_argument('--fps', type=float, default=60, help="the takes' frame rate")
    ap.add_argument('--tile', type=int, default=190)
    ap.add_argument('--crop', default='0,0.25,0.85,1', help='x0,y0,x1,y1: the part of each frame kept, as fractions')
    a = ap.parse_args()
    spec = {x['name']: x for x in json.load(open(a.pack, encoding='utf-8'))['strikes']} if a.pack else {}
    rows = [line.split('\t') for line in open(os.path.join(a.dir, 'strikes.tsv'), encoding='utf-8').read().splitlines()[1:] if line]
    if a.only:
        keep = a.only.split(',')
        rows = sorted((r for r in rows if r[1] in keep), key=lambda r: keep.index(r[1]))
    font = ImageFont.truetype(FONT, 18)
    small = ImageFont.truetype(FONT, 14)
    first = Image.open(os.path.join(a.dir, f'{rows[0][0]}_{rows[0][1]}', '00000.jpg'))
    x0, y0, x1, y1 = (float(v) for v in a.crop.split(','))
    box = (int(x0 * first.width), int(y0 * first.height), int(x1 * first.width), int(y1 * first.height))
    tw = a.tile
    th = round(tw * (box[3] - box[1]) / (box[2] - box[0]))
    label_w = 290
    sheet = Image.new('RGB', (label_w + a.cols * tw, 24 + len(rows) * th), (255, 255, 255))
    dr = ImageDraw.Draw(sheet)
    for c in range(a.cols):
        dr.text((label_w + c * tw + 4, 3), f'+{a.start + c * a.step:.2f} 秒', fill=(0, 0, 0), font=small)
    for r, row in enumerate(rows):
        fid, name, press, speed, length, h0, h1, bone, reach = row[:9]
        speed = float(speed)
        y = 24 + r * th
        for c in range(a.cols):
            t = a.start + c * a.step
            path = os.path.join(a.dir, f'{fid}_{name}', '%05d.jpg' % round((float(press) + t) * a.fps))
            if os.path.exists(path):
                sheet.paste(Image.open(path).convert('RGB').crop(box).resize((tw, th)), (label_w + c * tw, y))
            if float(h0) / speed - 1e-3 <= t <= float(h1) / speed + 1e-3:
                dr.rectangle((label_w + c * tw, y + th - 7, label_w + (c + 1) * tw - 1, y + th - 1), fill=(220, 30, 30))
        s = spec.get(name, {})
        button = f"{s['button']} 键  " if s.get('button') and len(rows) <= 6 else ''
        text = (f"{button}{s.get('zh') or name}\n{s.get('src', name)}\n够到 {float(reach):.2f} 米，×{speed:g}\n"
                f"{float(h0) / speed:.2f}–{float(h1) / speed:.2f} 秒能打中\n全长 {float(length) / speed:.2f} 秒")
        dr.multiline_text((8, y + 6), text, fill=(0, 0, 0), font=font, spacing=3)
        dr.line((0, y, sheet.width, y), fill=(160, 160, 160))
    sheet.save(a.out, quality=90)
    print(a.out, sheet.size)


if __name__ == '__main__':
    main()
