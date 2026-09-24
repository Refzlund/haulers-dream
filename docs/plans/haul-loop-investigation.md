# L04: bulk pickup / unload recurrence investigation

Status: source investigation complete; actual #256 loop reproduction and integrated fixes remain pending. Original investigation targeted baseline `a8c19586716f26b1571e5000d52763c5f381ad8d` on 2026-09-07. Subsequent root work reproduced the L04-O1 adapter defect in an isolated published-build run, then corrected the source comparison; changed-build and full hauling execution remain unverified. See [runtime-run-log.md](runtime-run-log.md). L04 / #256 remains open. #268, #261 and C021 retain separate acceptance items; similarity is not proof of duplication.

## Evidence and its limits

- [#256](https://github.com/Refzlund/haulers-dream/issues/256): v1.24.0.0, RimWorld 1.6.4871, 1,163 reported active mods. Read its actual [HD log](https://reports.refzlund.com/files/gExj9CQhrYNDfRXh.log) and [Player.log](https://reports.refzlund.com/files/hb7Z0et5XpPwhKjM.log). Captures are `issue256-hd.log` (123,960 bytes, 994 lines) and `issue256-player.log` (705,564 bytes, 9,612 lines) under `%TEMP%\haulersdream-goal-20260907`. The [reporter's comment](https://github.com/Refzlund/haulers-dream/issues/256#issuecomment-5241237484) says disabling bulk hauling stops the problem.
- [#268](https://github.com/Refzlund/haulers-dream/issues/268): v1.24.0.0, 338 reported mods. Read its actual [HD log](https://reports.refzlund.com/files/aHzDxBkZTM2TeaNK.log) and [Player.log](https://reports.refzlund.com/files/kJAP7S3jBnJ04jen.log), captured beside those files as `issue268-hd.log` and `issue268-player.log`. Its [initial comment](https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5430010324) suggests changing StackGap helped; the [later comment](https://github.com/Refzlund/haulers-dream/issues/268#issuecomment-5461920982) retracts that: loops returned, and disabling bulk hauling remained effective. Do not close the case using the earlier apparent workaround.
- Closed issue / PR history was read from captured GitHub pages under `%TEMP%\haulersdream-unified-ledger-20260907`, especially #138/#139/#189 and #214/#216. Historical fix claims are not current runtime proof.

In #256, repeated cycles around HD log lines 917–952 commit `DankPyon_WoolRox x97` to `ReelStorageNewLargeWoodCrate373297` and `Silver x241` to `ReelStorageNewLargeWoodCrate166595`, then commit both for unload to `Stockpile zone 6`. The warning at 06:56:10 is followed by further activity, including 06:56:25–28 and 06:58:10–13. There are repeated Studdart cycles and similar earlier Vlad activity.

Important interpretation corrections:

1. `BulkHaul: ... sweeping` is emitted while building a candidate job. Duplicate lines can be probes.
2. `storage-commit [bulk-sweep]` is emitted by `JobDriver_BulkHaul.CommitPlannedDestinations`, called after primary reservation in `TryMakePreToilReservations`. This demonstrates an actual driver start/reservation attempt, not merely planning. It does not certify pickup or completion.
3. `storage-commit [unload]` is emitted when `FindTargetOrDrop` selects and commits a destination, before inventory pull, carry and placement. It does not certify a physical deposit.
4. `free unmeasured` is `StorageCommitments.Trace`'s default optional argument, omitted by these callers. It does **not** mean the adapter failed or declared infinite capacity. `others enroute 0` reports other pawns' same-def claims, not all capacity, cross-def claims or the current pawn's commitments.
5. The logs lack original/resulting Thing IDs, physical cells, stable linked-group IDs, placement deltas and complete terminal job events. Repeated quantities demonstrate activity worth investigating, but do not prove each cycle moved the exact same Thing back to its original cell.

The crate-to-stockpile change is a concrete lead. Neither changing the warning nor repairing its counter demonstrates that this transport loop is fixed.

## Actual storage configuration

The #256 Player.log active package list includes `adaptive.storage.framework` (line 4731), `custom.evenstorage` (4943), `reel.expanded.storage` (5191), `custom.tidystorage` (5304), `ferny.progressionstorage` (5846), plus Neat Storage variants, Primitive Storage, Flickable Storage and As Above So Below. The list repeats later; do not count repetition as additional mods. RimIOT appears only in HD's warning example, not as an active package in this capture.

[Reel's Expanded Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=3237638097) uses Adaptive Storage Framework (ASF). Its exact crate def is absent from this machine's available Workshop XML, so the reporter's complete crate definition cannot be reconstructed from installed files. Do not claim a Reel assembly was inspected. The active Progression: Storage mod supplies a relevant public patch: at [commit 68092ddb4853a2bdf848b377e2d218d997c570e4](https://github.com/fernyrepos/Progression-Storage/tree/68092ddb4853a2bdf848b377e2d218d997c570e4), `1.6/Mods and Shit/Reel Storage/Patches/reel storage descriptions and rebalances patch.xml` replaces `ReelStorageNewLargeWoodCrate/building/maxItemsInCell` from 10 to 8, describing a 40-to-32 total reduction. This XML was retrieved and read; its exact version and resulting capacity in the reporter's save are unknown.

[Tidy Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=3684211288) describes instant, batched and pawn-hauling rearrangement modes, linked-group sorting, and integration with [Even Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=3684119183). These are configuration dimensions to test, not established causes. Their actual DLL/source and the reporter's selected settings were unavailable. Neither log supplies the crates' actual priorities, filters, cell contents, linked membership or sorting state.

#268 shares ASF and Neat Storage variants, but its active list lacks Reel, Tidy Storage, Even Storage and As Above So Below. It includes `Andromeda.StackGap` (Player.log line 1184). Its HD log repeatedly commits `Meat_Cow x6` and `RawPotatoes x60` to `RimWorld.StorageGroup`, then unloads meat to `Stockpile zone 5` and potatoes to an opaque `RimWorld.StorageGroup`. Later cargo exhibits similar activity. The generic group label cannot distinguish linked groups. ASF / HD storage handling is a useful common test seam; blaming a #256-only mod does not explain #268 without additional evidence.

## Inspected storage implementation

Read-only decompilation inspected the installed ASF assembly:

`C:\Steam\steamapps\workshop\content\294100\3033901359\1.6\Assemblies\AdaptiveStorageFramework.dll`

Assembly version `1.2.4.0`; SHA-256 `28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9`. Decompilation: `%TEMP%\haulersdream-goal-20260907\AdaptiveStorage-installed.cs`. This identifies the local artifact, not the reporter's exact ASF version.

- `AdaptiveStorage.ThingClass` extends `Building_Storage`. `Accepts` includes `HasCapacityForThing`; free capacity uses `StoredThings.CellWiseCount < CurrentSlotLimit`.
- When full, `HasCapacityForThing` distinguishes a spawned item inside the building from an outside/inventory item. The former must be in the stored valid set; the latter requires stacking capacity unless Performance Fish supplies that integration. `StoredThings.AcceptsForStacking` checks its accepted-def cache or, for custom stackability, compatible partial stacks. Subject identity, spawned state, position and stack compatibility therefore matter.
- `StoreUtilityPatches.PreventStorageLookupFaster` patches `TryFindBestBetterStoreCellForWorker`, invoking `ThingClass.HasCapacityForThing` when `slotGroup.Settings.owner` is that storage thing. Test ordinary buildings and linked groups; do not assume identical owner shapes.
- ASF patches `GetMaxItemsAllowedInCell` for per-cell limits and `NoStorageBlockersIn` to validate existing stack destinations. Both true top-up room and shared vacant stack slots matter.

The installed game's `Assembly-CSharp.dll` was also decompiled read-only into `StoreUtility-installed.cs` and `GridsUtility-installed.cs` in that TEMP directory. No assembly was executed as a mod or modified.

## Confirmed defect A: vacant stack slots become private room for each def

This production adapter/model mismatch is confirmed independently of whether it initiates #256.

`StorageCommitments.MeasureGroupUncached` in `Source/HaulersDream/StorageCommitments.cs` obtains `c.GetItemStackSpaceLeftFor(map, def)`. A cell with no first item becomes an empty cell; otherwise **all** its free space becomes def-specific `partial` capacity. `BulkHaul.ResolveGroupBudget` / `PriceDefInto` feed this into `Source/HaulersDream.Core/StorageGroupBudget.cs`. `RawSpaceFor` / `SpendCrossDefClaims` use the same measurement for live arbitration.

Actual vanilla capacity is:

`matching-def top-up + max(maxItemsAllowedInCell - itemCount, 0) * incomingDef.stackLimit`.

The second term is shared between all accepted defs, even in an occupied cell. The adapter labels it private top-up merely because the cell is occupied. `StorageGroupBudget.Consume` subtracts private capacity only from that def. Cross-def claims also cannot spend this room because they consume the shared *empty-cell* pool.

Minimal example: actual vanilla `ShelfSmall` has three stack slots. Fill its cell with one full steel stack and one full wood stack, leaving one vacant slot and no silver or cloth partial. Silver and cloth each receive one stack-limit of `PartialSpace`, with `EmptyCells == 0`. Spending or claiming that slot for silver does not remove cloth's purported private room. Actual `StoreUtility.NoStorageBlockersIn` initially accepts both because the item count is below the limit. This defect needs no foreign mod; ASF's higher limits make the same shape relevant to crates.

This can overbook storage and force retargeting. It does not alone prove the repeated loop: once storage physically fills, later queries may correctly stop. Establish whether the reported configuration repeatedly reopens, or appears to reopen, the same capacity and whether destination/claim decisions survive pickup and unload coherently.

Recommendation: represent and spend shared stack slots inside occupied cells separately from compatible existing-stack top-ups. Preserve filters, real `CanStackWith`, per-cell capacity overrides, heterogeneous limits and linked-group ownership. Do not simply make all partial room shared, which would deny valid top-ups. Apply the correct model to both one sweep and other pawns' live claims. The existing assumption that one empty cell belongs exclusively to one def is also coarse for multi-stack cells; validate the replacement's mixed-def use of initially empty cells.

## Confirmed defect B: candidate probes count as successful recurrence

`BulkHaul.BuildBulkJob` calls `HaulChurnGuard.NoteBulkAnchor(primary)` for automatic candidates just before returning. The #256 warning stack passes through `WorkGiver_Scanner.HasJobOnThing`, `JobGiver_Work`'s validator and `GenClosest`, demonstrating that an availability query reaches this write. As Above So Below wraps the search in that stack; wrapper presence is not causation.

Cache qualification: `TryBuildBulkJob` memoizes by tick, pawn, primary, forced/forceSweep flags and cached Job identity. The same valid automatic probe within one tick normally counts once. Tick advancement clears the cache; different pawns have separate keys; recycled Jobs invalidate entries. The counter is shared by Thing ID across pawns. Six successful builds on adjacent ticks within the 180-tick gap, or six distinct pawns probing the Thing, can reach the threshold without a haul executing.

`HaulChurnPolicy.RecordNetZeroReanchor` compares only time and stack count. Shrinking resets it; equal or larger stacks continue it. It observes no cell, map, pickup, placement or terminal job event. Splits/merges can also change identity independently of material progress. The build reaching the threshold has already created its candidate; the stamp affects later queries. The warning's assertions of physical nonmovement and likely foreign interference exceed the evidence collected.

Recommendation: retain executed-success-loop protection, but attach it to actual transport evidence. Moving the call to job start alone still does not establish completion or return of cargo. Define the pickup/delivery outcome, correlate source/destination and quantities, and account for interruptions, partial deposits and legitimate splits/merges. State observed facts in diagnostics and leave attribution unknown without evidence of foreign intervention.

## Confirmed defect C: bulk extras bypass backoff

`Patch_WorkGiver_HaulGeneral_ChurnBackoff` gates automatic primary `JobOnThing` results. `YieldRouter` and `EnRoutePickup` also check `IsBackedOff`. The bulk extra path in `BulkHaul.BuildPoolInto` / `TakeNearestEligible` does not. Its legal-haul check does not call the WorkGiver postfix.

A wool stack suppressed as primary can therefore join a silver-anchored sweep, and vice versa. This is a confirmed coverage hole and a credible explanation for activity continuing after a warning. The logs lack the identities and selected anchor needed to prove that exact bypass in #256. Preserve explicit player orders when closing automatic-intake gaps; do not accidentally change transport manifests or intentionally ordered pickup.

## Why destination selection can diverge

### Follow-up correction: inventory delivery is misclassified

The bill-design investigation subsequently inspected the actual installed `Verse.Thing` and `Verse.ThingOwner` types. Root independently verified both and the production call site. `Thing.ParentHolder` returns `holdingOwner?.Owner`, an `IThingHolder`; for a pawn inventory item that is `Pawn_InventoryTracker`. `ThingOwner` is the collection and does not implement `IThingHolder`.

`StorageCommitments.IsDelivering` currently compares `subject.ParentHolder` with `asker.inventory.innerContainer`. Those are different objects. The hand-carry branch can return true, but the intended inventory branch does not recognize an ordinary inventory item. `FreeUnitsFor` consequently includes the pawn's own same-def commitment in `mine` and passes `delivering:false` to the policy while selecting where to unload cargo that already reserved that room.

This comparison was introduced by commit `d4c6bc2` / PR #252 in the published v1.24.0. Existing Core tests explicitly supply `delivering:true`, and the source guard only checks for the text `ParentHolder`; neither verifies the actual adapter's value. This is a concrete newly introduced implementation defect, not a new interpretation of the warning counter. The independent ordinary-stockpile delivery comparison below now establishes a first-delivery failure from this mechanism. Its role in the reporter's actual crate configuration remains unverified.

The earlier investigation's statement that inventory possession was detected structurally was incorrect: it described the intent without checking the actual holder type. The following trace analysis is corrected accordingly. Root's published-build fixture observed all ten expected baseline answers, including both own-inventory failures and surrounding hand/floor/foreign/null/competing-claim controls. The corrected holding-collection comparison then passed all ten actual controls and independent review in run `0eb65b1bd45f4818b12a5be9c9f5ab0f`, with both own-inventory cases changing to true/10/allowed and the surrounding outcomes preserved. See [runtime-run-log.md](runtime-run-log.md). Full automatic pickup/unload remains a separate pending fixture; original report convergence is unverified. Own floor-planning claims and other pawns' claims still constrain new pickups.

Three decisions occur without one retained destination contract:

1. The bulk plan prices vanilla's chosen anchor cell and extras' strictly better storage using `CurrentStoragePriorityOf(t)`.
2. At driver reservation, `CommitPlannedDestinations` folds cargo by def and probes again with a still-spawned representative. `StorageEvidence.DestinationGroupFor` uses `Unstored`, unload filter context and `needAccurateResult:false`. The code expressly permits this claim to differ from the originally priced group.
3. `JobDriver_UnloadHauledInventory.FindTargetOrDrop` probes again using the inventory-held Thing, also from `Unstored`, with the default accurate-result flag. It commits the newly selected group. It does not retain the original bulk destination as preferred. The per-def cache assists cargo ordering; this final destination query is fresh.

Both recorded commit paths use the same Unstored priority floor. Their difference cannot be explained solely by different priority floors. Subject identity/state, held position, pawn position, capacity, claims, reservations and storage-mod state can change between them. Vanilla prioritizes storage priority before proximity: movement alone should not select a lower-priority stockpile while an eligible higher-priority crate remains available. The report does not reveal actual priorities or rejection reasons. The accurate-result flag affects how many group cells are considered before breaking; it preserves priority ordering.

The commitment gate intends to exclude a deliverer's own same-def claim, but its inventory holder comparison is wrong as described above. It also subtracts cross-def claims and memoizes measurements per tick/group/Thing/pawn/filter presence. Instrument the actual `delivering`, own/other claims and capacity results to establish whether self-subtraction caused the observed destination change. The logged `Stockpile zone 6` selection is the valid-storage delivery branch, not evidence of a no-storage home-area fallback.

A durable fix may need both accurate capacity and a retained intended destination that is revalidated before delivery. Retention must not force cargo into full, forbidden, deleted or unreachable storage. Valid retargets must update claims and not turn a net-zero return into renewed automatic demand. Select the implementation after the executed fixture identifies the failing transition.

## Recurrence assessment

| Earlier report/fix | Claim and present assessment |
| --- | --- |
| [#138](https://github.com/Refzlund/haulers-dream/issues/138) / [PR #139](https://github.com/Refzlund/haulers-dream/pull/139) | Low-to-high-priority food over-pickup and carrying surplus back were claimed fixed by a shared empty-cell budget, def-specific partials and deep-storage helper tests. Defect A is an established unhandled shape in that model/adapter: vacant slots inside occupied cells. It explains why the implementation cannot satisfy its broad claim. Whether #256/#268 use that shape still requires runtime evidence. |
| [PR #189](https://github.com/Refzlund/haulers-dream/pull/189) | Later called the over-haul portion of #138 already resolved. Arithmetic tests receiving correctly separated inputs do not verify the production cell scan's interpretation. |
| [#214](https://github.com/Refzlund/haulers-dream/issues/214) / [PR #216](https://github.com/Refzlund/haulers-dream/pull/216) | Earlier #177/#184/#192 fixes missed RimIOT path-time retargeting. #216 added source/destination/runtime gates plus the generic successful-loop backstop. The original mechanism was `Patch_StartPath_NetworkItemRedirect` rewriting live targets after planning. #256 has no active RimIOT evidence. Defects B/C invalidate assumptions in the generic backstop, but are not proof that current crate loops share the historical RimIOT cause. |

PR #216 reported green Core tests/reviews but explicitly left game verification outstanding. Proving a threshold function does not establish that the event called a haul represents execution, or that every automatic intake respects backoff. Preserve the specific RimIOT runtime protection while correcting generic counting and coverage.

## Runtime acceptance fixtures

Use the isolated harness described by the separate runtime investigation, with identified binaries/configuration. Observe ordinary workgiver selection and real drivers. Direct helper tests alone cannot close these items.

| Acceptance | Fixture and assertion |
| --- | --- |
| L04-O1: actual possession adapter | Put real cargo into a real pawn inventory and compare its `holdingOwner`, `ParentHolder` and the production `IsDelivering` result. Through `FreeUnitsFor`/actual storage selection, a live own claim must not reject that pawn's already-held cargo. Controls: own hand-held cargo, another pawn's inventory, spawned floor cargo with an own in-flight claim, null/missing inventory and competing other-pawn claims. Verify automatic bulk pickup then unload actually uses available reserved higher-priority storage; no helper-supplied delivery Boolean. |
| L04-A1: actual multi-slot adapter | Vanilla `ShelfSmall`, two full filler stacks, one vacant slot, incoming silver and cloth. After pricing/claiming one def, the other cannot claim that same slot. Exercise a single multi-def sweep and two pawns' claims through real measurements/storage queries. Control: a full shelf with a compatible partial stack must still accept a genuine top-up. |
| L04-A2: executed convergence | Low-priority source stockpile, higher-priority shelf with constrained room, nearby mixed cargo and sufficient carrying capacity. Run ordinary automatic jobs. Measure physical pickups/deposits; the amount sent to the higher destination must respect actual capacity. Surplus stays at source or reaches a valid alternative for a recorded reason. Continue beyond a trip: no repeating identical source/destination/count cycle. Bulk disabled is a control, not the final fix. |
| L04-B1: query side effects | Repeated ordinary `HasJobOnThing` without starting jobs: same pawn/tick; one pawn across at least six adjacent ticks; multiple pawns in one tick; recycled cached Job. No completed-loop evidence, warning or backoff may result solely from queries. |
| L04-B2: real repeated execution | Controlled successful pickup/unload return loop with physical deltas must trigger bounded protection. Controls: oversized stack shrinking over trips, partial absorbs, legitimate relocation, cancellation before pickup, forced orders, splits/merges, parallel pawns and save/load. Preserve the historical path-time foreign-retarget case with RimIOT where available. |
| L04-C1: extra intake | Establish real backoff for one stack, give a nearby different stack a valid automatic anchor job, and ensure the suppressed stack is not swept as an extra. Test already-built job / late-stamp timing too. Explicit orders remain usable. |
| L04-D1: storage matrix | Repeat capacity/convergence with installed ASF: linked/unlinked, one shared slot left, genuine partials, full storage, heterogeneous cell limits, capacity changing during the walk. Distinguish spawned and inventory subjects. Add exact Reel + Progression XML when available, recording applied limits. |
| L04-D2: integration discrimination | After the base case is understood, vary Tidy/Even mode and active sorting individually for #256; StackGap for #268. Performance Fish matters to ASF's full-storage branch. Isolate As Above So Below only if the same physical fixture differs with its wrapper. Mod removal is a diagnostic control, not a claimed compatibility solution. |

A decisive bounded trace needs: tick/map/pawn/job load IDs; candidate/selected/started/completed event; primary/extra Thing IDs; source cells/priorities; intended/selected building, stable group ID and cells; subject spawned/holder state; per-cell item identities, compatible top-up and shared slots; rejection reason; own/other same/cross-def claims; original/replacement targets; physical pickup/deposit deltas; terminal job result. Do not add unbounded per-cell logging to every production work scan.

Next step: reproduce A1 and B1 independently, then A2 with actual drivers before selecting the production fix. A/B/C need separate evidence even if delivered in one PR. #256 remains unresolved until actual transport converges and the relevant configuration matrix explains or rules out its observed crate-to-stockpile transition.

## Accepted ordinary-stockpile first-delivery comparison

The identified published and corrected runs in [runtime-run-log.md](runtime-run-log.md) establish L04-O1's actual execution consequence. In the published build, a real automatic bulk job pockets two five-unit steel stacks. Its own ten-unit claim then rejects those ten held units at the Critical destination; the first unload returns them to the Normal source zone. The merged ten-unit stack is subsequently delivered by native hand hauling. This is one wasted bulk cycle followed by recovery, not an endless-loop reproduction.

On corrected HD2FAD6BBB… / CoreB610A8EA…, using the same reviewed8EC7FDB9… harness and other assemblies/settings, the own-inventory query returns delivering=true/free10, the first unload deposits all ten into Critical storage, and the entire 1,200-tick follow-up stays at high75/source0/empty hands and inventory. All74 assertions, physical snapshots, loaded identities and whole logs passed independent review; root's copied/protected checks are unchanged. Fresh corrected-controller QA also passed.

This closes the ordinary-stockpile first-delivery subcriterion on that build. It does not establish the reporters' exact crate/ASF/large-modlist cause, close A/B/C, or replace persistence and integrated regression evidence. CAP01 separately has an accepted published seven-scene accounting baseline; its replacement capacity implementation is still unfinished.
