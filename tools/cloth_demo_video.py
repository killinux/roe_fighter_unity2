# Side-by-side video of a RoeClothDemo run: the takes of each fighter next to each other (bone cloth
# off | on, or one take per motion pack), one fighter after the other, the current move written
# under each take.
#   python cloth_demo_video.py [demo dir] [out.mp4] [--variants off,on] [--slow 2]
#   python cloth_demo_video.py _work\pack_demo out\motion_packs_demo.mp4
import argparse
import os
import subprocess

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'
FONT = r'C:\Windows\Fonts\msyh.ttc'
STEPS = {'clip:idle_02': '展示待机（idle_02）', 'clip:react_01': '展示反应 1（react_01）', 'clip:react_02': '展示反应 2（react_02）', 'guard': '站架', 'walk': '前进', 'back': '后退', 'side step': '侧步', 'skill 1': '技能 1（游戏动作）', 'skill 2': '技能 2（游戏动作）',
         'clip:idle_01': '游戏的战斗站姿（idle_01，裙子是游戏手 K）',
         'key 1': '按 1：技能 1（游戏的 skill_01）', 'key 2': '按 2：技能 2（游戏的 skill_02）', 'key 3': '按 3：超必杀（游戏的 skill_03）'}
# the attack buttons A-D on 1P's keyboard
KEYS = {'A': 'J', 'B': 'K', 'C': 'U', 'D': 'I'}
MOVES = {'jab': '刺拳', 'cross': '直拳', 'straight': '直拳', 'kick': '回旋踢', 'slash': '挥砍', 'hook': '勾拳',
         'uppercut': '上勾拳', 'roundhouse_lead': '前腿回旋踢', 'front_kick': '前踢', 'side_kick': '侧踢', 'roundhouse': '后腿回旋踢', 'low_kick': '低踢',
         'high_kick': '高踢', 'knee': '膝撞', 'elbow': '肘击', 'lunge_punch': '冲拳', 'reverse_punch': '逆冲拳',
         'back_fist': '里拳', 'chop': '手刀',
         'mai_jab': '冲步刺拳', 'mai_elbow': '肘击', 'mai_lunge': '扑身冲拳', 'mai_high_kick': '高踢',
         'kas_00051': '刺拳', 'kas_00055': '侧踢', 'kas_00185': '上勾拳', 'kas_00054': '高位回旋踢',
         'kyle_light_punch': '轻拳', 'kyle_light_kick': '轻脚', 'kyle_heavy_punch': '重拳', 'kyle_heavy_kick': '重脚（高踢）',
         'ethan_n1': '刺拳', 'ethan_n2': '前腿踢', 'ethan_n3': '转身后踢', 'ethan_f3': '冲步飞踢',
         'bot_light_punch': '轻拳', 'bot_light_kick': '轻脚', 'bot_heavy_punch': '重拳', 'bot_heavy_kick': '重脚'}
NAMES = {'g04': 'g04 Luf', 'a08': 'a08 Inase', 'b10': 'b10 Kart', 'g05': 'g05 Luf', 'kas': '霞（DOA6）', 'kas011': '霞 海盗裙（DOA6）',
         'fio005': 'Fiona PCF_005（Vindictus）', 'eve09': 'Eve 7 代潜降服（剑星）', 'mai': '不知火舞（DOA6）', 'eve37': 'Eve 百褶裙（剑星）'}
TITLES = {'off': '布料关', 'on': '布料开（RoeBoneCloth）', 'legacy': '旧版布料（10-02）', 'magica_style': '我们的骨骼布料（照 Magica 的做法）', 'swing': '骨骼布料，裙子照游戏手 K 甩（新）',
          'roe': '完全照 ROE：动起来也用游戏的物理参数',
          'roe_speedLimit_0': '照 ROE，只去掉游戏的每秒 1 米限速',
          'swing_old': '原来：站着也是模拟',
          ('g04', 'swing'): '现在：站着用手 K，动起来 swing',
          'rest_guard': '裙子以动捕站架为基准（第一版）', 'rest_stance': '裙子以游戏站姿为基准（52f11de）', 'drape': '裙子自然下垂（新）',
          'pack': '原来的普通攻击（Bandai 动捕）', 'own': '不知火舞的普通攻击（DOA6）',
          'guard_pack': '原来的站架（动作包的）', 'guard_stance': '现在：站架 = 游戏的战斗站姿（idle_01）',
          ('g04', 'guard_pack'): '原来：不知火舞的站架（DOA6）', ('g05', 'guard_pack'): '原来：不知火舞的站架（DOA6）',
          'game': '原来：照游戏浮空，飘带照关键帧', 'floor': '现在：受击倒地落地，技能升空，飘带模拟',
          'doa6': 'DOA6 自己的物理（软体胸、骨链、摆动骨）', 'doa6_breasts': '胸用 DOA6 软体，其余骨骼布料',
          'doa5lr_style': 'DOA5LR 式弹簧网（同样的骨链，换成 DOA5LR 的弹簧）',
          'magica': 'Magica Cloth 2 插件本身（同样的骨链和碰撞体）',
          ('a08', 'magica'): 'Magica Cloth 2（裙片 MeshCloth）',
          ('g04', 'magica'): 'Magica Cloth 2（裙片 MeshCloth）',
          ('kas011', 'magica'): 'Magica Cloth 2（网格布 BoneCloth）',
          ('kas011', 'doa6'): 'DOA6 自己的物理（网格布裙和袖）',
          ('kas011', 'magica_style'): '我们的骨骼布料（网格布的每一列当一条链）',
          'kawaii': '她游戏自己的（KawaiiPhysics）',
          'stellar': '剑星自己的（弹簧骨 + KawaiiPhysics + PhysX 刚体）',
          ('eve09', 'magica_style'): '我们的骨骼布料（披风没认出来）',
          ('eve09', 'stellar'): '剑星自己的（弹簧骨+Kawaii+刚体）',
          ('eve37', 'stellar'): '剑星自己的（Kawaii + Control Rig）',
          ('eve37', 'stellar_norig'): '剑星的 Kawaii，不跑 Control Rig',
          ('mai', 'doa6'): 'DOA6 自己的（软体、网格布、骨链）',
          'keys': '游戏手 K 的裙子（标准答案）', 'mc2_nokeys': 'Magica Cloth 2（不用关键帧）',
          'magica_full': 'Magica Cloth 2（全部交给它，插件自带预设）', 'old_default': '原来的：骨骼布料，技能用游戏关键帧',
          'ours_nokeys': '我们的骨骼布料（不用关键帧）', 'legs_nokeys': '只跟着腿（不模拟、不用关键帧）',
          'bandai1': 'Bandai 动捕（原来的默认）', 'accad_male2': 'ACCAD 开源动捕',
          'ufe_kyle': 'UFE 2：Robot Kyle', 'ufe_ethan': 'UFE 2：Ethan', 'ufe_bot': 'UFE 2：Mecanim Bot'}


