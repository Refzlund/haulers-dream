# F38 remaining acceptance: limited storage and identical command replay

Proposed bounded extension, 24 September 2026. The original producer c19 and independently accepted restart2425 remain frozen. Do not change their source, selection, controller, saves or result status. Use a separate fixture subtree and distinct host build, retaining the same candidate HD/Core and native provider images. This contract adds only the two remaining controls from `../native-fixture-design.md`; it does not add a provider or network matrix.

## Why these checks remain

The accepted original chain proves the reported whole-sweep cancellation, current/future/reserved/soft queued handoff, native negative controls, original save/load identity and eventual 107 stored cloth + 7 kept / 36 stored uranium. Its ample storage returns unmeasured capacity, so it cannot establish that source retirement preserves carried-surplus capacity protection. Its two processes also execute different command phases, so they are not identical-command replays.

The selected source criteria are:

| Boundary | Required behavior |
| --- | --- |
| `Patch_ReservationManager_SweepHandoff.ExplicitSourceOwned/TryRetire` | A successful supported explicit source reservation retires only that source from A; keeps A's job/cursor/unrelated reservation and destination rows; invalidates evidence before another same-tick read. Exact native `TakeInventory` is an explicitly supported source job. |
| `StorageEvidence.AddPocketedSurplus/AddPlannedPickups` | Count actual tagged surplus above Keep plus still-spawned remaining planned sources. Retired invalid targets contribute zero. |
| `StorageCommitments.FreeUnitsFor` / claim ledger | Measured native free space minus effective live claims. Querying must not fabricate or clear destination commitments. |
| `JobDriver_BulkHaul.CommitPlannedDestinations` | Preserve existing forced-destination arbitration. Do not suppress `InterruptCommittersTo` or directly write claims to obtain a result. |
| `BulkHaul.BuildBulkJobForced` | Actual explicit nearby planning clamps the requested primary to genuine available capacity, including a single-stack forced bulk result. |
| Replay boundary | Repeat the identical actual command sequence from one unedited native save; compare canonical input/output state at each command boundary, including actual job/Thing IDs and queue/target/count ordering. |

Review and freeze against the existing isolated candidate's **actual source tree**, not unrelated newer workspace changes. Its HD SHA is `6D1A238164C39C16FE4CF80A9CDFF6DBCEAACC0E550C5B803D333C9E6B409346`; Core is `815AAE0C28D7732318D9F202296A9308F39013BE2F690750086AC2A78378416D`.

## One constrained scene, two captures

Use three healthy controlled colonists and actual native warm-up/temperature/needs admission from the accepted fixture. Park original pawns. Require no other measured Cloth on the map. Disable unrelated automatic work and require the actual selected `unloadAllSurplus=false` setting. All scene stock below is explicit fixture input; carried tags and pickups must come from real jobs.

Create a genuine two-cell Cloth-only stockpile, native stack limit75 per cell, with67 cloth already physically stored. It has exactly **83 free units**. Sources are17,25,41 in A's nearby cluster and an additional40 in a distant cluster excluded from A's actual plan. Fail setup if the actual source list/order or raw stockpile capacity differs. The existing near/far geometry can be reused; do not edit built target/count queues to force membership.

Place all three actors in actual long native Wait jobs before saving. Capture an unedited native **pre-command** save, record and raw XML, including original pawn/item/Wait IDs, game tick and unique-ID counter, storage contents/filter/cells, ownership, settings and empty hauling commitments. No pending bulk plan or tag is synthesized or reconstructed on load.

The first capture is both producer and first replay. After saving, advance precisely one ordinary native tick and pause at the same boundary the second capture reaches under native `PauseOnLoad`. The second capture loads the same saved bytes, proves pre-first-tick equality, then reaches that same one-tick boundary. This avoids restoring derived storage rows by hand: A's actual command recreates its own claim in each run.

Both runs execute this exact sequence:

