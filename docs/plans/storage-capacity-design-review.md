# Independent review of the storage-capacity design

Reviewed 2026-09-07 against `storage-capacity-design.md`, current production storage/planner/claim code, actual installed Verse decompilations, and the installed ASF decompilation. This is a design challenge, not implementation or runtime acceptance. No product, harness, or controller edits; no builds, launches, deployment, Git operations, or network access.

## Assessment

The proposed decomposition into real stack top-ups and shared vacant slots at named cells is necessary for the listed CAP controls. The current def-wide ledger cannot represent incompatible same-def parcels, different eligible cell subsets, absent-def competition, or the CAP23 virtual-slot transition. Merely renaming empty cells to empty slots or charging all claims against shared slots cannot satisfy those controls. Retained resource allocations are a reasonable way to keep the hot gate from replaying every other pawn's full storage search.

The proposal is implementable in bounded stages, but it is not yet a sufficiently exact execution contract. Resolve the following points before several authors independently implement its layers.

## Required corrections and decisions

### 1. Copying the linked group's CellsList is not reentrancy-safe

The installed native `StoreUtility.TryFindBestBetterStoreCellForWorker` holds a local reference to `slotGroup.CellsList` and iterates it while calling `IsGoodStoreCell`. Installed `StorageGroup.CellsList` clears/refills a **static** `tmpCellsList`. Calling that getter from a nested HD query changes the native outer loop's list even if HD immediately copies the result. The proposal's “copy/deduplicate … or enumerate concrete members” alternatives are therefore not equivalent.

Require concrete member-slot-group enumeration in every potentially reentrant path, without invoking linked `StorageGroup.CellsList`. An outer snapshot established before native iteration could also be safe, but it needs an exact scope boundary and must not call the getter again from nested queries. CAP17 should check the *native outer loop's* coordinates/results as well as HD's copied snapshot. Thread-local HD scratch does not protect the shared vanilla list.

### 2. Define actual admission authority, not just a method name

Current `JobDriver_BulkHaul` comments incorrectly describe `TryMakePreToilReservations` as running exactly once only when the job genuinely starts. Actual Verse `Job.TryMakePreToilReservations` invokes the Job's cached driver; current HD calls it before takeover in `BulkHaul.cs:1613`, and other paths use this public preflight API. A claim publication cannot infer actual execution solely from entering that method.

Specify separate preview/preflight, queued, actual-start, loaded-active-job, and delivery phases. Actual start can be established through current Job and current driver identity, as the bill-gather review already requires for actor dispatch. Admission must be idempotent by Job/request identity; a cached preflight cannot consume resources or cause identical-row writes to preserve stale evidence. Main-thread access is explicitly detectable through the installed `Verse.UnityData.IsInMainThread`.

Also specify a load barrier: base `JobDriver.ExposeData` rebuilds toils at PostLoadInit; it does not repeat the normal startup reservation sequence. Clearing session claims requires rebuilding/repairing held cargo and active loaded plans before their next pickup/deposit can rely on empty claim state. Preserve legacy numeric toil layout or provide a separately verified migration. This is a missing acceptance case beyond the currently enumerated CAP rows.

### 3. Keep allocation repair out of the per-cell gate

“Import all live allocations,” “reconcile invalid allocations,” and augmenting reassignment cannot run independently for every `IsGoodStoreCell` call. Merely making those operations bounded *per call* still multiplies them by vanilla's cell loop and repeated workgiver probes. A scope-bound view is useful, but the design must name its owner and nesting rules.

Recommended split:

- The hot gate examines only the candidate cell's current physical records and an immutable index of overlapping allocation records. It never performs a whole-group match, reassigns another pawn, or publishes claims.
- Full main-thread admission/reconciliation operates once per explicit request/repair transaction, using owned snapshots and the same allocator. Its publication is atomic, and a nested callback cannot start a second admission halfway through a physical deposit or row replacement.
- A dirty/unreconciled cell yields an explicit incomplete/deferred decision, not unsupported or spare capacity. A bounded, fair map-level repair/continuation queue must make progress even if all current probes reject. Otherwise conservative stale claims can prevent the very job start that would repair them.

The initial budget must include touched claim records, individual items, compatibility calls, and matching steps, not only 200 cells. Define maximum repair latency/progress assertions for CAP13. No elapsed-time work limit should control gameplay allocation order in multiplayer; use deterministic work counters and measure timing separately.

### 4. Make deposit/materialization an atomic accounting transition

CAP23 cannot be implemented with only `(pawn,def)` evidence or a post-hoc subtraction of deposited units. The design correctly identifies this, but it must select a concrete delivery observation contract. Native placement can merge into several real recipients or choose an existing partial instead of materializing the intended virtual host. One returned Thing is not automatically a complete receipt.

