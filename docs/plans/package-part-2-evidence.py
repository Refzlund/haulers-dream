"""Create a source-only review package/list. Never stage, launch, fetch, or mutate product inputs."""
from pathlib import Path
from collections import Counter
import hashlib,json,re,subprocess,zipfile

repo=Path(__file__).resolve().parents[2];plans=repo/'docs/plans';evidence=plans/'evidence'
fixture_roots='''f02-own-inventory-20260924
f02-own-inventory-20260924/final-v10
f02-own-inventory-20260924/boundary-v11
f03-construction-20260920
f03-harvest-20260920
f03-harvest-hah-20260920
f04-rendered-20260920
f05-f06-robots-20260924
f05-f06-lifecycle-20260924
f07-rimmsqol-20260920/queued-save-v14/fixture
f08-alerts-20260920
f11-quantity-lifecycle-20260924
f11-quantity-lifecycle-20260924/v16-drafted-forbidden
f11-quantity-lifecycle-20260924/providers/v2-keep-lifetime
f11-quantity-lifecycle-20260924/fault-recovery/fault-diagnostics-v2
f11-quantity-lifecycle-20260924/fault-recovery/fault-diagnostics-v2/baseline-settings-v3
f11-quantity-lifecycle-20260924/fault-recovery/stability-input-v4
f12-explicit-point-20260924/native-fixture
f12-explicit-lifecycle-20260924/fault-fanout-v7
f12-explicit-ui-20260924/native-robot-v3
f12-explicit-ui-20260924/native-ui-v7-checkbox
f13-selected-shelf-20260924/native-fixture
f13-selected-shelf-20260924/native-late-shelf
f13-selected-shelf-20260924/native-late-shelf/baseline-walkable
f20-refill-20260920/fixture
f24-work-cadence-20260924/fixture
f25-sapient-persistence-20260924/native-fixture
f33-delivery-20260920
f34-live-20260920
f35-finish-wild-20260920/fixture
f36-build-storage-20260924/fixture
f38-handoff-source-20260924/native-fixture
f38-handoff-source-20260924/capacity-replay
f38-handoff-source-20260924/load-replay-v4
f40-transporter-20260924/native-fixture
f40-transporter-20260924/persistence
f40-transporter-20260924/persistence/consumer-v6
f40-transporter-20260924/ce-carrier-v6b
f40-transporter-20260924/shuttle-vf
f41-unfinished-implementation-20260924/native-fixture
f43-warning-20260924/native-fixture
retained-unfinished-20260924/f09-rcv1
retained-unfinished-20260924/af1
f45-boundaries-20260920
f45-contract-rejection-20260920
f45-menu-20260920
f45-pb-clone-20260920
f45-production-20260920
f45-restart-20260920
f45-shared-menu-20260920'''.splitlines()

def rel(p):return p.relative_to(repo).as_posix()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def pin(p,category):return dict(path=rel(p),sha256=sha(p),bytes=p.stat().st_size,category=category)
def snapshot(p):
    parts=[x.lower() for x in p.relative_to(repo).parts]
    return '.before.' in p.name.lower() or '.after.' in p.name.lower() or ('staging-review' in parts and 'files' in parts) or any(x in {'before','after','proposal','source','sources','bin','obj','.changeset','adapter-ancestors'}
        or 'before-' in x or x.startswith('fixture-source-') for x in parts)
def foreign(p):
    name=p.name.lower();parts=[x.lower() for x in p.parts]
    return any(x in {'proposal','source-proof','native-source','decompiled','compiled-hd','compiled-core'} for x in parts) or any(t in name for t in ['.native.','.decompiled.','.actual.','assembly-csharp','proposal.diff'])

direct={}
for p in (repo/'docs').rglob('*.md'):
    if snapshot(p) or foreign(p) or 'controller' in p.parts or 'host' in p.parts:continue
    direct[p]='authored-plan-review-or-history'
