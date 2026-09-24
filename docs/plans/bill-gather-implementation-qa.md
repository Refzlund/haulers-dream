# Ordinary bill gathering: independent implementation QA

Reviewed 2026-09-07, after the first focused implementation build and before gameplay verification. This reviewer did not author the product change and performed no edits to production, builds, tests, launches, deployment, network access, or Git operations. The review covers `BillGatherContract`, `JobDriver_GatherBillIngredients`, Core `BillGatherSelection` and its thirteen tests, the routing patch, the actual-start carrier approach patch, and affected job-family/unload guards.

The findings below describe the source reviewed before corrections. Root and the implementation author have been notified; a later correction needs its own review and runtime evidence. **This is not approval of BG01–BG12.**

## Findings

### P1 — A complete merge into existing inventory bypasses CE's capacity-cache notification

Location at review: `Source/HaulersDream/JobDriver_GatherBillIngredients.cs:170`, inside `TransferSelected`.

The direct `recipient.TryAbsorbStack(split, respectStackLimit: true)` records actual count deltas and calls `RegisterHauledItem`, but never calls the owning inventory's `NotifyAddedAndMergedWith`. When the entire split merges, the subsequent `TryAdd` branch does not run either. There is consequently no inventory owner mutation notification for this pickup.

This is a confirmed integration mismatch against the installed CE assembly, not just uncertainty about third-party callbacks:

- `C:\Steam\steamapps\workshop\content\294100\2890901044\Assemblies\CombatExtended.dll`, SHA-256 `3102BC2276C583E51FE85AE340E5B986F80452171DB63EA72D04F7651B96AFE3`; the installed v1.6 LoadFolders uses the package root.
- Decompiled `CombatExtended.HarmonyCE.HarmonyBase` patches `ThingOwner.NotifyAddedAndMergedWith` to `Harmony_ThingOwner_NotifyAddedAndMergedWith_Patch.Postfix`, which invokes `CE_Utility.TryUpdateInventory(__instance)` for a nonzero merge.
- Actual CE `CompInventory.currentWeight` and `currentBulk` return cached fields. `CanFitInInventory` consumes the derived available weight/bulk without refreshing that cache.
- `Utility_HoldTracker.Notify_HoldTrackerItem`, called by HD's `RegisterHauledItem`, changes hold records and does not refresh the capacity cache.

Therefore a later pickup can reuse bulk capacity that the first complete merge already consumed. Vanilla's live mass check can independently constrain weight, but it cannot correct CE's separate stale bulk dimension. The standalone Core receipt tests cannot observe this.

Recommendation: perform the actual owner merge notification, with the correct recipient and moved count, or a deliberately equivalent compatibility path; verify its ordering against observed receipts and callback exceptions. Required witness: CE enabled, bulk is the limiting dimension, a first pickup fully merges into an existing tagged stack, then a second pickup must see the reduced capacity. Record real physical quantities, CE cache values, and final native ingredient consumption.

### P2 — Merge target selection ignores another pawn's existing reservation

Locations at review: `JobDriver_GatherBillIngredients.cs:163` and the ignored `pawn.Reserve(receipt.Thing, job, ...)` return at line 207.

Every tracked stack still in the inventory is considered a merge recipient. The checks cover membership, stack compatibility, and room, but not another worker's existing reservation of that physical stack. A concrete interleaving is: worker B reserves a tagged stack in carrier A's inventory; A then starts its own floor-only gathering job, so its initially selected floor ingredients are valid. The new job-family guard prevents new sharing of A's cargo but does not erase B's prior reservation. A's gathered floor ingredients can now merge into B's reserved stack. A cannot reserve that resulting target, and final `BillGatherContract.UsableTarget` rejects it through `pawn.CanReserve`, producing an avoidable gather/bench/abort despite available inventory room for a separate stack.

This is a reservation/progress failure, not demonstrated item loss. Recommendation: do not mutate a recipient another pawn reserves, or provide an explicitly compatible reservation/allocation contract. Using a separate inventory stack avoids this collision. Required witness: establish B's real reservation before A's gather starts, verify it remains intact, and observe A completing useful work without merging into or claiming B's reserved recipient.

## Source checks with favorable results and practical limits

