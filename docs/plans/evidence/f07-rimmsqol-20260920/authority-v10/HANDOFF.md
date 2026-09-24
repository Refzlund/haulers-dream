# F07 command authority correction and complete producer precondition audit

24 September2026. V9 producer `5b7c58a4ea63471188a320682b2467bb` is preserved as failed. It passed actual menu capture and persistent-window preservation, then failed before productive ticking. Events93–94 show that undrafting synchronously ended Wait_Combat and started automatic BulkHaul job10 with `playerForced=False`, `workGiverDef=HaulGeneral`. The debug log separately records the subsequent forced sweep. The exception came from the fixture's post-action `Jobs().Single(j => j.workGiverDef == definition)`, not native option discovery.

## Concrete product cause and narrow correction

`Patch_TryTakeOrderedJob_BulkHaulTakeover` previously bypassed its takeover only for explicitly queued nearby commands. For an immediate command, `BulkHaul.TryTakeoverSecondOrder` appended the incoming primary to the running automatic bulk job and returned success. The incoming nearby job's permission/map identity was lost. That is meaningful: drafted continuation and live command permission checks depend on the identified order. Continuing ordinary automatic work is not evidence that the new explicit command retained its authority.

`takeover.diff` changes the bypass to every `NearbyHaulCommand.IsIdentifiedOrder(job)`. The native immediate/queued path now owns that complete incoming job. Ordinary HaulGeneral second-task takeover is untouched.

The actual native `Pawn_JobTracker.TryTakeOrderedJob` also returns success early when `curJob.JobIsSameAs(pawn, incoming)` is true. Native `Job.JobIsSameAs` compares definition, verb, bill and target fields, not WorkGiver, forced permission or map identity. `equality.diff` adds one exact nearby-command prefix: incoming identified nearby command versus current ordinary work returns false. Same-authority commands and every ordinary incoming order retain native comparison. This also protects the legitimate native single-haul fallback; replacing only the bulk driver's comparison would miss it. No global equality replacement or automatic scheduling change is introduced.

Actual native bodies are retained in `%TEMP%/hd-f07-20260920/native-api/Verse.AI.Pawn_JobTracker.cs` (TryTakeOrderedJob891–961) and `Verse.AI.Job.cs` (JobIsSameAs652–676). Current two changed source hashes: HarmonyPatches `BE7676877A49553C33EC2CBC80BAD446D914DF34EBDA44B6095A5889DE4ED30E`; WorkGiver_HaulNearby `5F992630AAFDCED52CB76DAD0B017800DB1C1FC63336749AA20C88B1FF0DDA1C`. Original files are preserved under `before/`.

## Fixture correction and remaining-stage audit

`fixture.diff` is against preserved `src/RimmsCommand.v9.cs.txt`. The v9 automatic-current trigger is intentionally retained as the first productive regression, not avoided by disabling Hauling. It requires an actual native automatic BulkHaul/HaulGeneral job after undraft, records its integer ID before native pooling can reset the object, invokes the actual offered nearby action, and requires an identified distinct command plus InterruptForced end of the original job. Native `JobIsSameAs` probes use inert target-identical Job objects, never started/reserved, to require ordinary-equality preservation, authority distinction and same-authority preservation. The actual command then has to finish pickup and unload through native ticking. Offered options, before/after current/queued jobs, full inventory roster and feedback are emitted before any dispatch assertion.

The audit identified an independent overstrict personal-stock assertion before execution. Keep is explicitly a def-level quantity; `CompHauledToInventory.GetHashSet` can retag same-def stacks and `DepositSwept` can merge into an already tagged stack. The initial seven-unit Thing is not promised permanent identity or stack size. The corrected fixture still requires exact physical total conservation, keep7 and at least the protected inventory quantity on every update. Settled stages require exactly that protected quantity. Every state records sorted inventory IDs/counts. The checkpoint binds the complete actual saved inventory roster and requires exact loaded equality before any fixture mutation; it no longer demands the original Thing survived every valid merge. After interruption, the observed retained amount becomes the protected baseline for the next scoped command and restart.

