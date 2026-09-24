# Common Sense gathering investigation — L01 / R01 / #258

Status: investigation complete; implementation and gameplay verification **not complete**. Investigation performed 2026-09-07 against `a8c19586716f26b1571e5000d52763c5f381ad8d`. No code changes, builds, game launch, or Git mutations were performed by this investigation.

## Conclusion and next action

Investigate the ordinary cooking gather route first. The actual #258 capture contains `CookMealSimpleBulk` ingredient-sharing probes with Common Sense's gather cede open, while HD's ordinary gather route categorically excludes this recipe because it allows ingredient mixing. The original #230 feature request explicitly used meals at a stove as its example. Changing the Common Sense toggle predicate again would not remove this exclusion.

This is a **demonstrated implementation coverage gap**, and a strong explanation to test for the current complaint. It is not yet a reproduced diagnosis of the exact moment the reporter described: the log does not record the selected job, its queue, the bench toggle, or all settings. The cook also already holds tagged ingredients, which independently suppresses ordinary gathering. Preserve these distinctions in the completion matrix.

Build a real-game witness for ordinary simple meals and the vanilla four-meal recipe, alongside a non-mixing control. Observe actual gathering, handoff, ingredient consumption, product creation, and repeated execution. Do not satisfy ordinary-bill coverage by switching the fixture to HD batch crafting.

## Evidence actually inspected

### Current report and attachments

