# F35 paired native runtime review

**Accepted for the scoped F35 finish-off fix.** Independently reviewed on 24 September 2026 after the interrupted review: the actual provider kills reproduce the missing follow-up on both baselines, while both candidates append one native corpse haul behind the existing order and deliver that same corpse. All three negative controls remain correct for each provider. No material concern warrants repeating these four runs.

| Provider / role | Native run | Result / assertions | Events |
| --- | --- | --- | --- |
| Allow Tool baseline | `31d630f08f234b9ebc05b3fdcce7ca59` | failed; 56/59 passed | 118 |
| Allow Tool candidate | `2ec4b39d3dfe4091870018b4c1dae09a` | passed; 60/60 | 119 |
| Keyz baseline | `e3941bd903de48409d4a6f2b7dd6e927` | failed; 53/56 passed | 114 |
| Keyz candidate | `d78661391a9241208d2d286675afeaf9` | passed; 57/57 | 116 |

Both baselines fail exactly `f35/0/execution-queue`, `f35/0/positive-native-haul`, and the aggregate `f35-finish-host-returned`. They remain failed evidence, not relabeled passes. Admission, death/corpse identity and all negative assertions passed. These are actual missing follow-ups, not setup refusals.

## Identities and legitimate execution

Read the complete `FinishWild.cs`, product hook, shared `SlaughterHaul` implementation, retained source review, all four assertion/event streams, manifests, native/desktop receipts, complete logs and Verify outputs. Current fixture source matches `D78CA285DCA9D07DE8CF9086106AA01C45DECEF0607D9DA45A6C8892BB6169AD`; Bootstrap and project match their selected-source records. The product hook matches `24C3507A8B82CD1A2F7176EEAE5D563E3DD1134B452C429E1D316986DBE265CF`.

All four actual loaded assembly lists equal their manifest lists, including versions, paths, hashes and MVIDs. Baselines loaded HD `D0C7FBE0…` / Core `186D8812…`; candidates loaded HD `098F9174…` / Core `AE5861E0…`. The identical host is `E990E7EF…`. Allow Tool `471F01CB…` and HugsLib `8BAD9C2E…` were selected in the six-package/twelve-image pair; Keyz `F789A178…` in the five-package/eleven-image pair. Both use game `5CF1B5BE…`, actual executing revision 591, and Harmony `353DAAFE…`. The retained 99-file product-copy record differs only in the four HD/Core DLL/PDB outputs.

The fixture uses the real provider designator, scanner and ordered-job dispatcher. Native productive toils execute the kill and haul. Controlled Melee 10, anesthesia, emptied starter inventory, disabled autonomous work, storage and disposable arena setup are disclosed. This is programmatic dispatch, not mouse input or an autonomous scanner-selection witness. No fixture call directly kills, executes, carries or delivers the animal.

## Observed behavior

Allow Tool baseline: actual finish job 34 kills deer 37892 at tick 164, producing allowed corpse 37898 outside Home with the provider's unforbid setting enabled. The queue contains only Goto 35. Goto succeeds at 433; the corpse is still outside storage at 1164, with no haul start.

Allow Tool candidate: job 22 executes deer 21505 at tick 163, creating corpse 21513. The post-execution queue is Goto 23 followed by HaulToCell 27. The finish job ends Incompletable at 164, demonstrating why a Succeeded-only finish hook would miss a real kill. Goto succeeds at 432, then job 27 starts from the native queue with `forced=False`. The same corpse enters the pawn's carry tracker at 598, appears in accepting storage at 733, and remains correctly stored through the settled assertion at 792. There is exactly one native corpse-haul start.

Keyz baseline: job 32 executes deer 38948 at tick 163, producing allowed corpse 38955 inside Home. Only Goto 33 remains queued; it succeeds at 432, and no haul has occurred by 1164.

Keyz candidate: job 19 executes deer 17669 at tick 163, producing corpse 17675. Queue order is Goto 20 then HaulToCell 27. The finish job ends Incompletable at 164; Goto succeeds at 432 before the sole native, nonforced corpse haul starts. The same corpse is carried at 598 and stored at 734, with successful settled observation at 795.

Each candidate also completes the three intended boundaries: wild disabled while tamed remains enabled produces a real allowed corpse but no follow-up; outside-Home forbidden death preserves the forbidden state and queues no haul; replacement before the killing toil leaves the victim alive with no corpse, execution callback or haul. Exact corpse/InnerPawn checks pass for all killed animals. Both baselines independently exercise these controls without unexpected failures.

