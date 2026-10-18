# Turn the frames written by RoeShowcase.Video / RoeDuel.Run into an mp4 with the game's sounds.
#   python make_video.py [video dir] [out.mp4]
# timeline.json comes in two shapes:
#   RoeDuel:     {"fps": 60, "frames": N, "sounds": [{"path": "Assets/ROE/...ogg", "frame": n, "volume": 1.0}, ...],
#                 "music": {"path": "Assets/ROE/stages/...ogg", "volume": 0.3}}   (music optional, looped)
#   RoeShowcase: [{"id": "a08", "clip": "skill_01", "frame": n, "frames": m, "fps": 60}, ...] - the sound of clip
#                skill_0N is the file in Assets/ROE/<id>/sfx_* named "skillN" / "skill0N" (no "hit"); die -> "die".
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'


def sfx_for(manifest, cid, clip):
    files = manifest.get(cid, {}).get('sfx', [])
    m = re.match(r'skill_0?(\d)(_hit)?$', clip)
    if m:
        n = m.group(1)
        want_hit = bool(m.group(2))
        pat = re.compile(r'skill_?0?%s(?!\d)' % n, re.I)
        cands = [f for f in files if pat.search(os.path.basename(f)) and ('hit' in os.path.basename(f).lower()) == want_hit]
        return cands[0] if cands else None
    if clip == 'die':
        cands = [f for f in files if 'die' in os.path.basename(f).lower()]
        return cands[0] if cands else None
    return None


def main():
    vdir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJECT, '_work', 'video')
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(vdir, 'showcase.mp4')
    timeline = json.load(open(os.path.join(vdir, 'timeline.json'), encoding='utf-8'))
    sounds = []     # (file, milliseconds, volume)
    if isinstance(timeline, dict):
        fps = timeline['fps']
        total = timeline['frames'] / fps
        for s in timeline['sounds']:
            sounds.append((os.path.join(PROJECT, s['path']), int(s['frame'] * 1000 / fps), s.get('volume', 1.0)))
    else:
        manifest = json.load(open(os.path.join(PROJECT, 'Assets', 'ROE', 'roe_manifest.json'), encoding='utf-8'))
        fps = timeline[0]['fps']
        total = max(seg['frame'] + seg['frames'] for seg in timeline) / fps
        for seg in timeline:
            s = sfx_for(manifest, seg['id'], seg['clip'])
            if s:
                sounds.append((os.path.join(PROJECT, s), int(seg['frame'] * 1000 / fps), 1.0))

    # background music, looped under everything: {"music": {"path": ..., "volume": 0.3}}
    music = timeline.get('music') if isinstance(timeline, dict) else None

    cmd = [FFMPEG, '-y', '-loglevel', 'error', '-framerate', str(fps), '-i', os.path.join(vdir, 'frames', '%05d.jpg')]
    for path, ms, volume in sounds:
        cmd += ['-i', path]
        print(f'  {ms / 1000:6.2f} s  x{volume:.2f}  {os.path.basename(path)}')
    if music:
        cmd += ['-stream_loop', '-1', '-i', os.path.join(PROJECT, music['path'])]
        print(f'  music x{music.get("volume", 0.3):.2f}  {os.path.basename(music["path"])}')
    if sounds or music:
        parts = []
        for i, (path, ms, volume) in enumerate(sounds):
            parts.append(f'[{i + 1}:a]aresample=48000,aformat=channel_layouts=stereo,adelay={ms}|{ms},volume={0.9 * volume:.3f}[a{i}]')
        inputs = len(sounds)
        if music:
            parts.append(f'[{len(sounds) + 1}:a]aresample=48000,aformat=channel_layouts=stereo,volume={music.get("volume", 0.3):.3f}[a{len(sounds)}]')
            inputs += 1
        mix = ''.join(f'[a{i}]' for i in range(inputs))
        parts.append(f'{mix}amix=inputs={inputs}:normalize=0:dropout_transition=0,alimiter=limit=0.95,apad[aout]')
        cmd += ['-filter_complex', ';'.join(parts), '-map', '0:v', '-map', '[aout]', '-c:a', 'aac', '-b:a', '192k']
    cmd += ['-c:v', 'libx264', '-crf', '17', '-preset', 'slow', '-pix_fmt', 'yuv420p', '-t', f'{total:.3f}', out]
    subprocess.run(cmd, check=True)
    print(f'{out}: {total:.1f} s, {os.path.getsize(out) / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
