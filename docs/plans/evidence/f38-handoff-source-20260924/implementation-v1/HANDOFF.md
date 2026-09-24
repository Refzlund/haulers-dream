# F38 source and managed handoff

24 September 2026. Implementation and managed checks complete; native acceptance is still pending. No Prepare, game launch, staging or commit performed by this agent.

## Frozen candidate

Base HEAD: `137a9050b429fd36aae1ab22a7aaa8d2077ee057`.

Isolated build: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f38-implementation-v1-20260924`.

| Artifact | SHA-256 | MVID |
|---|---|---|
| HaulersDream.dll | `6D1A238164C39C16FE4CF80A9CDFF6DBCEAACC0E550C5B803D333C9E6B409346` | `dabf8036-7f68-4d53-b53a-4ee82e1a9db4` |
| HaulersDream.Core.dll | `815AAE0C28D7732318D9F202296A9308F39013BE2F690750086AC2A78378416D` | `d1cd9abd-e3ab-44a9-b92d-f9154598bc5c` |

`selection.json` names the exact seven owned files and all 474 source inputs. `audit.json` verifies workspace/after/isolated bytes, retained before bytes, all source inputs and the tested Core DLL against the final Core DLL. Other shared working-tree changes were excluded by building HEAD plus this slice. The deployment guard does not exist.

Owned slice: `JobDriver_BulkHaul.cs`, `BulkHaul.cs`, `HaulersDreamGameComponent.StorageClaims.cs`, new `Patch_ReservationManager_SweepHandoff.cs`, new Core `SweepPickupPlan.cs`, new `SweepPickupPlanTests.cs`, and `.changeset/preserve-sweeps-during-haul-handoff.md`.

## Behavior and boundary

The new transpiler wraps all three native `EndCurrentOrQueuedJob` calls inside Reserve. It is not a single-call IL patch. Runtime item/source/job/driver/ownership guards make the two building-interaction paths and unsupported jobs call the original cancellation with unchanged arguments. The exact nine-argument Reserve signature, three-call count and call-site exception-block shape are checked before any rewrite; unsupported binding retains the original instructions and disables the success postfix.

The wrapper runs only after native Reserve installed the new actual reservation. The success-only postfix also covers old queued orders that have not reserved yet. Only the new forced job's explicit original source has authority; incidental extras do not. Both old and new definitions/drivers must match the supported exact types, including existing cached/current driver's pawn and job identity. New-job queue membership is deliberately not required: native TryTakeOrderedJob performs reservations before Enqueue at the retained Pawn_JobTracker lines 924/943/953.

Retirement validates old current/queued ownership and aligned lists, sets matching pickup slots to Invalid/zero without removing/reordering them, and clears matching scalar source references. Release affects one unambiguous default-layer source reservation only. The current walk/pause consumes that tombstone on its own next pre-tick; no synchronous driver jump occurs inside the other pawn's Reserve. Toil count/order and saved cursor fields remain unchanged.

Admission preserves slot-zero authority and gates incidental reservations through CanReserve. A fully retired nonempty aligned plan can finish normally and preserve subsequent queued work. Held cargo, tags, keep, manifest and loadedAnything are untouched. Route/plan memos and storage evidence generation are invalidated while destination rows remain, protecting already-held surplus. Destination-capacity interruption policy is unchanged.

## Validation retained

- Initial complete isolated build: `build.log`, 32.68 seconds, zero warnings/errors.
- Final reviewed driver-identity/signature revision: `build-review-revision.log`, 3.23 seconds, zero warnings/errors.
- `tests.log` and `test-results/f38-focused.trx`: **25/25 passed**, eleven new pickup-plan cases and fourteen existing storage-ledger cases. Cases cover current/future slots, anchor privilege, duplicate/idempotent retirement, malformed-plan purity, unrelated targets, deterministic retirement, and retaining held surplus while relinquishing only planned capacity.
- Final Core exactly matches the Core DLL loaded by those tests. The review revision changed only the game adapter, so these tests were not repeated without cause.
- `source.diff` is the complete before-to-final slice. `before/` and `after/` retain exact bytes; `reviewed-first-patch.cs` and `selection.before-review.json` retain the earlier source draft.

These are managed plan/arithmetic checks, not evidence that Harmony binding, live reservations, same-tick game evidence, path continuation or saves work. F38 remains open.

## Next bounded work

Independent source QA is assigned to `work_cadence_finish`. The fixture design is `../native-fixture-design.md`: exact unchanged baseline versus candidate, actual current/future/queued handoff, failed-reserve/probe purity, stale incidental-extra admission, held cargo/keep/capacity protection, actual tombstone checkpoint and full restart, physical conservation and deterministic replay. Start with ample genuine storage; do not hide a destination conflict by suppressing it. Root owns native launches and any eventual commit.
