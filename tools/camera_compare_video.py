# Two recordings of RoeFightScene.Record next to each other (frames/ of each, 30 fps), a title over each:
# the old fight camera on the left, the one that keeps out of the stage on the right.  No sound (the two
# matches part ways once the cameras differ: the computer's moves follow the screen).
#   python camera_compare_video.py <old dir> <new dir> <out.mp4> [--from 0] [--to 60]
#   python camera_compare_video.py _work\fight_camold _work\fight_camnew2 out\fight_camera_compare.mp4 --to 70
import argparse
import os
import subprocess
import tempfile

from PIL import Image, ImageDraw, ImageFont

FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'
FONT = r'C:\Windows\Fonts\msyh.ttc'
W, H, BAR = 960, 540, 64


def label(text, path):
    im = Image.new('RGBA', (W, BAR), (16, 16, 16, 255))
    d = ImageDraw.Draw(im)
    font = ImageFont.truetype(FONT, 30)
    w = d.textlength(text, font=font)
    d.text(((W - w) / 2, 12), text, font=font, fill=(255, 220, 90, 255))
    im.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('old')
    ap.add_argument('new')
    ap.add_argument('out')
    ap.add_argument('--from', dest='start', type=float, default=0.0)
    ap.add_argument('--to', type=float, default=None)
    ap.add_argument('--old-title', default='旧镜头：只管构图，打到场边会跑到围绳外面')
    ap.add_argument('--new-title', default='新镜头：只站在场地的空处，看两人的视线不被挡')
    a = ap.parse_args()
    frames = min(len(os.listdir(os.path.join(a.old, 'frames'))), len(os.listdir(os.path.join(a.new, 'frames'))))
    first = int(a.start * 30)
    last = frames if a.to is None else min(frames, int(a.to * 30))
    tmp = tempfile.mkdtemp()
    lo, ln = os.path.join(tmp, 'old.png'), os.path.join(tmp, 'new.png')
    label(a.old_title, lo)
    label(a.new_title, ln)
    graph = (f'[0:v]scale={W}:{H},pad={W}:{H + BAR}:0:{BAR}[a];[a][2:v]overlay=0:0[a2];'
             f'[1:v]scale={W}:{H},pad={W}:{H + BAR}:0:{BAR}[b];[b][3:v]overlay=0:0[b2];'
             f'[a2][b2]hstack=inputs=2[v]')
    cmd = [FFMPEG, '-y', '-loglevel', 'error',
           '-framerate', '30', '-start_number', str(first), '-i', os.path.join(a.old, 'frames', '%05d.jpg'),
           '-framerate', '30', '-start_number', str(first), '-i', os.path.join(a.new, 'frames', '%05d.jpg'),
           '-i', lo, '-i', ln, '-filter_complex', graph, '-map', '[v]', '-frames:v', str(last - first),
           '-c:v', 'libx264', '-crf', '20', '-pix_fmt', 'yuv420p', a.out]
    subprocess.run(cmd, check=True)
    print(f'{a.out}: {(last - first) / 30:.1f} s ({first / 30:.1f}-{last / 30:.1f} s of the matches)')


if __name__ == '__main__':
    main()
