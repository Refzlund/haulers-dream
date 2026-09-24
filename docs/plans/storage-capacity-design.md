# L04-A1/A2: storage capacity design

Status: proposed implementation design, 2026-09-07, with independent-review requirements incorporated below. No capacity implementation or runtime acceptance is claimed. L04-A1/A2 and the reported #256/#268 transport failures remain unverified. This document addresses capacity admission; L04-O1's actual inventory-holder recognition is a separate defect and implementation owned by root. It does not establish that any original report has been reproduced.

## Independent-review decisions

Root accepts the architectural recommendation in [storage-capacity-design-review.md](storage-capacity-design-review.md): identified physical resources and allocation records, a cheap read-only candidate-cell gate, and one main-thread owner of admission/reconciliation. The following requirements override any looser wording in the initial proposal:

- Potentially reentrant code **must not invoke linked `StorageGroup.CellsList` at all**. Copying its result still mutates the native outer iterator. Enumerate concrete member slot groups with owned coordinates. CAP-17 must observe native iteration too.
- Separate candidate preview, reservation preflight, queued plans, actual current-job/current-driver start, loaded active jobs and delivery. Admission is idempotent by actual job/request identity. `TryMakePreToilReservations` alone does not prove actual start; a cached Job can invoke it earlier.
- The hot cell gate does no whole-group matching, repair, allocation reassignment or publication. Main-thread admission and a deterministic fair repair queue own those changes. Worker queries can observe an immutable allocation index, but cannot publish claims or trigger a second transaction inside a physical transfer. Dirty resources require explicit deferral and guaranteed repair progress.
- Physical pickup/merge/deposit receipts, outstanding quantity slices and virtual-slot rebinding form one accounting transaction. Ambiguous or exceptional callbacks retain exclusion and request repair; they cannot announce spare room. Aggregate slices assigned to one held Thing must never exceed its actual count.
- Unsupported provider resources retain native exclusivity without reopening adjacent HD-owned resources. A provider veto remains refusal. Provider capability changes require draining/reconciliation, not a blanket native fallback across existing HD allocations.
- Matching is over known demands and explicitly movable allocations. Already-walking claims stay fixed unless a main-thread transaction atomically repairs their actual intents. Do not promise an optimal allocation for future unknown or arbitrary non-transitive compatibility.

Exact lifecycle hook placement, repair work counters/latency and provider capability detection still need implementation-level evidence before live integration. The first runtime slot fixture is being implemented independently; no incomplete allocator will be connected to reservation stripping. Accepted dependency order is physical resource projection → allocator/index → actual admission/load/delivery lifecycle → planner integration and full gameplay matrix.

## Decision

Replace the shared-empty-cell/per-def-partial model with **individual existing-stack top-ups and shared vacant stack slots at identified cells**. Admit actual cargo parcels against those resources, preserving each parcel's real Thing, carrier and storage context. Use the same allocator for the bulk plan and live commitments. A live commitment must retain an allocation to measured resources, rather than only a def-wide quantity which later readers guess how to spend.

Changing `EmptyCells` to a group-wide `EmptySlots` alone is insufficient. It would fix the occupied-shelf example but retain incorrect subject compatibility, accepted-cell subsets, heterogeneous linked storage, absent-def claims and stale observations. Conversely, charging every claim against shared slots would hide genuine existing-stack top-ups.

The smallest coherent change needs three layers: a game adapter which supplies identified resources and eligibility; a pure allocator which spends those resources; and immutable, session-local claim allocations backed by live cargo evidence. It does not require saved hard cell reservations, an optimal colony-wide packing solver or a new destination priority policy.

## Inspected evidence

Production files read: `StorageCommitments.cs`, `BulkHaul.cs`, `StorageCommitAdapters.cs`, `StorageEvidence.cs`, `JobDriver_BulkHaul.cs`, relevant `HaulToStack.cs` and unload call sites, plus Core `StorageGroupBudget.cs` and `StorageClaimLedger.cs`. The existing defect/destination investigation is [haul-loop-investigation.md](haul-loop-investigation.md).

Installed artifacts, identified independently of the reporters' versions:

