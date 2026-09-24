# L04-O1 automatic pickup and delivery fixture

Implementation: `tools/RuntimeHarness/StorageDeliveryScenario.cs` and the dedicated `StorageDeliveryObservers.cs`. This document describes implemented fixture logic, not an executed result. The fixture author has not built, copied or launched these files. Root owns Bootstrap/controller integration, compilation, independent QA and actual baseline/corrected runs.

This case is `L04-O1-DELIVERY`. It tests the concrete inventory ownership rejection through actual automatic hauling. The separate synchronous `L04-O1` adapter case remains useful for null/foreign/floor/hand controls. This fixture does not reproduce the original #256/#268 configurations or prove recurring-loop resolution under L04-A2.

## Scene and scope

After a new isolated home map starts, create one ordinary generated human colonist with only Hauling active and a Work timetable. The pawn begins with empty inventory and no current job/driver. No fixture code starts, injects, queues or orders a job, invokes a workgiver to manufacture a candidate, injects a claim or registers an inventory tag. The game's work scan must select the actual HD bulk job and its production finish action must queue the actual unload.

The enclosed supported 41x13 room has concrete ground, home area and constructed roof within six cells of supporting walls. Generated starting actors/landing objects are despawned and generated spawned steel is removed on this disposable map before seeding. The enclosure prevents unrelated external work from becoming reachable.

- Normal-priority source stockpile: two cells, two distinct actual Steel Things, five units each.
- Critical-priority destination: one cell, 65 Steel, exactly ten units of compatible top-up capacity under the actual 75-unit Steel stack limit.
- Destination is about 28 cells from the sources; sources are two cells apart. No other above-Normal storage group may accept Steel.
- Total physical Steel is exactly 75. Counts include all spawned Steel on the map plus the actor's actual inventory and hands. Steel used as wall material is not a Steel Thing and does not enter the count.

The fixture enables master/bulk/haul-to-stack/mark-for-unload and the actor's automatic intake. It sets automatic pickup delay off, carry fraction to one and absolute inventory mass cap to zero (ordinary default limits). An individual Steel unload/keep rule is a failed fixture precondition. It does not change stack limits, workgivers, job behavior, claim behavior or query answers. Other fixture setup assumptions are asserted and recorded.

`BulkHaulPolicy.DecideOrderedHaul` sends **automatic** hauls through SweepNeighbors regardless of the ordered-haul trigger setting. `BuildBulkJob` needs at least two candidates unless the lone candidate qualifies for its oversized-stack exception. The five-unit primary plus nearby five-unit extra therefore use ordinary automatic bulk selection without a forced-sweep workaround. The observed current bulk Job must retain both original Thing IDs with count queues of five each, and actual pickups must contribute five from each.

The ten-unit scene intentionally grades the **first bulk delivery**, then continues for 1,200 ticks after the first unload's real cleanup. A bad unload can merge the two sources into one ten-unit stack. That later lone stack fits in hands and usually no longer qualifies for automatic bulk conversion. A subsequent ordinary native hand haul can therefore converge even on the broken holder adapter. That later convergence is separately reported; it does not erase the witnessed bad first delivery. Do not enlarge the stockpile or inject new stacks to manufacture two loops. The source-zone net-zero cycle counter is not proof that the same physical Thing IDs returned to their original individual cells.

## Actual API and observation boundaries

Read-only Harmony observers use their own owner, `HaulersDream.RuntimeHarness.StorageDelivery.observers`, with installed target identities recorded. They do not skip original calls, change results/arguments, add toils or suppress exceptions. Disposal removes only this observer owner. Setup verifies exact production patch class, method, owner and assembly identities for the WorkGiver_HaulGeneral bulk route, IsGoodStoreCell gate, HaulToCellStorageJob counter and HaulToCell reservation replacement.

The production `FreeUnitsFor(Pawn, ISlotGroup, ThingDef, Thing, out bool)` call is observed directly. A nested observation frame records the actual `IsDelivering` return and actual private `UnitsMoving` return when that production call invokes them. The fixture never invokes either method to obtain the expected answer. It reads immutable claim rows and the component's read-only `PeekHashSet`, preserving actual subject identity, parent-holder type, inventory ownership, recorded claim quantity and physical state at the query. On the corrected delivery branch own claims are excluded without invoking the own-claim evidence callback, so `productionLiveUnits == -1` is permitted there. The baseline branch must actually return ten live units from production while returning `delivering=false/free=0`.

The high-cell `IsGoodStoreCell` result is observed after HD's postfix, excluding the adapter's recursive internal space scan. Thus a capacity query and a genuine storage gate decision are both required. A successful commit log alone is not evidence of a selected cell or a deposit.