for name in ['scoped-feedback-register.json','f10-activity-wording-verification.json','part-2-pr-source-map-audit.json','check-feedback-inventory.py','check-part-2-handoff.py','package-part-2-evidence.py','retain-unfinished-fixture-sources.py']:
    p=plans/name
    if p.exists():direct[p]='scope-or-owned-audit-tool'
direct[evidence/'f11-quantity-lifecycle-20260924/ROOT-FINAL-SOURCE-MATCH.json']='final-tested-source-match'
for name in ['part1-permission-guard-review.json','part-1-final-guards-20260924.json','check-final-permission-guard.py','retained-unfinished-20260924/retained-source-index.json']:
    p=evidence/name
    if p.exists():direct[p]='explicit-final-qa-or-source-retention'
for p in evidence.rglob('*.diff'):
    if not snapshot(p) and not foreign(p):direct[p]='owned-source-delta-history'
# Include compact authored audit summaries, not raw results, copied-input manifests, or giant pin lists.
for p in evidence.rglob('*.json'):
    if snapshot(p) or foreign(p) or any(x in p.parts for x in ['raw','controller','host']):continue
    if p.stat().st_size>131072:continue
    if p.name.startswith('independent-') and any(t in p.name for t in ['audit','facts','review']):direct[p]='compact-independent-audit'

archive=set();allowed={'.cs','.csproj','.xml','.ps1','.py','.config','.ts'}
root_counts=[]
for name in fixture_roots:
    root=evidence/name;assert root.is_dir(),name;owned=set()
    for sub in ['host','src','src-v3','controller']:
        d=root/sub
        if d.exists():
            owned.update(p for p in d.rglob('*') if p.is_file() and p.suffix.lower() in allowed
                and not any(x in {'bin','obj'} for x in p.relative_to(d).parts) and not foreign(p))
    owned.update(p for p in root.iterdir() if p.is_file() and p.suffix.lower() in {'.ps1','.py','.config','.xml'}
        and not foreign(p) and not any(t in p.name.lower() for t in ['audit','metadata','capture','snapshot','read-host']))
    if name=='retained-unfinished-20260924/af1':
        # Authored historical profiles, not copied runtime manifests or logs.
        owned.update((root/'controller/profiles').glob('*.json'))
    archive.update(owned);root_counts.append(dict(path=rel(root),files=len(owned),bytes=sum(p.stat().st_size for p in owned)))

ordinary=set(p for p in (repo/'tools/RuntimeHarness').rglob('*') if p.is_file() and p.suffix.lower() in allowed|{'.md'}
    and not any(x in {'bin','obj'} for x in p.relative_to(repo/'tools/RuntimeHarness').parts))
ordinary.update((repo/'scripts').glob('runtime-*.ps1'));ordinary.add(repo/'scripts/run-on-test-desktop.py')
ordinary.update((repo/'scripts').glob('check-*.ts'))

secret_patterns={
    'github-token':re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{35,})\b'),
    'private-key':re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    'aws-access-key':re.compile(r'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'),
    'openai-key':re.compile(r'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{35,}\b'),
    'literal-bearer':re.compile(r'(?i)\bBearer\s+[A-Za-z0-9._-]{28,}')}
findings=[];absolute_paths=[];decompile_namespaces=[]
for p in sorted(set(direct)|archive|ordinary):
    text=p.read_text(encoding='utf-8-sig',errors='replace')
    for kind,pattern in secret_patterns.items():
        for m in pattern.finditer(text):findings.append(dict(path=rel(p),line=text.count('\n',0,m.start())+1,kind=kind))
    if re.search(r'[A-Za-z]:[\\/](?:Users|HDQA|Steam)[\\/]',text):absolute_paths.append(rel(p))
    if p.suffix=='.cs' and re.search(r'^namespace\s+(?:Verse|RimWorld|UnityEngine|CombatExtended|SimpleSidearms)(?:[.;\s])',text,re.M):decompile_namespaces.append(rel(p))
