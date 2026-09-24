# Ordinary bill gathering: design review

Status: architecture recommendation, not implementation or runtime verification. Reviewed 2026-09-07 against `docs/plans/ordinary-bill-gather.md` BG01–BG12, the existing HD prep/routing/unload code, and the actual installed game/Common Sense assemblies. Root still needs the BG01 baseline before selecting the product fix. No production edits, build, game launch, Git mutation or network access occurred in this assignment.

## Recommendation

Prototype a **new fixed-layout ingredient-prep JobDef/driver** that owns the full original `targetQueueB` / `countQueue`, remaps actual transfers, and creates one fresh native `JobDefOf.DoBill` in its successful final toil. Keep the old `HaulersDream_BillPrepGather` def and driver as legacy save support; stop creating new instances of that old job once the replacement is accepted. This follows the proposed minimal-state handoff, with the safeguards below.

The benefit is concrete: native bill identity, ingredient quantities and recipe semantics survive the sweep without another ingredient-choice pass, while the new persisted driver does not reinterpret an old numeric toil index. It also preserves ingredient-relocation deference, which currently recognizes replacement by the final non-DoBill job def. A custom-def driver must never run the actual recipe toils; placed-ingredient accounting and consumption must remain under real `DoBill`.

Do not lift surgery, autonomous-bench, mech, forced-order or unstackable-ingredient exclusions as incidental parts of this slice. They have separate contracts. The ordinary mixing-recipe/any-tagged-stock exclusions can only be reconsidered when the retained-selection implementation and BG witnesses establish the replacement behavior.

## Actual API contracts inspected

Read-only decompilations are under `%TEMP%\haulersdream-bill-design-20260907`:

- `Verse.AI.Job.cs`, `Verse.AI.JobDriver.cs`, `Verse.AI.JobDriver_DoBill.cs`, `Verse.AI.Pawn_JobTracker.cs`.
- `Verse.AI.Toils_JobTransforms.cs`, `Verse.AI.Toils_Haul.cs`, `Verse.AI.ReservationUtility.cs`, `Verse.AI.ReservationManager.cs`.
- `Verse.Thing.cs`, `Verse.ThingOwner.cs`, `Verse.ThingOwner1.cs`, `Verse.Pawn_CarryTracker.cs`, `RimWorld.WorkGiver_DoBill.cs`, `RimWorld.Bill_Production.cs`.

Source assembly: `C:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`. Common Sense source is the prior read-only decompilation `%TEMP%\haulersdream-commonsense-20260907\CommonSense-installed.cs`, from the installed original Workshop package identified in `commonsense-investigation.md`. These are local artifacts, not proof of the reporter's exact assembly bytes.

