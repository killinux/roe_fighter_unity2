# The clothes burst demo (RoeBurstDemo.Run) as one video: per take the close camera and the fight camera side by
# side, what is happening written underneath, the game's sounds; the takes one after the other.
#   python burst_video.py [demo dir] [out.mp4]
import json
import os
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'
FFPROBE = FFMPEG.replace('ffmpeg.exe', 'ffprobe.exe')
FONT = r'C:\Windows\Fonts\msyh.ttc'


def captions_of(take):
    rows = []
    path = os.path.join(take, 'captions.txt')
    if os.path.exists(path):
        for line in open(path, encoding='utf-8'):
            if '\t' in line:
                f, text = line.rstrip('\n').split('\t', 1)
                rows.append((int(f), text))
    return rows


def compose(take):
    close_dir, fight_dir, out_dir = (os.path.join(take, d) for d in ('close', 'fight', 'frames'))
    if os.path.exists(out_dir):
        shutil.rmtree(out_dir)
    os.makedirs(out_dir)
    caps = captions_of(take)
    big = ImageFont.truetype(FONT, 30)
    label = ImageFont.truetype(FONT, 22)
    names = sorted(n for n in os.listdir(fight_dir) if n.endswith('.jpg'))
    for k, n in enumerate(names):
        a = Image.open(os.path.join(close_dir, n)).convert('RGB')
        b = Image.open(os.path.join(fight_dir, n)).convert('RGB')
        w, h = a.size
        bar = 64
        img = Image.new('RGB', (2 * w, h + bar), (16, 16, 20))
        img.paste(a, (0, 0))
        img.paste(b, (w, 0))
        dr = ImageDraw.Draw(img)
        for x, t in ((0, '近景（跟着挨打的一方）'), (w, '比赛镜头')):
            dr.rectangle([x + 10, 10, x + 20 + dr.textlength(t, font=label), 44], fill=(0, 0, 0))
            dr.text((x + 15, 12), t, fill=(255, 255, 255), font=label)
        now = [t for f, t in caps if f <= k]
        recent = [t for f, t in caps if f <= k and k - f < 75]
        text = now[-1] if now else ''
        colour = (255, 220, 120) if recent and recent[-1] == text else (230, 230, 230)
        dr.text((20, h + 12), text, fill=colour, font=big)
        img.save(os.path.join(out_dir, n), quality=92)
    return len(names)     # make_video.py reads <take>/frames and <take>/timeline.json


def probe(path):
    """(whether it has a sound track, its length in seconds)"""
    r = subprocess.run([FFPROBE, '-v', 'error', '-show_entries', 'stream=codec_type:format=duration', '-of', 'json', path],
                       capture_output=True, text=True)
    d = json.loads(r.stdout)
    return any(st.get('codec_type') == 'audio' for st in d.get('streams', [])), float(d['format']['duration'])


def main():
    demo = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJECT, '_work', 'burst_demo')
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(PROJECT, 'out', 'clothes_burst_demo.mp4')
    takes = [t.strip() for t in open(os.path.join(demo, 'takes.txt'), encoding='utf-8') if t.strip()]
    parts = []
    for t in takes:
        take = os.path.join(demo, t)
        n = compose(take)
        mp4 = os.path.join(take, 'take.mp4')
        subprocess.run([sys.executable, os.path.join(HERE, 'make_video.py'), take, mp4], check=True)
        parts.append(mp4)
        print(t, n, 'frames ->', mp4)
    # one after the other (re-encoded: the takes may differ in their audio)
    cmd = [FFMPEG, '-y', '-loglevel', 'error']
    for p in parts:
        cmd += ['-i', p]
    # a take without a sound (a foe from another game has none of the game's sounds: Mai) gets silence of its length
    audio, extra = [], len(parts)
    for i, p in enumerate(parts):
        has, length = probe(p)
        if has:
            audio.append(f'[{i}:a]')
        else:
            cmd += ['-f', 'lavfi', '-t', f'{length:.3f}', '-i', 'anullsrc=r=48000:cl=stereo']
            audio.append(f'[{extra}:a]')
            extra += 1
    streams = ''.join(f'[{i}:v]{audio[i]}' for i in range(len(parts)))
    cmd += ['-filter_complex', f'{streams}concat=n={len(parts)}:v=1:a=1[v][a]', '-map', '[v]', '-map', '[a]',
            '-c:v', 'libx264', '-crf', '18', '-preset', 'slow', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', out]
    subprocess.run(cmd, check=True)
    print(out, f'{os.path.getsize(out) / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
