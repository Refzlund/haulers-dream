# F35 / C007: haul after finishing a wild animal

**Implemented and compiled; native baseline/candidate evidence remains pending. No report closure.** The reporter asked whether haul after slaughter works when finishing wild animals, reported that it did not, and saw no errors. The comment does not identify the finish-off provider.

## Actual cause and supported providers

Existing SlaughterHaul.cs hooks only native JobDriver_Slaughter and JobDriver_Hunt. The two installed providers instead use their own JobDriver_FinishOff and DoExecution(Pawn slayer, Pawn victim). Neither reaches either existing HD hook. Native JobDriver_Execute is prisoner execution, and native JobDriver_Kill is a combat loop; patching those would not address these finish-off actions.

Inspected installed selection (full identities in inspected-binaries.json):

- Allow Tool, Workshop 761421485: LoadFolders.xml selects root plus v1.6; the JobDef FinishOffPawn names AllowTool.JobDriver_FinishOff. DLL SHA-256 `471F01CB…`, assembly 3.6.0.0; About description says 3.14.0. HugsLib is its declared dependency.
- Keyz' Allow Utilities, Workshop 3524716849: Common plus 1.6; KAU_FinishOffPawn names KeyzAllowUtilities.JobDriver_FinishOff. DLL `F789A178…`, assembly/About version 1.3.8.0. The strip-and-finish subclass calls the same base DoExecution once. Harmony is its declared dependency. Optional compatibility folders follow its original LoadFolders.xml; no package bytes were changed.
- Native Assembly-CSharp `5CF1B5BE…`; relevant provider/native decompilations are retained here as `.cs.txt`.

Both provider final toils start a melee verb and execute the victim. A success-only job finish hook is insufficient: native JobDriver.TryActuallyStartNextToil can defer instant-toil completion while a melee stance is busy; DriverTick checks the provider's dead/despawned-target failure first on the next tick. An actual completed kill can therefore end Incompletable. This ordering is supported by the actual source, but its frequency/end condition must be observed in the real witness.

The new, optional Harmony integration patches only the two exact declared DoExecution(Pawn,Pawn) methods. After the provider returns it requires the same current driver, slayer, job and target, plus a dead animal. It passes factionless wildlife to the existing wild-kill toggle and player-owned animals to the existing tamed-slaughter toggle; other factions' owned animals and non-animals are excluded. It reuses SlaughterHaul.TryAppendHaul, so eligibility/map policy, forbidden state, reachable storage, queue-at-end and duplicate-haul checks stay in one place. No provider toil, damage, execution, hunt, slaughter, or global death routine is replaced. Keyz's inherited strip variant reaches the base hook once.

Forbidden behavior is deliberate: native Pawn.Kill can forbid an undesignated corpse outside Home. Allow Tool optionally un-forbids adjacent things **inside** DoExecution; HD observes the resulting state after that policy has run. Keyz's inspected method does not un-forbid. HD does not silently un-forbid either provider's corpse. Consequently an outside-Home Keyz corpse that remains forbidden is expected to stay unhauled. Storage absence likewise remains a normal no-op. This is a material player-facing condition, not proof that finishing is still unhooked.

## Change and isolated compile

The only product edit is Source/HaulersDream/SlaughterHaul.FinishOff.cs, SHA-256 `24C3507A8B82CD1A2F7176EEAE5D563E3DD1134B452C429E1D316986DBE265CF`. Existing SlaughterHaul.cs and Core policy were not changed.

build-product.ps1 reuses the accepted F07 build operation with 461 byte-pinned frozen F07 inputs plus this new file. All outputs/intermediates live under `%TEMP%/haulersdream-f35-product-build-20260920`; its deploy destination is the same verified absent guard path. Actual PID 15268 joined exit 0: zero warnings/errors, 462 source/copy pins unchanged, deployment guard absent. Raw stdout/stderr, selected inputs and product-build.json are retained. HD output is `098F9174A394F44EB1C854368C24A9E9E7B3C79FCCDB39CAB11A4AE4998403D9`; Core is `AE5861E05113E4CAC0ED6BBB84CC3B4D46B656F55993FB18C432ED0FE1487777`. Use the corresponding built pair. No frozen F07 file was overwritten. Runtime staging must copy mod content and these outputs, not admit the build root's Source/obj tree as package content.

## Smallest useful native acceptance

Reuse the existing private native host; root launches only through the inactive-desktop launcher. No new controller/provenance framework is needed.

1. For **each actual provider separately**, compare existing F07 product (no new hook) with the candidate on a downed factionless animal, reachable accepting corpse storage, an eligible colonist and a non-forbidden corpse outcome. Invoke the provider's real offered order/work giver, let its native toils kill, and retain actual driver/job/end condition, corpse identity/forbidden flag, queue before/after, and native haul/destination. Include an existing queued non-haul order: candidate must append exactly one corpse haul behind it and actually deliver that same corpse without replacing existing work. The retained baseline should show the missing follow-up, not relabel it a pass. Use Home for the Keyz positive; Allow Tool's outside-Home positive can exercise its actual enabled un-forbid setting.
2. In the candidate's same bounded session, disable only the wild toggle while leaving the tamed toggle enabled and finish another wild animal: no added corpse haul. Retain a minimal tamed/wild classification check through a provider-supported colony-animal order if that provider actually offers one; do not manufacture an otherwise disallowed player-animal order merely to cover a branch. The original native slaughter path is unchanged.
3. One natural outside-Home **forbidden** finish (Keyz, or Allow Tool with its un-forbid setting disabled) must preserve the forbidden flag and add no haul. Observe one cancellation before execution: no corpse/follow-up. These are narrow boundary witnesses for this integration, not a repetition of the existing shared-haul policy suite.

Inspect complete raw events and logs, native material identity/queue evidence, and cleanup before acceptance. The provider-less game must still load normally; the patch's Prepare method skips absent providers. Actual runtime must confirm target patch installation and the observed finish ordering. Multiplayer and unidentified third-party finish-off providers are not claimed by this change. The report remains open until reviewed native evidence supports the intended behavior.
