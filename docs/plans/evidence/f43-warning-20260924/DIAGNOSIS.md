# F43: cloth recurrence warning and native workgiver consistency error

Investigated by work_cadence_finish on 24 September 2026. The initial source findings below led to the authorized four-file correction now frozen in `product-v1`; no native launch, staging, commit or inventory refresh has occurred. F43 remains open pending the bounded native work-scan witness and independent acceptance.

## Report and missing diagnostic

C021 is nullpat's16 August2026 23:59:49 CEST comment (`592938720043335749`). The existing19 September capture at `%LOCALAPPDATA%/Temp/haulersdream-scope-20260919/steam-comments-parsed.json` retains its full body: praise for improved behavior, a repeated cloth warning, a less frequent CanGiveJob/JobOnX message, and whether another mod should be removed. It does **not** describe observed physical cloth pickup/drop cycles or identify a responsible workgiver/mod. `captured/C021-retained-body.json` records its exact body and source hash. Its parsed body SHA is `0cdf7381cf6ad9d55fe87a6acdae50b0f1c975041f9f317f2a8906b9348b51d5`; this is a later parsed capture, not byte identity with the inventory's earlier raw-body SHA `dd115b631314f081ca33f3e0f18547c17f76125f955b05c30d9e963d19bf8eb4`.

