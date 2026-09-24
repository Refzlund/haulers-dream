# CAP01: actual shelf-slot capacity fixture

Implementation: `tools/RuntimeHarness/StorageSlotsScenario.cs`. Entry point: `StorageSlotsScenario.Run(map, expectedBehavior)`, returning a dedicated `StorageSlotsResult` with case ID `CAP01`. Root owns Bootstrap/controller integration, compilation, private-copy preparation, execution and evidence verification. The fixture author has not built or run it.

The fixture is an adapter/budget witness for L04-A1. It does not run a hauling job, advance simulation ticks, deposit incoming cargo, infer the original reporters' settings, or establish a cause of #256/#268. It uses the isolated actual game map and loaded production DLL; there are no product hooks or replacement storage classes in this change.

## Actual scene and production interfaces

Each independent scene creates an actual vanilla `ShelfSmall` (`Building_Storage`), made of WoodLog, at a separate cleared outdoor cell. Preconditions require an unlinked one-cell slot group and exactly **three** slots from the actual patched `GetMaxItemsAllowedInCell`. Real spawned Steel and WoodLog stacks fill their own runtime stack limits. In the CAP01 scenes, exactly one slot remains vacant. Incoming Silver and Cloth are actual Things using their runtime stack limits; no capacity constants are guessed from the UI.

Shelf snapshots record shelf/cell identity, maximum slots, item count, vacancies, and every physical stored Thing ID/def/count/stack limit. All initial generated actors and landing objects are despawned only on this disposable map. New ordinary human actors have work disabled. Every scene's stored physical IDs/counts must be unchanged at the end, confirming that plan consumption was virtual accounting rather than a simulated deposit.

The fixture resolves these methods from the actual loaded product assemblies:

- `StorageCommitments.MeasureGroup`, including its published `GroupSpace` fields.
- Private `BulkHaul.ResolveGroupBudget` with the actual generic budget dictionary, and private `PriceDefInto`.
- Actual Core `StorageGroupBudget.AvailableFor` and `Consume`, plus diagnostic readings of the published budget's private state.
- Production `TryCommit`, `UnitsMovingOf`, `ClaimedByOthersFor`, and five-argument `FreeUnitsFor`.
- Actual Harmony-patched `StoreUtility.IsGoodStoreCell`.

No `StorageGroupBudget` is initialized from manually invented decomposition inputs. `ResolveGroupBudget` creates it through production measurement, and both definitions must resolve to the same actual budget instance. A repeated explicit `PriceDefInto` verifies that pricing does not reset an earlier spend. Production/Core assembly identities and MVIDs are recorded in the result; root's ordinary harness manifest must continue recording their exact DLL hashes and game/mod versions.

## Scenarios and expected answers

| Scene | Observations | Published baseline | Corrected behavior |
|---|---|---|---|
| CAP01 per-plan, Silver then Cloth | Measure each incoming def; resolve/price Silver; consume one full Silver stack; resolve/price Cloth in the same dictionary | Both measures report zero empty cells and one incoming stack limit as `PartialSpace`, despite no compatible existing stack. Cloth still receives a full stack of room after Silver spends the only vacant slot. | Silver fits; Cloth receives zero after that slot is spent. |
| CAP01 per-plan, Cloth then Silver | Identical operations in reverse def order | Silver independently receives private room after Cloth spends the slot. | Cloth fits; Silver receives zero. |
| CAP01 cross-pawn, Silver claim then Cloth | A real carrier holds/tagged one full Silver stack; `TryCommit` records its claim; `UnitsMovingOf` and `ClaimedByOthersFor` must both prove the live quantity. Another pawn asks about actual spawned Cloth. | `FreeUnitsFor` still returns one full Cloth stack and the patched cell gate returns true. | Foreign Silver cargo spends the only vacant slot; Cloth gets zero and the cell gate returns false. |
| CAP01 cross-pawn, Cloth claim then Silver | Same live-cargo test with reversed definitions | Silver still gets room and a true cell gate. | Silver gets zero and a false cell gate. |
| CAP02 genuine top-up control | Full shelf: full Steel, full WoodLog, Silver missing exactly seven units; ask about Silver and Cloth | Silver has seven units of real top-up; after a virtual seven-unit spend it has zero. Cloth has zero. A different carrier's real seven-unit Silver claim makes a new Silver request zero. | Preserve these same answers. The repair cannot treat all occupied-cell room as shared or discard genuine partial capacity. |
| Fully occupied incompatible control | Full Steel, WoodLog and Uranium; actual incoming Silver and Cloth | Both plan budget and free-unit adapter return zero; both cell gates are false. | Preserve these answers. |
| CAP03 empty three-slot mixed-plan control | One shared budget; request one full stack each of Silver, Cloth, WoodLog, then Steel | First available quantity is three Silver stack limits. Spending only one Silver stack consumes the sole modeled *cell*, so all later definitions get zero. | Available quantities are three, two, one, then zero respective stack limits. Each positive request spends only one slot; a fourth incompatible stack is refused. |