Candidate workgiver results are logged separately from current jobs. A job is recorded as current only when the actor's actual `CurJob` equals its current driver's `job`, the driver's `pawn` is this actor, and that driver is the actual `curDriver`. `TryMakePreToilReservations` is not an execution observer: it can run on a cached driver before the job starts. The result additionally requires physical pickup/deposit and actual successful cleanup of both bulk and first unload.

`DepositSwept` is observed **after** its real split reaches/merges into inventory and production tagging finishes. The count delta is measured against inventory immediately before `DepositSwept`; the earlier `SplitOff` itself can temporarily leave the material outside floor/inventory containers and is not a conservation boundary. The actual original source IDs are required for each full five-unit pickup.

Actual `Pawn_CarryTracker.TryStartCarry` boundaries distinguish later floor-to-hands acquisition from inventory-to-hands transfer. Both actual `TryDropCarriedThing` overloads are observed around their complete operations, recording original carried identity, resulting destination identity, returned success and before/after physical counts. `Thing.SplitOff`, `Thing.TryAbsorbStack` and its `ThingWithComps` override provide additional original/result identity traces while the actor is within an observed operation. Nested absorb traces are identity evidence, never summed as transfers.

`Pawn_JobTracker.CleanupCurrentJob` captures the actual job/driver and end condition before pooling, then checks that the driver ended and both tracker references were released afterward. The final assertion requires `Succeeded` for the actual bulk and first unload. Failed or cancelled jobs cannot be interpreted as successful transport from their earlier reservation/claim event.

Conservation is checked after complete pickup, carry and drop calls and outer driver/toil boundaries, plus each harness tick. Total must remain exactly 75 and no Steel may land outside the two fixture stockpiles. Intermediate split/absorb states are recorded separately to avoid declaring normal in-progress transfers lost or duplicated. Roof, stockpile priorities/sizes and the actor's presence also remain monitored. These bounds cannot prove absence of arbitrary transient mutations outside the observed operation boundaries; raw identity traces support independent inspection of the actual paths exercised.

## Result criteria

Both expected modes require: ordinary automatic current bulk/workgiver identity; exact two-source pickup; ten genuinely tagged inventory units with a real own Critical claim of ten; a real ten-unit first-unload deposit; both actual HD driver cleanups succeeded; conservation; stable fixture configuration; healthy observers; and the complete follow-up window.

`baseline-gap` requires all those controls **plus**:

1. During the first actual unload, the inventory subject has ten tagged units, source is empty, Critical remains65, and the actual own Critical claim is10.
2. Production observes `IsDelivering=false`, live own units10 and `FreeUnitsFor=0`, without truncation; the actual high-cell storage gate refuses that inventory subject.
3. That first unload physically returns ten to the Normal source zone, adds zero to Critical and zero elsewhere, then completes successfully. This records one source-zone net-zero bulk cycle.

It returns `status=behavior-gap-observed`, `baselineGapObserved=true`, `requestedBehaviorSatisfied=false`. An arbitrary failure, no-job run, timeout or warning/backoff is not a baseline gap. The result records whether a later real `JobDriver_HaulToCell` picks up those ten, delivers them to Critical, ends successfully and settles. `laterNativeHandHaulConverged=true` does not make the requested first-delivery behavior satisfied.

`satisfied` requires all controls **plus**:

1. The same actual inventory/claim state returns `IsDelivering=true`, free10 and an accepting real high-cell gate.
2. The **first** actual unload deposits all ten at Critical and none at the source or elsewhere.
3. After its successful cleanup, Critical75/source0/inventory0/hands0 remains observed for at least 600 distinct ticks and 600 elapsed ticks. No additional bulk/unload job, native hand-haul job or source reacquisition occurs. The fixture still completes the 1,200-tick follow-up.

It returns `status=passed`, `requestedBehaviorSatisfied=true`. A later repair trip cannot satisfy this branch. A 6,000-tick overall budget produces `inconclusive` if the required sequence/window did not finish; Bootstrap must also preserve its wall-clock timeout and whole-Player.log checks.

## Bootstrap/controller contract

Instantiate `new StorageDeliveryScenario(map, manifest.expectedBehavior)` once after the normal isolated map checks. On subsequent real game ticks call `TryFinish()`. Null means pending. A non-null `StorageDeliveryResult` is terminal; attach it to a new `[DataMember]` field on the root result, e.g. `storageDelivery`. Constructor/setup exceptions must be recorded as inconclusive fixture failures, with no behavior conclusion. The class implements `IDisposable`; root must dispose it on earlier terminal/error/timeout paths as well.

The result has explicit fields for common controls, the observed rejection/acceptance branch, first pickup/deposit quantities, actual first job IDs, source-zone cycles, subsequent hand-haul quantities/convergence, stable ticks, physical min/max totals, final state and original source IDs. It also contains actual job records, transfer records, capacity queries and storage gate observations. Every snapshot has a tick/sequence, actual item identities/counts/holders/cells, physical subtotals and immutable claim observations.

