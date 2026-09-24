"""Read-only publication coverage audit; never advance feedback statuses."""
from pathlib import Path
import hashlib,json,re
root=Path(__file__).resolve().parent
register=json.loads((root/'scoped-feedback-register.json').read_text(encoding='utf-8-sig'))
cases={c['id']:c for c in register['cases']}
assert len(cases)==46 and len(register['sources'])==78
resolved={k for k,c in cases.items() if c['completion']=='resolved'}
opened=set(cases)-resolved
assert len(resolved)==23 and len(opened)==23
book=(root/'part-2-handoff.md').read_text(encoding='utf-8-sig')
def row_ids(section):
    found=[]
    for line in section.splitlines():
        if line.startswith('| '):found.extend(re.findall(r'\bF\d{2}\b',line.split('|')[1]))
    assert len(found)==len(set(found)),found
    return set(found)
accepted=book.split('## Accepted report-specific results to preserve',1)[1].split('## Remaining catalogue',1)[0]
remaining=book.split('## Remaining catalogue',1)[1].split('## Current native families',1)[0]
assert row_ids(accepted)==resolved,(row_ids(accepted)^resolved)
assert row_ids(remaining)==opened,(row_ids(remaining)^opened)
todo=book.split('## Part 2 TODO checklist',1)[1]
todo_ids=re.findall(r'^- \[ \] (F\d{2}):',todo,re.M)
assert len(todo_ids)==23 and set(todo_ids)==opened
ledger=(root/'feedback-since-last-update.md').read_text(encoding='utf-8-sig')
assert row_ids(ledger)==set(cases)
source_map=(root/'part-2-pr-source-map.md').read_text(encoding='utf-8-sig')
resolved_map=source_map.split('## Contributor credit',1)[0]
assert row_ids(resolved_map)==resolved,(row_ids(resolved_map)^resolved)
closing=[]
for issue in [259,263,266,269,271]:
    source=next(s for s in register['sources'] if s['source_id']==f'GH{issue}')
    mapped=source['case_ids']
    assert mapped and all(cases[c]['completion']=='resolved' for c in mapped)
    closing.append({'source':f'GH{issue}','cases':mapped,'url':source['url']})
body=(root/'part-1-pr-description.md').read_text(encoding='utf-8-sig')
assert {int(n) for n in re.findall(r'^Closes #(\d+)$',body,re.M)}=={259,263,266,269,271}
assert cases['F16']['completion']=='open'
missing=[]
for name in ['part-2-handoff.md','part-2-pr-source-map.md','part-1-validation.md']:
    p=root/name
    for link in re.findall(r'\]\(([^)]+)\)',p.read_text(encoding='utf-8-sig')):
        if '://' in link or link.startswith('#'):continue
        target=(p.parent/link.split('#')[0]).resolve()
        if not target.exists():missing.append({'from':name,'target':link})
assert not missing,missing
out={'status':'passed','resolvedMapped':len(resolved),'openGroups':len(opened),'todoGroups':len(todo_ids),
     'sourceReferences':len(re.findall(r'^\[[^\]]+\]: https?://',source_map,re.M)),
     'closingIssueSet':closing,'gh261NotClosed':True,'pendingLocalLinks':missing,
     'registerSha256':hashlib.sha256((root/'scoped-feedback-register.json').read_bytes()).hexdigest().upper(),
     'mapSha256':hashlib.sha256((root/'part-2-pr-source-map.md').read_bytes()).hexdigest().upper(),
     'handoffSha256':hashlib.sha256((root/'part-2-handoff.md').read_bytes()).hexdigest().upper(),
     'noFeedbackRefresh':True,'noLedgerMutation':True}
(root/'part-2-pr-source-map-audit.json').write_text(json.dumps(out,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'resolved':23,'open':23,'todo':23,'closingIssues':[259,263,266,269,271],'links':'passed'}))
