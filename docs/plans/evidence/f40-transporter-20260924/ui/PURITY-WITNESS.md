# F40 transporter menu purity: source witness

This is a source audit and a headless arithmetic witness. Native menu and multiplayer acceptance remain required. Frozen source lives in `before/` and `after-v3/`; `implementation.diff` contains only this UI slice, including one final admission guard.

## Before

`TransportLoad.WouldGiveBulkJobForMenu` called `TryGiveBulkJob(..., playerOrder: true)` and discarded the resulting Job. That path called `LoadRegisterOrUpdate`, which creates a missing entry, updates its map, and rewrites `totalNeeded`; successful planning also called `JobMaker.MakeJob`. Not reserving or claiming cargo did not make those menu operations pure. The old suppressor comment explicitly described and incorrectly excused that ledger refresh.

## After

1. Both bulk-load and transporter continuous-load providers ask `TransporterCommand.LoadBlock`. The existing vanilla suppressor calls `WouldGiveBulkJobForMenu`. That method uses `ProbeBulkJob`, which passes `playerOrder: true, menuProbe: true` to the shared selection path.
2. The probe calls `LoadAvailableForMenu`. It builds a private current-manifest dictionary, calls the existing `BucketFor` selector (which only chooses an already-created field), reads the bucket with `TryGetValue`, and invokes production `LoadLedger.AvailableToClaim` on existing claim dictionaries. It never registers, prunes, refreshes or writes an entry. The result is a fresh dictionary, not an alias to the manifest or claims.
3. The ordinary plan uses `LoadRegisterOrUpdate` and `LoadAvailableToClaim` as before. The automatic fair-share branch requires `!playerOrder`, so it cannot dereference the deliberately absent entry during a menu probe.
4. A successful menu plan sets `wouldGive` and returns before `JobMaker.MakeJob`. The only earlier alternate job builder, the passenger deposit-only recovery, is behind `!playerOrder`; it is unreachable by this probe. `StrandedSurplusMass` reads `PeekHashSet`, which does not reconcile or retag the carried set. Local selection collections, mass/stat caches and the existing per-tick boolean-only menu memo may change; these are ephemeral computations, not saved claims, jobs or cargo.
5. Unload offers use `StartBlock` and `HasStorage`. The storage availability search passes `needAccurateResult: false`; the actual in-tick delivery planner still searches again. Offers never allocate an unload Job. The toggle reads saved intent and live load ownership; its enumeration never changes the flag.
6. Actual load/unload Job creation happens only inside registered `IssueSynced`. The handler checks executing-command readiness, map provenance and live admission. It explicitly does not trust the menu memo: loading builds a fresh adapter and actual plan. A separate transmitted `continuous` boolean rechecks that the setting is still enabled and the pawn is still undrafted. Ordinary loading retains its existing drafted first visit. The unload gate also rejects newly arisen mental states at start and before a transfer.
7. `SetUnloadSynced` validates target/map when turning on and delegates to the authoritative flag setter. Turning off only removes intent and remains available after feature disablement/departure. Both handlers are registered independently of other feature registrations and fail closed in an active MP session if either registration fails. No MP attributes create an absent-assembly metadata dependency.

## Focused executable check

`LoadMenuProjectionTests` exercises the actual generic production projection, not a reimplementation. Twenty probes with an existing own claim and another pawn's claim return the correct available units; clearing each returned result leaves every input dictionary and claim reference unchanged. A missing saved entry returns current need without requiring registration or aliasing the manifest. These tests cannot prove the full Verse call graph or another mod's hooks are pure.

## Required native witness

Before and after repeated **actual provider enumeration**, vanilla-option suppression, and toggle enumeration, capture the native next-job-ID counter, RNG state, complete saved load-ledger entries, hold/inventory/hands owners and counts, pawn current job and full queued jobs, and saved unload flags/sessions. Repeat at one tick (memo hits) and after a tick/cache boundary (actual planner again). Assert exact equality for all authoritative state; only ephemeral scratch/memo values may differ. Include a valid load, an existing other-hauler claim, a missing ledger entry, an unloadable flagged hold, blocked/full storage and a non-primary flagged group member. The positive menu paths must actually produce an enabled offer.

For execution, change manifest/group/flag/feature/draft/mental state or target map after menu construction and before invoking the synchronized handler. Assert fresh rejection or freshly bounded work, with no stale cargo movement or stale queued order. Exercise continuous draft and setting races separately from ordinary drafted single-visit loading. Actual MP replay must show the same resulting jobs, flags, claims and cargo on both clients. Source registration success alone is not MP acceptance.
