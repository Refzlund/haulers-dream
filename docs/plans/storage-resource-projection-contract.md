# Physical storage resource projection contract

Status: proposed implementation contract, 2026-09-07. This is the first dependency of [storage-capacity-design.md](storage-capacity-design.md), incorporating its accepted independent-review decisions. It specifies an observation component only. No production implementation, admission guarantee, runtime acceptance, build, launch, deployment or claim repair is reported here.

## Decisions from independent review

Root accepts all five findings in [storage-resource-projection-review.md](storage-resource-projection-review.md). These implementation requirements override looser initial wording below; their actual code and fixture evidence remain required.

- **PRJ-R1:** Precharge at least **4 × N** native cell-list visits (blockers, possible item count, static fire, construction conflict), in addition to census/rechecks. Account separately for repeated patched slot-limit calls inside the native predicate, reservations, reachability and reviewed custom predicates. This is a bound on identified visits, not all possible provider runtime cost. An insufficient allowance must defer before calling the predicate.
- **PRJ-R2:** Permission for read-cache hydration does not waive its cost. For known vanilla footprints use a bounded direct occupied-rectangle traversal, preserving the exact native cell set; never trigger a cold whole-footprint virtual getter from a capped page. A custom/ASF cell override requires an inspected bound/readiness contract. Initialize expensive vanilla/ASF fixed filters through an explicit, separately accounted main-thread preparation stage with a declared complete-cost allowance; capped queries defer with `ProviderInitializing` until readiness is established. Do not call a base getter to bypass ASF's actual stuff restriction. Initialization readiness and work must appear in fixtures.
- **PRJ-R3:** ASF registry consistency requires actual membership, identical Thing reference/ID and recorded cell position for each observed physical item. Bind the inspected membership/index/position APIs with their actual signatures before claiming capability. `ContainsAndAllows` is a separate validity predicate: registered-but-disallowed consumes a slot and refuses top-up; absent/replaced/wrong-cell registry entries invalidate the observation. Do not infer identity from equal counts or repair the registry in the projector.
- **PRJ-R4:** Discovery cursors retain and validate guards for every completed member cell list, plus the member-list topology, within an explicit retained-guard budget. Equal-count mutations also invalidate coverage. Validate all guards before certifying whole-group coverage. A limit or oversized cell leaves named unresolved coverage and a documented larger-budget/preparation path; it may not silently count as visited. Page traversal may advance past a named unresolved cell, retaining it as incomplete. Fixtures must show stable-topology progress beyond 200 cells and 32 members, mutation of an already completed member, and a deferred large cell followed by usable room. Physical capacity still requires fresh revalidation before admission.
- **PRJ-R5:** Each scope operation checks main-thread, initialized Unity thread identity, disposal, captured actual Game/session/provider generation, and scope ownership of observations/cursors before reading live handles. Unknown enums and duplicate parcel IDs for different subjects are invalid requests. A per-operation reentrancy guard rejects callback reuse of an already open scope, as well as nested `Open`. The disposable suppression helper preserves an existing legacy scan flag and exact filter context on all exits, and does not confer forced-order authority or bypass the HD building filter. Standalone fixtures provide explicit session/catalog identities; opening the reader never creates or advances them.

The first implementation remains an unconnected observation component exposed to adapter fixtures. Allocation, fair repair, admission, delivery receipts, cold-load reconstruction and reservation transitions are later dependencies; no production caller may turn a deferred/unsupported result into an unbounded scalar allowance.

## Decision and boundary

Add an internal `StorageResourceProjector` to the game adapter assembly. It produces identified existing-stack deficits, shared vacant stack slots at concrete cells, and separate eligibility observations for an **actual Thing, carrier, faction, priority rule and storage context**. Its input and output contain no claim ledger or allocation budget. It does not choose a destination, spend capacity, publish commitments, reserve cells, move items, change jobs, remove reservations or repair another pawn's allocation.

Physical resources are independent of which cargo was priced first. Eligibility is cargo-specific. A shared vacancy is not copied into each def's private balance; a genuine compatible existing stack remains a separate top-up resource. A complete cell with zero resources, a refused edge, an unsupported quantitative provider and an unfinished observation are different results.

The first implementation exposes the component to independent adapter fixtures. Do **not** feed its result through the old `GroupSpace` / `StorageGroupBudget` scalar representation or connect it to reservation stripping before the allocator and admission owner exist. The old representation cannot retain the identities this component establishes.

## Current evidence and integration seams

Read independently for this contract:

