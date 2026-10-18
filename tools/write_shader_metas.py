# Give this project's shaders fixed GUIDs (the same ones import_ripped.py writes into the
# game's materials).  Run once after adding a shader; existing .meta files are rewritten
# only when the GUID differs.
import io
import os
import sys

from import_ripped import PROJECT, SHADER_MAP, shader_guid

META = """fileFormatVersion: 2
guid: {guid}
ShaderImporter:
  externalObjects: {{}}
  defaultTextures: []
  nonModifiableTextures: []
  preprocessorOverride: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    folder = os.path.join(PROJECT, 'Assets', 'RoeFighter', 'Shaders')
    for stem in sorted(set(SHADER_MAP.values())):
        shader = os.path.join(folder, stem + '.shader')
        if not os.path.exists(shader):
            print('missing', shader)
            continue
        guid = shader_guid(stem)
        meta = shader + '.meta'
        text = META.format(guid=guid)
        if os.path.exists(meta) and ('guid: ' + guid) in open(meta, encoding='utf-8').read():
            print('ok     ', stem, guid)
            continue
        open(meta, 'w', encoding='utf-8', newline='\n').write(text)
        print('written', stem, guid)


if __name__ == '__main__':
    main()