| Contract | Design consequence |
| --- | --- |
| `Job.Clone()` aliases target/count queues and `placedThings`, and copies `loadID`. | Do not use it as an independent continuation. Two live Jobs must not share a load-reference identity or mutable ingredient queues. |
| `Job.ExposeData` scribes bill by reference, queue/count lists as job data, `workGiverDef`, `jobGiverThinkTree`, and a save key for the ThinkNode. | Store the selected bill on the prep Job itself. Do not deep-save a duplicate Bill or require a process-local plan map. Preserve work context through the normal Job fields where appropriate. |
| `JobDriver.ExposeData` saves `curToilIndex`, `nextToilIndex`, remaining ticks and transition flags. At PostLoadInit it reconstructs toils through `SetupToils`. | Make the new prep's toil list structurally fixed. Decisions belong inside toil actions; do not insert/remove toils based on current inventory or live settings during reconstruction. |
| `Job.MakeDriver` constructs a fresh driver; `StartJob` uses it, rather than restoring an earlier driver's cursor. `SuspendCurrentJob` queues a suspendable Job but cleans its driver. | Keep new prep `suspendable=false` unless progress lives entirely in a restart-safe job payload and resumption is deliberately implemented. Save/load of an active driver and suspension into the queue are different operations. |
| `JobDriver_DoBill.TryMakePreToilReservations` reserves the bench, its sittable/interaction position, and best-effort ingredient targets. `ReserveAsManyAsPossible` has no success return and can leave some targets unreserved. | Prep should match bench/interaction exclusivity and validate actual ingredients at execution. “Called ReserveAsMany” is not evidence every planned stack belongs to the cook. |
| Native `ExtractNextTargetFromQueue` removes corresponding target/count entries and fails if the requested count exceeds the actual Thing's count. `StartCarryThing` supports inventory, enqueues a hand-capacity remainder, and normally fails if the full selected amount no longer exists. | A native continuation supports mixed inventory/floor selections and exact quantities. Preserve full outstanding requirements; do not shrink requested totals just to make invalid plans pass. |
| `DoBill.MakeNewToils` invokes `Notify_DoBillStarted`, collects, creates/resumes unfinished work, performs recipe work, then finishes/stores products. `IsContinuation` compares Bill identity. | Prep does not call recipe-start/consume/product hooks. Native continuation starts them once. Do not copy an already-started recipe's `placedThings` or driver work counters into this new path. |
| `CleanupCurrentJob` releases reservations, marks the driver ended, invokes all cleanup/finish actions, clears driver/job state and may return the Job to the pool. | Build/copy all native continuation data before switching jobs. Never access fields of a potentially pooled prep afterward. Finish actions run for failure and interruption too. |
| `StartJob` cleans the old job, assigns the new Job's start tick, overwrites its think-tree/giver from the explicit arguments, reserves it and can insert an opportunistic job before it. | Pass preserved giver/tree context explicitly. Successful native startup is a boundary to observe, not something established by allocating a Job. Use ordinary reservation failure handling and prevent ingredient unload/diversion through the handoff. |
| `EndCurrentJob(Succeeded)` can insert `Wait_MaintainPosture` and then return to normal job finding. | Merely ending prep reintroduces a gap and reselection; it does not deliver the retained plan. |
| `TryTakeOrderedJob` sets `playerForced=true`; a replacing order queues itself before interrupting current work. | Do not use this API for an automatic craft continuation. Do not enqueue the old craft in unconditional cleanup, ahead of the new order. |

## Full selection ownership and context

The initial replacement must copy **all** native selected targets/counts: existing own inventory, selected stock held by another pawn, and floor stock. Only eligible floor rows participate in the one-pass sweep; selected inventory rows remain part of the native plan. The existing implementation saves only floor rows and relies on reselection; that is the contract being replaced.

Use fresh lists owned by the prep. Retain `bill`, bench target A and ordinary `haulMode` (`ToCellNonStorage`), plus intentional context such as `workGiverDef`, locomotion urgency, expiry/override policy, relevant access flags and `source`. Actual `StartJob` supplies the selected think-tree/giver after candidate construction; capture those from the running prep at handoff and pass them to native StartJob. A JobTag is not a Job field; use the actual work context/current tag deliberately if a tag argument is needed. Do not fabricate player-forced state.

Normal newly selected vanilla bill jobs have no completed `placedThings`, no recipe work progress and no partially extracted active target. Initially exclude/leave native a candidate representing unfinished work, an already-started specialized flow, or a foreign shape whose state cannot be preserved. Preserve meaningful queue A / target C or decline such a foreign candidate explicitly; do not silently repurpose state owned by another patch. The ordinary path should not need a stored duplicate native Job.

At successful handoff, `JobMaker.MakeJob(JobDefOf.DoBill, bench)` gives a new load ID. Copy the remapped target/count lists independently; do not reuse the prep ID to make it appear to be the same job. If diagnostics need continuity, record the two IDs alongside the shared Bill ID. Native reservation references belong to the new Job, not the old one.

## Atomic transfers and exact remapping

The old `TakeToInventory` plus later tag toil cannot supply a reliable retained plan: it uses a transient intended amount and def-total delta, then selects an arbitrary inventory stack of that def. The native `ThingOwner<T>.TryAdd(..., true)` can merge into multiple existing stacks. A Boolean return does not identify those destinations; a false return can follow partial merges when no remaining stack slot is available.

