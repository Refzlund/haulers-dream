# BG02: ordinary vanilla four-meal recipe fixture

Status: implementation handed off for root integration, compilation, independent review and isolated runtime verification. No build, game launch, product fix or BG02 acceptance is claimed by this fixture author.

## Entry point and ownership

Changes are confined to `tools/RuntimeHarness/Bg01Scenario.cs`, `Bg01Observers.cs`, and this document. Root owns `Bootstrap.cs`, the controller, preparation, builds and launches.

The existing call remains BG01:

```csharp
new Bg01Scenario(map, manifest.expectedBehavior)
```

The new explicit case call is:

```csharp
new Bg01Scenario(map, manifest.expectedBehavior, manifest.caseId)
```

Only `BG01` and `BG02` are accepted by the three-argument constructor. Root must add `BG02` to its manifest/controller validation and bootstrap case branch, pass the explicit case ID, and retain the existing observation lifecycle (`Observe("tick")` and `TryFinish()`). These files do not modify the immutable prepared-run identity or infer a case from recipe availability. `expectedBehavior` remains exactly `baseline-gap` or `satisfied`.

## Cases and retained behavior

| Fixture input/criterion | BG01 | BG02 |
| --- | --- | --- |
| Actual recipe | `CookMealSimple` | `CookMealSimpleBulk` |
| Floor rice / potatoes | 5 / 5 | 20 / 20 |
| Nutrition per real ingredient | 0.05 / 0.05 | 0.05 / 0.05 |
| Required ingredient nutrition | Existing BG01 recipe contract | Runtime-verified single nutrition slot with base count 2.0 |
| One recipe product entry | `MealSimple`, count 1 | `MealSimple`, count 4 |
| Required actual product events | One, producing one meal | One, producing one four-unit meal Thing |
| Full inventory before first return | 5 rice and 5 potatoes outside bench radius | 20 rice and 20 potatoes outside bench radius |
| Maximum game ticks | Existing 9,000 | 12,000 |
| Stable post-product window | Existing 300 ticks and at least 300 distinct observed ticks | Same |

Both use the existing supported 41x13 disposable room, same deterministic colonist-generation seed, fueled stove, distant separated ingredient cells and three one-layer seeded filths. Initial actors/landing objects are despawned from the disposable map. Only Cooking and Cleaning work are active. The fixture enables HD ordinary ingredient gathering/sharing/unload tagging and the stove's gather toggle. It sets `batchByDefault=false`, CS `adv_cleaning=true`, `adv_haul_all_ings=false`, and disables CS's independent before-work insertion and hauling-over-bills mode so in-bill cleaning is attributable. Startup CS patching mode and actual product patch identities must match the existing requirements.

The fixture creates a normal `Bill_Production`, sets `RepeatCount=1`, allows only rice and potatoes, and uses `DropOnFloor`. It never creates or starts a job, inserts a toil, supplies candidate ingredients, changes a returned workgiver job, consumes a product iterator or enables an HD batch to manufacture the result. Normal automatic workgiver/job execution owns the hauling, crafting and cleaning.

BG01 keeps its original assertion IDs, observer Harmony owner, patches, numeric thresholds and outcome rules. Its constructor delegates to the BG01 constants. New consumption observation and stronger acquisition-conservation assertions are **BG02-only**; BG01 does not acquire additional consumption hooks or a new acceptance requirement. Shared result metadata gains additive fields.

## Actual API evidence and product shape

