# Independent review of the physical resource projection contract

Reviewed 2026-09-07 by `source_coverage_qa`. Scope: the proposed observation dependency in `storage-resource-projection-contract.md`, the accepted decisions in `storage-capacity-design.md`, current product consumers, and installed native/ASF APIs. This review did not edit product, harness or controller code, compile, launch the game, change claims, or implement an allocator.

The architectural boundary is appropriate: identified physical stack/slot observations, actual-subject eligibility, and typed incompleteness, with allocation/publication deferred to a separate owner. Correct the two definite work-budget defects below and resolve the identity/continuation contracts before treating the document as a bounded implementation specification. This is design review, not provider compatibility or gameplay acceptance.

## Findings requiring correction

### PRJ-R1: The native grid-visit upper bound omits the fire scan

The contract's deterministic-work section reserves `3 * N` grid visits for `IsGoodStoreCell`. The installed implementation can perform **four** scans of the candidate cell's Thing list:

1. `StoreUtility.NoStorageBlockersIn` traverses the list.
2. Its no-compatible-target branch invokes `GridsUtility.GetItemCount`, which traverses it again.
3. `FireUtility.ContainsStaticFire`, called by `IsGoodStoreCell`, traverses it again.
4. The construction-conflict loop in `IsGoodStoreCell` traverses it again.

An eligible vacant cell with no compatible partial and no fire can take all four paths. The stated bound is therefore too small without any third-party patch. Reserve at least `4 * N` for these native grid visits, separately from the projector's census and rechecks. `NoStorageBlockersIn` can also invoke the patched cell-limit getter inside its obstruction loop and again at its final capacity test; catalogue its cost rather than assuming the physical record's single explicit call is the only call. Keep reservation/reachability and custom-predicate cost limitations explicit; `4 * N` is not a bound on all work inside `IsGoodStoreCell`.

Acceptance: an actual instrumented no-partial/no-fire cell demonstrates the four native traversals, and a budget short by the reserved amount defers before the native predicate begins.

### PRJ-R2: Permitted lazy getters can exceed the budget before a bounded page exists

Installed `Building_Storage.AllSlotCellsList()` is:

```csharp
return cachedOccupiedCells ?? (cachedOccupiedCells = AllSlotCells().ToList());
```

`AllSlotCells()` is virtual. Thus even the inspected vanilla first read enumerates/materializes the entire building footprint, before the contract's bounded coordinate copy. Allowing cache hydration does not bound that hidden work. A supported XML-defined building can have a footprint larger than the page budget.

There is a second concrete cold path: ASF's declared `GetParentStoreSettings()` may call `CreateStuffLockedStorageSettings()`, which calls `ThingFilter.CreateOnlyEverStorableThingFilter()`. The installed filter factory resolves the root category and all applicable ThingDefs, then ASF clears that temporary allowance set. Vanilla's first `StorageSettings.EverStorableFixedSettings()` can also initialize the same kind of filter. These are not four constant-cost filter evaluations.

Make the readiness/cost decision independent of the permission to hydrate normal caches:

- For the inspected vanilla building enumeration, use a bounded known-footprint traversal or explicitly precharge the complete footprint before a cold getter. A custom `AllSlotCells` override requires its own inspected bound.
- Prepare the global vanilla fixed filter and ASF member fixed filters explicitly on the main thread with a separately accounted setup/slow-work budget, or precharge their complete known initialization cost. A hot/capped reader returns `ProviderInitializing` before invoking an unprepared expensive getter.
- Record the readiness prerequisite and initialization work in fixtures. Do not infer readiness merely from the usual spawn sequence, and do not replace ASF's getter with the base getter to avoid its actual stuff restriction.

Normal read-cache hydration is acceptable, but it is not an exception to the work-accounting promise.

## Decisions needed in the implementation contract

### PRJ-R3: Separate ASF registry membership from target validity

The proposed per-cell count equality plus `ContainsAndAllows` does not independently establish that the same physical item identities are registered. Installed ASF has distinct operations:

- `ThingCollection.Contains(Thing)` checks the `_indices` membership map.
- `ContainsAndAllows(Thing)` checks only `_validStoredThings`.
- `ItemCountAtMapCell(in IntVec3)` reports a per-cell list count.

`ContainsAndAllows == false` can mean a legitimately registered but disallowed item, or an absent/stale item. Equal per-cell counts do not distinguish a replaced identity or exchanged per-cell registry entries. Reporting both as a normal invalid top-up target weakens the promised `ProviderStateMismatch` classification.

Bind the declared `Contains(Thing)`, the indexed actual Thing access, and a stored-position read such as `MapPositionOf(Thing)` (after confirming membership/index validity), or an equivalently inspected bounded identity API. For every supported single-cell physical Item, require the registry to identify that same Thing reference/ID at that cell; then evaluate `ContainsAndAllows` separately. A present-but-disallowed target consumes a slot and refuses top-up. A missing, mismatched-reference or wrong-cell entry invalidates provider consistency. The projector must not repair it.

This strengthens the named-cell check; it is not a claim that every other cell in a large member was reconciled. Keep member-wide consistency unknown where it was not inspected. Do not use `CellWiseContains` as an assumed constant-time substitute: the installed implementation scans its list. `ItemsAtMapCell` returning `ReadOnlySpan<Thing>` still cannot be used through ordinary boxing reflection.

Acceptance: distinguish a registered invalid target from an equal-count registry/grid identity mismatch, including two cells whose recorded positions are exchanged. Neither fixture changes provider state through the projector.

### PRJ-R4: Define what certifies coverage after multiple pages