[#258](https://github.com/Refzlund/haulers-dream/issues/258) remains open, has no comments, and was last updated 2026-08-13 17:47:38 UTC. It reports HD 1.24.0.0, RimWorld 1.6.4871 rev591, and 192 active mods. The reporter says the HD settings and bench button are enabled and Common Sense's corresponding gathering setting is disabled.

Both public attachments were downloaded and inspected:

| Attachment | Observed evidence | Limits |
|---|---|---|
| [HD log](https://reports.refzlund.com/files/E8wkFNzmgXKFy07W.log) | 3,351 lines, 145 distinct timestamp-stripped messages; no `[BillPrep]` or `[Batch]` markers; 416 ingredient-sharing probes | Construction probes dominate the capture. These are decision/probe logs, not proof of executed jobs. |
| [Player.log](https://reports.refzlund.com/files/6SmQkvVtAZ8039dc.log) | New v1.24 Common Sense startup message; `avilmask.CommonSense` active; no unresolved toggle warning, no exception text | No Common Sense assembly fingerprint, runtime option values, bill identity, chosen driver, or HD gather-gate snapshot. |

The important HD-log lines are 3257–3258: at the log's `08-13 20:27:21`, pawn Райан probes `CookMealSimpleBulk`, with two tagged stacks and one inventory stack added to the ingredient candidate set. Immediately beforehand the log records 12 milk picked up. The other 414 ingredient-sharing probes concern `InstallNaturalLung`, and must not be mistaken for 414 crafting attempts or successful surgeries.

`SharedBillPatches.cs:69–82` can reach that sharing call only with `shareForCrafting` enabled and `CommonSenseCompat.GathersIngredients == false`. Consequently, ceding was **not permanently stuck on** during these recorded cooking and surgery probes. It does not establish the other two HD global settings or the chosen bill job.

The installed mod list also includes Nice Bill Tab, Most Skilled Bill, Smart Work Mode, FSF Complex Jobs, Vanilla Cooking Expanded, Simple Sidearms, and several custom Lensrub patches. This identifies useful integration axes, not culpable mods. The report does not include an isolating save or sufficient final-job tracing to blame one.

Temporary acquisition directory: `C:\Users\Arthur\AppData\Local\Temp\haulersdream-commonsense-20260907`.

| Captured file | SHA-256 |
|---|---|
| `258-HD.log` | `28DE55639234FE46D8A09A2DB73AF5F813990063DE7DD7604D40BFB8554320EB` |
| `258-Player.log` | `A3172AFBBFD2D7B65B7962E6A79D5DD98E4855C5D34AAA5EA318A6B71399F1C5` |

### Actual Common Sense assembly

The installed original Workshop package is `C:\Steam\steamapps\workshop\content\294100\1561769193`; About.xml identifies `avilmask.CommonSense` and supports RimWorld 1.6. The inspected assembly is `1.6\Assemblies\CommonSense.dll`, version `1.0.9416.33549`, SHA-256 `8FD51F969912CEDBC11CD72E89D40EC713448AFD86CE0BEEF8CE2BDF03A0E2CA`. This is the same package identity as the report, **not proof of identical assembly bytes**.

ILSpy decompilation, saved as `CommonSense-installed.cs` in the acquisition directory, establishes:

- `CommonSense.Settings.adv_cleaning` and `adv_haul_all_ings` are static Boolean fields with the names HD reads. They both default true.
- `JobDriver_DoBill_MakeNewToils_CommonSensePatch.Prefix` replaces vanilla `JobDriver_DoBill.MakeNewToils` whenever either option is enabled (decompiled lines 2347–2356).
- Its replacement takes the inventory-gather branch only when `adv_haul_all_ings` is true **and** the worker is player-faction and humanlike (line 2276).
- With gathering disabled, it calls vanilla `JobDriver_DoBill.CollectIngredientsToils`; the cleaning sequence remains afterwards (lines 2321–2344). Thus the cleaning-only premise used by PR #252 is supported by this actual assembly.
- With `optimal_patching_in_use` true, `Prepare` can omit the driver patch entirely when both options are false at startup (lines 2003–2010). Live option values alone do not prove a patch was installed. A later in-session option change requires inspecting the actual Harmony patch state.
- The cook ingredient sort in this original assembly is on the namespace-root patch class. HD also names a nested class for the catgirlfighter fork. That fork was **not** installed/decompiled in this investigation; support claims for it require its real assembly or source and runtime evidence.

## Why the previous fixes did not establish full resolution

| History | What was changed or claimed | Remaining distinction |
|---|---|---|
| [T07-R06](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785190210323617/#c564785528365458930) | Earlier gather → bench → unload loop, reportedly even with CS gathering off | Related history, with additional unspecified conditions. Do not label its mechanism identical to #258. |
| [PR #53](https://github.com/Refzlund/haulers-dream/pull/53), commit `351a90b`, June 22 | Following the Medieval Overhaul pumpkin-pie report, excluded every `allowMixingIngredients` recipe from ordinary inventory gathering | Containment by disabling a class of behavior. The exclusion still executes before the Common Sense test. The `#4` in this code comment is that PR's task number, **not GitHub issue #4**, which is a release PR. |
| [PR #68](https://github.com/Refzlund/haulers-dream/pull/68), [PR #70](https://github.com/Refzlund/haulers-dream/pull/70) | Allowed HD batch jobs under CS, then enabled that option by default | Batch jobs use a separate driver. This does not implement ordinary meal gathering. |
| [#230](https://github.com/Refzlund/haulers-dream/issues/230) | Requested per-bench gathering control, specifically describing ingredients near a stove | Its meal example belongs to the already-excluded ordinary route unless an HD batch mode is chosen. |
| [#243](https://github.com/Refzlund/haulers-dream/issues/243), [PR #244](https://github.com/Refzlund/haulers-dream/pull/244), merged August 2 | Fixed planned-order gates and notices; explicitly deferred narrowing the cleaning-or-gather cede | On August 3, the same reporter said CS gathering off stopped unwanted gathering, but HD gathering still would not start. |
| [PR #252](https://github.com/Refzlund/haulers-dream/pull/252), merged August 9 13:03:03 UTC, commit `d4c6bc2` | Narrowed ordinary gather and ingredient-share ceding to CS's gathering option | Corrects the broad cede; leaves recipe, worker, forced-order, tagged-stock, and other route gates untouched. It expressly reports no in-game verification. |
| [#258](https://github.com/Refzlund/haulers-dream/issues/258), August 13 | New report on the release containing #252 | Shows the old completion claim was insufficient. Its actual capture includes an excluded meal recipe with ingredient sharing active. |

The earlier pumpkin-pie report is [T09-R22](https://steamcommunity.com/workshop/filedetails/discussion/3742459652/564785528365527820/#c573793023877708643): a many-ingredient Medieval Overhaul baking recipe loops specifically under “Do until X.” The PR attributes this to disagreement between `HoldsTaggedStockForBill` and the per-slot mixing chooser. This investigation verified the exclusion's commit and the relevant code but did **not** reproduce that historical diagnosis. It must be challenged by the expanded runtime fixture before removing the containment.

The test gap is concrete. `BillGatherTruthTableTests.cs:86–140` obtains “who actually gathers” from `CommonSenseCedePolicy` plus three settings via `GatherOwnershipPolicy.ResolveGatherer`. It never creates a real bill, runs `WorkGiver_DoBill`, reads a recipe, exercises a worker, or observes a job. `HaulersDream.Tests.csproj:21` references Core only. Its 48 rows establish the modeled settings composition, not the “end to end” runtime behavior claimed in its comments. `GatherNotice.cs` similarly describes the three notice outcomes as exhaustive while the actual route has many additional gates. Keep useful policy tests, but narrow their claims and add runtime evidence.

## Current routing and driver matrix

All rows below assume a real usable bench and valid ingredients; other gates still apply. These are source-derived expectations, not gameplay results.

| Path/configuration | Current route and driver | Investigation meaning |
|---|---|---|
| CS absent; ordinary automatic, non-mixing recipe; at least two floor stacks | HD `BillPrepGather`, then a new work scan issuing native `DoBill` | Positive control for the actual runtime integration. |
| CS cleaning on, gathering off; same eligible ordinary bill | Same HD prep; subsequent native `DoBill` receives CS cleaning toils | Exact claimed #252 case. Verify that cleaning and consumption both happen after the sweep. |
| CS cleaning off, gathering off; same eligible bill | HD prep → vanilla `DoBill` | Separates cleaning interactions from generic handoff problems. |
| CS gathering on; ordinary player-humanlike bill | HD prep and sharing stand down; CS gathers inside native `DoBill` | HD bench toggle must not imply it controls CS gathering; no double gathering. |
| Ordinary `CookMealSimple` / `CookMealSimpleBulk`, CS gathering off | **No HD prep**, because `allowMixingIngredients` is true | Concrete ordinary-recipe support gap; build a failing gameplay witness first. |
| HD batch-flagged bill, including mixing meals, `allowBatchUnderCommonSense` on | `HaulersDream_BatchCraft` if plan feasible | Distinct behavior. Test separately; not a substitute for the previous row. |
| HD batch opt-in off while CS cleaning or gathering is on | Batch conversion suppressed; bill remains native | Existing explicit preference. Verify UI and actual mode agree. |
| `optimal_patching` on, both CS options off at startup; toggle gathering on later | CS patch may be absent although HD now cedes based on live value | Separate binding-state hypothesis. Record actual patch installation and cold-start behavior. |
| CS `Settings` type resolves, but gather field is unreadable | HD cedes conservatively | Do not describe this as verified compatibility or silently consider the feature fulfilled. Record assembly/type/field and resolve support explicitly. |
| Fork changes the `CommonSense.Settings` type identity | Current bridge treats CS as absent | Different from a missing field on a recognized type. Match the actual active assembly and patches before claiming either safe ceding or support. |

### Gate classifications

Relevant code: `Patch_WorkGiver_DoBill_InventoryRoute.cs:33–117`, `BillRouteGate.cs:71–116`, `JobDriver_BillPrepGather.cs`, `SharedBillPatches.cs:60–83`, `Patch_WorkGiver_DoBill_BatchRoute.cs`.

| Gate | Current intent | Required handling |
|---|---|---|
| `inventoryCraftDeliver && shareForCrafting && markForUnload` | Explicit feature settings plus the relay's source/unload dependencies | Preserve off behavior; test each independently. Do not pretend all three values were captured in #258. |
| Bench toggle off | Explicit per-bench veto | Preserve, including batch/planned orders. Test save/reload and mixed bench selection separately. |
| `allowMixingIngredients` | Blanket containment of historical pie loop | Ordinary meal behavior is requested and should receive a real implementation path. Do not merely delete the gate without proving safe handoff and multi-slot recipes. |
| Chosen `stackLimit == 1` | Containment of [#63](https://github.com/Refzlund/haulers-dream/issues/63), addressed by exclusion in [PR #65](https://github.com/Refzlund/haulers-dream/pull/65) | Leave intact during focused #258 work; carry its requested bulk-stonecutting support separately. It is not evidence that all non-mixing bills are supported. |
| `forced || job.playerForced` | Preserve an explicitly ordered craft's continuity; ordinary prep ends before crafting | Keep current off behavior during this fix. Test forced ordinary orders and forced HD batches separately. Supporting forced gathering would require retaining the original order and queue, not flipping this guard blindly. |
| Pawn bill giver / autonomous worktable | Surgery is not bench crafting; autonomous machines need container deposits | Preserve dedicated flows. Medical restrictions and material delivery must remain correct; do not reroute them as a side effect of enabling meal gathering. |
| Mech worker | Consistent exclusion across gather and carried-source injection to avoid dead-ending jobs | Keep consistent for this change. If broader feedback calls for mech crafting support, give it its own work-range/forbidden/reservation design and tests rather than lifting one of several matching guards. |
| Drafted, Lord/duty-directed, haul-incapable, missing tracking comp | Eligibility and player-intent protections inherited from `YieldRouter.IsEligible` | Preserve existing policy; show a specific reason when diagnosing a rejected bill. A global enabled checkbox cannot prove that a particular pawn qualifies. |
| Any usable tagged item already held | Avoid re-gathering indefinitely | Too coarse to prove the whole recipe is supplied. The #258 cook actually has tagged stock; test partial stock and changing inventory rather than bypassing the guard. |
| Fewer than two floor stacks | Avoid prep when there is no multi-stack trip to save | Intentional optimization. Positive fixtures must force two selected stacks; one-stack controls should finish correctly without prep. |
| Empty-sweep cooldown | Contains an unsuccessful gather | Diagnostic state, not successful resolution. Repeated empty sweeps require a cause, and successful but useless sweeps must be detected by observed progress. |

## Can vanilla consume gathered mixing ingredients?

The actual installed `Assembly-CSharp.dll` was decompiled for `RimWorld.WorkGiver_DoBill` and `Verse.AI.JobDriver_DoBill` into the acquisition directory.

1. `TryFindBestBillIngredientsInSet_AllowMix` produces a `List<ThingCount>` by satisfying each slot's value requirement, using the bill/slot filters and `IngredientValueGetter`.
2. `TryStartNewDoBillJob` copies each chosen stack and its exact count into `targetQueueB` and `countQueue`.
3. `CollectIngredientsToils` has no mixing-recipe exclusion. It extracts those queues and calls `StartCarryThing` with `canTakeFromInventory: true`, then places ingredients under the native `DoBill` job so the recipe flow can account for them.
4. CS's cleaning-only replacement calls that same native collection sequence.

Thus no source evidence says vanilla collection cannot consume inventory-held ingredients for mixing recipes. The integration problem is the **handoff**, not a type restriction in the collection toils.

Current HD prep copies only the initial floor selections. It moves and tags cargo, walks to the bench, and ends. It does not retain the original `DoBill` job or a complete ingredient assignment for its next work scan. The next chooser sees changed inventories, merged/split stack identities, changed positions, possibly different stock, and other actors' reservations. `HoldsTaggedStockForBill` only asks whether any held tagged stack matches any slot. Neither that Boolean nor the success of one pickup proves recipe completeness or preservation of the original selection.

The mixing chooser also contains per-slot value/rounding behavior that a handoff must preserve. Inspect multi-slot filters that overlap and merges into pre-existing stacks; do not assume “one def per slot” or “all tagged food belongs to this bill.” Those assumptions already failed in batch history.

Recommended design investigation after the witness exists:

- Prefer a handoff that retains the chosen native bill/quantity contract and remaps only the quantities actually transferred into inventory. Alternatively, keep the gather within the same native bill lifecycle if that composes safely with CS. Evaluate both against native reservations, job cancellation, save/load, recipe hooks, and the historic duplication trap involving custom job defs and `placedThings`.
- Preserve the original bill's repeat mode, target count, ingredients, and product accounting. Do not convert ordinary crafting to a different batching semantic just to reuse a tested driver.
- If the chosen plan becomes invalid mid-sweep, release claims correctly and revalidate or resume a native bill without throwing away useful cargo or starting another net-zero sweep.
- Do not use the original full floor counts as evidence that those exact quantities arrived. Strict carry caps, interrupted pickups, merges, forbidden changes, other pawns and save/load must be reflected in actual transfer bookkeeping.

This is a recommendation to investigate, not a claim that either design has been implemented or proven safe.

### Safeguards for a retained native `DoBill` handoff

Preferred direction to prototype: gather only for the selected ordinary native bill, retain its validated quantity contract, and hand the remaining work to a **native `JobDefOf.DoBill`** without asking the general chooser to invent a different ingredient assignment at the job boundary. Keep native recipe toils and CS cleaning responsible for their existing behavior. Do not lift the surgery, autonomous-bench, mech, or forced-ordinary guards as part of this prototype.

The following are design requirements, not optional polishing:

1. **Capture an owned, independent plan.** Preserve the bill reference, bench, original chosen `ThingCount`s, native job context and mode, and any originally selected inventory-held ingredients. Do not preserve only floor targets. The inspected native `Job.Clone()` aliases `targetQueueA`, `targetQueueB`, `countQueue`, and `placedThings`; calling it does **not** isolate lists that either driver mutates. Never share a mutable queue with a discarded or pooled candidate job.
2. **Planning remains observational.** `WorkGiver_DoBill.JobOnThing` can be queried repeatedly without any returned job starting. Do not queue a continuation, reserve stock, change the bill, or count a pickup from its postfix. A provisional payload must have bounded ownership/lifetime, and become active only for the job actually selected. Do not leave unconsumed entries in a static handoff dictionary for discarded candidates.
3. **Record actual transfers with provenance.** An intended pickup of N is not evidence that N entered inventory. Match split and merged destinations by the actual move, maintaining selected ingredient identity, allowed filters and quantities. Two equivalent source stacks may merge, while same-def stacks with different quality/stuff/HP/contamination or other recipe-relevant attributes may not. A def-total delta alone is insufficient proof of the final native target list. Cap each remapped count by verified available units and avoid assigning one unit to two overlapping ingredient slots.
4. **Finish partial plans correctly.** Strict capacity, CE constraints, a vanished/forbidden stack, or another pawn taking it can leave part of the original selection on the ground. Retain a valid mixture of gathered inventory and remaining native floor targets if the plan still satisfies the recipe. If it no longer does, release obsolete claims and revalidate explicitly. A partial valid gather may save trips; it must not be presented as a completed full sweep, lose already useful cargo, or trigger repeated gathering of the same insufficient set.
5. **Revalidate before native start.** Check the bill is still in the expected stack, not deleted/suspended, still requests work under its repeat mode/target, still permits this pawn, and the bench and selected ingredients remain usable. A second pawn may have reached a target count while this pawn gathered. Do not deep-save a duplicate `Bill` or manufacture a detached bill stack. Native `Job.ExposeData()` scribes `bill` by reference and the target/count queues as job data; follow that ownership model.
6. **Persist a single continuation owner.** Save the active prep phase, remapped quantities, remaining targets and valid continuation context in the appropriate scribed job/driver state. A process-local static plan cannot be the only copy. Cold-load and quickload must resume a real remaining phase, not replay a pickup or lose the continuation. Old prep jobs lacking the new fields need an explicit migration/fallback that keeps real cargo safe. Observe cross-reference resolution before validating loaded bill/thing references. Avoid handing the same native job to both the driver state and pawn queue as independent deep-owned objects.
7. **Interruption is not completion.** Emit the continuation once, only on the intended successful handoff path. A finish action also runs on cancellation/failure. Native ordered-job handling can enqueue a fresh player order before ending the interrupted prep; an unconditional `EnqueueFirst` in that finish action would put the old craft ahead of the new command. Test drafting, a new forced order, mental/medical interruption, bench destruction, and cancellation. Reclaim abandoned cargo through the established unload path behind actual player work. The successful handoff must prevent that same unload path from stealing ingredients before the native bill begins.
8. **Reservations cross the boundary deliberately.** Do not assume prep reservations survive native job cleanup. Establish the continuation's reservations through the native lifecycle, release stale prep claims, and handle another actor acquiring the bench or stack between jobs without stealing it or spinning. Verify two cooks and another hauler, including a save between gather completion and native start.
9. **Preserve native/CS recipe behavior.** Use the native job def for placement/consumption and run its recipe hooks exactly once. CS cleaning-only must still clean the room and then consume the retained inventory targets. If CS's gathering setting is enabled mid-prep, determine from actual toils how the native continuation will interact with that changed owner; do not double-gather or discard the selection. Test both an unchanged setting and the mid-job flip. Do not infer that a separate gather driver automatically preserves cleaning on a later, differently chosen job.

A retained handoff is successful only when these observations demonstrate more reliable execution than today's reselection. If native continuation semantics make this architecture unworkable, evaluate a single native-job gather phase against the same invariants instead of weakening them.

## Discriminating gameplay witnesses

Use the isolated runtime procedure owned by the main task. All fixtures use disposable saves, a fixed seed, real game definitions, and the actual Harmony-patched workgiver. Capture the selected job after all postfixes and observe job execution; invoking an HD postfix or helper directly is insufficient.

### First minimal witness

- Ordinary human colonist, healthy and not drafted/duty-directed; cooking enabled; empty tagged inventory; ordinary fueled/powered stove; available work and enough hunger/rest margin to finish.
- HD ordinary gather, carried-ingredient sharing, automatic unloading, and this bench's gather switch enabled. No HD batch flag. CS cleaning enabled, gathering disabled; record actual values and patch binding after initialization.
- A `CookMealSimple` bill for one meal, with two **different** allowed raw-food definitions at 0.05 nutrition per unit, five units of each. Read the runtime value getter to assert those values. Put them apart and away from the stove. The selected vanilla plan must contain both stacks. Different defs prevent native same-stack hand batching from accidentally matching the intended optimization.
- Baseline current code should show native collection and no HD prep because of the mixing gate. Both ingredients still need to be consumed and a meal produced; the failure is the missing requested sweep, not inability to cook.
- Corrected behavior must move all ten selected units into inventory before the pawn returns to the bench, then consume those ten and create exactly one meal through the native bill lifecycle. No second gather, stranded remainder, storage detour, or product duplication. CS cleaning remains functional.

### Actual-capture recipe and historical failure variants

| Variant | Required assertions |
|---|---|
| Ordinary `CookMealSimpleBulk` (vanilla x4 recipe) | Two food defs totaling 2.0 nutrition, for example 20 units of each at 0.05. One normal recipe execution creates four meals. Do not confuse its `Bulk` name with HD batch mode. |
| Partial tagged stock, matching the capture | Cook begins with tagged milk (the report records 12), plus the remaining selected food on the floor. Gather only a real shortfall; consume a valid complete mixture; do not keep deferring because any ingredient is held. |
| Repeated ordinary bills / Do until target | Several cycles; product count rises, target is respected, and each completed gather produces useful progress. Include existing products held in inventories. |
| Multi-slot vanilla fine/lavish recipe | Each slot's nutrition/filter requirement is satisfied without spending one stack twice or silently changing the chosen mix. |
| Actual Medieval Overhaul pumpkin-pie recipe at its actual large oven | Use the installed/supported mod definitions; reproduce T09-R22 or document what differs. Both repeat-count and Do-until modes; extra inventory food, overlapping slot filters, uneven stacks, and insufficient partial supplies. A simple-meal pass cannot close this historical recurrence test. |
| CS gathering on/off and cleaning on/off | Observe who gathers and whether cleaning runs, not only which Boolean says it should. Flip settings paused and between jobs; restart with optimal patching on/off. |
| Non-mixing two-stack bill | Positive integration control; ensure the meal change does not break existing tailor/smith recipes. |
| Bench/global off, forced ordinary, mech/surgery/autonomous controls | Dedicated native behavior still works, and prohibited HD prep stays absent. Distinguish intentional exclusions from unsupported requested features. |
| Two pawns / interrupted pickup / bill deleted or suspended / forced order / save and full restart mid-gather | Item conservation, claim cleanup, original player intent, valid continuation, no repeated empty or net-zero sweeps. |

Run the reproduced old behavior and corrected behavior through the same observation assertions. Do not waive the historical pie and ordinary-meal cases merely because a non-mixing recipe succeeds.

## Diagnostics needed before choosing the final repair

The current log cannot tell whether a missing marker means no eligible bill was found, a gate declined, a later patch replaced the result, or a selected gather failed before moving anything. Add targeted, bounded observations in the diagnostic/test build:

- Startup identity: HD and CS assembly path/version/hash; resolved settings types and field types; actual Harmony owners/methods for `WorkGiver_DoBill.JobOnThing`, `TryFindBestBillIngredientsInSet`, and `JobDriver_DoBill.MakeNewToils`; whether CS `Prepare` installed the relevant patch.
- Per distinct bill-routing decision: pawn/bill/bench IDs; recipe and mixing flag; forced flags; three HD globals and bench setting; CS live fields and read success; chosen queue counts and floor/inventory/unfinished provenance; existing usable tagged stock; worker eligibility; selected route or **specific decline reason**.
- Actual job lifecycle: final job def/driver, start/end condition, current bill, actual inventory delta and stable thing/count mapping at each pickup, delivery/consumption/product delta, and next selected job after prep. Count progress from executed transfers and completed recipe work, not repeated work-scan probes.
- Trace the same bill through a complete sequence. Aggregate identical scan decisions instead of flooding the ring buffer; the current capture shows how useful evidence can be buried by hundreds of identical construction or surgery probes.

Use this evidence to distinguish the recipe exclusion, partial-tag guard, binding drift, changed workgiver result, and handoff/unload loop before assigning cause. A Common Sense name in a Harmony owner list is not a diagnosis.

## L01 coverage beyond #258

| Source | Separate requirement / disposition |
|---|---|
| #258, #243, #230 | Gathering and controls work for intended ordinary crafting scenarios, including the meal example; historic fix claims receive real driver/consumption verification. |
| T07-R06 | Preserve a regression scenario for gather → bench → unload under CS, including gathering off. Its original linked HugsLib report has not been reanalyzed here; historical causation remains unverified. |
| [C130](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c571542958702267458) | **Distinct feature request:** provide clean-room-before-task behavior without needing CS. Also mentions more red errors without isolating either mod. Resolving #258 does not disposition this feature request. Current source search finds planned cleaning routes and permission controls, not an established automatic clean-before-task equivalent. Create a focused feature investigation with activation scope, opt-in/defaults, per-bench policy, interruption rules, and CS ownership. |
| #68 / #70 batch behavior | Keep batch mode semantics and its CS opt-in functioning independently. |
| Historical sort binding failure (#193 / #195) | Include actual assembly/fork binding verification for ingredient priority. Treat it as a different prior ineffective fix, not proof of #258's cause. |

Completion for this investigation does not close L01, #258, the pie-loop history, C130, or the goal. Each needs its listed implementation/disposition and independently reviewed runtime evidence.
