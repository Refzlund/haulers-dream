"""Retain selected first-party sources from old private builds; never execute them."""
from pathlib import Path
import hashlib, json, re, zlib

repo = Path(__file__).resolve().parents[2]
base = Path('C:/Users/Arthur/AppData/Local/Temp')
dest = repo/'docs/plans/evidence/retained-unfinished-20260924'
copies = []
missing = []

def retain(source, target):
    if not source.is_file():
        missing.append(str(source)); return
    data = source.read_bytes()
    if source.suffix == '.cs' and re.search(rb'^namespace\s+(?:Verse|RimWorld|UnityEngine|CombatExtended|SimpleSidearms)\b', data, re.M):
        raise RuntimeError('Refusing third-party source: '+str(source))
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.read_bytes() != data:
        raise RuntimeError('Retained source already differs: '+str(target))
    target.write_bytes(data)
    copies.append(dict(original=str(source), path=target.relative_to(repo).as_posix(),
                       bytes=len(data), sha256=hashlib.sha256(data).hexdigest().upper()))

def tree(original, target, extensions):
    for p in sorted(original.rglob('*')):
        if p.is_file() and p.suffix.lower() in extensions and not any(x in {'bin','obj','references','packages','Assemblies'} for x in p.relative_to(original).parts):
            retain(p, target/p.relative_to(original))

# Complete selected host projects, not just the changed methods. No compiled/reference trees.
for origin, target in [
    ('haulersdream-rcv1-destroy-count-root-build-20260920/src','f09-rcv1/host'),
    ('haulersdream-af1-observer-order-root-build-20260920/src','af1/host'),
    ('haulersdream-af1-observer-companion-root-build-20260920/src','af1/src/companion')]:
    tree(base/origin,dest/target,{'.cs','.csproj'})

rcv = base/'haulersdream-rcv1-destroy-count-binding-author-20260920'
tree(rcv/'scripts',dest/'f09-rcv1/controller/scripts',{'.ps1'})
tree(rcv/'tools',dest/'f09-rcv1/controller/tools',{'.xml'})
tree(rcv/'controls',dest/'f09-rcv1/controller/controls',{'.ps1','.py'})
for name in ['CURRENT-HANDOFF.md','NATIVE-CONTROL-PLAN.md','COMPILED-NEXT.md']:
    retain(rcv/name,dest/'f09-rcv1'/name)
# Retain the later corrected data-only reader separately; do not silently rewrite an old controller.
tree(base/'haulersdream-rcv1-home-sentinel-root-author-20260920',dest/'f09-rcv1/controller/final-reader',{'.ps1'})
tree(base/'haulersdream-rcv1-home-sentinel-execution-author-20260920',dest/'f09-rcv1/controller/final-reader-recipes',{'.ps1','.py'})
retain(base/'haulersdream-rcv1-home-sentinel-execution-author-20260920/HANDOFF.md',dest/'f09-rcv1/FINAL-READER-HANDOFF.md')

af1 = base/'haulersdream-af1-observer-runtime-binding-author-20260920'
profile = json.loads((af1/'control-source-profile.json').read_text(encoding='utf-8-sig'))
for key, target in [('baseController','controller/base/runtime-test.ps1'),('selectedController','controller/scripts/control.ps1'),('companionAbout','src/companion/About/About.xml')]:
    p=Path(profile[key]['path'])
    if p.exists():
        assert hashlib.sha256(p.read_bytes()).hexdigest().upper()==profile[key]['sha256']
    retain(p,dest/'af1'/target)
for name in ['control-source-profile.json','bound-profile.json']:
    retain(af1/name,dest/'af1/controller/profiles'/name)
retain(af1/'HANDOFF.md',dest/'af1/BINDING-HANDOFF.md')
tree(base/'haulersdream-af1-observer-launch-verify-author-20260920/launch',dest/'af1/controller/launch',{'.py','.ps1'})
tree(base/'haulersdream-af1-observer-launch-verify-author-20260920/verify',dest/'af1/controller/verify',{'.py','.ps1'})
retain(base/'haulersdream-af1-observer-native-independent-20260920/final-review.md',dest/'af1/ACCEPTED-DIAGNOSTIC-REVIEW.md')

report=dict(scope='Authored source retention only; no build/controller/native/package execution',
            copies=copies, unavailableHistoricalInputs=missing,
            totalBytes=sum(x['bytes'] for x in copies),
            estimatedDeflatedBytes=sum(len(zlib.compress((repo/x['path']).read_bytes(),9)) for x in copies))
(dest/'retained-source-index.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='copies'}))
