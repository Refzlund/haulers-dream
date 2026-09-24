# F33 independent source and build review

**Decision: no source/build blocker to the selected baseline and candidate native runs.** This is independent review by `f45_build_recipe` on 2026-09-20, not runtime acceptance or F33 closure. I did not change product, fixture or controller files, or execute a game/controller/build.

The actual scanner instance is the correct source of delivery intent: native menu code asks the scanner for its forced job before assigning `workGiverDef` to that result. The revised predicate distinguishes the native Hauling and Construction definitions that share scanner classes. Explicit HaulBuild still wins with the global tether disabled; HaulOnly stays untethered; ordinary unforced work stays untethered. It adds no assignment gate that would defeat an explicitly permitted route. The unchanged `TryBuildHaulOnlyOrder` still supplies false explicitly.

I read the complete four-scene fixture and its host/controller deltas. Native menu scenes invoke the actual provider's offered action in real Layout/Repaint; route scenes invoke the actual executor. Read-only observers follow native ordered/current jobs, QueueNext, blueprint/frame transition, real construction work and completion. Wood totals include ground, actor inventory/carry and frame containers. Cleanup follows the bounded observation interval, so clearing jobs cannot satisfy a preceding continuation assertion. Baseline assertion failures stay failures. The shape-only frame is now nine tiles away, outside native `FindNearbyNeeders`' eight-tile radius; this removes the vanilla batching confounder without changing a product expectation.

**The completion postfix is valid.** I read retained native `RimWorld.Frame.CompleteConstruction`: after `Destroy()`, the native method itself uses `base.Position` for the completion sound and for `GenSpawn.Spawn` of the finished building. The postfix's comparison of the destroyed frame's Position with the owned site therefore remains meaningful. Its separate destroyed-frame, actual wall, prior FinishFrame and work checks are retained.

**A real HaulOnly continuation remains an open runtime question.** The actual route uses a Construction scanner. Native `JobGiver_Work` emergency priority continuation can generate a related Construction job with `playerForced=true`. Scene 3 records and rejects that forced FinishFrame even when HD QueueNext was not called; ordinary nonforced Construction is separately allowed/accounted for. Do not waive that failure or call the entire route repaired merely because the initial delivery JobDef is correct.

Measured selected identities:

| Item | SHA256 / identity |
|---|---|
| Product source `InventoryConstructDelivery.cs` | `6F581C8161ADC9F0E372410DC61A3E5C54DB5C3D321CDD9B3E6923FBA7DFA85A` |
| Fixture `src/DeliveryIntent.cs` | `E5E3DC4C9FFC538703B6B01F192AB336FC71B22FDA8C823FB5EFD84D0E37F044` |
| Host DLL | `20858223CD8B890474A0142B1CE827FB489DC095A10C91B1838B3147703EC242` |
| Host PDB | `9F7FF4B8BF308DD61887F1476E27B55D09D1BE9D349CD73AB072963CCE25B731` |
| Host MVID, read from actual DLL in reflection-only mode | `1fb22920-3634-441a-bca5-c5a02a00b43d` |
| Candidate HD DLL | `A5F7170C61F7472567FE1F945ABB3EE68E732BD3044DE7AC02AD0515FC2330C0` |
| Candidate Core DLL | `DBA6B91FD520654644A15727E51799CDDFAAB662A72F6C4EA0845913929FDFFB` |

All 423 actual product copies match their recorded hashes; all 423 actual baseline inputs match their baseline hashes, with exactly the intended source differing between selections. The retained before source also matches that baseline. Both roles' four product DLL/PDB hashes and shared host hash match `role-selection.json`; all three selected host source/project hashes match the actual build record. I read both complete current build logs: each reports zero warnings and zero errors. These checks establish the selected successful builds; they do not substitute for executing the scenarios.

The reused controller changes only `scripts/runtime-test.ps1` plus its handoff document; the other 18 scripts and About file are unchanged. F33 selects four packages and ten images, retains metadata/private-runtime protection, and explicitly leaves full semantic/log acceptance to independent review. The host accepts only the two descriptive roles and still fails terminal status on any failed assertion. Root's launch adapter and inactive-desktop process ownership are separate existing launch infrastructure, not weakened by this source change.

Required next evidence is the actual baseline/candidate delivery, construction, forced-continuation, material, cleanup and whole-log outcomes. No broader matrix or extra review framework is needed for this source decision.