- `Source/HaulersDream/StorageCommitments.cs`: `MeasureGroup`, `MeasureGroupUncached`, `ActiveFilter`, `RawSpaceFor`, `InsideSpaceScan` and its current consumers. The current measurement calls `group.CellsList`, then aggregates by def; its tick memo has no reliable physical/filter/area mutation epoch.
- `Source/HaulersDream/BulkHaul.cs`: `BudgetGroupOf`, `ResolveGroupBudget`, `PriceDefInto` and their plan consumers. `BudgetGroupOf` currently identifies a linked `StorageGroup` or a concrete `SlotGroup` without reading linked cells. Keep that normalization for group lookup, but retain concrete member identity in every resource.
- `Source/HaulersDream/StorageCommitAdapters.cs`: HD's `IsGoodStoreCell` postfix and haul-count postfix already stand down while `InsideSpaceScan` is true. The current truncated/infinity fallback is not a result contract for the replacement.
- `Source/HaulersDream/StorageBuildingFilter.cs`, Core `StorageFilterContext.cs`: actual contexts are `Opportunistic`, `BeforeCarry` and `Unload`. The context stack is thread-local; membership in mutable allow/deny sets cannot be represented by a single `filtered` Boolean.
- Actual game `Assembly-CSharp.dll`, self-reported RimWorld **1.6.4871 rev591** (installed `Version.txt` separately says rev590), SHA-256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`, at `C:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`.
- Actual ASF 1.2.4.0, SHA-256 `28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9`, at `C:\Steam\steamapps\workshop\content\294100\3033901359\1.6\Assemblies\AdaptiveStorageFramework.dll`.

Read-only decompilations are in `C:\Users\Arthur\AppData\Local\Temp\haulersdream-goal-20260907` (`StoreUtility-installed.cs`, `GridsUtility-installed.cs`, `AdaptiveStorage-installed.cs`), `haulersdream-capacity-design-20260907` (storage, filter and stacking types), and `haulersdream-storage-owner-20260907\Zone_Stockpile.cs`. `RimWorld.SlotGroup`, `Verse.Zone`, `Verse.Map` and `RimWorld.IStorageGroupMember` were additionally decompiled to stdout from the installed assembly.

These local APIs, rather than the current comments claiming generic compatibility, establish the supported model. Existing comments explicitly acknowledge unmodeled mass/fill/similar-stack caps. A successful Boolean cell test does not prove those caps permit an entire stack.

## Proposed API

Names below are new proposed internal types, not existing methods. Use .NET 4.8-compatible readonly structs / sealed classes with get-only properties and privately owned arrays; no public mutable `List<T>` or array backing storage.

```csharp
internal static class StorageResourceProjector
{
    internal static ProjectionOpenResult Open(
        StorageProjectionRequest request,
        StorageProjectionLimits limits,
        StorageProviderCatalog providers);
}