The empty-shelf control consumes a request only when the **actual** budget permitted its full stack. It never fabricates progress by spending a rejected amount. Because the baseline rejects the second/third requests, those are not committed or physically placed.

Cross-pawn claims are explicit adapter fixture setup. Cargo is physically transferred through the real inventory `ThingOwner`, tagged through the actual injected `CompHauledToInventory.RegisterHauledItem`, and measured through production evidence. There is no hand-built `delivering=true` Core input and no claim derived solely from a candidate marker. The querying subject is spawned floor cargo, so this witness is independent of L04-O1's own-inventory delivery-credit bug. Claims created here are retired through production `Commit(..., 0, ...)` in `finally`.

## Results and future allocator integration

`expectedBehavior` accepts `baseline-gap` or `satisfied`. Every observation contains actual, expected-baseline, and expected-corrected values. Fixture assertions are distinguished from behavior assertions; a failed precondition or missing interface produces an explicit inconclusive result.

`baseline-gap` matches only if all baseline answers match, the eight CAP01 measurements show the published occupied-cell/private-room split, and requested behavior remains unsatisfied. Its status is `behavior-gap-observed` with `requestedBehaviorSatisfied=false`. An expected old failure is never a passed feedback item. `satisfied` requires every corrected behavior/control answer.

The implementation intentionally identifies its current binding as `published-scalar-budget-v1`. The replacement resource allocator interface is not yet chosen. When it changes, root must add a **reviewed harness adapter** that measures/allocates through the new production pipeline while retaining these physical scenes and behavioral expectations. Removing or changing old methods/fields is not success; current bindings will make that run inconclusive. Legacy decomposition diagnostics should then become replacement resource/slot-allocation diagnostics, not synthetic calls into retired Core code. No product scaffold has been added to anticipate that API.

This first fixture does not satisfy CAP04–23, ASF compatibility, actual pre-toil admission races, virtual-slot materialization, save/load, or L04-A2 multi-cycle pickup/unload convergence. Those remain separate witnesses. Root should first run the pristine Workshop baseline, inspect complete structured output and Player.log, then run the corresponding corrected production adapter once selected and independently reviewed.

Root's initial independent source review caught and corrected a floor-custody precondition before execution: spawned Things are registered in a map-owned `ThingOwner`, so `holdingOwner == null` is false in this installed game. Floor subjects now require `Spawned`, the actual matching map and `ParentHolder == map`, alongside the original position/count/filter checks. The earlier O1 runtime and native SpawnSetup establish this API fact. Fresh source review and an actual run remain required; correction alone is not fixture acceptance.

Independent reviewer `source_inventory` then found that initial re-pricing happened before the same definition was consumed, so it did not substantiate the claimed preservation of an earlier spend. Root added actual re-resolve plus explicit `PriceDefInto` calls after the first full-stack spend in both orders and after the genuine seven-unit top-up spend. Each must return the identical budget and retain zero remaining allowance for that consumed definition. The 30 quantity observations remain unchanged; the new assertions close the missing fixture coverage only after actual execution.

The same review requested stricter binding provenance. Root now requires exactly one loaded definition for each resolved type, the identical HD assembly for adapter/bulk/settings, the expected Core assembly for the budget, and the exact production gate/counter/reservation Harmony owner, class, method and assembly. These are fixture setup checks; patch presence alone is not execution evidence for the counter/reservation paths.

Reviewer `source_inventory` freshly re-read both corrections and accepted the source without further findings. Root's standalone harness build completed with zero warnings/errors, SHA256 `2DF2EC1F89683788F66649563153638CC690387AA136400B9B5A6350851A33F9`. Actual baseline execution and independent controller/result review remain required.
