"""Read-only consistency check for the complete, append-only feedback inventory.

Run: python docs/plans/check-feedback-inventory.py
Checks source identity, captured additions, definitions and cross-references.
It does not establish diagnosis, gameplay results or implementation completion.
"""
import collections
import datetime as dt
import hashlib
import json
import re
import runpy
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PLAN = ROOT / 'docs/plans'


def check_mapping_corrections(data, by_id, active):
    """Trace superseded IDs without treating their still-active requirements as retired work."""
    retired = {a['acceptance_id']: a for a in data['retired_acceptance_criteria']}
    assert len(retired) == len(data['retired_acceptance_criteria'])
    assert not set(retired).intersection(active), 'An ID cannot be active and retired'
    corrections = {c['correction_id']: c for c in data.get('mapping_corrections', [])}
    assert len(corrections) == len(data.get('mapping_corrections', []))
    superseded = set()
    for correction_id, correction in corrections.items():
        assert correction['reason'] and correction['evidence_report_sha256']
        assert not correction['observed_completion_evidence']
        assert all(sid in by_id for sid in correction['source_ids'])
        assert (ROOT / correction['focused_plan']).is_file()
        pairs = correction['acceptance_id_aliases']
        assert len({p['old_id'] for p in pairs}) == len(pairs)
        replacements = {p['old_id']: p['replacement_id'] for p in pairs}
        for old_id, replacement_id in replacements.items():
            assert old_id not in superseded, 'An alias belongs to one correction'
            superseded.add(old_id)
            old, new = retired[old_id], active[replacement_id]
            assert old['retirement_status'] == 'superseded-mapping-correction'
            assert old['mapping_correction_id'] == correction_id
            assert old['replacement_acceptance_id'] == replacement_id
            assert old['source_id'] in correction['source_ids']
            assert old['workstream_id'] == correction['removed_supplemental_workstream']
            assert new['workstream_id'] == correction['active_workstream']
            for key in ('source_id', 'scope', 'criterion_kind', 'subject_id', 'implementation_plan'):
                assert old.get(key) == new.get(key), (old_id, 'lost alias obligation', key)
            # Active criteria may add evidence requirements/links and record implementation
            # progress. Every original obligation and source association must still survive.
            for key in ('required_completion_evidence', 'subject_evidence_links'):
                assert isinstance(old.get(key, []), list) and isinstance(new.get(key, []), list), (
                    old_id, 'alias evidence must be a list', key)
                assert all(value in new.get(key, []) for value in old.get(key, [])), (
                    old_id, 'lost original alias evidence', key)
            assert old['verification_status'] == 'not-started' and not old['observed_completion_evidence']
            for child in old.get('child_acceptance_ids', []):
                assert retired[child]['parent_acceptance_id'] == old_id
                assert replacements[child] in new['child_acceptance_ids']
            if 'parent_acceptance_id' in old:
                assert old_id in retired[old['parent_acceptance_id']]['child_acceptance_ids']
                assert replacements[old['parent_acceptance_id']] == new['parent_acceptance_id']
            source = by_id[old['source_id']]
            assert old_id in source['retired_acceptance_ids']
            assert correction_id in source['mapping_correction_ids']
    assert superseded == {aid for aid, a in retired.items()
                          if a['retirement_status'] == 'superseded-mapping-correction'}
    for source in by_id.values():
        for aid in source.get('retired_acceptance_ids', []):
            assert aid in retired and retired[aid]['source_id'] == source['source_id']
        for cid in source.get('mapping_correction_ids', []):
            assert source['source_id'] in corrections[cid]['source_ids']
    # This source-level correction is semantic: consistent reverse maps alone missed it.
    if 'C424' in by_id:
        # Immutable pre-correction records, independently checked against the retained
        # 2026-09-08 inventory before migration. Equality with the active replacement
        # alone can lose a requirement on BOTH sides. Canonicalization is UTF-8 JSON,
        # ensure_ascii=False, sorted keys, compact separators, no terminal newline.
        # Only migration metadata is excluded; active status/observed evidence are
        # deliberately not hashed. These pins protect history, not future progress.
        original_pins = {
            'AC-L06-C424': '4AD07993E933386134BEFB07837FE111DA6CEE77E32DC2A12935CFE1C8C2C24F',
            'AC-L06-C424-S02': '81B766570C9D794F9974ACC3F2F9E6B8C59F63A260B28E3D2669E0F6E32B1508'}
        migration_fields = {'retirement_status', 'replacement_acceptance_id',
                            'retirement_reason', 'mapping_correction_id'}
        for aid, expected in original_pins.items():
            original = {key: value for key, value in retired[aid].items() if key not in migration_fields}
            canonical = json.dumps(original, ensure_ascii=False, sort_keys=True, separators=(',', ':'))
            assert hashlib.sha256(canonical.encode('utf-8')).hexdigest().upper() == expected, (
                aid, 'retired original obligation changed')
        cid = 'robot-source-mapping-correction-2026-09-08'
        correction = corrections[cid]
        assert correction['acceptance_id_aliases'] == [
            {'old_id': 'AC-L06-C424', 'replacement_id': 'AC-L36-C424'},
            {'old_id': 'AC-L06-C424-S02', 'replacement_id': 'AC-L36-C424-S02'}]
        assert correction['source_ids'] == ['C424'] and correction['subject_ids'] == ['C424-S02']
        assert correction['comparison_only'] == {
            'workstream_id': 'L08', 'source_id': 'C002', 'adds_acceptance_mapping': False}
        source = by_id['C424']
        assert 'L06' not in source['ledger_ids'] and 'L08' not in source['ledger_ids']
        assert by_id['C002']['ledger_ids'] == ['L08']
        subject = next(s for s in source['supplemental_subjects'] if s['subject_id'] == 'C424-S02')
        assert subject['ledger_ids'] == ['L36']
        assert subject['acceptance_ids'] == ['AC-L36-C424-S02']
        assert active['AC-L36-C424-S02']['scope'] == (
            'Establish why a eligible robot cannot be directly ordered to haul nearby resources. '
            'Verify command availability, reason for legitimately unavailable actions, actual execution, '
            'queued/forced handling, cancellation and inventory unloading. Pair with C426\'s construction '
            'success; construction working does not certify the manual bulk route.')
        addition = next(a for a in data['post_start_additions'] if 'C424' in a['source_ids'])
        assert cid in addition['mapping_correction_ids']
        assert addition['initial_new_source_workstream_mappings'] == 19
        assert addition['initial_new_acceptance_references'] == 41
        assert addition['new_source_workstream_mappings'] == 18
        assert addition['new_acceptance_references'] == 39
        plan = (ROOT / addition['plan']).read_text(encoding='utf-8')
        row = next(line for line in plan.splitlines() if line.startswith('| **C424-S02'))
        assert row.split('|')[1].strip().endswith('; L36')
        coverage = (PLAN / 'source-coverage.md').read_text(encoding='utf-8')
        row = next(line for line in coverage.splitlines() if line.startswith('| C424-S02:'))
        assert row.split('|')[2].strip() == '`AC-L36-C424-S02`'