- Full target/count lists are independently copied into the new prep, and the native continuation receives another independent copy and a fresh Job ID. The driver never clones a Job, copies `placedThings`, or creates a native continuation in cleanup. Source-level count remapping preserves the floor shortfall and observed destination quantities, and normalization detects aggregate over-allocation of one physical Thing. This does not verify real merge callbacks or consumption.
- The recipe validator uses the actual no-mix `CountRequiredOfFor` contract, and the installed vanilla allow-mix `GetBaseCount` / `IngredientValueGetter.ValuePerUnitOf` quantities. Its recursive allocation spends integer units once, backtracks overlapping slots, constrains no-mix slots by def identity, and has a 20,000-node budget plus a 512-depth bound. No additional arithmetic defect was found in this review. The tests exercise meaningful allocation cases but were only read; the retained original chooser, modded recipes, tolerance boundaries, and runtime cost still need witnesses. Budget exhaustion rejects the prep; it is not evidence that the recipe itself is invalid.
- The new driver has a fixed seven-toil layout and saves its scan limit, cursor, load/invalid/handoff flags before base `ExposeData` reconstructs the toils. Original rows retain stable indices while verified inventory portions append. The legacy JobDef/driver remains separate and non-suspendable; the new def is also non-suspendable. Source supports the chosen persistence design, but cold reload during walk and partial pickup and a real pre-change active prep remain unverified.
- Forced candidates remain native. Drafting/mental state and bill deletion/suspension fail the running prep. Final handoff checks queued work and `playerInterruptedForced`; cleanup cannot manufacture a successful continuation, and recovery unload is requested behind queued work. Native `StartJob` cleanup, new reservation acquisition, opportunistic-prefix behavior, and actual player-order scheduling still require BG07/BG09 observation.
- The carrier nudge moved from candidate `JobOnThing` into `JobDriver.Notify_Starting`, with exact current Job/driver identity and live settings gates. Installed native `StartJob` calls this after pre-toil reservations and its opportunistic-prefix decision. Candidate construction itself introduces no new reservation, actor dispatch, persistent continuation map, or progress stamp. Repeated rejected-candidate runtime probes remain required for BG12.
- Installed CS's cleaning-only path calls native `CollectIngredientsToils` and then its cleaning toils. That native collection supports inventory targets and strict selected counts. Its gathering path recognizes `ParentHolder == pawn.inventory`. The new retained-selection handoff is compatible with those inspected input shapes; actual CS mode changes, recipe completion, and cleaning execution remain runtime questions.

Actual vanilla API references were read from `%TEMP%\haulersdream-bill-design-20260907` (`Job`, `JobDriver`, `JobDriver_DoBill`, `Pawn_JobTracker`, reservation types, `ThingOwner` and `ThingOwner<T>`, `WorkGiver_DoBill`). Actual installed CS was read from `%TEMP%\haulersdream-commonsense-20260907\CommonSense-installed.cs`. The CE methods above were independently decompiled from its installed DLL during this review.

The next review point should address the two merge findings, rerun appropriate guarded build/tests, then obtain actual BG01/BG02 results before broadening the matrix. Positive source checks here close no runtime acceptance row.

## Corrected source re-review

The author corrected both findings: a recipient must be reservable before mutation; verified merge receipts are recorded before invoking the actual cached `ThingOwner.NotifyAddedAndMergedWith` method, with tagging in `finally`. If merge/add callbacks throw, already proven transfers retain their receipts before propagation rather than being replayed. Independent reviewer `runtime_test_setup` re-read the corrected implementation and accepted both source corrections without a new source blocker. The initial findings above remain preserved as the audit trail.

Root's corrected guarded product build completed with zero errors/warnings and all 13 structural guards. The focused receipt case raised the full automated suite to **2,906 passed, zero failed/skipped**, recorded in `%TEMP%\haulersdream-goal-20260907\bill-gather-results\bill-gather-corrected.trx`. HD SHA256 `2FAD6BBB4BB82FE046B71B1BCA76DCDC232DE66EBB163A6ED1BAD4AE05B537FF`; Core `B610A8EA8B5A39F5840EE58A9FEA6574B37F8B9DEF9523057E5946E3F97AB7FC`.

This accepts the source corrections only. Actual CE bulk-limited second pickup, the other-pawn prior-reservation case, changed-build BG01 and the wider BG02–BG12 matrix remain required. Neither the source approval nor helper tests establish those gameplay outcomes.
