"""Exercise guard regressions in a private source copy, never the working source."""
from pathlib import Path
import hashlib, json, shutil, subprocess, tempfile

repo = Path(__file__).resolve().parents[3]
scratch = Path(tempfile.mkdtemp(prefix='hd-permission-guard-'))
for subtree in ['Source/HaulersDream', 'Source/HaulersDream.Core', 'Source/HaulersDream.Tests']:
    for src in (repo / subtree).glob('*.cs'):
        dst = scratch / subtree / src.name
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dst)
(scratch / 'scripts').mkdir()
for name in ['lib.ts', 'check-non-colony-pawn-gates.ts']:
    shutil.copyfile(repo / 'scripts' / name, scratch / 'scripts' / name)
bun = str(Path.home() / '.bun/bin/bun.exe')
rows = []
def run(name, expected):
    result = subprocess.run([bun, 'scripts/check-non-colony-pawn-gates.ts'], cwd=scratch,
                            capture_output=True, text=True, encoding='utf-8')
    rows.append({'case': name, 'exit': result.returncode, 'expected': expected,
                 'output': result.stdout + result.stderr})
    assert result.returncode == expected, rows[-1]
run('actual source', 0)
mutants = [
    ('hosted robot admitted', 'ExplicitHaulCommand.cs', 'p.HostFaction != null', 'p.HostFaction == null'),
    ('additional unreviewed host read', 'NearbyHaulCommand.cs', 'bool miscRobot =', 'var extraHost = pawn.HostFaction; bool miscRobot ='),
    ('loader JobOn faction refusal removed', 'TransportLoad.cs',
     'if (pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger())',
     'if (pawn.IsQuestLodger())'),
    ('prisoner host read removed', 'Patch_WorkGiver_UnloadCarriers.cs', '.HostFaction', '.Faction'),
]
for name, filename, before, after in mutants:
    path = scratch / 'Source/HaulersDream' / filename
    original = path.read_text(encoding='utf-8-sig')
    assert before in original
    if filename == 'TransportLoad.cs':
        # Change only the real job-building overload, not HasJob or its forwarder.
        marker = 'bool playerOrder, bool menuProbe, out bool wouldGive)'
        prefix, body = original.split(marker, 1)
        changed = prefix + marker + body.replace(before, after, 1)
    else:
        changed = original.replace(before, after, 1)
    path.write_text(changed, encoding='utf-8')
    run(name, 1)
    path.write_text(original, encoding='utf-8')
run('restored source', 0)
out = {'scratch': str(scratch), 'guardSha256': hashlib.sha256((repo/'scripts/check-non-colony-pawn-gates.ts').read_bytes()).hexdigest(), 'cases': rows}
(Path(__file__).parent/'part1-permission-guard-review.json').write_text(json.dumps(out, indent=2)+'\n', encoding='utf-8')
print(json.dumps([{'case': r['case'], 'exit': r['exit']} for r in rows]))