def check_inventory():
    data = json.loads((PLAN / 'feedback-sources.json').read_text(encoding='utf-8'))
    historical = runpy.run_path(str(PLAN / 'apply-source-audit-corrections.py'))
    baseline = historical['baseline_records']()
    sources = data['sources']
    by_id = {s['source_id']: s for s in sources}
    active = {a['acceptance_id']: a for a in data['acceptance_criteria']}
    count = data['counts']
    assert len(by_id) == len(sources) == len({s['url'] for s in sources})
    assert len({(s['source_type'], s['platform_native_id']) for s in sources}) == len(sources)
    assert len(active) == len(data['acceptance_criteria'])
    expected_ids = set(baseline) | set(data['goal_start_refresh']['added_source_ids'])
    for sid, expected in baseline.items():
        for key, value in expected.items():
            assert by_id[sid][key] == value, (sid, key)
    assert by_id['C422']['platform_native_id'] == '592940620292699148'
    assert data['baseline_ledger']['primary_entry_count'] == len(baseline)
    subjects = [sub for source in sources for sub in source['supplemental_subjects']]
    by_subject = {s['subject_id']: s for s in subjects}
    assert len(by_subject) == len(subjects)
    added_ids = []
    for addition in data.get('post_start_additions', []):
        ids = addition['source_ids']
        assert not expected_ids.intersection(ids), 'Addition reuses an existing source ID'
        expected_ids.update(ids); added_ids.extend(ids)
        path = ROOT / addition['source_file']
        assert hashlib.sha256(path.read_bytes()).hexdigest() == addition['source_file_sha256']
        capture = json.loads(path.read_text(encoding='utf-8'))
        assert [s['source_id'] for s in capture['sources']] == ids
        plan_path = ROOT / addition['plan']
        plan_text = plan_path.read_text(encoding='utf-8')
        for captured in capture['sources']:
            s = by_id[captured['source_id']]
            for key in ('source_id', 'platform_native_id', 'url', 'author', 'posted_utc', 'posted_copenhagen', 'evidence_links'):
                assert s[key] == captured[key], (s['source_id'], key)
            assert hashlib.sha256(captured['body_text'].encode('utf-8')).hexdigest() == captured['body_text_sha256']
            assert dt.datetime.fromtimestamp(captured['posted_unix'], dt.timezone.utc).isoformat() == s['posted_utc']
            assert dt.datetime.fromisoformat(s['posted_utc']).astimezone(dt.timezone(dt.timedelta(hours=2))).strftime('%Y-%m-%d %H:%M:%S CEST') == s['posted_copenhagen']
            assert s['url'] in plan_text
            for sub in s['supplemental_subjects']:
                assert sub['subject_id'] in plan_text
                for aid in sub['acceptance_ids']:
                    ac = active[aid]
                    assert ac['scope'] in plan_text, (aid, 'definition does not match plan')
                    assert ac['required_completion_evidence'], aid
                    assert ac['implementation_plan'] == addition['plan']
        assert addition['subjects'] == sum(len(by_id[sid]['supplemental_subjects']) for sid in ids)
        assert addition['new_source_workstream_mappings'] == sum(len(by_id[sid]['ledger_ids']) for sid in ids)
        assert addition['new_acceptance_references'] == sum(len(by_id[sid]['acceptance_ids']) for sid in ids)
    assert set(by_id) == expected_ids, 'Missing or unregistered source addition'
    assert len(added_ids) == len(set(added_ids)) == count.get('post_start_additions', 0)
    assert {s['source_id'] for s in sources if not s['body_available']} == {'C240', 'T01-R02'}
    for source in sources:
        sid = source['source_id']
        assert len(source['ledger_ids']) == len(set(source['ledger_ids']))
        assert set(source['ledger_ids']) == set(source['original_ledger_ids']) | set(source['supplemental_ledger_ids'])
        assert len(source['acceptance_ids']) == len(set(source['acceptance_ids']))
        assert all(aid in active and active[aid].get('source_id') == sid for aid in source['acceptance_ids'])
        if source['initial_disposition'] == 'pending-investigation':
            assert all(f'AC-{lid}-{sid}' in source['acceptance_ids'] for lid in source['ledger_ids'])
        else:
            assert not source['acceptance_ids']
        for sub in source['supplemental_subjects']:
            assert sub['source_id'] == sid
            assert set(sub['ledger_ids']) <= set(source['ledger_ids'])
            assert sub['acceptance_ids'] == [f"AC-{lid}-{sub['subject_id']}" for lid in sub['ledger_ids']]
            assert all(aid in source['acceptance_ids'] for aid in sub['acceptance_ids'])
            assert all(related in by_id for related in sub['related_source_ids'])
    assert {w['workstream_id'] for w in data['workstreams']} == {f'L{i:02}' for i in range(1, 56)}
    for ws in data['workstreams']:
        lid = ws['workstream_id']
        assert ws['source_ids'] == [s['source_id'] for s in sources if lid in s['ledger_ids']]
        assert ws['acceptance_ids'] == [a['acceptance_id'] for a in active.values() if a['workstream_id'] == lid]
        assert ws['supplemental_subject_ids'] == [s['subject_id'] for s in subjects if lid in s['ledger_ids']]
    for aid, ac in active.items():
        for child in ac.get('child_acceptance_ids', []):
            assert active[child]['parent_acceptance_id'] == aid
        if 'parent_acceptance_id' in ac:
            assert aid in active[ac['parent_acceptance_id']]['child_acceptance_ids']
            assert ac['subject_id'] in by_subject
        if ac['definition_status'] == 'defined-in-linked-plan':
            assert ac['scope'] and ac['required_completion_evidence']
    relationships = {r['relationship_id']: r for r in data['relationships']}
    assert len(relationships) == len(data['relationships'])
    for r in relationships.values(): assert all(sid in by_id for sid in r['source_ids'])
    for s in sources: assert all(rid in relationships and s['source_id'] in relationships[rid]['source_ids'] for rid in s['relationship_ids'])
    assert not next(w for w in data['workstreams'] if w['workstream_id'] == 'L05')['source_ids']
    assert 'AC-L05-HISTORY' in active and 'AC-L48-C307' not in active
    assert any(a['acceptance_id'] == 'AC-L48-C307' for a in data['retired_acceptance_criteria'])
    check_mapping_corrections(data, by_id, active)
    types = dict(collections.Counter(s['source_type'] for s in sources))
    derived = dict(total_primary_entries=len(sources), baseline_primary_entries=len(baseline),
        refresh_additions=len(data['goal_start_refresh']['added_source_ids']), post_start_additions=len(added_ids),
        steam_entries=sum(v for k, v in types.items() if k.startswith('steam-')),
        github_bodies=sum(v for k, v in types.items() if k.startswith('github-') and k.endswith('-body')),
        github_conversation_comments=sum(v for k, v in types.items() if k.startswith('github-') and k.endswith('-conversation-comment')),
        readable_primary_bodies=sum(s['body_available'] for s in sources), unavailable_primary_bodies=sum(not s['body_available'] for s in sources),
        workstreams=len(data['workstreams']), primary_source_workstream_mappings=sum(len(s['ledger_ids']) for s in sources),
        original_primary_source_workstream_mappings=sum(len(s['original_ledger_ids']) for s in sources),
        supplemental_primary_source_workstream_mappings=sum(len(s['supplemental_ledger_ids']) for s in sources),
        sources_with_multiple_ledger_mappings=sum(len(s['ledger_ids']) > 1 for s in sources), acceptance_references=len(active),
        supplemental_subjects=len(subjects), supplemental_subject_workstream_mappings=sum(len(s['ledger_ids']) for s in subjects),
        retired_acceptance_references=len(data['retired_acceptance_criteria']), by_source_type=types,
        by_initial_disposition=dict(collections.Counter(s['initial_disposition'] for s in sources)))
    assert set(count) == set(derived), 'Every published count needs a derived check'
    for key, expected in derived.items(): assert count[key] == expected, (key, count[key], expected)
    coverage = (PLAN / 'source-coverage.md').read_text(encoding='utf-8')
    matrix_ids = re.findall(r'^\| \[(C\d{3}|T\d{2}-(?:OP|R\d{2})|GH\d+(?:-C\d+)?)\]\(', coverage, re.M)
    assert len(matrix_ids) == len(set(matrix_ids)) == len(sources) and set(matrix_ids) == set(by_id)
    for sid in added_ids:
        source = by_id[sid]
        assert source['url'] in coverage
        for sub in source['supplemental_subjects']:
            assert all('`' + aid + '`' in coverage for aid in sub['acceptance_ids'])
    return dict(status='passed-current-inventory-consistency', sources=len(sources), mappings=count['primary_source_workstream_mappings'],
        acceptance_references=len(active), preserved_baseline_sources=len(baseline), added_sources=len(added_ids),
        scope='Identity, captured bodies/timestamps, plan definitions, counts, cross-references and explicit robot mapping aliases only; no gameplay or implementation acceptance.')


if __name__ == '__main__':
    print(json.dumps(check_inventory(), indent=2))