`ThingOwner.TryTransferToContainer(..., out Thing)` is not a universal provenance solution either. It rejects map-to-container moves. Its out value is the split Thing passed to `TryAdd`, which can have been absorbed/destroyed; it need not be the surviving recipient of every merged unit. Inspect membership and actual deltas, not just non-null out values. `Thing.ParentHolder` is `holdingOwner?.Owner`; own inventory membership is `inventory.Contains(thing)` / `thing.holdingOwner == inventory`, or `thing.ParentHolder == pawn.inventory`, **not** `ParentHolder == inventory.innerContainer`.

Recommended transfer transaction within one instant toil:

1. Recheck current source, selected remainder, reservation, forbidden/area/reach conditions and current carry/CE capacity before splitting. Preserve the originally selected quantity requirement even if fewer units are presently movable.
2. Split at most the amount selected and permitted now. Use the actual split object's recipe-relevant attributes; do not reconstruct a Thing from its def.
3. For merges, iterate explicit compatible recipient Things and measure each recipient's actual count growth and the split's reduction. Restrict optional merging into pre-existing stacks to appropriately tracked/owned cargo so tagging does not accidentally claim personal inventory. Keep incompatible same-def variants separate. A remaining piece can be added with merging disabled, after which membership establishes its identity. If another mod overrides that operation, verify the resulting recipients or invalidate the plan rather than inventing a def-only mapping.
4. Record every actual `(recipient Thing, moved units)`; update the selection rows, tag those actual recipients with the correct CE growth notification, refresh pickup grace, and advance progress in the **same toil action**. Do not save a gap where inventory changed but plan/tag/cursor still describes pre-pickup state.
5. Return any unaccepted split safely to its source or nearby valid location, with its actual resulting identity preserved where possible. A failed placement is not permission to forget a detached Thing. Do not count requested units as moved.

A small append-only bookkeeping strategy avoids a second plan dictionary: keep a saved original scan-row count and cursor. During prep, each original row is visited once. A partial pickup leaves the remaining requested count on the original floor row and appends recipient/count rows for verified transfers. A complete pickup can replace that row with one recipient and append further recipients. Appended inventory rows are not new sweep work. Do not remove/reorder original rows while their indices drive the cursor; normalize and coalesce duplicate Thing targets at handoff.

Example: originally selected inventory milk 12 plus floor milk 8. Five actual floor units merge into the selected inventory stack. The continuation requires inventory 17 plus floor 3, **not** inventory 20, and not inventory 17 alone. If the source now contains fewer than the remaining three required units, the plan is invalid; the handoff must not quietly create an under-supplied recipe. Multiple rows targeting the same Thing must have aggregate requested quantity no greater than its available stack count.

An original complete native selection supplies the recipe allocation proof while quantities/attributes and allowed filters remain equivalent. Preserve that proof; do not independently refill overlapping recipe slots or rerun a chooser that spends physical units twice. If recipe-relevant values or allowed filters change, validate the retained quantities against every required slot or invalidate/replan outside the completed handoff. `InventoryShare.IsUsableForBill` means “usable for some slot,” not “this whole selection satisfies the recipe.”

## Partial pickup, invalidation and reservations

Capacity failure alone need not abort useful work. A complete selected recipe can remain valid as a mixture of gathered inventory and uncollected floor targets; native DoBill can finish those remaining collection trips. If no pickup is possible, avoid routing at all when this is cheaply knowable, or fall through to the valid native selection at execution. Do not manufacture a successful sweep count.

Before native continuation, validate: same referenced Bill is still in the expected bill stack; bench is spawned, usable and not burning; bill is not suspended/deleted; `ShouldDoNow` and `PawnAllowedToStartAnew` still allow a new iteration; required skill/work restrictions still hold; ordinary scope remains appropriate; all requested target amounts remain available/allowed with no aggregate over-allocation. A second cook may have reached a Do-until target while this cook walked. Native driver fail conditions do not repeat every workgiver eligibility check; constructing DoBill directly is not itself revalidation.

Do not call the full WorkGiver to choose the continuation: it may pick another bill, clear the bench, choose a different ingredient mix, or enter the old gather path again. Validate the retained bill/selection. A missing or newly forbidden target that prevents a complete selection means cancellation/recovery, not deleting that row and crafting anyway. Future ordinary work scanning may choose a new complete plan; already useful tagged inventory remains available. Bound repeated zero-progress conversion using actual execution evidence, not candidate probes.

