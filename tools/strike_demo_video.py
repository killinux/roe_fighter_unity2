# A video of a fighter doing every strike of a pack, one after the other, from RoeClothDemo.StrikeTakes: each take in
# real time, then the strike again at half speed.  The label says whose move it is (the pack JSON's "who" / "ufe" or "src" / "zh"),
# what RoeMotionPacks measured (hand or foot, how far it reaches) and lights up while the strike can hit.
#   python tools/strike_demo_video.py [takes dir] [out.mp4] [--pack tools/motionpacks/ufe_normals.json] [--id a08] [--slow 2]
#   python tools/strike_demo_video.py _work\strike_takes out\a08_ufe_normals.mp4
import argparse
import json
import os
import subprocess

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'
FONT = r'C:\Windows\Fonts\msyh.ttc'
BONES = {'LeftHand': '左手', 'RightHand': '右手', 'LeftFoot': '左脚', 'RightFoot': '右脚'}
LEVELS = {'High': '上段', 'Mid': '中段', 'Low': '下段'}
NAMES = {'a08': 'a08 Inase', 'g04': 'g04 Luf', 'b10': 'b10 Kart', 'g05': 'g05 Luf', 'kas': '霞（DOA6）', 'fio005': 'Fiona'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir', nargs='?', default=os.path.join(PROJECT, '_work', 'strike_takes'))
    ap.add_argument('out', nargs='?', default=os.path.join(PROJECT, 'out', 'a08_ufe_normals.mp4'))
    ap.add_argument('--pack', default=os.path.join(HERE, 'motionpacks', 'ufe_normals.json'), help='labels: who / ufe / zh per strike')
    ap.add_argument('--id', default='a08')
    ap.add_argument('--slow', type=int, default=2, help='the replay is this many times slower: 2 or 4 (0: no replay)')
    ap.add_argument('--fps', type=int, default=30)
    a = ap.parse_args()
    spec = {s['name']: s for s in json.load(open(a.pack, encoding='utf-8'))['strikes']} if a.pack else {}
    rows = []
    for line in open(os.path.join(a.dir, 'strikes.tsv'), encoding='utf-8').read().splitlines()[1:]:
        c = line.split('\t')
        if len(c) >= 11 and c[0] == a.id:
            rows.append(dict(zip(['id', 'strike', 'press', 'speed', 'length', 'hitStart', 'hitEnd', 'bone', 'reach', 'level',
                                  'knockdown'], c)))
    big = ImageFont.truetype(FONT, 32)
    mid = ImageFont.truetype(FONT, 25)
    small = ImageFont.truetype(FONT, 26)
    ff = None
    count = 0
    for n, r in enumerate(rows):
        folder = os.path.join(a.dir, f"{a.id}_{r['strike']}")
        frames = sorted(f for f in os.listdir(folder) if f.endswith('.jpg'))
        s = spec.get(r['strike'], {})
        speed, press = float(r['speed']), float(r['press'])
        hit0 = press + float(r['hitStart']) / speed
        hit1 = press + float(r['hitEnd']) / speed
        who = s.get('who', '')
        title = f"{NAMES.get(a.id, a.id)}　{n + 1}/{len(rows)}　{who + '：' if who else ''}{s.get('zh') or r['strike']}"
        # where the move is from: UFE 2's move name, or the pack's own "src" (a08_sword: Vindictus' clip names)
        origin = f"UFE 2 {s['ufe']}" if 'ufe' in s else s.get('src', r['strike'])
        info = (f"{origin}　×{speed:g}　{BONES.get(r['bone'], r['bone'])}，够到 {float(r['reach']):.2f} 米，"
                f"{LEVELS.get(r['level'], r['level'])}" + ('，击倒' if r['knockdown'] == 'True' else ''))
        # real time (every second frame of the 60-a-second take), then the strike again slowly (each frame slow/2 times)
        plan = [(k, '实时') for k in range(0, len(frames), 2)]
        if a.slow >= 2:
            first = max(0, int((press - 0.15) * 60))
            last = min(len(frames), int((hit1 + 0.5) * 60))
            for k in range(first, last):
                plan += [(k, f'慢放 1/{a.slow}')] * (a.slow // 2)
        for k, mode in plan:
            im = Image.open(os.path.join(folder, frames[k])).convert('RGB')
            w, h = im.size
            frame = Image.new('RGB', (w, h + 54), (18, 18, 18))
            frame.paste(im, (0, 0))
            d = ImageDraw.Draw(frame)
            d.text((16, 10), title, font=big, fill=(255, 255, 255), stroke_width=3, stroke_fill=(0, 0, 0))
            d.text((16, 54), info, font=mid, fill=(255, 225, 110), stroke_width=3, stroke_fill=(0, 0, 0))
            t = k / 60.0
            d.text((16, h + 10), f'{mode}　{t:4.2f} s', font=small, fill=(220, 220, 220))
            if hit0 - 1e-3 <= t <= hit1 + 1e-3:
                d.rounded_rectangle((w - 190, h + 8, w - 16, h + 46), radius=8, fill=(200, 40, 40))
                d.text((w - 172, h + 10), '能打中', font=small, fill=(255, 255, 255))
            if ff is None:
                ff = subprocess.Popen([FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                                       '-s', f'{frame.width}x{frame.height}', '-r', str(a.fps), '-i', '-',
                                       '-c:v', 'libx264', '-crf', '18', '-preset', 'slow', '-pix_fmt', 'yuv420p', a.out],
                                      stdin=subprocess.PIPE)
            ff.stdin.write(frame.tobytes())
            count += 1
    ff.stdin.close()
    ff.wait()
    print(f'{a.out}: {len(rows)} strikes, {count} frames, {count / a.fps:.1f} s, {os.path.getsize(a.out) / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
