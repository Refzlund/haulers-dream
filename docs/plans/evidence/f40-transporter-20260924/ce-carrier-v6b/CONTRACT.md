# F40 CE, carrier lifecycle, corpse policy and native Grave contract

Scope is GH267's already recorded CE/shared-carrier/container obligations. Reuse Core212 and original transporter persistence56/59/52; do not repeat that matrix. This is one new producer/restart pair against HD9739F033…/Core83EB8EB0…, with actual installed CE16.7.3.0 copied privately and pinned in full. Native game, Harmony, HD, CE and fixture are the only active packages. No VF, shuttle or network claim is made here.

## Actual provider and controlled capacity inputs

Pin the whole publicly installed Workshop2890901044 package, its About/LoadFolders metadata and CombatExtended.dll. No provider result or out argument is patched. Confirm the actual hauler has CE.CompInventory and HD's bridge binds the same four-argument Thing overload. `CompInventory.CanFitInInventory(Thing,out int,bool,bool)` computes the minimum of available weight/item weight, available bulk/item bulk and actual stack count. The test observes its actual returned count and CE inventory cache changes, not a substitute policy result.

Two clearly named fixture-only resources supply explicit capacity inputs: `HDTest_F40CEBulky` (Mass1, CE Bulk4) and `HDTest_F40CEMassless` (Mass0, CE Bulk4). Neither mutates a shipped def or provider stat. The ordinary clothed human's actual available values are recorded; remove generated equipment/apparel as explicit scene setup if needed and assert healthy native capacity. Require a positive CE fit strictly smaller than native mass fit and input12, rather than assume a pawn's capacity. Native room temperature must settle through its real cache tick before actors/jobs; preserve the established forecast/warmup contract and medical guards.

V4 supplies the native `ResourcesRaw` category on both test defs. Before the carrier order, and again before the separate massless order, require both defs' actual EverStorable(false), actual parent-aware stockpile acceptance and StoreUtility selection of a cell in the real stockpile. The failed v3 producer remains retained: its uncategorized defs were correctly unstorable, so their supported fallback floor placement could not satisfy this physical-storage contract.

## Producer: real pack-carrier visit and exact native checkpoint

Create one actual player pack animal, with bulky12 in its native inventory, and one actual human. Issue the real bulk-carrier order. The first pull must place the actual positive CE-fitting quantity in inventory, tagged; actual CE remaining fit then reaches zero and the next real pull places the remainder in hands, untagged. Require exact transfer identity/count/owner observations, source conservation12, no leaked retry, original job ID and native job success. Adequate ordinary storage keeps this witness about intake/recovery.

The new carrier visitCargo/handTail fields exist only between synchronous transfer→finalize→EndJobWith. Therefore this fixture explicitly instruments that **exact native lifecycle boundary**, rather than claiming a GUI pause can catch it. A one-shot `JobDriver.EndJobWith` PREFIX, gated to the exact owned carrier driver/job, `Succeeded`, real inventory/hands and finalized fields, calls native `GameDataSaveLoader.SaveGame` on the main thread. It never skips EndJobWith, changes arguments, delays the native job, writes fields or fabricates serialized state. Compare complete state immediately before/after saving; retain original bytes, record and XML. Producer then returns normally and must complete its ordinary recovery before its result can pass.

Native source proof: carrier assigns visitCargo/handTail/count before jumping to instant finalize, which invokes EndJobWith. Native `JobDriver.TryActuallyStartNextToil` has already set current toil4, ticksLeft and next flags. `ExposeData` serializes these values and reconstructs toils at PostLoadInit. Native `DriverTick` sees the restored instant last toil and advances beyond it, naturally ending Succeeded and running the same finish actions. The save records real current state at that call, not a manually invented job continuation. This bounded claim is explicitly distinct from an ordinary user save between frames.

Queue one real newer native Wait before the hand transfer. Preserve it in the exact checkpoint. On both producer continuation and restart, native carrier cleanup may append only owned-cargo recovery behind that newer order. Observe all actual job starts with Notify_Starting PREFIX and ends before cleanup using immutable IDs. Do not infer order from mutable pooled Job objects.

Native EndCurrentJob(Succeeded) may insert its mandatory one-tick Wait_MaintainPosture before the queue. V4 identifies that wrapper only with its exact JobDef/driver, expiry1, observed preceding Succeeded cleanup and actual native EndCurrentJob stack frame. Preserve each ID/event, and exempt only those positively identified wrappers from the queue-order comparison. The original Wait must still precede every other actual start, including every recovery/unload, and a real HD inventory recovery start is required. No arbitrary Wait or other job is ignored.

