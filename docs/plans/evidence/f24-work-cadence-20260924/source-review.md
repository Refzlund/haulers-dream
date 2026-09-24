# F24 work cadence: source investigation and smallest next witness

2026-09-24. Investigation only: no product edit, build, Prepare, native launch, or ledger status change. This does not claim reproduction of a reporter's save.

## Exact scoped obligations

Bodies and timestamps are from `docs/plans/scoped-feedback-register.json`.

| Source | Timestamp | Feedback |
| --- | --- | --- |
| [C003](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001633990) | 2026-08-30 21:26:17 +02:00 | Maya Fey: "I'm having the same problem as TripleZer0 and I don't have simple sidearms." |
| [C004](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592939955001626310) | 2026-08-30 19:35:14 +02:00 | TripleZer0: "Love the mod and the clean UI it has. However something appears to be broken at the moment. Whenever I send pawns to harvest/chop wood/Deconstruct they will do 1 job and then immediately haul the items instead of continuing to work. I had disabled all other hauling mods. Maybe its simple sidearms causing the problem as somebody else suggested." |
| [C022](https://steamcommunity.com/sharedfiles/filedetails/?id=3742459652#c592938720043260054) | 2026-08-16 05:45:13 +02:00 | Triel: "What kind of settings do I use for pawns to finish off the growing site before buggering off to unload? If there are plants to be cut on a growing zone, they seem to cut a few then move to haul" |

None supplies the saved settings, remaining carry allowance, job trace, or whether the order is ordinary work, a prioritized order, or a queued series. C003 means Simple Sidearms is not a necessary cause. It does not establish any individual other-mod cause.

## Concrete defect: an empty emergency scan is not an empty work scan

`Source/HaulersDream/HarmonyPatches.cs:304` treats every invalid result from `JobGiver_Work.TryIssueJobPackage` as the end of a work run and invokes `OpportunisticUnload.TryGetEndOfRunUnloadJob`.

The actual native scheduler distinguishes two work nodes:

- `Data/Core/Defs/ThinkTreeDefs/Humanlike.xml` evaluates `JobGiver_Work` with `emergency=true` before the ordinary needs/work subtree.
- `Data/Core/Defs/ThinkTreeDefs/SubTrees_Misc.xml`, `SatisfyBasicNeedsAndWork`, contains the ordinary `JobGiver_Work` node.
- Actual `RimWorld.JobGiver_Work.TryIssueJobPackage` searches `WorkGiversInOrderEmergency` when the flag is true, and `WorkGiversInOrderNormal` otherwise. The emergency node first services native prioritized work; if that yields nothing, it searches only emergency work.

Consequently, an empty emergency result can be replaced with an HD unload before RimWorld even searches available normal harvest, cut, or deconstruction work. The settle window often masks this, but it is not proof that work has finished. With short/zero grace, or expired grace between actual pickups, this path can send the pawn to storage while productive normal work remains. `IsEnteringDowntime` can bypass settle too; it should not be justified by an emergency-only empty scan.

The valid-result branch already handles emergency protection. The smallest correction is a guard in the **invalid-result branch only**, immediately before `TryGetEndOfRunUnloadJob`:

```csharp
// An empty emergency scan says nothing about the normal work searched later.
if (__instance.emergency)
    return;
```

Do not replace native work selection, rescan the map, intercept all plant jobs, or suppress actual emergency/prioritized work. A normal empty work scan must still reach the existing end-of-run unload path.

This omission is present both in current source and in the decompiled installed Workshop HD assembly. Observed identities:

- Native `Assembly-CSharp.dll`: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.
- Installed Workshop `3742459652/1.6/Assemblies/HaulersDream.dll`: `D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D`.

This identifies a published code path, not the exact binary/settings used by C003/C004/C022. Productive before/after evidence is still required.

## Related behavior that must not be misclassified as the defect

| Mechanism | Actual behavior / implication |
| --- | --- |
| Harvest section collection | Autonomous native grow-zone `Harvest` jobs in DropThenHaul mode are capped at eight targets by `Patch_GrowerHarvestSection`; later work scans resume the rest. A `SelfPickup` after eight plants is local collection, not proof of a storage trip. Forced/native harvest queues are not capped by this rule. |
| Grace | Default `unloadGraceTicks=2500`; clock restarts on actual inventory pickup, not on merely creating a floor yield. An old saved short value remains short. A long job/section can exceed the clock. |
| Interval | Default `intervalUnloadHours=1`; the component calls `PawnUnloadChecker` on each interval. That checker considers real jobs in `pawn.jobs.jobQueue`, not the current producer or its internal `targetQueueA`. It may queue after old grace expires, to run after current work. Fresh later pickups do not cancel an already queued unload. |
| Interval intent | The shipped KeepWorkingWhenFull description explicitly says that it still unloads on the interval. The interval setting is described as a backup. This can explain part of the reports, but is **not enough to justify silently deleting interval behavior for all active producers**. A blanket `WorkRunKind.Yield` guard would also affect continuous deep drilling. Leave this unchanged in the first correction; record the exact trigger in native evidence. |
| Full pack | Default `keepWorkingWhenFull=false`; when another output cannot fit, full-trigger unloading is intentional. `true` leaves overflow on the ground and preserves ordinary work; it does not disable interval, downtime, or economical long-relocation unloading. Strict carry weight is separate. |
| Other nearby cargo | `sweepNearbyWhileWorking` can fill the pack with unrelated nearby items earlier than the produced output alone would imply. Isolate this in the witness by disabling it. |
| On-the-way unloading | Continuing yield work uses a minimum journey of 16 tiles, or eight with a heavy load, plus load/detour gates. A distant next target can intentionally unload en route. Keep the witness targets local, and disable this feature in the causal zero-grace phase. |
| Growing-zone clearing | Native `WorkGiver_GrowerSow.JobOnCell` can return `CutPlant` for an unwanted or adjacent blocking plant, then `Sow` for a cleared cell. This differs from harvesting the desired crop. `Sow` is currently non-yield work; the existing settle gate protects brief transitions. Do not claim that a crop-harvest test alone covers C022. |

Useful settings guidance after verification: a larger collection grace and interval off prevent time-based trips; Keep working when full prevents the capacity-triggered trip while leaving overflow for hauling. None is currently a promise to finish an arbitrarily large zone regardless of food, sleep, player orders, distant relocation, or capacity. A whole-zone lock would be a separate feature decision, not necessary for this narrow defect.

## Recurrence assessment

Local commit `97d9768b1414621d9a532bec0fd525baaf1bb240` (2026-06-24, PR #73) is titled "pawns no longer run home to unload after a single mined/harvested block." Its product diff adds the settle gate to `ShouldDivert` for a selected non-yield work job; it does **not** fix empty emergency scans. Earlier collection grace was also increased from 60 to 2500 ticks, explicitly to avoid a trip per item.

The later reports repeat the same symptom, and the source exposes a different scheduler boundary still violating the earlier accumulation intent. Classify this as **recurring symptom / previously unhandled work-scan boundary**, pending actual reproduction. Do not label it a proven regression of the exact #73 cause or a confirmed cause of each reporter's save.

## Smallest productive native witness

Reuse the accepted F03 base-harvest setup and observers from `f03-harvest-20260920/runtime-review.md` (producer `4d0a7d7debae4db29586b15bad7b96e2`). That already proves one real native cotton harvest, physical attribution, self-pickup, storage delivery, and unchanged default grace. Do not rerun H&H, construction gathering, robot positives, or the whole gathering-mode matrix.

Use one isolated pawn/map and three sequential productive phases, with enough measured native allowance for each phase, real storage, no foreign hauling mods, full needs, and normal work enabled. Advance through actual native jobs; do not assign fabricated successes or invoke the unload helper as the causal trigger.

1. **Field boundary and causal contrast:** 17 mature cotton plants in a compact grow zone, sowing disabled, skill/stat configuration with measured harvest yield chance at least one, ordinary Growing enabled and Hauling disabled. DropThenHaul, no incidental nearby sweep. For the discriminating baseline/candidate phase use the real exposed settings `unloadGraceTicks=0`, `intervalUnloadHours=0`, `opportunisticUnload=false`. This makes the faulty emergency path observable without waiting or overlapping interval/geometry causes. Require native ordinary harvest to resume beyond both eight-target section boundaries. The old build should show a real storage-bound unload while harvestable targets remain; the candidate should collect the complete field first, then unload via a genuinely empty normal work scan. This is a controlled source regression, not a claim that reporters used zero grace.
2. **Chopping / C022 clearing:** two mature poplars blocking a small grow zone configured for a different crop, native `GrowerSow` selecting `CutPlant` and then actual sowing. Check selected def, allowed cutting, growth season, reservations, harvestable output, and sufficient native allowance up front. This covers actual wood production and the cut-to-sow work sequence C022 describes; cotton alone would miss it. Require both tree targets removed before the first storage trip under the isolated trigger settings, then eventual tagged wood delivery. A tree's removal alone is insufficient: account for its real wood output.
3. **Deconstruction:** two nearby wood-stuff structures with native Deconstruct designations. Require both productive native jobs and their real returns before a storage-bound unload; verify remaining targets were available/reservable and the exact returned quantity is conserved through inventory, hands and destination.

For user-default cadence, add one **candidate-only** 17-cotton phase with unchanged 2500-tick grace and one-hour interval, proving no early storage trip while outputs keep refreshing grace and headroom remains. This is small and separate from the controlled causal comparison. Do not grow into a mode/settings Cartesian matrix.

Observe at least: native job ID/def/workgiver/playerForced/start/end, emergency flag and raw result before HD's postfix, result after HD, last pickup tick, current/queued/internal target counts, remaining productive targets, exact yield/held/stored quantities, native capacity/headroom, and calls to the interval/full/manual unload paths. Observers must not call additional work scans, which can reserve targets or change selection.

Acceptance boundaries:

- No HD unload returned from an empty emergency work node; ordinary available work actually runs next.
- Local `SelfPickup` remains allowed between sections; it must not be counted as the reported storage trip.
- Exact output conservation and eventual normal end-of-run storage delivery, with no unowned reservation/queue leftovers.
- Existing emergency or prioritized native work remains authoritative; forced manual/full-pack unload behavior is unchanged by source boundary. Use focused branch checks plus retained accepted evidence where applicable rather than another broad runtime matrix.
- Baseline must fail for the observed premature native unload, not for settings, inadequate capacity, a botched harvest, a blocked grow zone, needs, or an invented queue-empty oracle.

If all three productive phases pass but a normal-settings run unloads early, use the recorded trigger first. A documented full/interval/relocation trigger needs settings/behavior analysis; it is not evidence that the emergency guard failed. Keep F24 open until those observed paths are classified and the final behavior is explained in its resolution.