At prep start, reserve the bench **and interaction/sitting spot**, then selected ingredients with native-style semantics. Newly created/merged held recipients need ownership protection as well as corrected queue references. During prep the new JobDef must join HD's unshareable-preloaded-stock protection so another HD consumer cannot take the cook's ingredients. Existing native/foreign reservation checks still apply to selected stock held by another pawn.

At handoff, StartJob releases old reservations and reserves the new Job in the same simulation call, but cleanup/other patches are reentrant boundaries. Recheck remaining targets and observe native reservation results. Use `preToilReservationsCanFail:true` for a legitimate race rather than turning a now-unavailable bench into a red error. Native best-effort ingredient reservation can still omit targets, so inspect actual claims/physical interactions in BG09; do not infer full acquisition from the driver returning true. Do not pre-register a separate native Job in a static map or double-own it in both a driver field and the queue merely to reserve across this boundary.

## Successful handoff and player intent

Prefer a successful final toil with one synchronous native `StartJob`, passing `lastJobEndCondition: Succeeded` and explicit preserved context. Build/validate first, mark the prep's handoff decision committed, then switch and return immediately. Actual `JobDriver.TryActuallyStartNextToil` tests `ended` / current-job identity after init actions, so it will not legitimately continue the old instant-toil chain after a successful switch.

Do not use an unconditional finish action or a general finalizer factory: both are queried on interruptions, and finalizer jobs can run before queued work. Do not use `TryTakeOrderedJob`, which changes forced status and queue semantics.

Before constructing the automatic continuation, give precedence to a replacing player order, drafting/mental/downed state, and queued real player work. A queued order arriving during prep should not find an automatic continuation inserted ahead of it. A conservative policy is to abandon this immediate continuation when such work is pending, finish/recover the gathered cargo through normal rules, and let the actual queue run. Do not resurrect the old craft after a cancel. Do not purge unrelated queued jobs as a handoff shortcut. Housekeeping queued unloads require separate handling so they cannot steal this recipe's ingredients while a valid continuation is starting.

`StartJob` may insert an opportunistic prefix. Returning to the bench before handoff normally removes vanilla's distance-based opportunity, but this is not a sufficient invariant for HD or other prefixes. Verify HD's normal unload, opportunistic unload, inventory sharing, en-route pickup, save cleanup and softlock-recovery gates protect the new prep and then the native bill's selected inventory. Audit all explicit old-def references and semantic sets in `HaulersDreamDefOf.cs`, `PawnUnloadChecker.cs`, `OpportunisticUnload.cs`, `InventoryShare.cs`, `EnRoutePickup.cs` and game-component save cleanup. Do not disable a global option to protect one continuation.

## Common Sense and relocation composition

The actual installed CS prefix replaces `DoBill.MakeNewToils` when either cleaning or gathering is enabled. With gathering off, its replacement calls native `CollectIngredientsToils`, then appends cleaning and native recipe toils. With gathering on, it consumes the selected queue itself, recognizing own inventory via `ParentHolder == pawn.inventory`, using count-preserving extraction/splits, and later passing ingredients through hands to placement.

The proposed handoff keeps real `DoBill`, so cleaning and recipe hooks remain owned by that actual installed pipeline. If CS gathering changes from off to on during HD prep, do not overwrite global CS settings: the native continuation still carries the exact remaining selection, including already gathered inventory. Test the installed CS branch with both complete inventory and partial floor remainders. Source inspection suggests its own-inventory path can handle this shape, but BG05 must establish no duplicate pickup/consumption and actual cleaning.

CS determines iterator layout when SetupToils enumerates it; toggling options can change layout on a later reconstruction. Keeping HD prep separate avoids adding another conditional offset to that native/CS saved layout. It does not claim to solve all existing CS save/load changes after native crafting has already begun. Include the prescribed paused flips and restart modes as runtime controls.

