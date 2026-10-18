# The game manual as a page next to the exe (user 10-11 "写个游戏说明"): docs/game-manual.md (the text, in the repo) ->
# out/ROEFighter2/游戏说明.html with its pictures in out/ROEFighter2/manual/ (game renders: not in the repo).
#   python tools\make_manual.py [--shot select=<frame.jpg>] [--shot fight=<frame.jpg>] [--open]
# A picture is placed where the text has "<!-- 图: <file> | <caption> -->" and the file is in the pictures folder:
# keys.png (the keyboard, drawn here), roster.jpg (the select cards, from Assets/RoeFighter/Generated/<id>/<id>_portrait.png)
# and the screenshots given with --shot (copied as <name>.jpg; kept from an earlier run when not given).
import argparse
import html
import os
import re

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FONT = r'C:\Windows\Fonts\msyh.ttc'
FONT_BOLD = r'C:\Windows\Fonts\msyhbd.ttc'
ROSTER = [('a08', 'INASE'), ('g04', 'LUF'), ('b10', 'KART'), ('g05', 'GODDESS LUF'), ('m02', 'AMANO'), ('b14', 'CRANE KART'),
          ('b14_outfit1', 'PINK CRANE KART'), ('kas', 'KASUMI'), ('kas011', 'KASUMI 海盗裙'), ('mai', 'MAI'), ('fio005', 'FIONA'),
          ('eve09', 'EVE'), ('eve37', 'EVE 百褶裙'), ('eve_nikke06', 'EVE 粉色紧身衣'), ('raven', 'RAVEN')]

# ---- pictures

MOVE, ATTACK, SKILL, FUNC, OK = (70, 140, 220), (235, 150, 40), (215, 70, 70), (120, 120, 130), (60, 170, 100)


def draw_keys(path):
    """1P's keyboard: the keys the game uses, coloured by what they do, on a faint full keyboard."""
    u = 70                                    # one key, in pixels
    ox, oy = 30, 60
    font_key = ImageFont.truetype(FONT_BOLD, 26)
    font_lab = ImageFont.truetype(FONT, 15)
    font_leg = ImageFont.truetype(FONT, 20)
    used = {
        'F1': (FUNC, '1P 电脑'), 'F2': (FUNC, '2P 电脑'), 'F3': (FUNC, '动作包'), 'F4': (FUNC, '物理'),
        'F5': (FUNC, '裙子'), 'F6': (FUNC, '爆衣'), 'F7': (FUNC, '回选人'), 'F8': (FUNC, '组队'),
        '1': (SKILL, '技能 1'), '2': (SKILL, '技能 2'), '3': (SKILL, '超必杀'),
        'W': (MOVE, '跳'), 'A': (MOVE, '左'), 'S': (MOVE, '蹲'), 'D': (MOVE, '右'),
        'J': (ATTACK, 'A 攻击'), 'K': (ATTACK, 'B 攻击'), 'U': (ATTACK, 'C 攻击'), 'I': (ATTACK, 'D 攻击'),
        'H': (ATTACK, '回避'), 'Y': (ATTACK, '吹飞'),
        'L': (SKILL, '技能 1'), 'O': (SKILL, '技能 2'), 'P': (SKILL, '超必杀'),
        'Enter': (OK, '确认'), 'Space': (OK, '确认'), 'Esc': (FUNC, '退回'),
    }
    rows = [
        (0.0, [('Esc', 0, 1)] + [(f'F{i}', 1 + i + (0.5 if i > 4 else 0), 1) for i in range(1, 9)]),
        (1.3, [(c, i, 1) for i, c in enumerate('`1234567890-=')] + [('Back', 13, 2)]),
        (2.3, [('Tab', 0, 1.5)] + [(c, 1.5 + i, 1) for i, c in enumerate('QWERTYUIOP[]')] + [('\\', 13.5, 1.5)]),
        (3.3, [('Caps', 0, 1.75)] + [(c, 1.75 + i, 1) for i, c in enumerate("ASDFGHJKL;'")] + [('Enter', 12.75, 2.25)]),
        (4.3, [('Shift', 0, 2.25)] + [(c, 2.25 + i, 1) for i, c in enumerate('ZXCVBNM,./')] + [('Shift', 12.25, 2.75)]),
        (5.3, [('Ctrl', 0, 1.5), ('Alt', 2.5, 1.25), ('Space', 3.75, 6.25), ('Alt', 10, 1.25), ('Ctrl', 13.5, 1.5)]),
    ]
    width, height = int(ox * 2 + 15 * u), int(oy + 6.5 * u + 70)
    im = Image.new('RGB', (width, height), (250, 250, 252))
    d = ImageDraw.Draw(im)
    d.text((ox, 14), '1P 的键盘（2P 用方向键 + 小键盘，见下表）', font=font_leg, fill=(40, 40, 50))
    for y, keys in rows:
        for name, x, w in keys:
            x0, y0 = ox + x * u + 3, oy + y * u + 3
            x1, y1 = ox + (x + w) * u - 3, oy + (y + 1) * u - 3
            colour, label = used.get(name, (None, None))
            if colour is None:
                d.rounded_rectangle((x0, y0, x1, y1), 8, fill=(236, 236, 240), outline=(205, 205, 212))
                d.text((x0 + 8, y0 + 6), name, font=ImageFont.truetype(FONT, 16), fill=(170, 170, 178))
                continue
            d.rounded_rectangle((x0, y0, x1, y1), 8, fill=colour, outline=tuple(max(0, c - 40) for c in colour), width=2)
            d.text((x0 + 8, y0 + 4), name, font=font_key if len(name) <= 3 else ImageFont.truetype(FONT_BOLD, 20), fill='white')
            d.text((x0 + 8, y1 - 22), label, font=font_lab, fill='white')
    legend = [(MOVE, '移动 / 跳 / 蹲（按住远离对手的方向 = 防御）'), (ATTACK, '普通攻击'), (SKILL, '技能'), (OK, '选人确认'), (FUNC, '功能键')]
    x = ox
    for colour, text in legend:
        d.rounded_rectangle((x, height - 52, x + 26, height - 26), 5, fill=colour)
        d.text((x + 34, height - 55), text, font=font_leg, fill=(40, 40, 50))
        x += 34 + d.textlength(text, font=font_leg) + 34
    im.save(path)