assert not decompile_namespaces,('Non-owned namespace needs review',decompile_namespaces)
assert not findings,('Possible credential requires review; values deliberately not printed',findings)

zip_path=plans/'part-2-fixture-sources.zip'
entries=[pin(p,'authored-fixture-source') for p in sorted(archive)]
with zipfile.ZipFile(zip_path,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for p in sorted(archive):
        info=zipfile.ZipInfo(rel(p),(2026,9,24,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o644<<16
        z.writestr(info,p.read_bytes())
with zipfile.ZipFile(zip_path) as z:
    assert z.testzip() is None
    for e in entries:assert hashlib.sha256(z.read(e['path'])).hexdigest().upper()==e['sha256']
direct[zip_path]='source-only-fixture-archive'
metadata_path=plans/'part-2-evidence-files.json'
docs=[pin(p,c) for p,c in sorted(direct.items())]
normal=[pin(p,'runtime-source-or-check') for p in sorted(ordinary)]
included_paths=set(direct)|archive|ordinary;local=[];missing=[]
for p in direct:
    if p.suffix!='.md':continue
    text=p.read_text(encoding='utf-8-sig',errors='replace')
    for target in re.findall(r'\]\(([^)]+)\)',text):
        if '://' in target or target.startswith('#') or target.startswith('<'):continue
        target=target.split('#')[0]
        q=(p.parent/target).resolve()
        if not q.is_relative_to(repo):continue
        if q.is_file() and q not in included_paths:local.append(dict(fromPath=rel(p),target=rel(q),bytes=q.stat().st_size,reason='machine-local captured evidence, proprietary reference, frozen input or historical snapshot'))
        elif not q.exists():missing.append(dict(fromPath=rel(p),target=rel(q),reason='historical or external evidence unavailable at packaging audit; no proof fabricated'))
data=dict(schemaVersion=1,scope='Explicit publication allowlist; no automatic staging or public upload',
    docsForceAddPaths=[e['path'] for e in docs]+[rel(metadata_path)],docsFiles=docs,ordinarySourceFiles=normal,
    fixtureArchive=dict(file=pin(zip_path,'source-only-fixture-archive'),entries=entries,roots=root_counts,
        uncompressedBytes=sum(e['bytes'] for e in entries),instructions='Ordinary ZIP extraction restores exact relative source paths. Requires separately obtained game/provider inputs and reviewed fresh pins; never run historical manifests blindly.'),
    excludedPolicy=['No game/provider DLLs, executables, native decompiles or proprietary reference XML','No raw Player/debug logs, native result/event captures, saves or screenshots','No copied product trees, before/after snapshots or older duplicated fixture revisions','Large historical manifests, source pin graphs and raw original feedback inventory stay machine-local'],
    machineLocalReferences=local,missingHistoricalReferences=missing,
    audit=dict(secretPatterns=list(secret_patterns),secretFindings=[],absoluteMachinePathFiles=absolute_paths,
        absolutePathsAreContextNotCredentials=True,ownedCsNamespaceCheckPassed=True,archiveCrcAndSha256Passed=True,
        docsFiles=len(docs),docsBytes=sum(e['bytes'] for e in docs),runtimeSourceFiles=len(normal),runtimeSourceBytes=sum(e['bytes'] for e in normal),
        noStageCommitNativeOrNetwork=True))
metadata_path.write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print(json.dumps(dict(docsFiles=len(docs),docsBytes=sum(e['bytes'] for e in docs),metadataBytes=metadata_path.stat().st_size,archiveFiles=len(entries),archiveRawBytes=sum(e['bytes'] for e in entries),archiveBytes=zip_path.stat().st_size,ordinaryFiles=len(normal),machineLocalLinks=len(local),missingHistoricalLinks=len(missing),secretFindings=findings)))