Current `Patch_WorkGiver_DoBill_Routing` runs last and defers only when the final result is not plain DoBill. A new prep def preserves that rule. Inserting gather toils into native DoBill would leave its def unchanged and would require a shared, observational gather-ownership marker/predicate for relocation; otherwise relocation can take a stack the new phase intended to gather.

## Why not inject gather toils into DoBill first?

A single native job has real advantages: one reservation owner, one job ID, no queue handoff, and existing native recipe bookkeeping. It would be attractive if no iterator replacement or persisted jobs existed.

Here it has additional concrete costs:

- CS can bypass the vanilla MakeNewToils body entirely. Patching only vanilla's body misses cleaning-only CS; patching the returned sequence or common collection iterator needs actual ordering and scope verification.
- Adding a prefix changes every subsequent numeric `curToilIndex` / `nextToilIndex`. Old active native jobs and CS variant layouts require an explicit migration scheme, not a guessed offset.
- A fixed always-present block would still need a scribed selection/progress/ownership marker that survives reload and is not recomputed from current inventory/settings. Vanilla JobDriver_DoBill has no fields for that state. A static association alone is insufficient.
- Conditional insertion based on “has floor ingredients” is especially unsafe: gathered inventory changes that condition, so a loaded job can restore its old index into a different operation.
- Relocation still sees DoBill, and existing save/unload/share gates must recognize an HD-owned gather phase inside it.

Therefore the new prep def is justified by persistence and composition, not convenience. Reconsider in-driver gathering only if a focused prototype proves these costs lower than the handoff and passes the same BG matrix; do not switch architectures merely to avoid a failing fixture.

## Old saves and new save invariants

Keep the legacy def/driver available with its existing toil shape and serialized fields. Loaded old prep jobs have only floor selections and cannot truthfully reconstruct omitted inventory ingredients. Let the old bounded driver finish its existing work or take its normal failure/recovery path; future newly selected work uses the new driver. Do not invent a retained native plan from the legacy floor list. Existing tagged cargo must remain recoverable. Preserve legacy exclusions/cleanup memberships while it can still be loaded.

New prep owns its complete selection in job queues, plus scribed progress such as scan limit/cursor, handoff/terminal phase and schema version. Persist fields before calling base `ExposeData` at PostLoadInit when they determine any reconstruction behavior; better still, make layout independent of those values and defer cross-reference-dependent validation to the first execution checkpoint. The base rebuild occurs inside base ExposeData, before a later derived PostLoadInit block.

No duplicate deep-owned pending native Job is needed. Before the final atomic switch, only prep exists; afterward, native DoBill is current or is the game's queued continuation behind a genuine opportunistic prefix. Save/restart at walk, partial pickup and ready-to-handoff stages must conserve counts and not replay transfers. Separate normal save/load tests from forced suspension tests; the latter creates a fresh driver.

## Implementation sketch (design only)

```text
route(native candidate):
    check ordinary scope, ownership, full queue/count shape, benefit and carry opportunity
    create fresh new-prep Job; copy full selection and intentional context
    return candidate; do not reserve, queue, move another pawn, or create external plan state

new prep, fixed toils:
    validate actual selected plan and initialize persisted one-pass cursor
    choose next eligible original floor row; skip own/other inventory rows for prep
    walk with live forbidden/reservation checks
    atomically transfer -> exact recipient deltas -> remap rows -> tag -> advance cursor
    loop by stable toil references
    return to bench
    validate bill, full remapped selection, priority/player intent and actual ownership
    if invalid/cancelled: recover without a successful continuation
    else: allocate fresh native DoBill + independent normalized queues
          StartJob(native, Succeeded, preserved context, reservation failure allowed)
          return immediately
```

One additional existing side effect matters to BG12: `SharedBillPatches.Patch_WorkGiver_DoBill_JobOnThing` calls `SharedInventoryApproach.MaybeApproach` from a candidate postfix. That helper excludes float-menu previews, but an ordinary unselected work-scan probe can still issue `TryTakeOrderedJob` to an idle carrier. Depending on postfix order, this can happen before conversion. A stateless new prep payload alone does not satisfy the whole seam's observation-only requirement. Put any needed carrier nudge at actual dispatch/start and test an unselected probe whose native selection includes another pawn's inventory. Distinguish HD-added effects from vanilla's own workgiver bookkeeping when instrumenting BG12.