Retaining only the current member's enumerator is insufficient for `WholeGroupCoverageComplete`. For example, finish member A on page one, start B, add a cell to A without changing the linked group's `members` list, and finish B on page two. B's enumerator and the group-members enumerator can remain valid while the purported exhaustive coverage misses A's new cell.

Choose a concrete bounded rule: retain/check version guards for **all completed member cell lists** involved in the traversal, or use a separately established topology owner/epoch with the required coverage. Bound retained cursor state as well as per-page output. If the necessary guards cannot be retained/validated within that bound, return an explicit incomplete result; reaching the current enumerator's end is not a current-topology completeness certificate. A same-count replacement of a known list must also invalidate its guard. Physical rows remain stale observations even when topology coverage is valid.

An oversized deferred cell must have a named continuation reason and a viable larger-budget/preparation route. Decide whether traversal stops there or advances while retaining it as an explicit unresolved cell; neither route may silently count it as covered. The later fair owner is still required before live integration. The standalone projector can prove deterministic continuation on stable topology without implementing that owner now.

Acceptance: more than 200 cells, more than 32 members, a changed already-completed member, an equal-count list mutation, and a deferred large cell followed by a usable cell. Repeated pages must make measurable progress on unchanged supported input, and must not claim complete coverage after the mutation counterexample.

### PRJ-R5: Make lifecycle and invalid-use guards executable prerequisites

The proposed externally established session ID and provider generation are sensible, but no existing scalar claim generation is a replacement for either. Specify who supplies their current values in the first standalone fixture/component. A private captured `Current.Game` reference plus the externally assigned session identity can reject a quickload/replaced game even when map numeric IDs repeat. Opening the reader must not create a session or advance a capability generation.

Require every public scope operation, not just `Open`, to reject wrong-thread, disposed, wrong-session, wrong-scope observation/cursor, invalid-cell and capability-generation cases **before dereferencing its live handle table**. `Verse.UnityData.IsInMainThread` is available and compares managed thread IDs; ensure its normal game initialization happened before exposing the component. Unknown enum/context values and duplicate parcel IDs referring to different actual subjects are invalid requests, not default acceptance modes.

The own-gate helper must preserve an already-active legacy `insideSpaceScan` scope and restore both suppression and explicit filter context exactly once on exception/disposal. It must not suppress the HD building filter or set forced-order authority. Nested `Open` must defer before discovery, and direct calls on the existing scope during a predicate callback need an explicit operation-reentrancy guard; the phrase "normal calls on the owning scope are allowed" must not permit recursive reuse of mutable operation state from that callback.

## Decisions endorsed and limits retained

- Keep physical resources independent of probe order and keyed by session/map/concrete parent/cell/target identity. Excluding linked-group identity from the physical key correctly prevents linking from duplicating capacity.
- Count all physical Item occupants, including full/incompatible/provider-invalid stacks. Compress vacancies, use checked wide arithmetic, and exclude the actual subject itself from existing-target top-up. Unsupported multi-cell cargo cannot create one independent vacancy claim per footprint cell.
- Preserve actual `StorageSettings.AllowedToAccept(Thing)`, native `IsGoodStoreCell`, directional existing-target `CanStackWith`, and each concrete member's fixed filter. The additional linked-plus-concrete-member intersection is an explicit design decision already accepted; do not replace linked settings with stale individual unlinked settings. ASF's declared parent getter and actual-member capacity/validity calls are necessary.
- Preserve real ASF refusal, including its full-member inside/outside branches and actual PerformanceFish branch. A true member-level predicate is insufficient by itself to certify quantities. Catalogue unclassified quantitative providers as unsupported, with affected-member boundaries; neither unsupported nor deferred means infinity or a free native-reservation fallback across HD-owned resources.
- Accept ordinary read-cache hydration with PRJ-R2's accounting. Keep gameplay/settings notifications, random-state changes, jobs, claims, reservations, tags and provider registry repair outside the reader.
- Keep scope-only eligibility reuse and typed observation/capability/eligibility outcomes. Re-run relevant live predicates after independent decisions or mutations; identity/count guards alone do not establish unchanged filters, areas or arbitrary custom compatibility state. Only reviewed predicate behavior may supply a usable edge.
- Initial catalogue support should name the inspected native and ASF shapes, actual overrides/patch identities and reviewed applicability rules. A startup Harmony inventory cannot prove the absence of every native detour. State that evidence limitation rather than inventing universal compatibility. Version/generation changes need explicit invalidation before publication by a later owner.
- No allocator, admission guarantee, repair fairness guarantee or native-reservation transition is approved here. Keep the first implementation exposed to independent adapter fixtures only; do not adapt its output back into the old scalar `GroupSpace`/`StorageGroupBudget` pipeline.

## Evidence identity

Independently rehashed installed files:

| Artifact | SHA-256 |
| --- | --- |
| Game `Assembly-CSharp.dll` | `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A` |
| ASF `AdaptiveStorageFramework.dll` | `28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9` |

Read current product files and the captured native/ASF decompilations cited by the contract. Fresh decompilation to stdout independently checked `Verse.UnityData`, `Verse.Zone`, `RimWorld.SlotGroup`, `RimWorld.FireUtility`, `Verse.ThingFilter`, `AdaptiveStorage.ThingClass` and `AdaptiveStorage.ThingCollection`. The native/ASF findings above do not rely on the proposal's validation claims.

The contract revision read near completion had SHA-256 `B9839E300D5106E065E39E768C17E99DBE0B18FD072CF5B3ED4ADF8742D48E6F`; the accepted capacity design had SHA-256 `47FD954B7FE0B4CDE68F17FC9833297D2A298B01BAD7C0514066AACD7A5CCC51`. Root separately owns the known metadata correction: the file reports rev590 while the executing game reports rev591. Preserve those literal sources separately; this review makes no version claim about the original reporters' installations.