The original `%TEMP%/haulersdream-goal-20260907/corpus.json` is absent at its recorded location. No earlier diagnostic text was found in the current repository or QA acquisition/analysis/runtime-temp roots. The expressly linked [DropText diagnostic](https://www.droptext.app/20pwbxh7) now returns404 through agent-reach's Jina route; its page says the item does not exist, was removed or expired. The separate web open was unavailable. Actual response body/headers are retained. This establishes current unavailability, not which of those causes occurred. The original stack, workgiver, mod list, settings and reporter binary identities remain unavailable; no mod attribution can be recovered from the quoted suffix alone.

## Exact native emitter

Read-only decompilation binds the installed Assembly-CSharp SHA `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`, MVID `61e41735-6189-4da4-9d21-0260257b5097`. This is the current installed game, not an assertion that the reporter used those bytes.

`RimWorld.JobGiver_Work.TryIssueJobPackage` first selects a thing via `scanner.HasJobOnThing(pawn,t)` (or a cell via `HasJobOnCell`). Once a target is valid it calls the same provider's `JobOnThing`/`JobOnCell`. A null result emits `Log.ErrorOnce(...,6112651)` with the provider, target and pawn, followed by the reported CanGiveJob/JobOnX suffix (`captured/JobGiver_Work.native.cs:219–261`). That is **availability and job-construction disagreement inside the work scan**. It is unrelated to a multiplayer packet, network synchronization or a desync check. Because native code uses one ErrorOnce key, the observed frequency is not a reliable count of all inconsistent scans.

`WorkGiver_Scanner.HasJobOnThing` itself calls virtual `JobOnThing != null`. Native `WorkGiver_HaulGeneral` and its `WorkGiver_Haul` base do not override HasJobOnThing. The general haul's Has query therefore runs both actual HD JobOnThing postfixes. Native hauling checks automatic haul eligibility and obtains a storage job; no foreign provider is required to reach HD's bulk-upgrade path.

## A concrete HD-only route connects the two warnings

The captured pre-correction source and HEAD9549cad contain the legacy call at the end of `BulkHaul.BuildBulkJob` (`BulkHaul.cs:1038–1045`). It records an automatic **candidate construction**, before any reservation, pickup, delivery or job completion. `HaulChurnGuard.NoteBulkAnchor` counts Thing ID, time and unchanged/increased stack count; the sixth construction inside the180-tick gap stamps2500-tick backoff and emits the cloth warning. Its wording asserts repeated actual hauling, nonmovement and likely foreign interference, none of which those observations establishes.

The actual source sequence at the sixth qualifying probe is:

1. Native inherited HasJobOnThing calls patched HaulGeneral.JobOnThing. The priority-first churn postfix sees no existing backoff and leaves the ordinary job available.
2. The ordinary-priority bulk postfix constructs an eligible bulk candidate. Its trailing NoteBulkAnchor crosses6, stamps backoff and warns, then still returns the candidate. HasJobOnThing returns true.
3. Native JobGiver_Work has selected that target and immediately requests its real JobOnThing. The priority-first churn postfix now replaces the ordinary result with null.
4. BulkHaul's cheap automatic gate rejects that null vanilla job before its same-tick cache lookup (`HasPotentialBulkWork`→`AcceptsHaulDestination`). The cached sixth candidate consequently does not restore it. Native JobGiver_Work receives null and emits the consistency error.

This is a source-supported causal path, **not yet a captured complete native sixth-probe WorkGiver chain** and not proof that it was the reporter's exact path. If another provider supplied the original target, its own predicate, patches and live state must be inspected instead. Do not quarantine a workgiver or suppress the native error merely to hide this disagreement.

## Existing evidence reused and limits

The accepted published B1 query capture0077 and moving-job capture6425 already establish that candidate probes create false recurrence. The retained reviewed account in `l04-b1-fixture.md` records five fresh candidate builds during one original moving Bulk15 job, counter1→6, one warning and backoff2532, while none of those probe candidates starts. The original real job nevertheless delivers10 and remains stable601 ticks. The old raw runtime directories are not present at the documented current TEMP location, so this investigation cites their retained accepted reviews rather than pretending to have reread those raw files.

The separately accepted L04-O1 first-delivery comparison establishes HD's own-inventory claim adapter defect and a corrected ordinary-stockpile delivery. It does not identify C021's cloth destinations or turn a warning-only report into a proved physical loop. No duplicate storage/provider matrix is needed for the narrower workgiver inconsistency.

The retained executed-attempt design says its proposed replacement recorder/policy is unimplemented; the captured pre-correction source still calls NoteBulkAnchor during query construction. I found no already-applied query fix to credit. Replacing the call with job-start counting would still mistake attempts for completed net-zero cargo movement. Preserve the existing per-job failure/retarget guards and separately evidenced RimIOT path-time protections when removing query-generated recurrence.

## Current implementation disposition

The subsequently approved bounded disposition is to remove this unsupported generic inference, its dead state and obsolete pure API/tests without constructing a new generic physical-cycle recorder. This explicitly loses only that candidate-based inference. F15/F17's actual executed-loop causes remain separate open work; no replacement recorder is made a prerequisite for F43, and this removal does not claim to solve every physical loop. This supersedes the initial investigation's suggestion that a complete replacement success-loop policy remained an L04 implementation obligation: no speculative universal replacement detector is required merely to replace the unsound counter.

The exact four-file implementation is frozen in `product-v1`. Matched actual-reference baseline/candidate builds passed with zero warnings/errors; 41 focused guard tests passed; the normalized compiled comparison accounts for every difference and confirms real failure/placement/foreign-retarget guards remain unchanged. The bounded native witness has compiled and its source/controller/image pins are ready for independent review. Actual native execution and acceptance remain pending.

## Smallest discriminating native follow-up

Use one private ordinary-stockpile scene with two small native Cloth stacks, ample valid higher-priority storage and a healthy controlled colonist. Reuse B1's read-only actual counter/cache/physical snapshots, normal native climate warmup and held worker setup. Perform five actual HasJobOnThing probes for the same anchor on consecutive real ticks, without assigning their candidates. Then enable only ordinary hauling for that actor and invoke the actual native JobGiver_Work scan once. Observe, without replacing returns: selected scanner/type/Def, nested Has result, exact native JobOnThing result, Note counter transitions, exact warning/error text and both native stacks.

The baseline must establish the complete ordered Has-true→backoff→final-null→native ErrorOnce6112651 chain on the exact original Cloth identity, with unchanged source/cell/count/holder, no pickup, no reservations/claims and no candidate job start. Record the native error honestly; do not suppress it or redefine a clean-runtime check to zero. One fixture-owned expected baseline error can receive explicit independent baseline-gap disposition while remaining visible in raw result/logs. A failure to select the intended scanner is inconclusive, not reproduced.

The identical corrected-source case must leave repeated availability probes physically inert, produce an eligible real job with no query-derived recurrence or native consistency error, then assign that returned job normally and observe productive pickup/delivery, conserved quantity and stable completion. Keep the legitimate existing per-job/foreign-retarget protection obligations separate. No provider combination, multiplayer launch, full-storage matrix or new universal diagnostic framework is required for this discriminating case.

## Evidence-backed player answer

“These messages alone are not a reason to remove another mod. The cloth warning can be triggered by repeated job checks even when that cloth has not been moved. RimWorld's ‘synchronized’ message means a workgiver said work was available but then returned no job; it does not indicate multiplayer desynchronization. We found an HD path that can produce both, and are verifying that exact sequence. Your old diagnostic link is no longer available, so we cannot identify the exact workgiver or mod combination from your report. We will not attribute it to another mod without that evidence.”

After the source correction and native chain pass, change only the verification sentence to describe the verified HD fix and its tested scope. Do not claim the unavailable historical stack or an observed physical cloth loop has been independently reproduced.