Inspected the installed RimWorld game assembly at `C:\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`, SHA-256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`, and `Data\Core\Defs\RecipeDefs\Recipes_Meals.xml`.

The XML defines `CookMealSimpleBulk` with `allowMixingIngredients=true`, `IngredientValueGetter_Nutrition`, one ingredient slot of 2.0, one `MealSimple` product entry of four, and work amount 1,200. This is an ordinary vanilla recipe, independent of HD batch crafting.

Installed `GenRecipe.MakeRecipeProducts` creates **one Thing per products entry**, sets `stackCount = CeilToInt(product.count * efficiency)`, calls `Notify_RecipeProduced`, then yields the result of private `GenRecipe.PostProcessProduct`. It does not produce four separate postprocess calls for this recipe. The fixture's existing postfix observes that actual call without enumerating or replacing the product sequence.

BG02 validates the live recipe and provider shape before execution: one nutrition slot with base count 2.0, actual nutrition value getter, plain `RecipeWorker`, no unfinished Thing, one four-meal product entry, no special products, no minification, and a meal stack limit of at least four. It calculates the actual worker/work-table efficiency and requires 1.0. This is necessary because installed `RecipeDef.ResolveReferences` can assign `workTableEfficiencyStat` even when absent from XML; simply requiring a null field would incorrectly reject the native recipe.

BG02 additionally reads HD's actual `HaulersDreamGameComponent.Instance.IsBatchBill(bill)` and requires false for the new bill. It does not call `SetBatch`. Actual executed jobs must include exactly one matching native `DoBill` with exactly `JobDriver_DoBill`, a genuine `WorkGiver_DoBill` attribution, no player-forced flag, and no batch job.

## Ingredient identity and consumption evidence

The existing enclosing holder/carry observers count **net increases in inventory plus hands**, so inventory-to-hands movement is not counted as another acquisition. BG02 requires exactly 20 acquired units of each def and at most one executed gather job. Initial Thing IDs and floor/inventory/hand identities with counts are included in transfer events.

Do not require a floor decrease inside the narrower `ThingOwner.TryAdd` callback. Current production `TransferSelected` calls `SplitOff` **before** `TryAdd`; the original floor Thing may already be despawned at that prefix. BG02 instead verifies at every settled pre-product driver/tick boundary that floor + inventory + hands still totals exactly 20 rice and 20 potatoes. Transient owner notifications remain diagnostic rather than purported conservation boundaries. Final zero ingredients, exact acquired quantities and independently observed consumption complete the accounting.

The new BG02-only observer hooks the exact installed signature:

```csharp
RecipeWorker.ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
```

Its prefix captures the actual Thing ID/def/count, worker, map, native job ID and native context. Its postfix checks the same object was destroyed by the call and attributes its actual pre-call quantity once per Thing ID. A changed context, unknown ingredient, already-destroyed object, duplicate identity, wrong worker/map, missing product event or unsuccessful destruction makes consumption attribution fail. The observer never destroys anything itself.

Installed `Toils_Recipe.FinishRecipeAndStartStoringProduct` first materializes the native recipe products, then calls `ConsumeIngredients`, which calls `recipe.Worker.ConsumeIngredient` for each selected real ingredient. Therefore consumption events are expected **after** the product callback, inside the same native recipe operation, and before completed native job cleanup. The fixture records this actual order instead of assuming ingredient destruction precedes the product callback.

At completion, BG02 requires exactly 20 rice and 20 potatoes attributed to unique consumed Things within the single successfully cleaned-up native job. It does not require exactly two consumption calls: legitimate real stack splits can change Thing count without changing the required 40 unique units. Any duplicate call or incorrect aggregate fails.

New events are `native-ingredient-consumed`, plus BG02-specific identity/count details appended to `fixture-ready`, `settled-held-transfer`, and `native-product-created`. The observer owner is `HaulersDream.RuntimeHarness.BG02.observers`; all patches are read-only prefixes/postfixes/finalizers and removed when the scenario finishes.

## Outcome rules and schema handoff

Requested behavior requires all of the following together:

- Correct automatic ordinary recipe execution, one successful native job cleanup and one actual native four-meal product event.
- Three seeded filths removed, each paired with its actual `MessesCleaned` increment in an installed CS cleaning toil of that same native job, before the first product callback.
- Both full ingredient quantities in inventory before the first bench return; no incomplete first return, second ingredient excursion, repeated acquisition or outside-bench ingredient drop.
- Exact 20+20 acquisition and native consumption, zero remaining ingredients, exactly four meals, bill repeat count zero and at least 300 stable post-product ticks.
- Healthy observation callbacks and intact supported roof.

`baseline-gap` requires successful native cooking, cleaning, cleanup, identity/count controls and a real incomplete first return without the full inventory sweep. An arbitrary failure or timeout is not a baseline reproduction. `requestedBehaviorSatisfied` remains false when only the expected baseline gap is witnessed. A timeout is explicitly `inconclusive`; root's separate harness wall-time limit remains in force.

The existing `ScenarioResult` remains the return type and retains every old field. Additive metadata fields are:

- `caseId`, `recipeDefName`, `expectedRice`, `expectedPotatoes`, `expectedProductUnits`, `maximumTicks`, `requiredStablePostProductTicks`.
- `initialRiceThingId`, `initialPotatoThingId`.
- BG02 consumption fields: nullable `riceConsumed`, `potatoesConsumed`, `nativeIngredientConsumptionExact`, `ingredientAcquisitionConserved`, plus `ingredientConsumptionEvents`. These are null for BG01, which has no new consumption witness.
- Each consumption record contains actual Thing/def identity, pre-call count, remaining count, consumed units, destroyed/attributed flags, native job ID, tick and event sequence. No live Verse object is serialized.

New BG02 assertions include `fixture-bg02-native-nutrition-recipe`, `fixture-bg02-bill-batch-disabled`, `fixture-bg02-native-product-shape`, `fixture-bg02-observers-installed`, `execution-bg02-native-ingredients-consumed-once`, `execution-bg02-acquisition-conserved` and `execution-bg02-observer-healthy`. Shared execution/behavior IDs retain their meanings with the explicit case quantities.

Completed bounded evidence: independent source review, published baseline `c53b6850534447739d614000f49c2bfc`, corrected `85b5a354d624423288b0fa975cae247a`, complete result/event/log/assembly/protected-file review, corrected bill-validator review and final full controller verification. BG02 is accepted on HD2FAD6BBB…/CoreB610A8EA… with harness8EC7FDB9…. The final helper is `C72F6F38A72FA878ED94E4015C66F5C78F45EF6505E48544641CED00E729081D`. Both PS7 and5.1 passed all ten independent final controls with zero exceptions. Exact run hashes and per-run log dispositions are in [runtime-run-log.md](runtime-run-log.md). BG03 and later scenarios, the reporter's partial-inventory variant and final integrated retesting remain unfinished.
