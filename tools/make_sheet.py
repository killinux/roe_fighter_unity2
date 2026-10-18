# Put the still renders of each character side by side: <dir>/<id>_sheet.jpg
#   python make_sheet.py [dir] [height]
import glob
import os
import sys

from PIL import Image

d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), '_work', 'shots')
H = int(sys.argv[2]) if len(sys.argv) > 2 else 900
ids = sorted({os.path.basename(f).split('_')[0] for f in glob.glob(os.path.join(d, '*_?_*.png'))})
for cid in ids:
    files = sorted(glob.glob(os.path.join(d, cid + '_?_*.png')))
    ims = [Image.open(f).convert('RGB') for f in files]
    ims = [im.resize((int(im.width * H / im.height), H), Image.LANCZOS) for im in ims]
    sheet = Image.new('RGB', (sum(i.width for i in ims), H))
    x = 0
    for im in ims:
        sheet.paste(im, (x, 0))
        x += im.width
    out = os.path.join(d, cid + '_sheet.jpg')
    sheet.save(out, quality=90)
    print(out, sheet.size)