Controller verification must require all `fixture-storage-delivery-*` assertions and these common execution assertions:

- `execution-storage-delivery-ordinary-chain`
- `execution-storage-delivery-exact-real-pickups`
- `execution-storage-delivery-real-own-claim-inventory`
- `execution-storage-delivery-physical-unload`
- `execution-storage-delivery-successful-driver-cleanups`
- `execution-storage-delivery-conservation`
- `execution-storage-delivery-layout-intact`
- `execution-storage-delivery-observer-healthy`
- `execution-storage-delivery-followup-window`
- `execution-storage-delivery-expected-capacity-branch`

The only expected false assertions on a valid baseline are:

- `behavior-storage-delivery-first-unload-high`
- `behavior-storage-delivery-settled-without-rehaul`

Both must be true for `satisfied`. Independently verify the structured result quantities and actual event chain rather than trusting `expectationMatched` alone. A passed baseline execution means the old defect was observed, never that feedback is resolved. Preserve existing binary hashes, environment manifest, protected-player-file checks and error capture through the terminal boundary.

Event contract:

| Event | Meaning |
| --- | --- |
| `storage-delivery-fixture`, `storage-delivery-observer-bind` | Scene and actual observer targets/assemblies. |
| `storage-delivery-candidate` | Workgiver returned a candidate; not execution evidence. |
| `storage-delivery-current-job` | Actual current Job/driver/workgiver identities, initial queue and forced flag. |
| `storage-delivery-free-query`, `storage-delivery-cell-gate` | Observed production capacity branch and actual high-cell gate with real custody/claims/counts. |
| `storage-delivery-bulk-pickup` | Completed production inventory pickup, exact original split identity and physical delta. |
| `storage-delivery-carry-transfer` | Complete hands-transfer boundary; distinguishes inventory movement from later floor reacquisition. |
| `storage-delivery-physical-deposit` | Real carried drop result, original/resulting physical IDs and before/after counts. |
| `storage-delivery-split`, `storage-delivery-absorb` | Identity transitions; intermediate states, not independent transfer totals. |
| `storage-delivery-job-cleanup` | Actual end condition and released Job/driver after cleanup. |
| `storage-delivery-settled-state` | Conservation and follow-up states after complete operations/driver boundaries. |
| `storage-delivery-observer-fault`, `storage-delivery-result` | Explicit observation failure or terminal structured result. |

Independent QA should first challenge observer ordering, actual current-job identity, post-split conservation, claim liveness and result serialization. Baseline and corrected execution remain unfinished until root runs both identified binaries and checks the output. This scene does not cover save/load, cross-pawn contention, the L04-A capacity repair, arbitrary storage providers, or repeated transport cycles in the original reports.

## Pre-execution QA correction

Independent reviewer `source_coverage_qa` found that the original drop postfix's `object[] __args` was not observational under the installed Harmony implementation. Its argument array is initialized before the original and restored into ref/out arguments after an `__args` postfix; the initially null out-result could therefore be written back into the native caller. No delivery runtime used that version. Root replaced it with the actual native `resultingThing` parameter read **by value**, shared by the two inspected native overloads. This avoids argument-array restoration and observes the real post-call value without replacing it. Fresh source review and actual execution remain required.

The same reviewer freshly inspected the named by-value correction and accepted the source for execution without further blockers. Reviewed scenario SHA256 `40B2CB8F04D74FC8C50BFCE87179EDC11924B59B736FB515F31C7807B729CB22`; observers `7106624764211063B6DFC36CCFFDCFCDE54469ACE9E7703AFA16845D42B9E89D`. Root's standalone harness build passed with zero warnings/errors, binary `2DF2EC1F89683788F66649563153638CC690387AA136400B9B5A6350851A33F9`. Controller and actual baseline/corrected run acceptance remain separate.

Subsequent controller QA required quantitative identity/timeline reconciliation, beyond summary flags. Root added typed `highCell`, `sourceCells` and `stableSinceTick` to the result and retained the already observed complete `start-carry` records in `transfers`, alongside pickups/drops. A start-carry record has actual before/after states and no original/resulting Thing fields; it observes a transfer boundary, not a native success-return Boolean. This enables independent recomputation of later hand-haul acquisition and physical custody/cell subtotals. Prepared unlaunched delivery copies with harness 2DF2EC1F… predate these fields and cannot satisfy the strengthened controller contract; new copies are required. No scenario/job behavior or observer hook changes are introduced by retaining this existing evidence.

Bootstrap now also calls the delivery scenario's settled observer on every actual `GameComponentTick`, using `game-tick` so each tick emits its physical state. Frame-level completion polling remains separate. This preserves the complete bounded follow-up window even when several game ticks run within one frame, allowing the controller to recompute distinct stable ticks from raw physical observations instead of trusting the scalar count alone.
