# Pull the VideoClips out of a game bundle (e.g. the gacha reveal videos, which show a
# character as the game itself renders it - a reference for tuning our shaders).
#   python extract_video.py <bundle file> <out dir>
import io
import os
import sys

import UnityPy


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    bundle, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    env = UnityPy.load(bundle)
    for obj in env.objects:
        if obj.type.name != 'VideoClip':
            continue
        clip = obj.read()
        res = clip.m_ExternalResources
        name = clip.m_Name
        ext = os.path.splitext(clip.m_OriginalPath)[1] or '.mp4'
        data = None
        src = os.path.basename(res.m_Source)
        for path, f in env.file.files.items() if hasattr(env.file, 'files') else []:
            if os.path.basename(path) == src:
                r = f if hasattr(f, 'read') else None
                if r is not None:
                    r.seek(res.m_Offset)
                    data = r.read(res.m_Size)
        if data is None:
            # fall back to UnityPy's resource lookup
            from UnityPy.helpers.ResourceReader import get_resource_data
            data = get_resource_data(res.m_Source, obj.assets_file, res.m_Offset, res.m_Size)
        target = os.path.join(out, name + ext)
        open(target, 'wb').write(bytes(data))
        print(f'{name}: {clip.Width}x{clip.Height}, {clip.m_FrameCount} frames at {clip.m_FrameRate:.2f} fps, '
              f'{len(data) / 1e6:.1f} MB -> {target}')


if __name__ == '__main__':
    main()
