# One picture of the hit effects RoeHitFx.Sheet filmed: a row per effect, a column per moment after it starts.
#   python tools/hitfx_sheet.py [_work/hitfx] [out/hit_effects_sheet.jpg] [--only ufe_light,ufe_medium]
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

FONT = r'C:\Windows\Fonts\msyh.ttc'
ZH = {'ufe_light': 'UFE 轻击', 'ufe_medium': 'UFE 中击', 'ufe_heavy': 'UFE 重击', 'ufe_block': 'UFE 防住',
      'ufe_crumple': 'UFE 瘫倒', 'ufe_groundbounce': 'UFE 落地反弹'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir', nargs='?', default=os.path.join('_work', 'hitfx'))
    ap.add_argument('out', nargs='?', default=os.path.join('out', 'hit_effects_sheet.jpg'))
    ap.add_argument('--only', default='')
    ap.add_argument('--tile', type=int, default=240)
    a = ap.parse_args()
    lines = open(os.path.join(a.dir, 'fx.txt'), encoding='utf-8').read().splitlines()
    times = [t for t in lines if t.startswith('times\t')][0].split('\t')[1].split(',')
    names = [l.split('\t')[0] for l in lines if not l.startswith('times\t')]
    if a.only:
        keep = a.only.split(',')
        names = [n for n in names if n in keep]
    w = a.tile
    h = w * 3 // 4
    label = 300
    font = ImageFont.truetype(FONT, 18)
    small = ImageFont.truetype(FONT, 14)
    sheet = Image.new('RGB', (label + w * len(times), 28 + h * len(names)), (24, 24, 24))
    d = ImageDraw.Draw(sheet)
    for k, t in enumerate(times):
        d.text((label + k * w + 8, 4), f'{float(t):.2f} 秒', font=font, fill=(255, 210, 80))
    for r, n in enumerate(names):
        y = 28 + r * h
        d.text((8, y + 8), ZH.get(n, ''), font=font, fill=(255, 255, 255))
        d.text((8, y + 34), n, font=small, fill=(190, 190, 190))
        for k in range(len(times)):
            p = os.path.join(a.dir, f'{n}_{k}.jpg')
            if os.path.exists(p):
                sheet.paste(Image.open(p).resize((w, h)), (label + k * w, y))
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    sheet.save(a.out, quality=88)
    print(f'{a.out}: {len(names)} effects x {len(times)} moments')


if __name__ == '__main__':
    main()