Require a bounded before/after or transfer-receipt observation which identifies actual recipient counts/cells, then atomically updates physical-resource bindings and outstanding quantities. While placement is in progress, affected resource admission must be deferred. On ambiguous callbacks or exceptions, mark the affected allocations for main-thread repair without announcing released capacity.

Cargo evidence also needs a global physical-unit constraint. After several parcels merge into one held Thing, independently clamping each parcel to that Thing's count can double-count the same surviving units. Maintain quantity slices/receipts or perform a deterministic many-to-many reconciliation whose aggregate allocations to each physical held stack do not exceed its actual count. If cargo from different intents becomes fungible, the redistribution rule must be explicit. Never silently use an arbitrary same-def representative.

The CAP23 example should check 30 + 20 in one 75-unit slot through both arrival orders, first-claim cancellation, a partial drop, a merge into another real target, and immediate reentrant/next-decision queries. Exact physical delivery and claim rebinding must agree before admitting the third request.

### 5. Unsupported-provider fallback must respect existing ownership

Provider refusal and lack of a quantitative adapter need separate results at the **resource/member** boundary. `IsGoodStoreCell == false` or ASF's actual acceptance veto is refusal of that edge. A successful Boolean acceptance whose quantitative cap is unknown is unsupported for certified expansion. Do not infer quantitative support merely from a Building_Storage subtype or from the absence of an obvious mod name; caps can be implemented by Harmony patches to storage/stacking seams.

Define the recognized capability set and how mixed linked groups are handled. A group containing supported vanilla cells and an unsupported provider must not globally fall back to native behavior over cells still promised by HD, whose native reservations have already been removed. Retain HD exclusion for its existing allocated resources, and retain native exclusive reservations for unsupported resources. A provider becoming unsupported while allocations exist needs a draining/repair transition; `TryCommit == false -> run vanilla` is unsafe there.

The installed ASF checks in the proposal are well founded: per-cell limits are distributed, valid stack targets are independently checked, full-building inside/outside cases differ, and linked settings can bypass its worker-owner prefix. The ASF adapter should respect an actual provider veto and must not relabel it unsupported to bypass it. Fixed-filter handling for linked members is a deliberate strengthening of a vanilla inconsistency and deserves its reverse-member-order test.

### 6. Scope matching guarantees to known demands

A bounded max-flow/augmenting pass over existing partial-stack edges is useful for CAP07's known flexible/restricted demands. It need not become a colony-wide colored-bin optimizer. Published foreign claims should normally be fixed; reassignment belongs to an explicit main-thread repair/admission transaction which updates every affected intent atomically. A future unknown request cannot have been guaranteed a slot by an earlier online allocation.

Clarify whether CAP07 tests a single known demand set, movable not-yet-executed allocations, or already-walking fixed claims. State the expected outcome when reassignment is forbidden. Virtual-tail sharing should initially use verified stable compatibility cases; arbitrary stateful/non-transitive `CanStackWith` overrides cannot be made safe by a cached equivalence key. Existing real-stack checks must still call the actual directional API.

## Smallest coherent implementation sequence

1. Implement the owned cell/resource projection and typed result contract for vanilla and the inspected ASF version. Prove actual input decomposition for CAP01–09, CAP14, CAP16–17 and numeric CAP20, with nested native iteration as a negative control. Do not connect an incomplete allocator to reservation stripping.
2. Implement the quantity allocator and immutable cell/stack allocation index. Use partial-stack matching over known demands, compressed vacant slots, deterministic work bounds, and stable compatibility for shared virtual tails. Exercise CAP10–12 and CAP23 synthetic transitions, while retaining real adapter tests from stage 1.
3. Implement one main-thread admission/reconciliation owner with explicit preflight/start/load/delivery phases, physical receipts and materialization. Cover CAP15, CAP18–19, CAP22–23 plus the load barrier and callback reentrancy. Keep hot-gate reads separate from this repair path.
4. Integrate BulkHaul planning and actual driver pickups/delivery with the same certificates, removing def-folded reselection and unsafe zero/infinity fallbacks together. Run CAP13 continuation, CAP21 automatic multi-cycle execution, both native job-factory consumers, and the real ASF matrix. Benchmark complete request cost and worst-case map/tick work, not just allocator arithmetic.

Internal stages can temporarily decline uncertified bulk work, but that is not final CAP acceptance. Serializing every hauler with native whole-cell reservations is a useful control or unsupported-provider fallback, not a simpler final solution: it fails shared multi-stack and shared-tail requirements. Rebuilding every foreign parcel's allocation in every gate is another useful correctness prototype, but fails the hot-path contract. The resource-indexed model with a separate main-thread repair slow path is the smallest architecture I can support for all listed criteria, after the execution decisions above are explicit.
