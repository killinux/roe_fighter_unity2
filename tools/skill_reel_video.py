# The skill reels (RoeFightScene.SkillReel: a fighter's three game skills in the real fight) as one video: each reel
# with its sounds (make_video.py), a caption naming the fighter and the skill while it plays, then all of them in a row.
#   python tools\skill_reel_video.py out\skills_reel.mp4 _work\skills_m02 _work\skills_a08 _work\skills_g04 _work\skills_g05
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'
FONT = 'C\\:/Windows/Fonts/msyhbd.ttc'
LABELS = {1: '技能 1', 2: '技能 2', 3: '超必杀'}


def caption(text, first, last, y, size):
    text = text.replace(':', '\\:').replace("'", "\u2019")
    return (f"drawtext=fontfile='{FONT}':text='{text}':fontsize={size}:fontcolor=white:borderw=3:bordercolor=black@0.8:"
            f"x=(w-text_w)/2:y={y}:enable='between(n,{first},{last})'")


def reel(vdir):
    """One reel with its sounds and the captions: vdir/reel_captioned.mp4."""
    raw = os.path.join(vdir, 'reel_raw.mp4')
    subprocess.run([sys.executable, os.path.join(HERE, 'make_video.py'), vdir, raw], check=True)
    info = json.load(open(os.path.join(vdir, 'reel.json'), encoding='utf-8'))
    who = f"{info['name']}（{info['p1']}）"
    filters = [caption(f"{who} 的三个技能（游戏自己的战斗动作）", 0, info['skills'][0]['from'] - 1, 'h-90', 34)]
    for s in info['skills']:
        filters.append(caption(f"{who}  {LABELS.get(s['skill'], s['skill'])}", s['from'], s['to'], 'h-90', 40))
    out = os.path.join(vdir, 'reel_captioned.mp4')
    subprocess.run([FFMPEG, '-y', '-loglevel', 'error', '-i', raw, '-vf', ','.join(filters),
                    '-c:v', 'libx264', '-crf', '17', '-preset', 'slow', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', out],
                   check=True)
    return out


def main():
    out, dirs = sys.argv[1], sys.argv[2:]
    parts = [reel(d) for d in dirs]
    listing = os.path.join(os.path.dirname(os.path.abspath(out)), '_skill_reels.txt')
    with open(listing, 'w', encoding='utf-8') as f:
        for p in parts:
            f.write("file '%s'\n" % os.path.abspath(p).replace('\\', '/'))
    subprocess.run([FFMPEG, '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', listing, '-c', 'copy', out], check=True)
    os.remove(listing)
    print(out)


if __name__ == '__main__':
    main()