The Allow Tool baseline has one observer-label defect: sequence 76 says `f35-goto-ended`, Incompletable, at tick 1322 in scene 1, before actual Goto 72 starts. `queuedGoto` still references a pooled previous Job while the next finish job is dispatched, so the observer can attach its Goto finish action to that reused object before `queuedGoto` is replaced. The actual new Goto 72 is then explicitly observed starting at 1322 and succeeding at 1590 (sequences 78 and 80). The spurious Incompletable callback sets `gotoFinished` false, cannot satisfy the completion gate, and occurs in a no-haul baseline control. It does not invalidate the positive baseline defect, candidate queue ordering, corpse identity or negative outcomes. Preserve this raw row; if this fixture is extended later, clear the old `queuedGoto` before dispatching the next scene. No automatic rerun is justified by this labeling issue.

## Logs, cleanup and Verify disposition

All native processes joined with exit 0, without deadlines or launch errors. All desktop receipts show `switchedDesktop=False`, only `Default` input-desktop samples, empty cleanup errors and zero active owned processes before close. Desktop stderr is empty. The four event streams have contiguous sequences and correct run identities; each Verify embeds a result equal to its retained raw result. All four Verify outputs report `protectedChanges: []`.

The complete Player logs contain 1906/1906 lines for Allow Tool baseline/candidate and 1879/1878 for Keyz baseline/candidate. Each has two early Mono fallback-library notices, Direct3D refresh/vsync timing notices and non-power-of-two texture warnings (two for Allow Tool; three for Keyz). Keyz baseline additionally reports a map-generator scatterer unable to find a ruins cell. These precede the admitted scene and do not explain or obstruct the observed provider behavior. Performance tables and the final 302-line allocator statistics are diagnostic output, not gameplay exceptions. No exception/crash, translation-data error, provider patch error or scenario cleanup error was found. Each runtime capture reports zero Unity errors. The candidate HD debug logs independently record one queued haul and one storage commit for the exact positive corpse; baseline debug logs contain no such haul. The logs are not represented as warning-free.

Generic Verify remains `not-verified`: it requires this semantic review, lacks its generic `scenario-observed` marker, and flags the early Mono notices. Baselines additionally retain expected-status/failed-assertion problems because their real status remains failed. This review accepts the F35-specific scene outcomes and terminal events; it does not alter Verify or erase baseline failures. Fixture cleanup attempts all owned objects, observers, zone, settings, Allow Tool setting, parked pawn endpoints and speed; any error would fail the result. None occurred. Disposable terrain/Home, elapsed biological time and former Lord duty are explicitly not restored; no player save was loaded or written.

## Scope of closure

The new hook addresses a demonstrated integration omission: these provider finish-off drivers never entered the existing native slaughter/hunt hooks. It also handles the observed real-kill/Incompletable ordering. This supports F35's wild finish-off behavior for the two identified providers, with eligible pawn, enabled wild setting, allowed corpse and accepting reachable storage, preserving existing orders.

The reporter's exact provider and historical save cause remain unknown. No arbitrary finish-off provider, Multiplayer, colony-animal runtime, Keyz Shift/strip runtime, provider-absent startup or new native hunt/slaughter runtime is claimed by these runs. The strip subclass/base-hook relationship and permitted Allow Tool colony classification have source evidence; Keyz rejects colony targets. Existing shared policy and native hunt/slaughter paths are unchanged. Final combined-build checks remain the parent task's responsibility. Player guidance review is in `../guidance/review.md`.

## Retained raw hashes

Hashes below are freshly read SHA-256 prefixes identifying the reviewed raw files in the corresponding run directories.

| Run prefix | result.json | events.jsonl | Player.log | verify.json | manifest.json |
| --- | --- | --- | --- | --- | --- |
| `31d630f0` | `27B06EC0EB0AC4CB` | `DB6298CFE54364EA` | `0388D89EC028034B` | `F996B92914FA99AA` | `BD8F1F62386972A7` |
| `2ec4b39d` | `3EEF741321ED9E38` | `440A7901533BE996` | `AC1774EF027AF060` | `B0BED0E34CFBD5A8` | `F7B76452F0A6E89A` |
| `e3941bd9` | `134A228D5D754986` | `1430477D3D8E8D8D` | `B1A6D4F33A2EA360` | `ECF4F6871DD048DA` | `B01376AF0AEAD5D7` |
| `d7866139` | `1649A2F3D0C473B6` | `B9CFADAAEF6F9140` | `2F524BD3AA15453C` | `D463D949799CF35D` | `1BF03AE700F234DD` |