1. Issue A's real `NearbyHaulCommand.IssueSynced` for the17 source. Require an actual bulk plan `[17,25,41]`, real native reservations, and a destination claim covering83 before pickup. Advance normal game ticks until the17 is genuinely in A's inventory and A is walking toward25. Set actual Keep7 through the supported comp preference API, then let one ordinary tick make that preference visible, as in the accepted fixture. At the paused command boundary, A must still hold tagged17, with10 actual surplus and66 planned pickup units: **effective moving76**, **free capacity for C7**.
2. Give B a native, player-forced `TakeInventory` order for the25 source, using the normal native job construction/admission contract and an unchanged count25. This is explicitly labelled the supported native inventory-order control, not a nearby UI command or a synthetic tag. Its real reservation must retire A's current25 entry without ending/replacing A's original bulk job, shifting indices, releasing the41 reservation, or clearing A's destination row. Immediately, in the same paused tick, require **A effective moving51 = held surplus10 + remaining41**, and **C free capacity32 =83−51**. B's personal inventory order contributes no storage claim and its cargo must remain genuinely untagged. This isolates capacity released by source retirement without a second storage commitment obscuring the arithmetic.
3. Issue C's real nearby command for the distant40. Require the actual native/product plan to request **exactly32**, preserving A's job and41 reservation. No forced-full-destination arbitration is disabled; genuine remaining capacity is positive. The same-tick effective claims must total **51+32=83**, leaving0 free. All query probes preserve actual jobs, queues, reservations, tags, raw rows, stock and per-command RNG state apart from declared derived caches.
4. Let native ticks complete the three orders and ordinary A/C delivery. Require A's original sweep to finish with58 held before its51-unit unload, Keep7 still held, B's original `TakeInventory` to finish with25 held/untagged, and C to pick/deliver32 while leaving8 of its source on the ground. The stockpile finishes at **150 =67 original +51 A +32 C**, across exactly two legal cells with neither stack exceeding75. Full conservation is **190 =150 stored +7 A kept +25 B personal +8 ground**. No measured hand cargo, lost unit, extra pickup, unexpected claim or source reclaim is permitted. Observe at least300 ordinary settled native ticks.

This arithmetic distinguishes the relevant regressions: forgetting the retired25 leaves only7 free; forgetting A's10 held surplus admits42; counting its kept7 admits25; deleting A's whole claim admits83. The exact32 pickup and full physical destination therefore test more than a counter alone.

The native TakeInventory constructor/driver reservation and untagged-cargo behavior must be source-reviewed before implementation. Use actual supported job admission, never write A's plan, cursor, reservation, tag, loaded flag or capacity rows. If that native contract differs, report it and choose the smallest equivalent supported source-order control; do not silently manufacture personal inventory ownership.

## Replay comparison and evidence

Use the same controlled per-command `Rand.PushState(seed)`/`PopState()` scope in both runs, recording seed plus inner before/after compressed state through read-only observation. This makes the RNG input explicit; it is not a claim that vanilla save files persist a process's global RNG state. Do not leave an RNG scope open across frames, suppress native ticks, or write job/Thing/unique-ID counters. Main-thread commands run at paused native boundaries. Waiting actors and ordinary work settings prevent unrelated command selection during those boundaries.

Capture canonical, ordered snapshots before and after each command and at the actual first-pickup/Keep boundary: game tick; pawn/Thing/job IDs; current driver/cursor/toil; full queue order and target/count positions; native reservation ownership; raw/effective storage rows; measured raw/free capacity; native tags/Keep; all Cloth custody/counts; and the explicit command RNG receipt. Sort unordered observation collections by native IDs/coordinates without changing the live collections. Do not normalize away differing job IDs, counts, order or ownership. Compare each recorded command boundary from the first successful capture with the corresponding second-run boundary. Mismatches remain failures for bounded diagnosis.

Both captures independently prove the physical completion/stability rules. Final unrelated idle job/position/RNG behavior is outside this command-boundary determinism comparison and remains visible in raw events; this is not whole-world deterministic replay or a Multiplayer network session. The first capture's passing result, original save/record, complete expected boundary receipt and all ten images become protected, exact-hash inputs to the second capture. Preserve original XML and never edit/reconstruct it.

Minimum native work: **one setup/save/command/physical capture plus one exact-save repeat**, with root owning Prepare/Launch/Verify. No baseline repetition, no new provider tier, and no re-run of already accepted current/future/queued cases. Independent source/build review precedes execution; whole event/physical/log/process/protected-file review precedes scoped acceptance.
