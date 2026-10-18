# Burn a demo's captions into its frames (RoeKofDemo writes captions.json: [{"from": frame, "to": frame, "text": "..."}]):
# a dark band along the bottom with the Chinese line (the HUD's font has no Chinese), in place.
#   python tools/caption_frames.py <demo dir>
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

FONT = r'C:\Windows\Fonts\msyhbd.ttc'


def main():
    d = sys.argv[1]
    caps = json.load(open(os.path.join(d, 'captions.json'), encoding='utf-8'))
    frames = os.path.join(d, 'frames')
    done = 0
    for c in caps:
        for f in range(c['from'], c['to']):
            p = os.path.join(frames, f'{f:05d}.jpg')
            if not os.path.exists(p):
                continue
            im = Image.open(p).convert('RGB')
            w, h = im.size
            size = max(18, h // 26)
            font = ImageFont.truetype(FONT, size)
            draw = ImageDraw.Draw(im, 'RGBA')
            text = c['text']
            while draw.textlength(text, font=font) > w * 0.92 and size > 14:
                size -= 1
                font = ImageFont.truetype(FONT, size)
            tw = draw.textlength(text, font=font)
            y = int(h * 0.80)
            draw.rectangle((0, y - size * 0.45, w, y + size * 1.45), fill=(0, 0, 0, 150))
            draw.text(((w - tw) / 2, y), text, font=font, fill=(255, 230, 140))
            im.save(p, quality=92)
            done += 1
    print(f'{d}: {len(caps)} captions on {done} frames')


if __name__ == '__main__':
    main()