## Review and runtime gate

BG01/BG02 must show native products and consumption, including actual cleaning, not just a new prep job name. BG03/BG08 must assert exact identity/count remapping under partial transfer and merging. BG04 requires the historical pie and multi-slot cases. BG05 validates actual CS patch layout/ownership. BG07/BG09 cover new orders, cancellation, competing actors and bill invalidation. BG10 covers old and new saved jobs across process restart. BG11 preserves excluded workflows. BG12 observes real unselected probes and no new persistent state or actor dispatch.

No criterion is marked satisfied by this design review. Runtime findings can revise the architecture; any revision must retain quantity conservation, native recipe execution, player precedence and explicit save compatibility.

## Implementation handoff, 2026-09-07

The focused implementation is now present for root's first build and independent review. This is a source implementation handoff, not a runtime result. No build, test runner, deployment, game launch, git operation or network request was performed by the implementing subagent. All BG01–BG12 rows remain unverified.

### Files and resulting behavior

- `Source/HaulersDream/Patch_WorkGiver_DoBill_InventoryRoute.cs` creates `HaulersDream_GatherBillIngredients` for beneficial ordinary selections with at least two selected floor stacks. It copies the complete native target/count lists, including selected inventory. Mixing and the presence of existing tagged stock no longer categorically exclude the route. Forced ordinary work, medical/autonomous/mech routes, whole-stack special recipes, unfinished/foreign continuation payloads and an HD batch's owned fallback remain outside it. No selected-stock reservation, actor order or external plan state is created in this candidate postfix.
- `Source/HaulersDream/JobDriver_GatherBillIngredients.cs`, its `Defs/JobDefs/Jobs.xml` entry and `Source/HaulersDream/BillGatherContract.cs` implement the separate retained-selection architecture. Original queue rows keep stable cursor indices; receipt rows can be appended. The fixed seven-toil layout and scribed scan limit, cursor and terminal flags belong only to the new non-suspendable driver. The legacy prep driver and def retain their existing payload and numeric toil layout.
- `Source/HaulersDream.Core/BillGatherSelection.cs` remaps exact observed transfer receipts, normalizes physical references, rejects missing/overallocated units, and checks that whole selected units can satisfy all current ingredient slots without double allocation. This bounded check can backtrack through overlapping filters. It neither reselects stock nor changes native consumption. Its 20,000-node and 512-frame upper bounds conservatively reject excessive validation complexity; they cannot authorize an incomplete recipe.
- Actual pickup splits at most the selected quantity and current carry/CE allowance. Explicit merges target only tracked cargo and record both recipient growth and source reduction. A remaining split is added with merging disabled and accepted only with verified identity, ownership and count. Successful receipts remap the same job before the one-pass cursor advances. A capacity-limited floor remainder stays in the complete native plan. The handoff normalizes actual identities and independently checks current bill attachment, suspension, repeat/target eligibility, skill/work capability, forbidden/reach/reservation/filter/radius constraints and every ingredient slot.
- Only the successful final toil creates a fresh-ID `JobDefOf.DoBill` with independent lists and preserved bill/workgiver/think-tree/locomotion/expiry context. It uses native `StartJob` with normal reservation failure permitted. It never executes recipe toils under the custom def, clones a Job, copies its load ID, uses a forced-order API for continuation, or manufactures a continuation in cleanup. An existing queued order prevents handoff. Failed/interrupted work leaves tracked cargo recoverable, with any cleanup unload behind queued work and normal drafting safeguards.
- `Source/HaulersDream/SharedBillPatches.cs` moves this route's old carrier nudge from candidate `WorkGiver_DoBill.JobOnThing` to actual `JobDriver.Notify_Starting`, with current job/driver identity and master/share settings gates. This retains `SharedInventoryApproach` itself and its other consumers. Native selected jobs, new gather and legacy gather are recognized; no unselected candidate can dispatch this bill nudge.
- `Source/HaulersDream/HaulersDreamDefOf.cs`, `PawnUnloadChecker.cs` and `OpportunisticUnload.cs` recognize both gather defs in the appropriate cargo/unload families. The shared custom-driver set covers softlock protection and queued-job save cleanup. Direct inspection of `Patch_ScribeSaver_InitSaving` confirms it leaves the current job intact, so active gather persistence is not intentionally interrupted by saving. `BillRouteGate.cs` only updates the moved nudge's documentation reference.
- `scripts/check-bill-route-gate.ts` now discovers the new gather def and examines executable code for full-list copying, live provenance/recipe validation, a single final native handoff, independent identity and actual-start carrier dispatch. `scripts/check-sweep-walk-guard.ts` includes the new driver's cursor-aware forbidden walk. These guards are structural defenses, not evidence that a real pawn followed the intended lifecycle.

