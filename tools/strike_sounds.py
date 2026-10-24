# The block's sound for every fighter (RoeFightScene.StrikeSounds): ROE's light hit made into a dry tick - its first 0.14 s,
# high-passed (4 poles at 1.5 kHz: the thump goes, the crack stays), fading out over 0.1 s, peak at 0.7.
# Writes Assets/RoeFighter/Generated/sounds/block_tick.wav (Generated is never committed: it is made from the game's sound).
#   python tools/strike_sounds.py [light hit .ogg]
# Run before the scene build; without the file the build uses the light hit itself, softer, for blocks.
import os
import subprocess
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FFMPEG = r'D:\Program Files\ffmpeg\bin\ffmpeg.exe'
LIGHT = 'Assets/ROE/b01/sfx_battlefield_pc_b01_tutorial/SFX_H_battle_suit_12_skill01_1_hit.ogg'
OUT = 'Assets/RoeFighter/Generated/sounds/block_tick.wav'
RATE = 44100


def main():
    src = os.path.join(PROJECT, sys.argv[1] if len(sys.argv) > 1 else LIGHT)
    chain = 'atrim=0:0.14,highpass=f=1500:poles=2,highpass=f=1500:poles=2,afade=t=in:d=0.002,afade=t=out:st=0.03:d=0.1:curve=exp'
    raw = subprocess.run([FFMPEG, '-v', 'error', '-i', src, '-af', chain, '-ac', '1', '-ar', str(RATE), '-f', 's16le', '-'],
                         capture_output=True, check=True).stdout
    x = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    if len(x) == 0 or np.abs(x).max() < 1e-4:
        sys.exit(f'nothing came out of {src}')
    x *= 0.7 / np.abs(x).max()
    out = os.path.join(PROJECT, OUT)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with wave.open(out, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    env = np.sqrt(np.convolve(x * x, np.ones(441) / 441, mode='same'))
    print(f'{OUT}: {len(x) / RATE:.3f} s, peak 0.70, loudest 10 ms at {np.argmax(env) / RATE * 1000:.0f} ms, from {os.path.basename(src)}')


if __name__ == '__main__':
    main()
