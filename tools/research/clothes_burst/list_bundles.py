"""Read-only: list objects (type, name, size info) in a few ROE bundles with UnityPy."""
import sys, os, collections
import UnityPy
AB = r"D:/Program Files (x86)/Steam/steamapps/common/Rise of Eros/RiseOfEros_Data/StreamingAssets/AssetBundles"
for b in sys.argv[1:]:
    p = os.path.join(AB, b + '.ab')
    env = UnityPy.load(p)
    cnt = collections.Counter()
    rows = []
    for obj in env.objects:
        t = obj.type.name
        cnt[t] += 1
        if t in ('Mesh', 'GameObject', 'SkinnedMeshRenderer', 'Material', 'Texture2D', 'AnimationClip', 'MonoBehaviour', 'AssetBundle'):
            try:
                d = obj.read()
                nm = getattr(d, 'm_Name', None) or getattr(d, 'name', '')
                extra = ''
                if t == 'Mesh':
                    subs = d.m_SubMeshes
                    extra = 'verts=%s subs=%s shapes=%s' % (getattr(d.m_VertexData, 'm_VertexCount', '?'), [s.indexCount // 3 for s in subs], len(d.m_Shapes.shapes) if d.m_Shapes else 0)
                    if d.m_Shapes and d.m_Shapes.shapes:
                        names = []
                        # blend shape channel names
                        try:
                            names = [c.name for c in d.m_Shapes.channels]
                        except Exception:
                            pass
                        extra += ' channels=%s' % names[:40]
                if t == 'MonoBehaviour':
                    try:
                        sc = d.m_Script.read()
                        extra = 'script=%s' % sc.m_ClassName
                    except Exception:
                        pass
                rows.append((t, nm, extra))
            except Exception as e:
                rows.append((t, '?', 'ERR %s' % e))
    print('=====', b, dict(cnt))
    for r in rows:
        if r[0] in ('Mesh', 'MonoBehaviour', 'AnimationClip', 'Material') or (r[0] == 'GameObject' and len(rows) < 60):
            print('  ', r)