### Meaningful tests supplied, not yet run

`Source/HaulersDream.Tests/BillGatherSelectionTests.cs` supplies thirteen cases: partial transfer into selected inventory with an exact floor shortfall; one full transfer into multiple actual recipients without consuming their pre-existing unselected units; zero-pickup preservation; invalid-receipt atomicity; disappearance of a required remainder; duplicate physical allocation; distinct same-definition variants; ordinary mixed meal accounting; backtracking through overlapping ingredient filters; indivisible nutrition units across multiple slots; non-mixing slots across same-kind physical stacks; changed filters/exhausted search; and bounded recursion for oversized selections. They exercise the Core quantity state transitions called by production. They do not exercise ThingOwner callbacks, game reservations, Common Sense toils, save cross-references or actual consumption.

### Source review results and remaining runtime risks

Read-only decompilation verified the actual static `ReachabilityImmediate.CanReachImmediate(start, target, map, mode, pawn)` signature and the native `StartJob` named arguments. Owned inventory bypasses path/radius checks as HD's existing ingredient injection does; another pawn's inventory is reached through that carrier. `Thing.ParentHolder` is the owner (`Pawn_InventoryTracker`), not its `innerContainer`. Native `NotifyAddedAndMergedWith` only dispatches transporter notifications in the inspected game; direct merges still require the actual CE fixture because CE's patched mass/bulk cache and hold accounting cannot be proven by the Core receipt test.

Pickup rejection attempts to restore a detached surviving split into its source stack, then onto the source/pawn map, then into an inventory/carry owner. Unknown callback behavior invalidates the recipe and cannot authorize crafting. A foreign mod that defeats every placement and ownership API can still reach the explicit restoration error; this extreme path is not a demonstrated conservation guarantee. BG08 should first cover real CE limits, merge recipients, failed adds and source disappearance, including the actual maps/owners after failure. A callback exception is surfaced; it is not silently treated as successful gathering.

Settings are checked when selecting a new gather, and capacity is checked live per pickup. A live Common Sense toggle is allowed to affect the fresh native DoBill iterator after handoff; the complete retained selection must prevent a second selection cycle. This ownership transition needs BG05 runtime observation. New gather is non-suspendable to avoid fresh-driver cursor replay when native suspension requeues a Job; save/load of its running driver remains supported by its scribed fields. Legacy active prep uses the unchanged historical finish/failure path and never invents omitted inventory selections. These are design choices pending BG07/BG10 observations.

For root's next review: preserve the pristine Workshop baseline, build this source only through the isolated setup, run the new Core cases and source guards, then observe BG01/BG02 with actual native consumption and CS cleaning before expanding the matrix. In BG03, retain the one-floor-stack native fast path while also testing a useful multi-floor sweep with pre-existing tagged inventory. Independently review final-handoff reservation races and queued orders, direct merge/CE notifications, the bounded recipe check and cold-reload toil/cursor ownership. A passing build or a new gather job name closes none of those runtime requirements.

### Independent QA correction: owner notifications and pre-existing claims

Root reports the first implementation built, all thirteen guards passed and 2,905 Core tests passed. Those results precede this correction and do not establish its behavior. Independent product QA identified two real source defects in the first implementation; see `bill-gather-implementation-qa.md`.

