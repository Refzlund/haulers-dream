# Independent F24 v8 acceptance

**Accepted for the bounded F24 scheduler correction:** all 59 assertions pass, all 653 events are contiguous, and zero Unity errors are captured. The independent audit verifies original/retained raw hashes, exact manifest and ten loaded assembly bindings, every assertion, every event's order, all 126 gate samples, all 86 physical conservation samples, native work-giver/job identities, fresh production/grounding totals, actual stump lineage, successful storage delivery and protected cleanup. Reproduce with `audit.py`; output is `independent-runtime-audit.json`.

| Phase | Native work and exact output | Completion |
|---|---|---|
| Zero-grace cotton | GrowerHarvest 37/73/95 completes 17 plants in sections 8/6/3; 170 cloth. Native SelfPickup between sections; remaining ground output gathered by native en-route jobs. Normal empty work scan returns unload106; all170 stored. | tick3420 |
| Grow-zone clearing | GrowerSow CutPlant145/170 clears both immature poplars and their actual native stumps; 49 wood. Native Sow165/181 then produces both replacement cotton plants. Normal empty work scan returns unload187; all49 stored. | tick5260 |
| Deconstruction | Ordinary unforced Deconstruct211/222 completes both wooden walls; actual native leavings3+3. Native collection and unload229 stores all6 wood. | tick6008 |
| Default cadence | GrowerHarvest238/277/297 completes all17 plants in sections8/6/3;171 cloth. Last native pickup9158; unload426 starts12402,3244 ticks later, after the unchanged2500-tick grace. All171 stored. | tick12836 |

No storage unload starts with productive targets remaining. The zero-grace phases use an actual empty **ordinary** work scan; empty emergency scans do not substitute unloading. The default phase uses its unchanged idle backstop after grace. Ordinary one-tick job-transition pauses occur throughout without becoming premature storage trips. The driver/observer controls remain those reviewed for v8; the host does not inject productive work or repair resource counts.

## Actual stump proof

The missing v7 accounting is directly observed here, without assuming a constant yield:

- Tree36430 yields22 at3951. Native TrySpawnStump returns36432 (`ChoppedStump`), and native PlantCollected places that same object in CutPlant145's targetQueueA. The same job then works the stump and places2 additional wood at3987. Original tree plus stump retire before the job succeeds. The24 wood is physically picked up.
- Tree36429 yields22 at4449. Its actual returned/queued stump36437 remains in native CutPlant170 and yields3 at4485. Both retire; native en-route collection later brings this25 wood into the existing cargo. The original stack36431 receives the merged total49 and reaches storage.

The host compares observed work against each actual target's runtime harvestWork (the concrete chopped-stump definition overrides its base), counts fresh native placements only, and checks exact physical totals at every sampled change. Two returned-stump receipts, two native-queue receipts, both stump work completions, and all four fresh wood placements are independently paired by source/job identity.

## Cleanup and logs

All six parked background wood stacks3303–3308 return with their exact original counts, cells and rotations; the holder is empty. Scene cleanup restores its captured settings/speed/pawns; the disposable test arena and former Lords are explicitly not reconstructed. Native28868 and controller26524 joined with exit0. Private desktop receipt is Default-only, no switch, timeout, cleanup error or surviving process. Verify retains `protectedChanges: []`; its remaining flags request manual full review, note the absent generic `scenario-observed` event, and identify dynamic Mono fallback messages. Its automatic `not-verified` label is not converted or hidden.

Reviewed the entire Player.log through shutdown and complete HD debug log. The log contains two startup dynamic-library fallback messages, Direct3D refresh/vsync timing notices, Header.png mipmap notice, ordinary profiler tables and allocator statistics. No scene, product, cleanup or shutdown exception is present. The native debug trail agrees with the actual collection/unload sequence and the final successful terminal. Installed text reports rev590 and the running game rev591; the exact bound Assembly-CSharp hash remains `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.

## Closure and retained limitation

Together with the retained native premature-unload baseline, this establishes the implemented F24 work-continuation correction across harvesting, actual grow-zone tree/stump clearing plus sowing, deconstruction and default cadence. The independently reviewed three-file commit selection matches both complete tested C# files, apart from line endings, and is ready for the root's focused staging. F24's received premature-trip feedback can move to resolved with this bounded evidence and the final assembled-mod integration checks tracked separately.

The older v6 run `9d927540ecb04375ab8bb44fbce48d36` remains an **unexplained distinct stranded-cargo observation**: its first91 cloth stayed in inventory, rather than an early storage trip interrupting work. V7 and v8 do not reproduce it. All126 v8 gate samples are medically eligible and temperature spans4.9–12.6C, so neither this success nor the earlier source hypothesis proves what gated that older pawn. Do not label that run fixed, medically explained, or passed. Retain it as a separate follow-up/recurrence lead with its original raw evidence; it should not be silently folded into the now-verified scheduler defect. No broad rerun is recommended absent a concrete new observation or source cause.