| Artifact | SHA-256 / evidence location |
| --- | --- |
| `C:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll` | `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A` |
| Installed ASF `3033901359\1.6\Assemblies\AdaptiveStorageFramework.dll`, version 1.2.4.0 | `28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9` |
| Existing decompilations | `%TEMP%\haulersdream-goal-20260907\StoreUtility-installed.cs`, `GridsUtility-installed.cs`, `AdaptiveStorage-installed.cs`; `%TEMP%\haulersdream-bill-design-20260907\Verse.Thing.cs` |
| Additional read-only decompilations for this design | `%TEMP%\haulersdream-capacity-design-20260907\`: `Verse.ThingWithComps.cs`, `RimWorld.CompQuality.cs`, `RimWorld.StorageSettings.cs`, `RimWorld.Building_Storage.cs`, `RimWorld.StorageGroup.cs`, `RimWorld.ForbidUtility.cs`, `Verse.AI.HaulAIUtility.cs` |

Actual API facts which constrain the design:

1. `GridsUtility.GetItemStackSpaceLeftFor(cell, map, def)` sums all same-def stack deficits, **without `CanStackWith`**, then adds `max(GetMaxItemsAllowedInCell - GetItemCount, 0) * def.stackLimit`. `GetItemCount` counts Things whose category is Item. `GetMaxItemsAllowedInCell` normally reads the edifice's `MaxItemsInCell`, otherwise one. See the decompiled GridsUtility lines 267–316. This scalar is not a decomposition and is not an exact subject-specific capacity oracle.
2. `StoreUtility.NoStorageBlockersIn` tests **existingStack.CanStackWith(incomingThing)** and whether that existing stack is below its limit. If no compatible partial exists, the item count must be below the cell limit. It also rejects construction/passability blockers. A Boolean success proves some storage is possible, not how much. See StoreUtility lines 117–143.
3. `Thing.CanStackWith` checks destroyed state, item category, relic state, def and Stuff. `ThingWithComps.CanStackWith` additionally invokes every component's `AllowStackWith`. `CompQuality.AllowStackWith` compares actual quality. Def equality, def-plus-stuff and a hand-written quality key are all insufficient general substitutes. Use the actual directional method; do not assume arbitrary mod overrides form a transitive equivalence relation.
4. `IsGoodStoreCell` does **not** call `StorageSettings.AllowedToAccept`. The outer `TryFindBestBetterStoreCellForWorker` does. `IsGoodStoreCell` checks cell forbiddance, blockers, reservations, static fire, construction conflicts and reachability. The start for reachability comes from the subject's spawned parent/interaction cell when appropriate, otherwise the carrier. Replacing this with a distance or pawn-to-cell-only test changes semantics. See StoreUtility lines 215–242 and 327–369.
5. Actual `IntVec3.IsForbidden(pawn)` uses `CaresAboutForbidden`, `InAllowedArea` and squad-duty distance. It is not synonymous with the home area or a raw allowed-area bitmap. Preserve the actual call, including its pawn-state exceptions. See ForbidUtility lines 157–171.
6. `StorageSettings.AllowedToAccept(Thing)` checks the Thing filter and recursively the owner's parent settings. Linked `Building_Storage.GetStoreSettings()` returns the linked group's settings. The group's `GetParentStoreSettings()` returns the **first member's** parent settings. Consequently, checking the linked settings alone does not establish every member's fixed-filter acceptance. Check the actual member's parent/fixed settings too, and identify the member on the trace. A stricter intersection can expose an existing vanilla linked-group inconsistency; it must be deliberate and tested.
7. `StorageGroup.CellsList` clears and refills a **static temporary list**. Calling it while native storage search retains that list corrupts the outer search even if the result is immediately copied. Enumerate concrete member slot groups without invoking the linked getter in reentrant paths; own/deduplicate the resulting coordinates. Verify both the native outer iteration and HD snapshot.
8. ASF patches `GetMaxItemsAllowedInCell` to call `ThingClass.GetMaxItemsForCell(in cell)`. Its `UpdateMaxItemsInCell` distributes `CurrentSlotLimit` across `_maxItemsByCell`, reducing the sum to the selected limit; `MaxItemsInCell` is not an interchangeable per-cell value. Preserve the actual patched cell call. See ASF lines 6221–6240, 6441–6484 and 12678–12723.
9. ASF's `NoStorageBlockersIn` patch adds `IsValidStackDestination(existing)`, which uses `StoringAdaptiveStorage()?.ContainsAndAllows(existing)`. A compatible but invalid stored stack contributes **zero** top-up room, even when a different valid partial makes the cell's Boolean test succeed. See ASF lines 12742–12790. The adapter must validate each top-up target, not infer all are valid from one successful cell check.
10. ASF `HasCapacityForThing` distinguishes free slots, an outside/inventory subject and a spawned subject already inside the building. The full-building outside branch uses `StoredThings.AcceptsForStacking`, with a Performance Fish exception; the inside branch uses `ContainsAndAllows`. Its worker prefix only recognizes a `slotGroup.Settings.owner` which is a `ThingClass`; a linked `StorageGroup` owner has a different shape. Preserve this installed behavior through a tested ASF adapter for the actual member rather than assuming the worker's prefix ran for every pooled cell. See ASF lines 6689–6728 and 12727–12740.

## Current failure surfaces

| Current operation | Consequence |
| --- | --- |
| `MeasureGroupUncached`: any occupied cell puts its entire scalar capacity into `PartialSpace` | Every incoming def privately receives the same vacant stack slots inside that cell. |
| `ResolveGroupBudget`: first subject fixes the shared empty-cell pool | Later subjects inherit the first subject/carrier's accepted subset, even when their valid cells differ. |
| `PriceDefInto` / `IsPriced(thing.def)` | A second same-def Thing can inherit incompatible quality/stuff top-ups or different filter eligibility. |
| Average empty-cell capacity | Distinct cells and limits are lost. With capacities 1 and 3 slots, spending one cell at an average of 2 cannot identify which real room remains for another subject. |
| `PriceDefInto` subtracts only other-pawn claims for defs encountered by that sweep | A foreign live claim for a def absent from the sweep is omitted from the extras' budget. |
| `SpendCrossDefClaims`: foreign claims are rounded at vanilla stack limit with zero top-up credit | Valid foreign top-ups consume unrelated shared room; deep-storage slots and disjoint eligible cells are not identified. |
| Same-def scalar subtraction in `FreeUnitsFor` | Claims for incompatible same-def cargo are treated as interchangeable; a disjoint destination subset can be charged against the asker. |
| `EnoughFor` reaches 24 stack limits, then `Unbounded` bypasses cross-def claims | One plan's maximum does not bound existing claims plus a new plan. In BulkHaul it also discards the finite witnessed floor entirely. |
| Truncated adapter scans fail open | An unfinished observation becomes permission to keep the full count or select the group. Unseen cells are not evidence for that quantity. |
| Tick/Thing/pawn/filtered memo | A same-tick deposit, stack mutation, changed filter contents or area can invalidate the answer. The stale error is not inherently one unit: an entire stack or several slots may change. A Boolean filtered flag also conflates distinct active contexts. |
| `Commit`/`TryCommit` merely record requested units | A stale plan can publish an over-capacity claim; `TryCommit == true` currently certifies recording, not successful admission. |
| Bulk driver folds by def and reselects a destination at start | The capacity used to price individual pickups is not retained; a different destination can receive the old unvalidated quantity. |

These are source-level consequences. They are not a claim that each one occurred in #256/#268.

## Data contract

Names below describe proposed roles, not existing classes.

### Physical snapshot

`StorageSnapshot` contains a map/session identity, canonical group identity, concrete member slot-group identities and copied cell coordinates. Each `CellResource` contains:

- The cell, actual member, patched maximum stack slots and current Item count; `vacantSlots = max(limit - itemCount, 0)`.
- Existing item records with actual Thing identity, count, limit and sufficient state to revalidate the exact Thing/compatibility observation. Each record is its own resource; no group-wide `partialByDef` sum.
- Known ASF identity/valid-stack state and per-cell limit. Reject an invalid ASF top-up target independently of other items in the cell.
- Observation completeness and a query/transaction lifetime. Unknown, incomplete and fully scanned zero are distinct results.

Use `long` for multiplication/sums and clamp only at public integer boundaries. Do not materialize millions of individual slot objects: vacant slots are an integer per cell, expanded into a virtual slot only when a bounded request actually opens one. Oversized existing stacks have zero top-up capacity, not negative room.

### Cargo parcel and eligibility

`CargoDemand` identifies one actual source/held Thing or a verified continuation of it, its carrier, requested quantity, destination intent, storage context and stable request/claim identity. Two equal defs remain separate demands. A deterministic ordinal is based on world identities/queue order, not hash iteration.

Eligibility is an edge between a demand and a **specific cell/resource**. In a recursion-safe scope which suppresses only HD's own commitment gate, require:

1. Same map and live member/group membership; concrete destination enabled, correct faction, and the caller's existing priority rule. Capacity code must not turn `Unstored` into the pickup priority floor.
2. Actual Thing acceptance through the selected member's settings and parent/fixed settings. Apply ASF member capacity/state checks when ASF owns the cell; confirm the loaded adapter identities.
3. The actual `IsGoodStoreCell` result for this Thing and carrier, preserving other mods' patches. Do not disable all Harmony patches to measure space.
4. HD's actual `StorageFilterContext` and current building filter at that cell. `Unload` bypasses HD's opportunistic/before-carry building exclusions; it does **not** bypass vanilla Thing filters, allowed areas or ASF acceptance.
5. For an existing stack: `existing.CanStackWith(subject)`, positive actual stack deficit, and any storage-specific stack-destination validity. A subject's own already-spawned stack is not spare destination capacity for adding that same material.

Copying/creating a dummy Thing of the same def is not an acceptable replacement: it changes Stuff, quality, components, ownership and ASF's spawned/inside branch. Planning uses the real source state; admission and delivery revalidate with the real current state. A future held-state acceptance which has not been observed must not be described as guaranteed.

### Allocation and live claim

An allocation identifies units assigned to an existing stack or to a virtual new stack slot at a named cell. A virtual stack has a representative actual parcel and a remaining tail. One opened slot consumes one slot, even in an otherwise empty three-slot shelf. It never consumes the whole cell.

Tail sharing requires actual compatibility with the intended host and the same destination eligibility. Vanilla Stuff/quality and known stable component behavior can be cached for the lifetime of the transaction. Do not collapse arbitrary directional/stateful mod overrides into a global compatibility key; either evaluate a supported safe rule for the prospective merge or leave such tail sharing uncertified. Existing real compatible top-ups must still be counted. A def/stuff/quality signature may be a cache prefilter, never the final acceptance authority.

Extend/replace the current immutable claim row so it carries parcel-level allocations and live evidence identity. Per-def totals can remain for presentation and legacy summaries; they must cease to determine capacity. A row keyed only by `(pawn, def)` with one sample cannot describe two qualities or two valid destination subsets.

Claims remain derived session state, cleared/rebuilt on load. Do not serialize raw Thing/cell snapshots as permanent reservations. The current `storageClaimGeneration` remains useful, but writes publish complete immutable replacements. Queries do not commit, reserve cells, interrupt jobs or advance a persistent allocation order.

## Allocation procedure

1. **Import all live allocations to this group**, including every def absent from the candidate sweep. Charge their identified resources, not a guessed number of cells in the asker's subset. Clamp each parcel's outstanding quantity to fresh live evidence. Import only once; do not subtract same-def totals again afterward.
2. **Reconcile invalid allocations before using their supposed room.** If a target stack disappeared, membership/filter/limit changed, or a merge/split changed the cargo's actual identity, repair its resource assignment from live state. Held cargo gets priority over new pickup requests. Keep a reason and an explicit unallocated remainder if the world no longer has room; do not silently drop its intent and admit competing cargo into the same now-contended resources.
3. **Allocate compatible existing-stack top-ups first.** These are capacity-limited demand-to-stack edges. Within the candidate plan, use augmenting reassignment/max-flow on this small graph so a flexible request does not consume the only partial reachable by a restricted one while another suitable partial remains unused. Foreign claims only consume the top-ups to which they were actually allocated. Never convert their entire quantity into shared-slot demand.
4. **Use compatible virtual tails, then open actual vacant slots.** Allocate the remaining requests to eligible cells; an opened slot has the incoming stack limit, not the cell's total/average capacity. Keep each tail attached to its virtual host. Choose deterministically: existing intended destination first when valid, then constrained requests/cells and stable distance/cell/Thing-ID ties. Restriction ordering must not alter the external storage priority rule.
5. **Repair a bounded assignment conflict rather than calling it full.** Reassign an unfulfilled virtual slot to another valid cell when doing so frees the only cell available to another demand. Existing delivered physical items cannot be moved by this allocator. Allocation changes to other live claims must be published atomically and preserve those claims' quantities and valid destinations; otherwise treat them as fixed for this transaction. Do not silently move a walking pawn's destination without updating/revalidating its allocation contract.
6. **Return a certificate for the admitted quantity.** Distinguish `Admitted(n, allocations)`, `KnownNoCapacity`, `Incomplete(reason, cursor, witnessedAllocations)` and `Unsupported(reason)`. A lower witnessed quantity is safe to admit; it is never permission for the remaining desired quantity. An inability to find a globally optimal colored-slot packing is an allocation/search limitation, not proof that the physical group is full.

This intentionally does not promise globally optimal packing for arbitrary non-transitive stackability and carrier-restricted cells. The safety guarantee is that every admitted unit has non-overlapping, eligible resources. The top-up graph avoids the simple, preventable loss of valid partial capacity. Harder bounded-search conflicts must be surfaced and retried/continued, with fixtures showing progress, rather than hidden as zero or infinity. A full colony packing optimizer would be a separate, much larger performance/behavior change.

### Why retained allocation details are the minimal useful claim extension

Without allocation details, every query must remeasure every other claim using **that claim's** real pawn, subjects and context, then solve the common resource problem before answering. Merely replaying all foreign def counts is still wrong. Retaining the allocation provides an indexed charge for the relevant cells and avoids a full scan per foreign def on each storage gate call. Fresh cargo evidence and resource validation remain necessary; retaining a certificate does not make stale physical state trustworthy.

A prototype can reconstruct allocations deterministically from all actual parcels for correctness testing. Do not ship that prototype's unbounded claim-by-cell scans on the hot gate path without measured bounds.

## Integration and transaction boundaries

**Bulk plan:** one scratch allocation view per canonical group, shared by primary and extras. Replace both `ResolveGroupBudget`'s first-subject baseline and `PriceDefInto`'s once-per-def pricing. Every admitted candidate spends the same identified resources after all live claims. A later same-def candidate gets its own eligibility. Keep the intended group/member and admitted parcel quantity with the Job's plan; do not reconstruct it by a lowest-ID per-def sample at driver start.

**Job start:** repeat admission on the main thread against current physical state and claim generation, then publish claims and final queue counts as one transaction after successful source reservation. If another pawn committed after planning, shrink/remove the affected pickups before any pickup, or decline/replan the job. Reservation failure publishes no usable claim. Plan replacement must preserve already-held cargo and unrelated in-flight parcels; it must not exclude all of the pawn's older rows merely because a new plan exists.

**Gate and counter:** the gate must answer whether the particular candidate cell has an eligible residual allocation for that subject, not whether some other cell in its linked group has space. The counter obtains the total certified quantity for the actual request from the same allocation view. After this replacement there must not be a second same-def subtraction by `StorageCommitPolicy`.

Do not encode refusal as `int.MaxValue`, invent a one-unit admission, or return null to a caller which immediately calls `StartJob`. The current `Toils_Recipe`/vanilla job-factory consumers require a non-null job. Use the search and pre-toil reservation/admission seams to decline an automatic pickup safely when no positive certified count exists; no zero-count job may reach its carry toil. A positive job object used for API compatibility must not be treated as capacity authorization. This boundary needs a real runtime test, not only arithmetic tests.

`TryCommit` needs separate outcomes for **unsupported arbitration** and **capacity refused**. Falling through to vanilla reservations is appropriate when HD has not taken responsibility for a destination. It is not an appropriate response to a measured conflict with HD claims after other haulers' vanilla destination reservations have been removed. The reservation-strip bind tripwire must verify the revised admission path too.

**Delivery:** L04-O1 determines possession correctly. Capacity then credits only the deliverer's applicable outstanding allocations/material, while retaining its other incompatible/new-pickup commitments. Try the allocated eligible destination first; validate it before use and update the allocation if the world actually changed. Cell choice must honor the allocation's residual resources. Arrival retargeting is still required for real changes during walking, but is not the proof that initial overbooking is fixed.

**Splits, absorbs and deposits:** transfer the outstanding parcel quantity through real pickup/merge bookkeeping. After deposit, physical occupancy accounts for deposited material and the outstanding claim decreases by the same quantity; do not charge both indefinitely or release before the physical change is visible. Revalidate before a different same-tick decision. If the existing tagging callbacks cannot preserve a source identity through a merge, reconstruct from real compatible held stacks and trace the reconciliation; never select one arbitrary same-def sample for all remaining material.

There is a second transition to reconcile: when two compatible haulers share one virtual slot, the first deposit materializes a **real destination stack**. The second hauler's remaining allocation must become a top-up allocation on that resulting stack. It must not continue charging a new vacant slot in addition to the now-occupied physical slot. Likewise, if the first deposit absorbs into another existing compatible stack, follow that actual resulting target and quantity. Capture the actual placement/split/absorb result at an existing delivery bookkeeping seam, or reconstruct only when the before/after cell state and cargo evidence identify the assignment unambiguously. An ambiguous observation requires fresh allocation repair, not a guessed identity. This requirement can require a narrowly scoped vanilla delivery observation in addition to HD's existing unload bookkeeping; it cannot be supplied by a def-total evidence callback alone.

**Forced orders:** preserve the explicit player's override. It may preempt relevant conflicting allocations under the existing policy, but cannot create physical slots. Cross-def slot conflict must be considered when transferring claim priority. Record preemption and reconcile interrupted pawns' held cargo; do not grant the same physical room to both. Keep non-player automatic planning side-effect free.

**Unknown storage providers:** a Boolean acceptance patch alone cannot certify a multi-unit quantitative mass/fill-line/group cap. ASF has inspected quantitative cell APIs and individual-target validity; implement that known adapter. For another provider, use its verified numeric adapter or explicitly return unsupported for bulk expansion and retain the native reservation/haul route. Do not strip reservations for unsupported destinations. A native one-stack fallback remains a fallback, not proof that all of that provider's compatibility requirements have been met. Multi-cell item footprints require their own resource footprint; do not model them as independent single-cell placements accidentally.

## Bounded scanning and physical freshness

Remove the semantic use of `Unbounded` for cell storage. A witness of 24 stack limits is finite. It may terminate a scan only **after** live allocations and the tentative requests have been charged, and only when the admitted quantities are fully backed by those witnessed resources. Large/no-cell-grid destinations are different cases; containers continue using their own enroute capacity system.

Keep a bounded cheap observation pass (initially 200 examined cells, counting rejected cells), but preserve a cursor and completeness state. Include the actual candidate cell and relevant claimed cells explicitly; do not gate an unscanned selected cell from a zero in the first 200. A large, nearly full group must be able to discover room after cell 200 through deterministic continuation. Persist only cursor/topology progress across decisions; old physical counts are revalidated when used. A bounded per-map continuation queue is acceptable if repeated work scans cannot monopolize a tick and the progress test passes. Never make continuation availability depend on repeatedly selecting the same Thing forever.

Partial/incomplete means one of: admit the smaller witnessed safe quantity; continue the scan within its decision budget; or schedule a retry without admitting unknown extras. It does not mean accept the original full count. Also avoid permanently denying a group because a bounded first slice saw no room.

Replace arbitrary same-tick capacity reuse with a **scope-bound view**: reuse within one synchronous storage search/job-building transaction, then discard or revalidate for the next independent decision. A published admission freshly checks all resources it allocates, current cargo evidence and the claim generation. Reusing an old candidate Job is allowed only as a hint; pre-toil admission must still check current room.

This avoids needing comprehensive hooks for every mod's `stackCount` mutation. A tick number, ledger generation or first-item fingerprint alone cannot detect all physical changes. If future optimization adds a storage mutation generation, inventory/cell/stack/filter/area/ASF-limit changes all need coverage or the final fresh resource checks remain mandatory. A same-thread nested query must not overwrite an outer snapshot; a background query cannot publish gameplay claims from an unvalidated world view.

## Failing fixtures and acceptance evidence

All examples below are fixtures to implement, not executed results. Core fixtures validate allocation invariants; adapter fixtures must use actual game Things/buildings/API results. L04-A2 must exercise ordinary workgivers and real drivers, observing pickup/deposit and terminal jobs over multiple trips.

| ID / acceptance | Fixture and decisive assertion |
| --- | --- |
| CAP-01 / L04-A1 | Vanilla ShelfSmall: one full steel and one full wood stack, one vacant slot. Silver then cloth, both full requested stacks. Exactly one new slot may be assigned, in one sweep and in two same-tick pawn admissions. Reverse subject order. |
| CAP-02 / L04-A1 | Full three-slot shelf with a silver partial missing 7 and two full fillers. Silver gets exactly 7, cloth gets 0. A foreign silver claim for those 7 consumes no unrelated vacant slot elsewhere. |
| CAP-03 / L04-A1 | Initially empty three-slot shelf, three mutually incompatible defs. All three can occupy one slot each; the first must not consume the whole cell. A fourth cannot. |
| CAP-04 / L04-A1 | Same def, different actual Stuff or quality, and a full cell containing a partial compatible with only one. Incompatible incoming gets zero top-up even if the vanilla def scalar is positive; compatible incoming retains the real deficit. A second same-def candidate cannot inherit the first candidate's allowance. Use genuinely stackable fixture defs/components; do not assume ordinary unstackable quality weapons supply a vanilla partial-stack case. |
| CAP-05 / L04-A1 | Two partial stacks of one def in a multi-slot cell, only one compatible with the subject. Count only that stack's deficit; do not sum both because the cell passes its Boolean blocker check. |
| CAP-06 / L04-A1, D1 | Linked cells with different patched slot limits (e.g. 1 and 3), mixed occupied/vacant states, and subjects restricted to different members. Assign individual slots; no average capacity. Compare linked/unlinked ASF and vanilla shelf/zone group shapes. |
| CAP-07 / L04-A1 | Pawn A allowed only cell X, pawn B only Y. A's claim at X does not subtract Y. Then both allowed X only: their claims contend. A third flexible demand must not consume the sole partial of a restricted same-compatible demand while another partial is available. |
| CAP-08 / L04-A1 | Linked members with different fixed filters, plus two actual same-def subjects straddling quality/HP filter ranges. Check actual `AllowedToAccept`, each member's parent settings and per-Thing edges. Reverse linked member order to expose first-member inheritance. |
| CAP-09 / L04-A1 | HD `BeforeCarry` versus `Opportunistic` filter context in one tick, and an allowed/denied override edit between independent decisions. Then `Unload`. Verify exact current context, not a Boolean filtered cache key; vanilla storage filters and pawn allowed area continue to apply during unload. |
| CAP-10 / L04-A1 | Existing cloth claim, new silver primary and steel extra, all targeting one shared vacant slot. Cloth is absent from the new plan. Neither primary nor extras can spend cloth's allocated slot. Include the planning pawn's older held cargo. |
| CAP-11 / L04-A1 | Foreign cargo uses an existing compatible partial at X, new different-def cargo needs a vacant slot at Y. Both fit. Repeat with the foreign claim restricted to a slot at X and the new pawn restricted to Y; no aggregate subtraction may hide Y. |
| CAP-12 / L04-A1 | Exactly 24 initially witnessed slots, all claimed by other defs; a new 24-stack-capable sweep gets zero there. Add 25th witnessed slot and admit only its real capacity. MaxStacks early-exit cannot create infinity or ignore prior claims. |
| CAP-13 / L04-A1 | More than 200 cells; first 200 full/forbidden, a later cell valid. No unsafe full-count admission, no permanent false-full. Continuation finds and uses that cell within a recorded finite work budget. Also place a live claim beyond the first slice. |
| CAP-14 / L04-A1 | In one tick, measure a partial/slot, then physically fill it by one full stack without writing an HD claim, and query/commit the same Thing/pawn/group again. Second decision must see the changed capacity. Repeat with filter, allowed area and ASF limit changes. |
| CAP-15 / L04-A1 | Two candidate Jobs built before either starts. First start commits. Second start re-admits and shrinks/declines before pickup; no over-capacity row is recorded. Include failed source reservation and repeated availability probes with no starts. |
| CAP-16 / L04-A1, D1 | ASF cell with a valid compatible partial and an invalid/disallowed compatible partial. Boolean cell success must not authorize both deficits. Exercise `ContainsAndAllows`, full-building outside/inventory versus inside subject, linked Settings owner, and loaded Performance Fish branch separately where available. |
| CAP-17 / L04-A1 | Enter a nested query for a different linked group while the outer group snapshot is being evaluated. Outer copied coordinates and allocations remain its own. No mutation of vanilla `StorageGroup.CellsList` or HashSet-order dependence. |
| CAP-18 / L04-A1 | Compatible stack split, inventory absorb and partial deposit between decisions. Observe real IDs, counts, outstanding allocations and physical totals. Deposited units are counted exactly once; unrelated same-def incompatible held cargo keeps its own destination/claim. |
| CAP-19 / L04-A1 | Corrupt/unknown quantitative provider or multi-cell footprint unsupported by the adapter. Bulk does not expand using infinity and HD does not remove native destination reservation on an unsupported result. Capacity refusal cannot be mistaken for this fallback. |
| CAP-20 / L04-A1 | One-unit items, oversized stacks, zero/negative remaining room and very large limits. No integer overflow, zero-count carry job, null job passed to StartJob, or unbounded virtual-slot allocation. Test direct ordinary haul and bench-product haul callers. |
| CAP-21 / L04-A2 | Low-priority source, constrained higher-priority shelf, mixed nearby cargo and ample inventory capacity. Ordinary automatic bulk pickup/unload respects admitted quantities. Observe multiple subsequent work cycles: no repeated identical source/destination/material/count cycle. Bulk-off is a control; compare positive supported ASF variants afterward. |
| CAP-22 / L04-A2 | Capacity/filter/membership changes during walking. Initial admission is proven correct; invalidated allocations produce a recorded legitimate replan without silent surplus pickup or repeated return demand. Force an explicit order against conflicting claims and verify preemption remains finite and physical counts conserved. |
| CAP-23 / L04-A1, A2 | Two compatible haulers allocate 30 and 20 units into one vacant 75-unit slot. First deposits 30, creating or absorbing into the actual target stack. Second retains exactly its 20-unit top-up allocation, and a third compatible request sees 25 remaining units, not another 75 or zero. Reverse arrival order and repeat with cancellation, partial deposit and a concurrent incompatible request. |
| CAP-24 / load barrier | Save and cold-load active pickup and delivery jobs while session allocation state is cleared. Rebuild or defer admission before the next physical transfer without assuming startup reservations rerun. Preserve old numeric toil layouts, material quantities and player orders. |
| CAP-25 / actual admission phase | Invoke cached reservation preflight without starting, then queue/start/retry the same Job and inspect current job/driver identity at publication. No duplicate allocation, probe-only claim or stale evidence-generation reuse; source-reservation failure leaves no capacity authorization. |
| CAP-26 / bounded repair and mixed ownership | Dirty a claimed resource, query from a worker and reentrantly during transfer, and change one linked member's provider capability while another retains HD claims. Queries cannot publish or reopen conflicting resources. Deterministic main-thread repair must make measurable fair progress within its documented work/latency bounds. |

For every admitted decision, assert: allocated quantity equals admitted quantity; each top-up total is no greater than its real deficit; distinct incompatible virtual hosts consume distinct slots; opened slots per cell do not exceed its residual count; every edge is allowed for its actual subject/pawn/context; all competing allocations, including absent defs, are represented. Also assert total material conservation through real pickup/deposit and no claimed implementation evidence without runtime output.

Capture bounded decision traces containing resource IDs/cells, actual Thing/Stuff/quality, member and stable linked-group ID, physical deficit/slot counts, context, admitted/requested amount, same/other-pawn allocation IDs, completeness, rejection or replan reason, and physical pickup/deposit deltas. Gate-level per-cell text logging stays off by default. The issue reports still require their separate configuration investigation after these controlled fixtures pass.

## Performance and ownership

Let C be examined cells, I their item records, D new parcel demands (BulkHaul currently caps source entries at 24), R overlapping live allocation records and E tested eligibility edges. Physical enumeration is O(C + I), charging already-indexed live allocations O(R), and eligibility O(E) game calls, with up to O(D(C + I)) potential edges; cache edges only inside the current scope. Split counts are compressed; complexity must not scale with an arbitrary unit/slot limit. Existing-partial matching needs an explicitly bounded graph/augmentation budget, with capacity-sized augmentations rather than one iteration per unit. General virtual-slot reassignment can be combinatorial; bound it, return incomplete on exhaustion and test continuation/progress.

Target the current 200-cell cheap-pass bound and 24-source plan bound first. Add explicit ceilings for inspected Things, eligibility calls, matching work and continuation work **per map/tick**, not only per one candidate. A cell with a huge Thing list cannot report vacant slots from a partially counted Item list: omit that cell's uncertified resources until its count is complete and fresh. Values beyond existing bounds need measurements rather than an invented assurance. Publish examined cells/Things, compatibility/eligibility calls, claim intersections, allocations and matching steps alongside p50/p95/max timings. Compare no-claim vanilla shelves, large sparse/near-full zones, many claimants and ASF high-slot cells. The hot `IsGoodStoreCell` path must not perform a whole-group scan for every cell; open a bounded outer query scope and reuse its view, or answer from the candidate cell plus indexed allocations when called outside a search scope. Benchmark stale-view invalidation too. A safe but quadratic replacement is not ready to ship.

Suggested implementation split, in dependency order:

1. **Allocator owner:** replace the Core scalar budget with cell/stack resources, actual-demand edges and allocation certificates; implement the slot/top-up and subset fixtures, overflow and incomplete-result contracts. Avoid calling this done on synthetic correctly-decomposed inputs alone.
2. **Adapter/claims owner:** one person owns `StorageCommitments`, the subject-aware `StorageEvidence`/claim bridge and ASF adapter, so measurement and live accounting cannot diverge. Add fresh transaction admission and resource-indexed claims. Coordinate L04-O1 through a narrow possession interface; do not rewrite or reclassify that independent repair.
3. **Planner integration owner:** update BulkHaul and its driver to retain actual parcel allocations, re-admit before pickup, preserve prior held cargo and stop def-folded destination reselection from bypassing the quantity certificate. Integrate gate/counter/reservation outcomes with the adapter owner as one reviewed change.
4. **Runtime fixture owner:** independently implement the actual vanilla/ASF adapter and execution fixtures, including negative controls and same-tick world mutation. Root remains sole builder/controller/game operator under the current workflow.
5. **Independent QA:** inspect actual API bindings, read the adapter-to-Core input decomposition, and attempt absent-def, disjoint-area, incompatible-partial, truncation and stale-start counterexamples. Review traces against physical outcomes before accepting L04-A1/A2.

The safe milestone is a single allocation contract working across measurement, planning, admission and real delivery with the listed controls. Renaming fields, more arithmetic tests on already-correct inputs, or successful unload retargeting do not satisfy it.