1. The installed Combat Extended assembly at Workshop `2890901044/Assemblies/CombatExtended.dll`, SHA256 `3102BC2276C583E51FE85AE340E5B986F80452171DB63EA72D04F7651B96AFE3`, patches `ThingOwner.NotifyAddedAndMergedWith` to call `CE_Utility.TryUpdateInventory`. Its `CompInventory.currentWeight` and `currentBulk` expose cached fields; `CanFitInInventory` reads those caches without refreshing them. Direct `TryAbsorbStack` plus HD's hold notification omitted the owner event. A complete merge could therefore leave the next pickup's CE allowance stale. This was independently confirmed by decompiling the installed CE patch and CompInventory, not inferred from vanilla's otherwise-transporter-only notification body.
2. Existing tagged inventory can already be reserved by another pawn before gathering starts. The first merge loop did not require its recipient to be reservable until after moving the ingredient. It could contaminate the retained selection with a stack unavailable to this cook and abort on handoff despite a viable separate-stack destination.

The corrected merge loop reserves its actual recipient for this gather before `TryAbsorbStack`; a failed reservation skips that candidate. Successful physical transfers record an incremental receipt, remap the remaining original row and mark the load before owner/hold callbacks. A cached exact `MethodInfo` invokes the real protected `ThingOwner.NotifyAddedAndMergedWith(Thing,int)` method through normal reflection dispatch, so installed Harmony patches participate. It does not reproduce CE cache internals or rely on publicized compile-time access to a protected runtime member. `RegisterHauledItem` runs in `finally` around that notification. Proven transfers are also recorded/notified when an absorb callback throws after moving the units; exceptions still invalidate and surface the failure.

The new-stack path observes ownership and unchanged count in `finally` around `TryAdd`, because native `NotifyAdded` occurs after insertion and can throw before `TryAdd` returns. Its receipt and tag are still recorded when the actual stack was inserted. Each receipt is remapped once; there is no later replay of the accumulated receipt list. Reservation failure on final receipt verification now invalidates the plan explicitly instead of ignoring the return value.

One additional Core case checks two incremental receipts into an already-selected held stack: twelve original units plus two observed moves of five normalize to twenty-two, while the original floor row is replaced only by its last portion. There are now fourteen focused Core cases. The structural bill guard also requires recipient reservation before absorb and dispatch of the actual native owner merge notification. These changes have not been built or tested by the implementing subagent. The mandatory CE runtime witness remains: a first pickup fully merges into tagged stock, then a second pickup would exceed the remaining CE weight or bulk; observe live cached values, accepted second count and exact floor remainder. A separate two-pawn reservation fixture must show a pre-claimed tagged recipient remains unchanged while the gathered source becomes an independently reserved stack and native crafting still completes.

## Sibling direct-merge path discovered during integration

Root's subsequent source comparison found the same missing owner-notification shape in existing `JobDriver_BulkHaul.DepositSwept`: it directly calls `target.TryAbsorbStack(split, true)`, measures the source reduction and calls `RegisterHauledItem(target, moved)`, without dispatching `ThingOwner.NotifyAddedAndMergedWith`. `CECompat.NotifyHeld` invokes only the separate HoldTracker API. The installed CE hook/cache evidence above therefore applies to a concrete additional path requiring investigation, not merely the new bill driver. No bulk-path runtime failure or fix is claimed yet.

Add a distinct CE bulk-merge witness: actual automatic first pickup fully absorbs into already tagged stock, then a second pickup challenges remaining CE weight/bulk. Record actual inventory cache values before/after the merge, native owner-notification invocation, physical recipient growth and source reduction, next accepted pickup and floor remainder. Also test a callback that throws after a real absorb without losing the receipt/tag or replaying units. Preserve pre-existing personal stock and other-pawn reservations. This sibling investigation does not reopen the already accepted non-CE BG01/O1 traces, nor does their success certify the CE path.

The other direct absorbs found in `JobDriver_SelfPickup` and `YieldRouter` are source-restoration paths and require their own ownership-context inspection; do not automatically apply an inventory notification to a spawned floor recipient. The new gather's rollback likewise needs context-sensitive treatment. Track any confirmed affected sibling in its source-level acceptance mapping rather than declaring all direct absorbs fixed together.