def draw_roster(path):
    """The select cards side by side, the name under each."""
    cards = []
    for cid, name in ROSTER:
        p = os.path.join(PROJECT, 'Assets', 'RoeFighter', 'Generated', cid, f'{cid}_portrait.png')
        if os.path.exists(p):
            cards.append((Image.open(p).convert('RGB').resize((180, 240)), name))
    if not cards:
        return False
    rows = (len(cards) + 6) // 7                    # at most 7 a row, the rows as even as they go (13: 7 and 6)
    cols = (len(cards) + rows - 1) // rows
    w, h = 180, 240 + 40
    im = Image.new('RGB', (cols * w + (cols + 1) * 10, rows * h + (rows + 1) * 10), (24, 24, 30))
    d = ImageDraw.Draw(im)
    for k, (card, name) in enumerate(cards):
        x, y = 10 + (k % cols) * (w + 10), 10 + (k // cols) * (h + 10)
        im.paste(card, (x, y))
        size = 22                                   # a long name (PINK CRANE KART) smaller, to stay under its card
        while size > 12 and d.textlength(name, font=ImageFont.truetype(FONT_BOLD, size)) > w - 6:
            size -= 1
        f = ImageFont.truetype(FONT_BOLD, size)
        tw = d.textlength(name, font=f)
        d.text((x + (w - tw) / 2, y + 246 + (22 - size) / 2), name, font=f, fill=(255, 225, 120))
    im.save(path, quality=90)
    return True


# ---- the text

def inline(text):
    text = html.escape(text, quote=False)
    text = re.sub(r'`([^`]+)`', r'<code>\1</code>', text)
    text = re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', text)
    return text


def convert(md, pictures):
    """The manual's Markdown (headings, paragraphs, nested lists, tables, picture markers) as HTML; the table of contents."""
    out, toc = [], []
    lines = md.replace('\r\n', '\n').split('\n')
    i = 0
    para = []
    list_stack = []                     # indent of each open <ul>

    def flush_para():
        if para:
            out.append(f'<p>{inline(" ".join(para))}</p>')
            para.clear()

    def close_lists(to=-1):
        while list_stack and list_stack[-1] > to:
            out.append('</li></ul>')
            list_stack.pop()

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        m = re.match(r'<!--\s*图:\s*([^|]+?)\s*\|\s*(.+?)\s*-->', stripped)
        if m:
            flush_para()
            close_lists()
            name, caption = m.group(1), m.group(2)
            if name in pictures:
                out.append(f'<figure><img src="manual/{html.escape(name)}" alt="{html.escape(caption)}">'
                           f'<figcaption>{inline(caption)}</figcaption></figure>')
            i += 1
            continue
        if not stripped:
            flush_para()
            i += 1
            continue
        h = re.match(r'^(#{1,3})\s+(.*)$', line)
        if h:
            flush_para()
            close_lists()
            level, title = len(h.group(1)), h.group(2)
            anchor = f's{len(toc) + 1}' if level == 2 else ''
            if level == 2:
                toc.append((anchor, title))
            attr = f' id="{anchor}"' if anchor else ''
            out.append(f'<h{level}{attr}>{inline(title)}</h{level}>')
            if level == 1:
                out.append('<!--TOC-->')
            i += 1
            continue
        if stripped.startswith('|'):
            flush_para()
            close_lists()
            rows = []
            while i < len(lines) and lines[i].strip().startswith('|'):
                rows.append([c.strip() for c in lines[i].strip().strip('|').split('|')])
                i += 1
            head, body = rows[0], [r for r in rows[1:] if not all(re.fullmatch(r':?-{3,}:?', c) for c in r)]
            out.append('<div class="table"><table><thead><tr>' + ''.join(f'<th>{inline(c)}</th>' for c in head) + '</tr></thead><tbody>')
            for r in body:
                out.append('<tr>' + ''.join(f'<td>{inline(c)}</td>' for c in r) + '</tr>')
            out.append('</tbody></table></div>')
            continue
        li = re.match(r'^(\s*)- (.*)$', line)
        if li:
            flush_para()
            indent = len(li.group(1))
            if not list_stack or indent > list_stack[-1]:
                out.append('<ul><li>')
                list_stack.append(indent)
            else:
                close_lists(indent)
                out.append('</li><li>')
            out.append(inline(li.group(2)))
            i += 1
            continue
        if list_stack and line.startswith(' '):
            out.append(' ' + inline(stripped))     # an item's continuation line
            i += 1
            continue
        close_lists()
        para.append(stripped)
        i += 1
    flush_para()
    close_lists()
    nav = '<nav class="toc"><b>目录</b><ol>' + ''.join(f'<li><a href="#{a}">{inline(t)}</a></li>' for a, t in toc) + '</ol></nav>'
    return '\n'.join(out).replace('<!--TOC-->', nav, 1)


CSS = """
:root { --bg: #f6f6f8; --card: #ffffff; --text: #1f2330; --muted: #5d6475; --line: #e2e4ea; --accent: #c0392b;
        --head: #2b2f3a; --code: #eef0f4; }
@media (prefers-color-scheme: dark) {
  :root { --bg: #15161a; --card: #1e2026; --text: #e7e8ec; --muted: #a2a7b4; --line: #33363f; --accent: #ff7b6b;
          --head: #f0f1f4; --code: #2a2d35; }
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text); line-height: 1.75;
       font-family: "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", sans-serif; font-size: 16px; }
main { max-width: 1000px; margin: 0 auto; padding: 24px 16px 64px; }
h1 { font-size: 34px; margin: 8px 0 12px; color: var(--head); }
h2 { font-size: 24px; margin: 40px 0 12px; padding-bottom: 6px; border-bottom: 2px solid var(--accent); color: var(--head); }
h3 { font-size: 19px; margin: 26px 0 8px; color: var(--head); }
p, li { color: var(--text); }
ul { padding-left: 1.4em; margin: 6px 0; }
code { background: var(--code); padding: 1px 6px; border-radius: 4px; font-family: Consolas, monospace; font-size: 0.92em; }
.table { overflow-x: auto; margin: 12px 0; }
table { border-collapse: collapse; width: 100%; background: var(--card); }
th, td { border: 1px solid var(--line); padding: 7px 10px; text-align: left; vertical-align: top; }
th { background: var(--code); white-space: nowrap; }
figure { margin: 18px 0; background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 10px; }
figure img { width: 100%; height: auto; border-radius: 6px; display: block; }
figcaption { color: var(--muted); font-size: 14px; margin-top: 6px; }
.toc { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 10px 18px; margin: 16px 0; }
.toc ol { margin: 4px 0 0; padding-left: 0; list-style: none; columns: 2; }
.toc a { color: var(--accent); text-decoration: none; }
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--md', default=os.path.join(PROJECT, 'docs', 'game-manual.md'))
    ap.add_argument('--out', default=os.path.join(PROJECT, 'out', 'ROEFighter2', '游戏说明.html'))
    ap.add_argument('--shot', action='append', default=[], help='name=path of a screenshot (select, fight, skill)')
    ap.add_argument('--open', action='store_true', help='open the page in the browser')
    a = ap.parse_args()
    pics = os.path.join(os.path.dirname(a.out), 'manual')
    os.makedirs(pics, exist_ok=True)
    draw_keys(os.path.join(pics, 'keys.png'))
    draw_roster(os.path.join(pics, 'roster.jpg'))
    for s in a.shot:
        name, path = s.split('=', 1)
        Image.open(path).convert('RGB').save(os.path.join(pics, name + '.jpg'), quality=90)
    pictures = set(os.listdir(pics))
    md = open(a.md, encoding='utf-8').read()
    body = convert(md, pictures)
    title = re.search(r'^#\s+(.*)$', md, re.M).group(1)
    page = (f'<!doctype html>\n<html lang="zh-CN"><head><meta charset="utf-8">'
            f'<meta name="viewport" content="width=device-width, initial-scale=1"><title>{html.escape(title)}</title>'
            f'<style>{CSS}</style></head><body><main>\n{body}\n</main></body></html>\n')
    open(a.out, 'w', encoding='utf-8').write(page)
    used = sorted(p for p in pictures if f'manual/{p}' in page)
    print(f'{a.out}: {len(page) // 1024} KB, pictures {", ".join(used)}')
    if a.open:
        os.startfile(a.out)


if __name__ == '__main__':
    main()