## Consumer: exact reload, productive recovery and three remaining provider seams

Load the exact original passing producer bytes in a fresh process. Before its first tick, require identical actor/carrier/cargo identities, ownership, tags/Keep, current carrier driver fields, toil flags and queued Wait. At native savedTick+1, the original carrier must end by its native lifecycle; the newer Wait must run first, then real recovery stores all12, with no remaining measured carried/inventory stock and300 stable ticks. Never remove/reconstruct driver fields after load.

Then run the bounded remaining phases using explicit scene inputs:

1. Fill real CE bulk with kept bulky ballast and place massless3 in an actual pod. Assert native Mass0, CE Bulk4, actual CE fit0 and positive source count. The real transporter order must use hands, store all3 and finish with pulled3/delivered3. Ballast remains kept; no CE cache/fit/result overrides.
2. Four small real animal corpses, each produced by native Pawn.Kill and physically moved into a pod one at a time, cover exactly the backpack policy branches: off+ordered→hands; on+ordinary automatic→inventory; on+DisposalOnly automatic→hands; on+DisposalOnly ordered→inventory. Use an otherwise empty fitting hauler and require actual CE fit>=1 before every case, so capacity cannot mask policy. Automatic work must originate from the actual WorkGiver HasJobOnThing/JobOnThing and native StartJob without playerForced; ordered work uses the real synced transporter command. Observe actual transfer owner immediately after transfer, without rewriting out arguments.
3. These same corpse phases use actual enabled native Graves whose filters accept the corpse, with no competing cell storage. Require native StoreUtility selection of the actual Grave as an IHaulDestination (invalid cell, real building target), actual container-owner transfer, same corpse identity, original job Succeeded and delivered1. A floor drop, later recovery or shelf cell cannot satisfy this callback witness. No additional storage provider is needed.

Each phase has finite tick/wall limits, exact conservation by identity and quantity, actual source/owner and original-job end assertions, a healthy/environment guard and settled physical evidence. Only named cases run. Preserve any failed native capture and diagnose its specific cause before changing product or oracle. Root owns Prepare/launch/Verify; author supplies compiled host, full selection/copy pins, exact source diff and concise handoff first.


## V5 native job observation correction

V4 physically stored12 but failed its queue assertion because its stack-frame name test could not identify native EndCurrentJob behind Harmony wrappers. It also missed HaulToCell.Notify_Starting, which overrides the base method. V5 snapshots every owned Pawn_JobTracker.StartJob input before possible pooling and records return admission using immutable IDs (current/ended/queued). The ordering check stays conservative over all those native start requests.

Only the exact one-tick Wait_MaintainPosture with its exact native driver may be exempt. It must be entered within the same tracker's scoped EndCurrentJob(Succeeded,startNewJob:true), after the actual predecessor cleanup, with native branch arguments cancelBusyStances=false/fromQueue=false/addToJobsThisTick=false and a stationary pather. Scope restoration uses a nested-safe void finalizer, not stack strings. Original queued Wait must still precede every actual hands/HD recovery entry; it and the original carrier must succeed, all12 must be stored and remain stable300. Product, definitions, original save oracle, and controller remain unchanged.


## V6 consumer-only pod-toggle correction

The v5 producer56b55 native tick194 checkpoint and failed consumer9a8 remain preserved. V6 changes only the consumer OrderPod helper: actual SetUnloadSynced precedes explicit and automatic commands; it observes the active flag and exact live StartBlock(None) before admission. A completed empty hold clears intent, so every fresh corpse phase also uses the real toggle. No saved flag is invented, and no product gate is bypassed. Automatic Hauling priority remains the same scene input and is set before its admission observation. Original saved-state equality, job/queue/conservation/CE/Grave oracles remain unchanged. The controller already accepts a passed predecessor producer without requiring equal host identity; its exact save/record/provenance checks remain byte-identical. Run only RESTART from the retained56b55 original save, not a replacement producer.


## V6b exact predecessor host binding correction

The v6 handoff incorrectly said producer-host equality was absent: the later original-producer image loop does require it and root Prepare refused safely. V6b changes only that loop. Only original56b55 with v5 host0EDB20F2/MVID514d25c8 uses the retained predecessor host pin. All ten other original images and native save/record/pass guards stay unchanged. Normal selected consumer host remains94BC2F22/MVID870c1793 everywhere. No source rebuild: exact v6 compiled source and host are reused. V6 source/selection/refusal remain retained.
