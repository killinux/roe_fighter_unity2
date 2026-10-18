import sys, os, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bones_cmp import clusters, nude, suit
skip = re.compile(r'(?i)labia|clitoris|anus|vagina|cheek|chin|eyebrow|eyeball|eyelid|eye_|lip|mouth|tongue|teeth|jaw|nose|forehead|ear|face|uplid|lowlid|squint|brow')
for ch in ('a08', 'g04'):
    nc, nl = clusters(nude[ch]); sc, sl = clusters(suit[ch])
    miss = sorted(b for b in set(nc) - set(sl) if not skip.search(b))
    print('==', ch, 'body-relevant nude skin bones absent in suit skeleton:', len(miss))
    print('  ', miss)
    print('   suit bones with breast/chest/butt/twist in name:', sorted(b for b in sl if re.search(r'(?i)breast|chest|butt|twist|calfsub', b)))
