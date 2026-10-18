# A grid video of RoeClothPlayDemo takes that play together (they follow the same script): one tile per take, rows and
# columns as asked, the fighter and the take's title over each tile and, under it, the move she is doing (her own strike
# names) and, with --numbers, the take's breast numbers (breasts.txt: how far the breasts move against the chest).
#   python tools/takes_grid_video.py <demo dir> <out.mp4> --grid "a08_auto,g04_auto;a08_auto_nude,g04_auto_nude"
#          [--titles auto=穿着,auto_nude=爆衣后] [--tile 480x360] [--numbers]
# Several demo dirs: "--grid" names a take as dir_index:take ("1:kas_auto") with the dirs given as dir1+dir2.
import argparse
import os
import subprocess

from PIL import Image, ImageDraw, ImageFont

import cloth_demo_video as cdv

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)


def read_numbers(folder):
    """{(id, setup): (mean cm, most cm, speed cm/s)} of the whole take from breasts.txt."""
    path = os.path.join(folder, 'breasts.txt')
    out = {}
    if os.path.exists(path):
        for line in open(path, encoding='utf-8'):
            p = line.rstrip('\n').split('\t')
            if len(p) >= 7 and p[2] == 'all':
                out[(p[0], p[1])] = (float(p[4]), float(p[5]), float(p[6]))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dirs', help='demo dir(s), several joined with +')
    ap.add_argument('out')
    ap.add_argument('--grid', required=True, help='rows separated by ;, takes by , (take = <id>_<setup>, or n:<id>_<setup>)')
    ap.add_argument('--titles', default='', help='setup=title or id_setup=title, comma separated')
    ap.add_argument('--tile', default='480x360')
    ap.add_argument('--fps', type=int, default=30)
    ap.add_argument('--numbers', action='store_true', help='write the breast numbers of each take under it')
    a = ap.parse_args()
    dirs = a.dirs.split('+')
    tw, th = (int(x) for x in a.tile.split('x'))
    titles = dict(kv.split('=', 1) for kv in a.titles.split(',') if '=' in kv)
    script = []
    for line in open(os.path.join(dirs[0], 'script.txt'), encoding='utf-8'):
        t, what = line.strip().split(' ', 1)
        script.append((float(t), what))
    takes = [cdv.read_takes(d) for d in dirs]
    numbers = [read_numbers(d) for d in dirs]
    rows = []
    for row in a.grid.split(';'):
        cells = []
        for cell in row.split(','):
            cell = cell.strip()
            k = 0
            if ':' in cell:
                k, cell = cell.split(':', 1)
                k = int(k)
            folder = os.path.join(dirs[k], cell)
            info = None
            for (cid, setup), v in takes[k].items():
                if f'{cid}_{setup}' == cell:
                    info = (cid, setup, v)
            if info is None:
                raise SystemExit(f'no take {cell} in {dirs[k]}')
            cells.append((k, folder, info, sorted(f for f in os.listdir(folder) if f.endswith('.jpg'))))
        rows.append(cells)
    count = min(len(c[3]) for r in rows for c in r)
    cols = max(len(r) for r in rows)
    head, foot = 0, 64 if a.numbers else 38
    big = ImageFont.truetype(cdv.FONT, max(18, tw // 22))
    small = ImageFont.truetype(cdv.FONT, max(15, tw // 30))
    width, height = cols * tw, len(rows) * (th + foot)
    width -= width % 2
    height -= height % 2
    ff = subprocess.Popen([cdv.FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{width}x{height}',
                           '-r', str(a.fps), '-i', '-', '-c:v', 'libx264', '-crf', '19', '-preset', 'slow', '-pix_fmt', 'yuv420p', a.out],
                          stdin=subprocess.PIPE)
    for f in range(count):
        frame = Image.new('RGB', (width, height), (18, 18, 18))
        d = ImageDraw.Draw(frame)
        t = f * 2 / 60.0
        what = ''
        for start, step in script:
            if t >= start - 0.05:
                what = step
        for r, cells in enumerate(rows):
            for c, (k, folder, (cid, setup, (about, moves)), files) in enumerate(cells):
                x0, y0 = c * tw, r * (th + foot)
                im = Image.open(os.path.join(folder, files[f])).convert('RGB').resize((tw, th), Image.LANCZOS)
                frame.paste(im, (x0, y0))
                title = titles.get(f'{cid}_{setup}') or titles.get(setup) or cdv.TITLES.get((cid, setup)) or cdv.TITLES.get(setup) or setup
                d.text((x0 + 10, y0 + 6), cdv.NAMES.get(cid, cid), font=big, fill=(255, 255, 255), stroke_width=3, stroke_fill=(0, 0, 0))
                d.text((x0 + 10, y0 + 8 + big.size + 4), title, font=small, fill=(255, 225, 110), stroke_width=3, stroke_fill=(0, 0, 0))
                if what in moves:
                    move = moves[what]
                    label = f'{what} 键：{cdv.MOVES.get(move, move)}'
                else:
                    label = cdv.STEPS.get(what) or cdv.MOVES.get(what, what)
                d.text((x0 + 10, y0 + th + 6), f'{label}　{t:4.1f} s', font=small, fill=(220, 220, 220))
                if a.numbers and (cid, setup) in numbers[k]:
                    mean, most, speed = numbers[k][(cid, setup)]
                    d.text((x0 + 10, y0 + th + 8 + small.size + 4), f'乳摇（整段）：平均 {mean:.1f} cm，最大 {most:.1f} cm',
                           font=small, fill=(140, 220, 255))
        ff.stdin.write(frame.tobytes())
    ff.stdin.close()
    ff.wait()
    print(f'{a.out}: {count} frames, {count / a.fps:.1f} s, {os.path.getsize(a.out) / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