def read_takes(folder):
    """{(id, variant): (about, {button: move})} from takes.txt (older runs: cloth off/on, Bandai moves)."""
    path = os.path.join(folder, 'takes.txt')
    takes = {}
    if not os.path.exists(path):
        bandai = {'A': 'jab', 'B': 'slash', 'C': 'cross', 'D': 'kick'}
        for cid in NAMES:
            for v in ('off', 'on'):
                takes[(cid, v)] = ('', bandai)
        return takes
    for line in open(path, encoding='utf-8'):
        cols = line.rstrip('\n').split('\t')
        if len(cols) < 4:
            continue
        moves = dict(kv.split('=', 1) for kv in cols[3].split('|') if '=' in kv)
        takes[(cols[0], cols[1])] = (cols[2], moves)
    return takes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir', nargs='?', default=os.path.join(PROJECT, '_work', 'cloth_demo'))
    ap.add_argument('out', nargs='?', default=os.path.join(PROJECT, 'out', 'bone_cloth_demo.mp4'))
    ap.add_argument('--chars', default='', help='fighters, one after the other (default: as filmed - takes.txt; older runs g04,a08)')
    ap.add_argument('--variants', default='', help='takes to put side by side, left to right (default: as filmed)')
    ap.add_argument('--fps', type=int, default=30)
    ap.add_argument('--slow', type=float, default=1.0, help='play this many times slower')
    ap.add_argument('--suffix', default='', help='added to every title, e.g. the motion pack')
    a = ap.parse_args()
    script = []
    for line in open(os.path.join(a.dir, 'script.txt'), encoding='utf-8'):
        t, what = line.strip().split(' ', 1)
        script.append((float(t), what))
    takes = read_takes(a.dir)
    if not a.chars:
        filmed = os.path.join(a.dir, 'takes.txt')
        ids = [line.split('\t', 1)[0] for line in open(filmed, encoding='utf-8')] if os.path.exists(filmed) else ['g04', 'a08']
        a.chars = ','.join(dict.fromkeys(i for i in ids if i))
    big = ImageFont.truetype(FONT, 34)
    small = ImageFont.truetype(FONT, 28)
    colours = [(255, 255, 255), (255, 225, 110), (140, 220, 255), (200, 255, 160)]
    ff = None
    count = 0
    for cid in a.chars.split(','):
        variants = a.variants.split(',') if a.variants else [v for (c, v) in takes if c == cid]
        frames = [sorted(os.listdir(os.path.join(a.dir, f'{cid}_{v}'))) for v in variants]
        for k in range(min(len(f) for f in frames)):
            ims = [Image.open(os.path.join(a.dir, f'{cid}_{v}', frames[n][k])) for n, v in enumerate(variants)]
            w, h = ims[0].size
            frame = Image.new('RGB', (w * len(ims), h + 56), (18, 18, 18))
            d = ImageDraw.Draw(frame)
            t = k * 2 / 60.0
            what = ''
            for start, step in script:
                if t >= start - 0.05:
                    what = step
            for n, (v, im) in enumerate(zip(variants, ims)):
                frame.paste(im, (w * n, 0))
                about, moves = takes.get((cid, v), ('', {}))
                title = (TITLES.get((cid, v)) or TITLES.get(v) or (about.split(';')[0] if about else v)) + a.suffix
                d.text((w * n + 16, 10), f'{NAMES.get(cid, cid)}　{title}', font=big, fill=colours[n % len(colours)],
                       stroke_width=3, stroke_fill=(0, 0, 0))
                if what in moves:
                    move = moves[what]
                    label = f'{what} 键（{KEYS.get(what, what)}）：{MOVES.get(move, move)}' + (f'（{move}）' if move in MOVES else '')
                else:
                    label = STEPS.get(what) or MOVES.get(what, what)
                d.text((w * n + 16, h + 10), f'{label}　　{t:4.1f} s', font=small, fill=(220, 220, 220))
            if ff is None:
                ff = subprocess.Popen([FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                                       '-s', f'{frame.width}x{frame.height}', '-r', str(a.fps / a.slow), '-i', '-',
                                       '-c:v', 'libx264', '-crf', '18', '-preset', 'slow', '-pix_fmt', 'yuv420p', a.out],
                                      stdin=subprocess.PIPE)
            ff.stdin.write(frame.tobytes())
            count += 1
    ff.stdin.close()
    ff.wait()
    print(f'{a.out}: {count} frames, {count / (a.fps / a.slow):.1f} s, {os.path.getsize(a.out) / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