Other preconditions were checked together:

- Native ordinary hauling is enabled only to prove its menu coexistence and the observed auto→explicit regression. It becomes priority0 before drafted scenes. Drafted command permission comes from the real RIMMS editor; no WorkGiver flag is hand-written.
- Two five-unit stacks discriminate bulk pickup. Completed stages leave all material in the receiving stockpile except protected inventory. The single-stack fallback is offered only after that settlement, so no leftover eligible neighbor is manufactured by an earlier unfinished scene.
- The queued test uses a genuinely non-idle native Goto to a different distant cell, with queue=true at the existing command entry. Permission is revoked after actual enqueue and before native admission; its original predecessor must complete naturally. The rejected order's floor material remains part of the later physical census.
- Interruption waits for real inventory growth while the Bulk job is still current; the120-tick pickup delay provides an observable partial-load interval. Replacement Goto must cause InterruptForced, add no successful drafted delivery and preserve every unit. No manual productive toil is invoked.
- The fresh save waits for the candidate's generated Delivery to physically hold a piece away from storage. It records actual job/WorkGiver/map, manifest/transit, held piece, full inventory and RIMMS config. Restart observes these before changes and requires complete native delivery with the protected amount retained. There is still no accepted checkpoint; v10 producer and its bound restart must both run.

## Exact builds and selection

`build-product.ps1` selected all461 original F07 inputs by their retained hashes, replacing exactly the two authorized source files. It recovered old runtime text from the already recovered Product and the three unrelated current F20 source differences from their retained original copies. No F20, H&H or F35 implementation was folded into this product. `product-inputs.json` records every selected source/copy/prior/current hash; all selected files remained unchanged after build. Build passed with zero warnings/errors in20.94s; deployment remained disabled.

Clean package: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-authority-20260924/Product`. Its99 files preserve all95 nonbinary F07 runtime inputs exactly; only the two DLLs and PDBs are replaced. Product output and successful reflection-only metadata receipts: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f07-authority-20260924/`.

| Selected image | SHA-256 | MVID |
| --- | --- | --- |
| HaulersDream | `A20FBD9FB139668D0BD50DBE9496B75C728AB7023CC7277BA24388FE148E8C0F` | `f30c55c6-9106-4cb9-99e9-616c22677778` |
| HaulersDream.Core | `1A30CF3CBFC96C7BD0980CBE46482C07A58519F4D31FBD9D269A830A6AE9E332` | `36bbf3f3-9ff2-4186-857a-166bf81f74f9` |
| Hostv10 | `8E304DE9AC32430D2FDE81BC6DC5DA069A8BB7647F6E73AAD29391AB82708DF4` | `d541ec8a-b058-4fa2-90d6-b9ed505c5379` |

Host source `A4EEEB0664723B2FFFC50F014543E4F2239D33201819D7B667B460A7F953C715` built once with zero warnings/errors in4.32s. Output `%TEMP%/hd-f07-20260920/build-v10/Assemblies`; PDB `D56B2D8F57E53E5DFF80E910F1FD7C3A490FD8944B62359790A5D0E417C612C3`. Bootstrap/project are unchanged from v9.

Use the separately preserved `controller-v10/scripts/runtime-test.ps1`, hash `CD45E574DCC88068B8194C2BB97A0205AA3962021160CCDBFAEE4E274BE4ED2B`. Its only change is the four exact HD/Core pin occurrences, retained in `controller.diff`; its parser reports no errors. `launch-v10.ps1`, hash `83435173C97866C8DB2C9B2AE5CEED537099E64C5909D1A48B9DE43F4A3DBA82`, only selects that new controller. Original controller/launcher and old product remain untouched. Root still owns Prepare, inactive-desktop launch, join, copied evidence, Verify and independent native acceptance. None was performed by this author.
