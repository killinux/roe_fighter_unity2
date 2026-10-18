import sys, os, UnityPy
AB = r"D:/Program Files (x86)/Steam/steamapps/common/Rise of Eros/RiseOfEros_Data/StreamingAssets/AssetBundles"
for b in sys.argv[1:]:
    env = UnityPy.load(os.path.join(AB, b + '.ab'))
    for obj in env.objects:
        if obj.type.name == 'TextAsset':
            d = obj.read()
            raw = d.m_Script if isinstance(d.m_Script, (bytes, bytearray)) else d.m_Script.encode('utf-8', 'surrogateescape')
            print('==', b, d.m_Name, len(raw), 'bytes')
            print(raw[:600])