internal sealed class StorageProjectionScope : IDisposable
{
    internal CellProjection ObserveCell(IntVec3 cell);
    internal StorageProjectionPage ObserveGroupPage(StorageProjectionCursor cursor);
    internal CellEligibility ObserveEligibility(
        StorageParcelProbe parcel, CellProjection cell);
    internal ProjectionValidation Recheck(
        StorageParcelProbe parcel, CellProjection cell, CellEligibility eligibility);
    internal ProjectionWork Used { get; }
}
```

`ProjectionOpenResult` contains either an opened scope or an explicit `Deferred` / `InvalidRequest` reason. A scope is owned by one caller, on one main thread, within one synchronous operation. Dispose once through `using`; no scope survives a tick, yield, load, transfer or callback handoff. All exits restore scope state in `finally`.

`StorageProjectionRequest` contains:

| Field | Meaning |
| --- | --- |
| `Map`, `MapSessionId` | Actual map reference plus an externally established per-game/load session identity. `map.uniqueID` alone is insufficient across quickload. Opening a projection must not create or advance the session identity. |
| `ISlotGroup Group` | Expected canonical group, obtained through `BudgetGroupOf` from a concrete selected slot group. Accept only inspected `SlotGroup` / `StorageGroup` shapes; no arbitrary interface enumeration. |
| `ProjectionPurpose` | `PlanObservation`, `AdmissionObservation`, `DeliveryObservation`, `CellObservation`, or `AvailabilityObservation`. This describes observation, never grants authority to commit. |
| `StoragePriorityRule` | Explicit `StrictlyBetterThan(floor)`, `AtLeast(floor)` or `WithinSelectedGroup`; the caller supplies its existing search rule. Do not invent `Unstored` for carried goods or change distance/priority selection policy. |
| `StorageFilterContext` | Exact context, captured explicitly rather than inferred again from a later ambient scope. |
| `ProviderCatalogGeneration`, `QueryId` | Immutable capability binding generation and caller-assigned diagnostic query identity. Neither is a physical mutation epoch. |

`StorageParcelProbe` contains the actual `Thing Subject`, actual `Pawn Carrier` (nullable only for `AvailabilityObservation`), explicit `Faction`, stable diagnostic parcel ID, and a positive requested-unit bound. Equal defs are separate probes. Unit count constrains later work; it does not turn a witnessed floor into unlimited room.

Opening or observing validates nulls, session identity, map membership and item shape. A real hauling carrier must be spawned on the requested map. Keep the actual subject's spawned/held state, `SpawnedParentOrMe` and holder identity. A spawned floor item can have `holdingOwner == map.spawnedThings` and `ParentHolder == map`; **do not require a null owner**. A held Thing's `ParentHolder` is its actual holder object, not its `ThingOwner` container. These observations do not establish who owns its claim. Sources requiring a materialization/withdrawal API are outside this physical-cell projector.

### Results and identity

Use separate dimensions rather than one overloaded success Boolean:

- `ObservationState`: `Complete`, `Deferred` or `Invalidated`.
- `CapabilityState`: `Supported`, `Unsupported`, or `BindingFault`.
- `EligibilityState`: `Eligible`, `Refused`, or `NotEvaluated`.
- Reason codes identify the stage, such as `NeedsMainThread`, `NestedProjection`, `BudgetExhausted`, `ProviderScanRequired`, `ProviderInitializing`, `GroupChanged`, `SubjectChanged`, `ProviderStateMismatch`, `UnsupportedQuantityRule`, `WrongFaction`, `BelowPriority`, `ThingFilterRefused`, `MemberFixedFilterRefused`, `ProviderRefused`, `NativeCellRefused`, `HdContextRefused`, `InvalidStackTarget` and `SelfTarget`.

`Refused` requires an actual negative result from the named rule. It is not a convenient conversion for a missing binding, exception or exhausted budget. A known refusal remains recorded even if another capability is unknown; neither may produce an eligible resource.

Each `CellProjection` freezes:

| Record | Frozen data |
| --- | --- |
| Observation identity | Session ID, map ID, query ID, scope-local observation sequence, capability generation, observed game tick for diagnostics only. |
| Destination identity | Canonical group key, **concrete parent key**, cell coordinates, actual concrete slot-group identity, provider ID/version and binding status. |
| Physical census | Patched maximum item slots, complete item count, vacant-slot count, all observed Item IDs/counts/limits/footprints, and census validity. |
| Existing stack resources | One resource per actual item identity: its cell, actual Thing ID, observed count, observed `def.stackLimit`, nonnegative deficit, and provider target-validity observation. This is not yet an edge for every subject. |
| Provider observations | ASF actual cell limit, per-cell stored count, member slot-limit/state and applicable acceptance observations, where supported. Unknown values are nullable/typed unknown, not zero or infinity. |
| Coverage / work | Which requested cell was fully read; incomplete census reason; exact work counters. |

Resource keys are `(MapSessionId, map.uniqueID, concrete parent identity, cell, kind, existingThingId?)`. The vacant resource is one compressed `VacantSlots` record at that cell, not a guessed identity for an as-yet nonexistent stack. Existing top-up keys include `thingIDNumber`. Group identity is an index key, not part of the physical resource identity: linking/unlinking must not manufacture new copies of the same physical room.

Concrete building identity uses its `thingIDNumber`; stockpile identity uses `Zone.ID`. Include a kind discriminator and retain scope-local reference equality checks. Linked group diagnostic identity can use `StorageGroup.loadID` / `GetUniqueLoadID()`. Unidentified custom parents are unsupported until an adapter supplies stable identity. Never use labels, def names, object hash iteration, first-member identity or a shared List reference as a resource key.

Output properties are immutable observations; the real Thing/Map objects are not immutable. Keep real references in a private scope-local handle table for predicates/revalidation. A DTO passed to a future worker consumer must contain values only; a worker cannot dereference the handle table. `CellEligibility` references the exact cell observation ID and parcel ID, and contains separate eligible top-up edges plus the cell's vacant-slot eligibility. An eligible vacant slot has a per-opened-stack capacity of the supported subject's real stack limit; it is not multiplied into a per-def shared balance.

## Concrete cell discovery without linked getter calls

The installed `StorageGroup.CellsList` clears/refills a **static** temporary list. Native `TryFindBestBetterStoreCellForWorker` holds this list while it calls `IsGoodStoreCell`. Calling the getter and then copying it already damages the native iterator.

Rules for all component paths, including diagnostics and rechecks:

1. `ObserveCell(c)` obtains `map.haulDestinationManager.SlotGroupAt(c)` and checks its concrete parent and expected canonical group. It does not enumerate the group.
2. For a concrete `SlotGroup`, read its inspected parent's concrete cells. Actual `SlotGroup.CellsList` delegates to `parent.AllSlotCellsList()`. Vanilla `Zone_Stockpile.AllSlotCellsList()` returns `cells`; the building getter can create a whole occupied-cell cache through a virtual enumeration. Use bounded direct known-footprint traversal for supported vanilla buildings, or a separately established ready/budgeted provider path under PRJ-R2. Read/copy only the bounded requested coordinates before predicates. Never mutate or sort a source list.
3. For a linked `StorageGroup`, read `members` directly. Resolve each supported `ISlotGroupParent.GetSlotGroup()` and its concrete cell list; copy coordinates into caller-owned bounded page storage. Do not invoke the linked group's `CellsList`, `GetEnumerator`, a convenience API that obtains it, or native `TryFindBestBetterStoreCellForIn` / its private worker.
4. Do not use `Zone.Cells`: its actual getter can shuffle the live zone cell list with `Rand`. Use the stockpile's concrete `AllSlotCellsList()` / underlying inspected `cells` instead. Do not use `HeldThings`, which hides additional enumeration and work.
5. At every copied coordinate validate that `SlotGroupAt(c)` is the exact expected concrete slot group, its parent belongs to the requested map, and `BudgetGroupOf(actual)` matches the requested group. Deduplicate by physical cell identity. Overlapping or stale registrations become explicit invalid/mismatched records, not duplicated resources.
6. Validate copied identity/count guards after provider calls. A changed member/cell source invalidates affected observations. A reference/count check alone cannot prove an unchanged same-count list; retain normal `List<T>.Enumerator` version checks where enumerating known lists, and validate each resource's live cell registration. Do not claim an omniscient topology epoch.

The group-page cursor is a **discovery continuation**, not a durable capacity certificate. It may retain opaque main-thread list enumerators / concrete member references and the bounded next ordinal, but no resumable live predicate scope. A normal List version failure reports `GroupChanged`; it cannot silently skip or restart and still declare exhaustive coverage. No native enumerator or provider-returned List is exposed in immutable output.

Each page names its completed cells and any unprocessed remainder. `RequestedCellsComplete` is separate from `WholeGroupCoverageComplete`. Only a verified complete traversal of the applicable topology can certify the latter. Previously observed physical rows must be refreshed before a later admission; finishing topology discovery does not refresh them. A bounded allocator can use identified, complete witnessed cells without knowing every group cell, provided it checks every overlapping allocation on those cells. It cannot infer that an unseen cell is free, that the entire group has zero room, or that seeing 24 stacks permits arbitrary claims. Fair repeated paging/repair belongs to the later main-thread owner, not to a side effect of this reader.

## Physical observation and eligibility algorithm

### Physical census

1. Resolve and validate concrete ownership/provider before making a quantitative promise. Read a bounded, owned copy of `map.thingGrid.ThingsListAt(c)`. Charge **all** encountered Things to the work budget, including non-items used by native obstruction checks. An unfinished cell census yields no vacancy count usable for allocation.
2. Count every `ThingCategory.Item`, including full, incompatible or invalid stored stacks. Such stacks still occupy physical slots. Validate their actual spawned map/cell footprint and positive count. A corrupt/ambiguous census is invalidated; do not drop an inconvenient object to create room.
3. Call the actual patched `c.GetMaxItemsAllowedInCell(map)` once for the physical record. For vanilla its base value comes from the edifice's `MaxItemsInCell`, otherwise one. For supported ASF it must agree with the member's actual `GetMaxItemsForCell(in c)`. A mismatch is not permission to choose the larger answer.
4. `vacantSlots = max((long)maxItems - itemCount, 0)`. Existing over-capacity storage has zero vacancies, while legitimate compatible partials may still exist. Each item has `deficit = max((long)def.stackLimit - stackCount, 0)`; oversized stacks have zero top-up, not negative capacity. No sentinel infinity. Use `long` for sums/multiplication and range-check at external integer boundaries.
5. A single-cell storage building is not required: multi-cell buildings are projected cell by cell. Multi-cell **cargo/items** need a tested footprint adapter because one item can occupy multiple cell resources; do not assign it independent one-slot resources in each cell. Mark those affected observations unsupported until that adapter exists.
6. Do not use `GetItemStackSpaceLeftFor` as authority. It combines vacant slots with same-def deficits and does not test actual stack compatibility. It may be recorded only as explicitly labeled legacy diagnostic data in a fixture.

### Eligibility of one actual parcel at one fully observed cell

Run the following inside the projection's own-gate suppression/context scope. Record each predicate actually evaluated; a short circuit leaves later predicates `NotEvaluated`, not implicitly true.

1. Validate the current subject/map/holder state and concrete destination registration against the observation. Parent `HaulDestinationEnabled` must be true. For a parent that is a `Thing`, require its actual `Faction == parcel.Faction`, as in vanilla's group search. For zones do not invent a building-faction restriction. Apply the explicit priority rule to actual selected/group settings. This validates candidates; it does not replace native distance/randomized search.
2. Call the **Thing overload** of `StorageSettings.AllowedToAccept(subject)` on the effective concrete slot group's `Settings`. It preserves linked group settings and their existing recursive parent rule. Never use `AllowedToAccept(subject.def)`, a dummy Thing or a clone. `IsGoodStoreCell` alone omits this outer filter entirely.
3. Additionally evaluate the actual concrete member's fixed/parent settings with the same real subject. A linked group's actual `GetParentStoreSettings()` uses its **first** member. Check the chosen member too, retaining the outer restriction. Do not instead enforce `IStorageGroupMember.ThingStoreSettings` / `Building_Storage.settings`, which are individual unlinked settings and can be stale relative to the linked policy. The deliberate intersection must be tested in both linked member orders.
4. If ASF owns the cell, invoke the exact ASF member predicates described below. Record an actual `HasCapacityForThing == false` as `ProviderRefused`; do not use an unsupported fallback to undo it. A linked `Settings.owner is StorageGroup` must not skip these checks.
5. Call `StoreUtility.IsGoodStoreCell(c, map, subject, carrier, faction)` with **all other patches present**, suppressing only HD's recursive commitment/count gate. This retains native forbiddance/allowed-area semantics, reservations, construction conflicts, static fire and reachability from the actual subject's spawned parent/interaction cell. Do not duplicate a hand-written reduced version. A null carrier is valid only in availability mode and preserves the native faction-reservation branch; no hauling/admission eligibility is inferred from that result.
6. For `Opportunistic` / `BeforeCarry`, call the actual active `storageBuildingFilter.IsGroupAllowed(concreteSlotGroup)` under the explicit context. This uses the concrete building's def/package and current override sets. `Unload` bypasses only HD's building-filter exclusion, not steps 1–5 or stack validity. Forced-order arbitration remains a caller policy; the projector never calls `PushForcedOrder` to manufacture acceptance.
7. For each positive-deficit existing target, require its real `def.EverStorable(false)`, the exact directional `existing.CanStackWith(subject)`, and the provider's **individual target validity**. Exclude `ReferenceEquals(existing, subject)`: the same spawned source cannot be counted as an existing recipient for its own material. Record target ID and individual predicate results even when another target makes the Boolean cell gate succeed.
8. Recheck live identity/count/slot/provider guards before returning a complete eligibility record. A detected mutation produces `Invalidated`, not a narrowed number presented as exact. There is no tick-wide memo. Eligibility reuse is limited to the same synchronous scope, same actual parcel, same cell observation and unchanged guards; after a physical change or arbitrary callback boundary, observe again.

Exact Thing predicates preserve Stuff, quality and custom component rules. A def/stuff/quality signature can be diagnostic or a cheap negative prefilter, never final authority. The projector emits only edges to real current stacks. Compatibility among future virtual-stack tails and prospective merge identities belongs to the allocator, not this component.

## Provider binding and refusal boundaries

Build `StorageProviderCatalog` once on the main thread after mod patches have been installed. It is immutable for a generation and describes actual loaded type/assembly/method identities, reviewed behavior and capability restrictions. Bind exact signatures once; no assembly scan, filesystem hashing, Harmony inventory or log formatting per cell. Runtime traces record assembly version/hash/MVID and relevant patch identities at setup, not by rereading DLL files on the hot path.

### Vanilla physical slots

The initial supported shape is the inspected `Zone_Stockpile` / `Building_Storage` implementation, including XML-defined slot counts, where there is no unmodeled quantitative provider. Modded building subclasses or storage comps are not automatically supported merely because they inherit `Building_Storage`. A reviewed provider can extend the capability set.

Inventory relevant Harmony patches at least on `GridsUtility.GetMaxItemsAllowedInCell`, `StoreUtility.NoStorageBlockersIn`, `StoreUtility.IsGoodStoreCell`, the private storage worker, `StorageSettings.AllowedToAccept(Thing)`, applicable storage-parent predicates and stacking/cell-limit overrides. Detection is evidence about extension points, **not proof** of quantitative semantics. Recognize known HD suppression/filter patches and reviewed ASF patches by exact method identity; recognize a reviewed Boolean-only restriction only when its scope and lack of additional quantity semantics are established. Unknown quantity-affecting patches, components, native detours or overrides require a capability decision; absence of a familiar package name is not evidence of support.

If an unclassified global storage patch cannot be scoped to a member, report the uncertainty for all potentially affected members. If a reviewed patch can establish that it applies only to a particular comp/provider, preserve support elsewhere. Do not blacklist all third-party cargo `CanStackWith` implementations merely because they differ from vanilla: real existing-target compatibility can still be queried, while unproven future-tail equivalence is not supplied by this projector. Opaque predicate cost/state changes may still require explicit deferral or a provider contract.

### Exact inspected ASF binding

Use a soft dependency against loaded `AdaptiveStorageFramework` types, with all required signatures validated before reporting support:

| Actual type/member | Required use |
| --- | --- |
| `AdaptiveStorage.ThingClass` | Detect the actual concrete parent and its inspected base/interfaces. Validate inherited/custom overrides before certifying subclasses. |
| `ThingClass.GetMaxItemsForCell(in IntVec3) -> int` | Compare against the actual patched cell limit. Reflection parameter type is `typeof(IntVec3).MakeByRefType()`. |
| `ThingClass.GetParentStoreSettings() -> StorageSettings` and `FixedFilterAllows(Thing) -> bool` | Preserve ASF's own fixed filter, including `lockStorageSettingsToStuff`. It declares **new**, not virtual-override, `GetParentStoreSettings`; a `Building_Storage`-typed call misses the ASF method. Bind the declared ASF method / predicate. |
| `ThingClass.HasCapacityForThing(Thing) -> bool` | Check the actual concrete member with the actual source/held state, including the full-building outside/inside branches. |
| `ThingClass.ContainsAndAllows(Thing) -> bool` | Per-target membership/validity; internally checks `_validStoredThings` IDs. A disallowed stored item still consumes a slot. |
| `AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+FixMissingValidStackDestinationCheck.IsValidStackDestination(Thing) -> bool` | Invoke the exact static target predicate used by installed ASF's `NoStorageBlockersIn` patch. It calls `StoringAdaptiveStorage()?.ContainsAndAllows(thing) ?? true`. Also validate that the target's actual storing parent is the projected member; a mismatch is dirty evidence, not a generic true. |
| `ThingClass.StoredThings`, `CurrentSlotLimit`, `AnyFreeSlots`, `ContentsPacked` | Read-only provider state; never set its slot limit or refresh its stored-item registry from a projection. Packed/off-grid contents are not silently converted into free physical cells. |
| `AdaptiveStorage.ThingCollection.ItemCountAtMapCell(in IntVec3) -> int`, sealed override `Count`, `CellWiseCount` | Verify the fully observed cell's physical count agrees with ASF's registry; cost guard for member-wide predicates. A mismatch requires reconciliation by its owner / later repair, not a projector registry write. |
| `AdaptiveStorage.ModCompatibility.PerformanceFish.Active -> bool` | Record the actual branch condition; the full-building outside branch permits a path when this is active. Do not infer it from package-name substring matching. |

`AdaptiveStorage.Utility.ThingExtensions.StoringAdaptiveStorage(Thing)` resolves a spawned item's slot-group parent, otherwise its actual holder. This reinforces the holder rule above. Do not assume a missing/null holding owner for stored items.

ASF distributes `CurrentSlotLimit` over `_maxItemsByCell`; a building-wide average or `MaxItemsInCell` substitute is invalid. Its worker prefix `PreventStorageLookupFaster.Prefix` only checks `slotGroup.Settings.owner is ThingClass`. Linked settings have a `StorageGroup` owner, so running only that worker is insufficient for member-level acceptance. The projector calls the concrete member's predicate deliberately, retaining both linked policy and its actual fixed filter.

ASF's `HasCapacityForThing` can call `StoredThings.AcceptsForStackingCustom`, which scans the member's entire stored collection and calls actual `CanStackWith`. The `Count` getter is constant-time; `TotalStackCount` is a sum and must not be used as the cost preflight. Charge the potential member scan before invoking the predicate. Do not call `ItemsAtMapCell` via `MethodInfo.Invoke`: its `ReadOnlySpan<Thing>` return cannot be boxed. The inspected integer count API and target-by-target identity predicates suffice for the initial consistency check.

### Unknown, changed and faulty providers

A provider returning false is refusal of that subject/cell/resource. A missing quantitative model is unsupported. A known binding throwing or a registry/grid mismatch is a fault/invalidated observation. None is infinity or a fresh free slot. Preserve the concrete affected resource/member boundary in a mixed linked group.

A capability generation change invalidates observations; the projector only reports it. The later allocation owner must retain exclusion/drain existing HD allocations while unsupported resources retain native exclusivity. This component cannot authorize `TryCommit failed -> use vanilla` or disable the whole linked group's gate. One unsupported member must not reopen another member's still-owned resources.

## Reentrancy, worker reads and side effects

`Verse.UnityData.IsInMainThread` is available in the installed game. Check it **before dereferencing Map, Thing, parent settings, live cell lists or provider state**. Worker calls return `Deferred(NeedsMainThread)` with zero world-read/predicate counters. They may later consume immutable values published by the separate allocation owner; they cannot run a second projection, populate a cache, publish claims or schedule repairs through this API.

Add one narrow internal disposable helper in `StorageCommitments` when implementing this component, proposed name `SuppressOwnGateForProjection()`. Preserve the prior `insideSpaceScan` state in a thread-local depth/scope and restore it on every exit. This is the only required current-product seam in the first component. Do not expose an arbitrary public Boolean setter. The scope must cover provider/filter/stack predicates as well as `IsGoodStoreCell`, because their callbacks can reenter HD.

Use a separate thread-local projection-entry depth. A new `Open` during an active projection returns `Deferred(NestedProjection)`; it does not reuse mutable scratch or recursively project another group. Each owning-scope operation also guards active-operation reentry, so a predicate callback cannot recursively reuse its mutable scope. The own-gate flag causes HD's recursive adapters to stand down while the real native/provider predicate runs; it does not unpatch Harmony or suppress another mod. Opening a projection while a later allocation owner reports an in-progress physical transfer must likewise defer; the later owner supplies that explicit barrier without giving the projector allocation authority.

Hot `IsGoodStoreCell` integration is deferred to the allocator/index stage. It must not run `ObserveGroupPage`, member-wide ASF scans, reconciliation or all-parcel eligibility for each native candidate. Prefer the native verdict already being evaluated plus the immutable candidate-cell allocation index. Do not recursively call the entire storage search to manufacture a verdict. If a fresh physical/capability observation is required and unavailable within the cell budget, return typed deferral to the owner; it must retain exclusion and provide fair main-thread progress.

No projector code may mutate storage filters, priorities, ASF limits/registries, claims, reservations, jobs, inventory, tags, pawn areas, map contents, random state or live cell lists. Do not invoke `Notify_SettingsChanged`, `UpdateAllValidStoredItems`, `GenPlace`, `TryAdd`, `TryAbsorbStack`, `Reserve`, `StartJob`, or withdrawal/materialization helpers. Diagnostic events/counters are scope-local values returned to the caller; logging/queue publication belongs outside the hot reader.

There is an explicit limit to the phrase “read-only”: normal native reachability/predicate calls may hydrate their own read caches. ASF's `GetParentStoreSettings` lazily sets `_fixedStorageSettings` and can construct a stuff-locked filter on first read; the building's occupied-cell getter can also initialize its cache. The recommended contract allows **normal provider read-cache initialization**, but no gameplay/settings notification or HD state mutation. Do not claim byte-for-byte object immutability. If strict no-cache-write execution is required for a path, catalog/member preparation must happen beforehand on the main thread and that path returns `ProviderInitializing` until ready; do not use a base getter to evade the real ASF filter. These preparations must be explicit and tested, not hidden writes to allocation state.

## Deterministic work budget

Budget work, not only accepted cells. Provisional defaults below are implementation starting limits requiring runtime profiling, not measured performance guarantees:

| Counter | Cell observation limit | Explicit plan/admission page limit |
| --- | ---: | ---: |
| Concrete members visited | 1 | 32 |
| Cell coordinates copied/validated | 1 | 200 |
| Thing-grid entries inspected, including rechecks | 128 | 4096 |
| Direct native `IsGoodStoreCell` calls | 1 | 200 |
| Effective/member filter evaluations | 4 | 800 |
| Direct target compatibility/validity evaluations | 256 | 4096 |
| Potential internal provider item visits | 0 member-wide scans | 4096 |

Precharge a whole opaque operation before calling it when its known bound is available. For ASF's potentially scanning capacity predicate use `StoredThings.Count` and charge its possible compatibility calls too. The first version can conservatively charge the whole member for every full-member predicate call rather than adding an unproven cross-parcel cache. Larger known predicates are `ProviderScanRequired`, allowing the later owner to schedule a specifically budgeted slow observation; the hot gate must never hide them inside one nominal cell call.

Distinguish measured HD loop counters from charged upper bounds inside inspected native calls. For an unchanged cell list of length `N`, installed `IsGoodStoreCell` can visit it in the obstruction, item-count, static-fire and construction loops; reserve at least `4 * N` native grid visits plus up to `N` native compatibility/target-validity opportunities, in addition to HD's own census and edge checks. Account separately for repeated cell-limit calls, reservations, reachability and applicable patched predicates. Report reserved upper bounds separately from directly observed calls. The table's ceilings apply to their combined budget, not only visible HD loops. This can defer before the coordinate limit; it is not a constant-time or universal total-runtime claim.

An individual cell whose census exceeds the remaining budget contributes no spare slots. Do not split a cell census across a physical mutation and treat it as one complete observation. Return a continuation reason with the unprocessed address. Charge duplicate/refused/unreachable candidates and revalidation, not just successful resources. Bound page/result memory by the same counters and keep vacant slots compressed. No whole-group `ToArray`, sort, provider collection sum or all-def scan before applying the budget.

Counters bound HD-controlled loops and known provider scan upper bounds. They cannot interrupt an arbitrary Harmony predicate, reachability search or custom `CanStackWith` after it starts; do not advertise a hard millisecond deadline. Measure elapsed time separately for diagnosis, never use wall time to choose gameplay allocation order. A callable with unknown expensive or mutating behavior needs a reviewed provider/call policy, not an invented cost of one. A deterministic main-thread owner must later guarantee progress for deferred large cells/providers; repeatedly exhausting a small hot budget is not completion of CAP13/CAP26.

## First implementation acceptance and remaining decisions

The following are projector acceptance checks only; they do not close CAP allocation/convergence requirements:

1. Actual `ShelfSmall`: two full incompatible fillers plus one vacant slot produce two zero-deficit occupied stacks and exactly one shared vacancy. Empty three-slot shelf reports three compressed vacancies. Full shelf with one compatible partial reports that exact target deficit and zero vacancies; full incompatible shelf yields no eligible resources.
2. Same-def actual Things with different Stuff/quality/component compatibility and different pawn/context/filter eligibility yield distinct edges. Reverse probe order produces identical physical resource identities/counts. Unload changes only HD context filtering.
3. Heterogeneous linked cells retain concrete members/limits/fixed filters. Both member orders expose the deliberate linked-plus-actual-member intersection, including ASF's stuff-locked **declared** parent getter.
4. Actual ASF cells with different distributed limits, valid and invalid compatible targets in one cell, a full member's outside/inside subjects, and the linked-owner bypass preserve exact predicate outcomes and observed counts. No test may mark an invalid target valid merely because another target passes.
5. Instrument an outer **native** linked storage iteration. Nested projection of a different linked group must leave its coordinate sequence untouched. Assert the projector makes zero linked `StorageGroup.CellsList` getter calls, including failed/diagnostic/recheck paths. Exercise nested predicates and exception exits to prove both scopes restore correctly and claims/jobs/reservations/settings/random state remain unchanged apart from declared provider read caches.
6. Worker invocation performs no world reads/predicate calls and returns typed deferral. Budget exhaustion yields explicit incomplete coverage, never infinity/full-group rejection. Same-tick stack/filter/slot/member changes and provider mismatch invalidate or freshly recompute the affected observation.
7. Mixed supported/unsupported/refused members retain separate outcomes. A generation change is visible without projector writes to either native reservations or HD ownership. Those transitions require additional lifecycle tests before live integration.

Recommended decisions before product code starts:

- **Accept normal read-cache hydration**, documented above, while prohibiting gameplay and allocation side effects. A literal prohibition on all provider cache writes requires a separate readiness/prewarm contract and would also affect native reachability. Do not silently promise both.
- **Start with the inspected vanilla and ASF binding shapes, with explicit unclassified capability results.** Record the actual loaded patch inventory in the first runtime evidence. Choose reviewed patch applicability rules rather than treating method presence or a package allowlist as proof. New provider versions need binding/semantic validation; the hashes above are evidence anchors, not a proposed permanent hash lock.
- **Use bounded named-cell pages and separate coverage completeness.** Do not delay every positive witnessed resource until the whole group is scanned; do not infer whole-group negative results from a page. The later owner must establish fair paging, refresh and overlapping-allocation checks.
- **Keep ASF member-wide acceptance outside hot cell projection unless already prepared within its exact observation lifetime.** The proposed counters need real runtime calibration. The current API does not expose a hard interruptible cost bound for all custom predicates.

After these choices are accepted, implement the component and its narrow own-gate scope, then run the independent real-map projection controls. Allocation, lifecycle repair, native-reservation fallback transitions and executed pickup/unload remain separately owned dependencies.

## First unconnected implementation: source handoff and build

The initial implementation is now present in `StorageResourceProjector`, `StorageProjectionModel`, `StorageProjectionTopology`, `StorageProviderCatalog` and `StorageProjectionAsfBinding`, with Core `ProjectionWorkBudget`/`ProjectionFlagScope` helpers and the narrow internal `StorageCommitments.SuppressOwnGateForProjection()` seam. **No existing allocation, admission, budget or hauling path calls the projector.** Existing holder correction is preserved.

The explicit setup sequence is environment → catalog → budgeted member preparation → synchronous scope → cell/page/actual-parcel eligibility/recheck → disposal. Catalog/session generations belong to the caller. Actual zone cells are indexed during preparation with a retained list guard; map registration alone does not prove membership. Optional building footprint-cache warmup is separately precharged and occurs outside capped observations. Cold hydration or stale membership invalidates the old observation and requires appropriate preparation and a fresh scope.

Page output copies have an `OutputRecords` budget. Pending diagnostics return at most64 rotating sampled rows plus one discovery remainder, with total/offset/count metadata; the full retained unresolved ledger is not recopied each page. Successful retries use swap-with-last removal, avoiding uncharged interior-list shifts. Larger work allowances can revisit oversized cells within the same live scope; larger retained-state limits require a fresh scope. These are implementation mechanisms requiring actual progress tests, not a measured performance guarantee.

Root guarded build completed with zero compiler warnings/errors and all13 source guards. The ten new headless cases plus existing suite total **2,916 passed, zero failed/skipped**. TRX: `%TEMP%\haulersdream-goal-20260907\storage-projector-results\storage-projector-first.trx`. Product SHA256: HD `4D3E93848D7E543B1D4C05A8A898D6CE25BB5084CECE60DE07839EDA9D9E422A`; Core `C57C4ABB9EB64A35FC77AA066D125EC94BDC36A7FE872BB1907495005D296F20`. No deployment or game launch occurred for this build.

Independent source/API review is assigned to `source_inventory`. A separate native CAP02 fixture is being authored for real shelf/zone observations; the broader ASF, large/mixed-group, reentry, worker, lifecycle and mutation controls remain required. The inspected native and ASF versions, unknown-predicate/provider outcomes and limited floor/pawn custody support are explicit provisional boundaries, not completed compatibility dispositions. This source/build handoff does not accept projector adapters, a capacity fix, or any feedback source.

## Corrected source review, 2026-09-07

The first CAP02 run exposed an additional catalog integration omission: HD's actual void exception-observer Finalizer on native `IsGoodStoreCell`. Exact owner/target/method/kind recognition passes fresh source review and24 bounded registration/signature controls. The corrected native component now has accepted actual evidence after full assertion/patch-inventory coverage and per-operation reported-work consistency checks passed fresh independent review. Helper7A084EB requires all427 source-derived ordered assertions, the exact reviewed CE0508 module/patch tuples,13 exact non-Compatibility operation counters, bounded Compatibility, all14 Page ceilings, the shared grid ceiling and preparation-formula consistency. Future product modules require a newly reviewed token tuple. Actual comp/def lists are not separately exported, so their consistency bounds are not independent counts. No allocator/admission path uses this API yet.

Accounting evidence must preserve the API distinction: returned cell/eligibility `Work` is that operation's `budget.Used`, so a subsequent physical observation may legitimately report fewer native calls than the preceding eligibility operation. Scope `Used` accumulates open/operation work internally, but CAP02 does not export it. Direct cumulative-scope accounting, exhausted-budget rejection, paging progress and opaque-call cost controls remain required future runtime evidence; summing CAP02 rows is not an observation of the unexported property.

The initial review found and root corrected three defects. Linked storage's first-member fixed filter now comes from the actual `IStorageGroupMember.ParentStoreSettings` getter, whose concrete interface implementation is also in the patch inventory; its identity/readiness/filter is retained separately from ASF's declared stuff-locked fixed settings. Eligibility checks the actual linked filter while preserving declared unlinked semantics.

ASF's potential full-member capacity branch atomically reserves **two member passes before the first ItemAt read**: the adapter's predicate preflight and the provider's scan. Per-item comp checks charge both classification and later predicate callbacks; the analogous native grid preflight also reserves comp callbacks. `ProviderMemberScan` shares the actual arithmetic with atomic-refusal/overflow tests. Four new controls pass; total2,920 tests, zero failures/skips. Exact TRX and DLL identities are in [runtime-run-log.md](runtime-run-log.md).

Native `MinifiedThing` subjects now return typed `UnreviewedPredicate` before filter unwrapping; targets return the same boundary before native/ASF stacking. **Required unfinished integration:** inspect and bind inner Thing identity/custody/predicates with a finite cost, or independently justify another complete resolution; this provisional unconnected adapter boundary is not an accepted support exclusion. Runtime controls must include full wrapped targets and wrapped incoming parcels with custom inner predicates, proving no unchecked inner callback under deferral and later demonstrating supported behavior.

The 8 September [minified support investigation](minified-projection-support.md) records the exact outer-only guard and distinct inner filter/stack paths. It also identifies the provisional adapter's ordinary-item filter dependency gap: special-worker classification does not establish virtual HP/stat/quality/book dependencies. This requires a discriminating fixture and focused independent design; no named-mod causation or gameplay defect is inferred from source alone.

Fresh independent source review approved these corrected paths. Source hashes: catalog `EA4E11F86C7DEC4387F8CA41BD8A11B6D9125949456C8493401565113D641AFA`, projector `AB91C59B344D3D327D9E0A5A214BCF1B938771A4E1461016FD3DDBB366FFAA91`, Core budget `C273C36DAC2B4C5E22D47F90E31EEC6B175C914FC6CA8ED60F71C03A36132FCB`. Actual linked ASF distinct-filter tests in both member orders, a full-member allowance short by the second scan, nested comp accounting, and every other declared runtime control remain unfinished. The later exact-finalizer catalog correction and accepted native [CAP02 fixture](cap02-fixture.md) are recorded above and in the run log. CAP03-B is being implemented against actual ASF/Neat Storage; its runtime remains unfinished. No allocator or hauling-path wiring exists yet.

