# Hauler's Dream — Mod Compatibility

How Hauler's Dream (HD) coexists with other mods, from code-level investigation — decompiled
assemblies, **cloned mod source**, and XML patches cross-checked against HD's own patch surface —
across a real ~430-mod load order (originally a 49-mod order; expanded for the "Haul Urgently" and
"modded mechs / animals / robots" passes).

## How HD is built to be compatible

HD hooks a small set of **vanilla** methods and relies on **idempotent, tag-driven re-issue** rather
than owning the job pipeline:

- Yield pickup: a prefix on `GenPlace.TryPlaceThing` (the 9-arg out-overload) + a postfix on
  `GenLeaving.DoLeavingsFor`. Work type is inferred from the pawn's vanilla `JobDriver` class, so HD
  does **not** replace `JobDriver_Mine` / `JobDriver_PlantWork` / `JobDriver_Deconstruct` / etc.
- Bulk haul: a postfix on `WorkGiver_HaulGeneral.JobOnThing`.
- Unload: a custom job + think-tree triggers (a `JobGiver_Work.TryIssueJobPackage` postfix, a
  `GameComponent` backstop, a gizmo). Items are tagged in a `CompHauledToInventory`.
- Haul-to-stack: a postfix on `StoreUtility.TryFindBestBetterStoreCellFor`.
- Pawn eligibility: scoop, bulk-haul, and auto-unload all gate on **one** predicate
  (`YieldRouter.IsEligible` → `EligibilityPolicy`): humanlike colonists, or colony mechs when
  `allowMechanoids` is on. So whatever HD loads into a pawn's inventory, HD can also unload it —
  the load and unload halves are provably symmetric. Non-humanlike, non-mechanoid pawns (animals,
  modded robots) are **never** loaded by HD; they keep vanilla single-stack hauling untouched.

Because every load is **tagged** and re-found from the tags, any external interruption (a draft, a
forced job, a mental break, another mod cancelling the job) is self-healing: a trigger re-issues the
unload. And as a hard backstop, a **red alert** fires if a pawn is ever left holding items it cannot
put away (see the in-game "Cannot unload inventory" alert).

## Flagged mods in the investigated load order

### Real overlap — works, but worth testing
- **Common Sense** (`avilmask.commonsense`) — the only genuine functional overlap. It runs its own
  parallel unload system on the **same** vanilla `JobGiver_UnloadYourInventory` node, cross-tags every
  item entering a pawn's inventory (`ThingOwner<Thing>.TryAdd` postfix → its `WasInInventory` flag),
  and its `Pawn_JobTracker.CleanupCurrentJob` transpiler ("put back to inventory", **on by default**)
  returns an **interrupted carry** to the pawn's inventory instead of dropping it. All of this
  *composes* (no crash): CS's unload only triggers for items **you** marked via its gear-tab button
  (HD never sets that flag), and an interrupted HD haul/unload that lands back in inventory is still
  HD-tagged, so HD re-issues. **Test:** mark an item via CS's gear tab while a pawn also carries
  HD-scooped stock (no deadlock); interrupt an HD haul mid-carry (item returns to inventory, HD
  re-unloads, nothing stranded). No load-order requirement.
- **Cook-ingredient sort: HD now cedes to CS.** Both mods transpile the *same* `SortBy` call in
  `WorkGiver_DoBill.TryFindBestBillIngredientsInSet_AllowMix` to reorder a cooking bill's ingredients (CS
  by spoilage; HD's optional `cookSpoilingFirst` (default on, itself spoilage-first) and `cookMostStockFirst` (default off)). Two
  transpilers cannot both rewrite one call, so previously load order decided the winner, and when HD won,
  CS logged a one-time yellow `[Common Sense] ... patch 0 didn't work` and its default spoilage sort went
  silent. HD's transpiler now stands down whenever Common Sense is installed (a `Prepare()` gate), so CS's
  sort always applies cleanly on that non-batch cook path; HD's own batch-cook ingredient picker still honors
  the cook keys (it is CS-immune by design). (Same cede philosophy as the DoBill flow above.)
- **Red errors while running both are not HD-caused (verified by cloning CS).** Two independent code-level
  passes found no HD-caused uncaught exception in the interaction. The once-suspected "started 10 jobs in
  one tick" churn is impossible: HD's bulk-haul job leaves `targetB` at `IntVec3.Invalid` (-1000,-1000,-1000),
  so Common Sense's opportunistic-haul distance gate never passes and CS just skips HD's job. Every shared
  Harmony seam is condition-disjoint or cedes. HD tags any error it *is* responsible for with its `HDGuard`
  signature, so a red naming HaulersDream is HD's to fix and one that does not (for example a `[Common Sense]`
  frame) is not; check the stack trace for that signature before attributing reds.

### "Haul Urgently" — Allow Tool & Keyz' Allow Utilities (verified by cloning both)
- **Allow Tool** (`unlimitedhugs.allowtool`) and **Keyz' Allow Utilities** (`keyz182.allowtoolutils`)
  — both implement "Haul Urgently" as `WorkGiver_HaulUrgently : WorkGiver_Scanner` whose
  `JobOnThingDelegate` defaults to `HaulAIUtility.HaulToStorageJob`. That is a plain single-stack
  vanilla haul which **never** routes through `WorkGiver_HaulGeneral.JobOnThing` — the method HD's
  ordinary bulk-haul postfix patches — so historically an urgent haul moved one stack per trip.
- **HD now sweeps urgent hauls too** (`Patch_HaulUrgently_BulkHaul`): a soft-dependency postfix
  resolves both `KeyzAllowUtilities.WorkGiver_HaulUrgently` and `AllowTool.WorkGiver_HaulUrgently`
  by name (no compile-time ref; `Prepare()` skips the patch entirely when neither mod is loaded) and
  runs the SAME `BulkHaul.TryBuildBulkJob` conversion HD uses for vanilla hauls — so an urgent haul
  now picks up the nearby cluster and makes one storage trip. It inherits all of HD's bulk-haul
  gating (the `bulkHaul` setting, eligibility, map gate, carry ceiling, trigger): with bulk-haul off
  it stays vanilla single-stack, exactly as before. Container/genebank urgent jobs (not `HaulToCell`)
  are declined and keep their vanilla flow. (Verified by decompiling Allow Tool 1.6 + cloning the Keyz
  source; both confirmed to share the identical `WorkGiver_HaulUrgently` shape.) Shift +
  "Haul Urgently" can still cancel an in-progress HD job via `CheckForJobOverride`; HD re-issues
  (self-recovering). Four nuances:
  - **PUAH co-install caveat (the historical "acting funny"):** both mods carry a compat handler that
    name-detects the literal type `PickUpAndHaul.WorkGiver_HaulToInventory` and rebinds urgent-haul to
    PUAH's bulk-into-inventory giver. That rebind — PUAH-driven — is the old "PUAH + Haul Urgently
    acting funny." HD ships no `PickUpAndHaul.*` type (its assembly is `HaulersDream`), so HD is
    **never** detected and the rebind never targets it. (HD is a PUAH *replacement* — running both
    together is unsupported; if you do, urgent-haul becomes a PUAH job, independent of HD.)
  - **"Do Not Haul" is honored automatically:** Keyz' `KAU_NoHaulDesignation` postfixes
    `HaulAIUtility.PawnCanAutomaticallyHaulFast`. HD's bulk-haul sweep calls that same method on every
    candidate, so Do-Not-Haul items are excluded from HD's sweep with no HD-side code.
  - **Swept "extra" / stale marker (cosmetic):** an urgent-*designated* item near another haul can be
    picked up as an HD bulk extra — it still reaches storage, just via HD's consolidated unload. If HD
    then unloads it into a container/shelf, Allow Tool leaves a cosmetic, self-healing urgent marker (it
    patches only `Toils_Haul.PlaceHauledThingInCell`); **Keyz patches the container toil too, so Keyz
    has no gap.** No HD change needed.
  - **Both wrap the same placement toil, in load order (harmless, but it shows in stack traces):** HD and
    Keyz each postfix the toil *factory* `Toils_Haul.PlaceHauledThingInCell` and wrap the returned toil's
    `initAction` — HD to bound haul churn, Keyz (`Toils_Haul_Patch.cs:11-26`, only while its Haul Urgently
    feature is enabled) to clear its urgent designation. Both wrappers are
    pass-through (each simply calls the action it wrapped), and **neither declares a `[HarmonyPriority]`**,
    so which one ends up nested inside the other depends on mod load order. That is deliberate: nothing
    either wrapper measures is affected by the other, so pinning an order would encode an untested claim
    for no observable gain. The only visible consequence is diagnostic — a crash during a haul placement
    shows **both** mods' `initAction` frames whatever the actual cause, so read the frames *above*
    `Toils_Haul…b__0`, not below it. (HD's own exception breadcrumb says the same thing in words: being on
    the call stack is not blame.)

### Can interrupt HD jobs (self-recovering)
- **Automatic Stump Chopping** (`arylice.rimworld.automaticstumpchopping`) — prepends a
  `CutPlant(stump)` job per felled tree; a big forest harvest can briefly front-load a cutter's queue,
  but it only prepends (never clears), so HD's queued unload/route work resumes.
- **Better Autocasting for VPE** (`dev.tobot.vpe.betterautocast`) — auto-casts psycasts on an interval and
  can force-interrupt the current job to cast. It patches `CompAbilities.CompTick` / ability getters — a
  surface HD does **not** touch (zero patch overlap, verified by cloning). An interrupt mid-HD-haul is safe
  *by construction*: HD's carried loot lives in **tagged inventory** (not the carry tracker, so VEF's
  `keepCarryingThing` cannot drop it), and HD's finish actions re-queue the unload + release claims on the
  forced-end path — the haul simply pauses for the cast, then completes. If you dislike that pause, add HD's
  job defs (`HaulersDream_BulkHaul`, `HaulersDream_UnloadInventory`, optionally `HaulersDream_BillPrepGather`
  / `HaulersDream_BatchCraft`) to Better Autocast's in-game **Blocked Jobs** list. (This safety generalizes:
  any mod that force-ends a pawn's job is handled the same way.)

### Job-selection & pathfinding mods — composes
- **Perfect Pathfinding** (`jp.perfectpathing.updated`) — forces the accurate A* heuristic for genuinely
  optimal paths. HD only *consumes* `map.pathFinder.FindPathNow` (for its hybrid pickup ranking + en-route
  path checks), so it inherits PP's accuracy automatically — HD reads, never re-implements, pathfinding.
  Inert on a no-PP order (HD's path calls are then plain vanilla). No conflict.
- **While You Are Nearby** (`PureMJ.MjRimMods.WhileYouAreNearby`) — postfixes
  `JobGiver_Work.TryIssueJobPackage` to swap the chosen job for a nearer-equivalent one. HD's two postfixes
  on that same method now run **last** (`HarmonyPriority(Priority.Last)`), so HD reacts to the *final* job
  While You Are Nearby picked instead of racing it.
- **While You're Up / Jobs of Opportunity** (`CodeOptimist.JobsOfOpportunity`) — opportunistic
  haul-on-the-way; it transpiles `Pawn_JobTracker.TryOpportunisticJob`, a **different** seam from HD's
  work-scan postfix, so the two don't collide. (HD *replaces* this mod's role — running both is redundant,
  not harmful.)
- **Rust / native job-assignment mods (for example Celeritas Smart Pawn).** A mod that scores *which pawn
  takes which job* and injects its pick at the front of a work giver's candidate list sits at a different
  layer from HD, which upgrades *how* a chosen haul executes (a postfix on `WorkGiver_HaulGeneral.JobOnThing`).
  HD only reads the candidate/haulable lists such a mod injects into, so there is no shared patch target: HD
  simply bulk-upgrades a haul the other mod picked, and its self-healing tagged unload plus anti-churn guards
  bound any friction to extra trips, never a crash or a stranded item. (Celeritas Smart Pawn itself has been
  removed from the Steam Workshop and is closed-source native Rust, so it cannot be regression-tested; a crash
  originating in its own FFI engine is attributable to it, not to HD, via HD's `HDGuard` error signatures.)

### Threading and performance mods (compatible; HD is thread-safe on its hot paths)
- **RimThreaded - Continued** (`LuniX`, 1.6) parallelizes only particle simulation and drawing,
  background-thread RNG, and off-thread sound, with **no shared patch targets** with HD's hauling/job code;
  **RimSmooth** (1.6) is 26 single-threaded perf tweaks (caching, tick throttling, dictionary lookups).
  Neither threads the per-pawn WorkGiver/JobGiver scan, so HD's job code runs single-threaded under both:
  **compatible by construction.** (The classic `cseelhoff` RimThreaded *does* thread pawn AI across pawns,
  but it is 1.4-only; it is the reference threat model for the note below.)
- **HD is already thread-safe where it counts,** by design: its hot scan-path scratch is `[ThreadStatic]`
  and its cross-pawn arbitration tables (the anti-churn guards, the cache registry) are `lock`-guarded,
  because a threading mod does not auto-fix a non-whitelisted mod's own statics. As forward-insurance against
  a future 1.6 AI-threading mod (for example RimMT, if it threads AI), two more per-tick memos were hardened
  to match their already-`[ThreadStatic]` siblings: `HaulToStack`'s stack-cell memo is now `[ThreadStatic]`
  (like `BulkHaul`'s plan cache) and the yield/haul job-def memo is a `ConcurrentDictionary`. Both are zero
  behaviour change single-threaded. A few genuinely cross-pawn structures (self-pickup claims, the load
  ledger, the inventory-tag re-heal) stay main-thread-scoped: latent only under a full pawn-AI-threading mod,
  documented for that day rather than locked pre-emptively.

### Smarter Construction (`dhultgren.smarterconstruction`) — a crash HD now contains (issue #235)
**Symptom.** Every colonist wanders idle. Nobody tends wounds, nobody hauls, nobody cleans — but they
still eat and sleep normally, and a **forced** (right-click → prioritise) order still works. Mining a
vein by force produces no steel because the yield is never hauled. It reads as mass laziness, not as an
error, and the log fills with `NullReferenceException`s that name neither mod obviously.

**Cause (two halves, neither of them HD's).** Smarter Construction postfixes
`WorkGiver_ConstructFinishFrames.JobOnThing` and, for any wall-class frame it wasn't forced onto, calls
`ClosedRegionDetector.WouldEncloseThings`, which dereferences two of its own `MapComponent`s without a
null check (`EncloseThingsCache.GetCache(target.Map).GetIfAvailable(…)` and
`ClosedRegionCreatedByAddingImpassable(target.Map.GetComponent<WalkabilityHandler>(), …)`). When either
is absent, that throws. The second half is vanilla's: `JobGiver_Work.TryIssueJobPackage` wraps the
per-work-giver **scan** in a try/catch, but the **tail call** that turns the winning target into a job
(`scannerWhoProvidedTarget.JobOnCell/JobOnThing`) sits *after* that try/catch with no guard at all. So
the throw escapes the whole work think-node, RimWorld's priority sorter catches it and skips that node
on every scan, and the pawn is left with no work while food/rest (separate nodes) and forced orders (a
separate path) keep working — exactly the symptom.

**What HD does now.** HD already had a guard on that method; it now (a) reports the fault with the
offending mod named, (b) **contains** it, so the pawn merely finds no work that scan instead of losing
work altogether, and (c) after three faults from the same work giver, **switches that one work giver
off for the session** and raises a **"Work giver disabled by another mod's error"** alert naming
it. Everything else keeps running. While Smarter Construction is the mod being switched off, **wall
frames stop being finished on their own** until you update or remove it — but you can still order one by
hand: a right-click **"Prioritize constructing"** issues a single job normally, because RimWorld builds
that order directly (`FloatMenuOptionProvider_WorkGivers`) without consulting the check HD switched off,
and Smarter Construction's own crash is skipped on a forced order anyway. The colonist just won't carry
on with it afterwards. The list clears when you restart the game.
HD never disables one of its own work givers this way — its own bugs stay loud. The fix is general: it
covers any mod that throws from any work giver's `JobOnThing`/`JobOnCell`, not just this one.

The rest of the Smarter Construction overlap is unchanged and fine: its destructive/cancel paths are
gated on `!playerForced`, and HD's construction tether + delivery jobs set `playerForced = true`, so
they are immune to them. This failure is in its *autonomous* (`!forced`) branch, which is why it is
listed here rather than under "composes".

### Modded drugs and the drug policy — an error HD now contains (issue #232)
**Symptom.** Right-clicking a colonist onto a modded alcohol — reported for 「中性私酿」 from *Rimsenal
Xenotype Pack – Harana* — logs `Error in FloatMenuWorker FloatMenuOptionProvider_PickUpItem:
System.ArgumentException: Value does not fall within the expected range.` and the "Pick up" options
never appear. The trace stops at `JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory` and names
Hauler's Dream's patches on it.

**OBSERVED (decompile-verified).** The throw is RimWorld's own, and it happens *before* any HD code.
`DrugPolicy`'s per-`ThingDef` indexer walks its entry list and, on no match, ends in a bare
`throw new ArgumentException();` — with **no message**, which is why .NET supplies that otherwise
baffling sentence and nothing in the error names a drug, a colonist or a mod. It is reached from
`Pawn_DrugPolicyTracker.AllowedToTakeScheduledEver` → `CurrentPolicy[thingDef].allowScheduled`, which is
the **second clause** of `ShouldKeepDrugInInventory`. That predicate has two callers, neither of which
guards it: `FloatMenuOptionProvider_PickUpItem.GetOptionsFor` (the reported one — the throw aborts the
float-menu build, so the options are lost) and `JobGiver_DropUnusedInventory.TryGiveJob`, which runs it
for every drug in the pack of every undrafted colonist standing in the Home area, every think pass. So
the menu is the visible half; the invisible half is that the colonist's whole "put down what you don't
need" routine fails silently on each attempt.
There *is* a genuine latent defect in the same area, and it is **not** this report's mechanism:
`DrugPolicy.InitializeIfNeeded` only creates entries for defs matching `category == ThingCategory.Item
&& IsDrug`, while the query sites test `IsDrug` alone — so a drug def outside the Item category would be
asked about but never entered. It cannot be what happened here, because
`FloatMenuOptionProvider_PickUpItem.GetOptionsFor` short-circuits on `category != ThingCategory.Item`
*first*: reaching the throw at all proves the def satisfies the entry-creation predicate.

**OBSERVED — ruled out.** Two plausible-looking explanations were decompiled and eliminated. (1) A drug
def with no `CompProperties_Drug` makes `InitializeIfNeeded`'s sort comparer NRE — but every missing
entry is *added before* the in-place sort, and `PostLoadIniter` wraps each `ExposeData` in try/catch, so
such a def loses only its ordering, never its entry. (2) *[KV] Save Storage Settings* is a common
suspect for cross-save policy data and is installed locally, so it was decompiled: its `LoadDrugPolicy`
overwrites matching entries **in place** and creates new policies through `MakeNewDrugPolicy()`; it
never clears the list and never removes an entry.

**INFERRED — not confirmed.** The def itself could not be read: that mod is not installed here. Three
mechanisms remain, none of them verified. Vanilla `DrugPolicy.CopyFrom` does `entriesInt.Clear()` and
re-copies with no re-initialisation, so a policy copied from a shorter one stays short — that one is
session-scoped and would heal on a save/reload. Or the def was absent from `DefDatabase<ThingDef>` at
the moment the policy was built or load-repaired (a load-order or patch-timing accident). Or a mod
edits policy entries directly. Which of the three it is decides whether it is a save-data problem, a
load-order problem or an upstream bug — and nothing in the report distinguishes them.

**Why HD appeared in the trace, and why that was misleading.** Two reasons, neither of them evidence.
Harmony annotates a patched method's frame with *every* mod that hooked it, whether or not that mod's
code ran — and here HD's postfix demonstrably did not run at all, because Harmony skips postfixes when
the original throws. It could not have thrown even if it had run: on the float-menu path the stack is a
**ground** stack, so HD's own guard (`drug` must be inside *this* pawn's inventory) returns immediately.
On top of that, HD 1.21.0.0's error handling returned the exception in a way that made Harmony re-throw
it and **restamp** the trace onto HD's own hook — the exact false attribution fixed in #236, in this
same release.

**What HD does now.** It contains the failure at the seam it already holds, and reports it once per drug
def with the def named. When the check throws, HD answers **"keep this drug in the pack"** — RimWorld's
own fall-through answer, since that method returns "drop it" only when a six-clause conjunction all
holds. That matters: a failed call otherwise yields "drop it", which would have the colonist dump the
stack at their feet on every think pass. With the containment, nothing is dropped and the right-click
menu builds normally. HD **does not repair the policy** — it never adds the entry, re-initialises the
list or replaces the policy. A drug policy is shared, saved data and one of the two callers runs on the
clicking player's UI thread, so writing to it there would be both a save mutation and a multiplayer
desync. The throw still happens on every call; HD only stops it costing anything.
HD also had its **own** copy of the same unguarded lookup, in the new "let a colonist in withdrawal take
a kept drug from another colleague" feature (#229), asking about a def taken from an arbitrary
colonist's inventory. That now goes through the same safe lookup, which treats a drug with no policy
entry as *allowed for addiction* — the value RimWorld's own initialiser would have given it, and the
only one the player cannot have overridden, since a drug with no entry has no row in the drug-policy
dialog. **But "the policy permits it" is not "RimWorld can finish the job", so HD also declines to route
a colonist to a drug RimWorld itself cannot evaluate.** That leg does not merely allow a drug, it moves
a dose into the addict's own pack — and RimWorld re-checks it there on the very next think, through the
same lookup that has no entry to find. Routing there would leave the addict holding a dose it can never
take (the check throws every think, and RimWorld then skips its whole drug-satisfaction decision) and
can never put down (the drop loop keeps it, because the pawn *is* addicted). So for a drug in that
state HD stands down and the colonist is left exactly as it would be without HD installed — which is
what an optional extra owes the game it is bolted onto.

**What a report needs to settle this.** If you hit it, the following would identify the mechanism: the
drug's `<category>` and its `<ingestible><drugCategory>`; whether its def carries an
`<li Class="CompProperties_Drug">`; whether the error survives a save → reload (it would rule the
`CopyFrom` path in or out); whether the affected colonist's drug policy was ever copied from another one
or loaded by a settings-persistence mod; and any `Could not do PostLoadInit on` line near a `DrugPolicy`
in the log.

**A separate latent RimWorld bug found in passing, not HD's and with no workaround:** two clauses apart
in the same predicate, `AllowedToTakeScheduledEver` dereferences `CurrentPolicy` with no null check
while `ShouldKeepDrugInInventory` itself does guard it — so a Biotech mutant with `disablePolicies`
throws a `NullReferenceException` there. Recorded so nobody re-derives it.

### Overlap by design — composes
- **Smarter Deconstruction & Mining** (`mlie.smarterdeconstructionandmining`) — postfixes
  `JobDriver_Mine` / `RemoveBuilding` `MakeNewToils` to interleave roof-removal; does **not** replace
  the drivers or clear the queue, so HD's yield hook still fires and mine/deconstruct routes resume.
- **Replace Stuff** (`memegoddess.replacestuff`) — its `Mineable.TrySpawnYield` transpiler wraps the
  8-arg `GenPlace.TryPlaceThing`, which dispatches into the 9-arg out-overload HD hooks → mined ore
  still routes into inventory. (Do **not** also patch the 8-arg overload, or yields double-process.)
- **Smart Farming** (`owlchemist.smartfarming`) — touches only the grow/sow/harvest work-givers,
  distinct from hauling.
- **Better Workbench Management** (`falconne.bwm`) — can optionally count carried inventory toward
  "do until you have X" bills (read-only, cooperative).
- **Save Storage Settings** (`savestoragesettings.kv...`) — changes *what* stockpiles allow; HD's
  unload already handles "no better storage" gracefully (and the red alert covers a true dead end).
- **Storage frameworks — Adaptive Storage Framework** (`adaptive.storage.framework`), **Neat Storage**
  (`sbz.neatstorage`), and the same-family **LWM's Deep Storage / RimFridge / Reel's Expanded Storage /
  Storage Type Categories** — compose **by construction**. HD validates a destination only through the
  vanilla `StoreUtility.IsGoodStoreCell` (→ `NoStorageBlockersIn`) and `GetItemStackSpaceLeftFor` (→
  `GetMaxItemsAllowedInCell`) — the exact two methods ASF transpiles to enforce its per-cell capacity
  and accept filters. So ASF's capacity rules apply *inside* HD's calls automatically, and ASF storage
  always resolves as a **cell** (HD takes the correct unload branch). HD never *count*-overfills a
  deep-storage cell. The one nuance is an LWM **mass-limited** DSU: HD's pre-estimate counts slots
  (mass-blind, because LWM's per-cell mass cap isn't exposed on the standalone `GetItemStackSpaceLeftFor`
  HD prices with), so it can transiently over-estimate by up to one carried stack — but the deposit re-gate
  (`IsGoodStoreCell` re-runs LWM's mass check on placement and floor-drops/re-routes the remainder) bounds
  this to one-cycle re-haul churn; HD never actually overfills or loses items. ASF patches none of
  `JobDriver_HaulToCell` / `ReservationManager` / `HaulAIUtility`, so HD's no-cell-reservation prefix has
  nothing to collide with. It *does* prefix one storage finder — `StoreUtility.TryFindBestBetterStoreCellForWorker`
  (`StoreUtilityPatches.cs:18-31`, vetoing a candidate whose ASF building has no capacity) — which HD calls
  into and therefore obeys; an earlier revision of this page wrongly said ASF patched no finder at all.
  Neat Storage ships **no assembly** (pure ASF
  buildings), so it's covered transitively. (ASF + Neat + LWM verified by decompiling/cloning the
  assemblies; the multi-pawn no-reserve race onto one LWM/ASF multi-stack cell is also bounded by the same
  deposit re-gate — extra trips at worst, never loss.)
  **ASF's own storage bookkeeping is the part that matters for crashes**, and HD mutates none of it: ASF
  transpiles `ThingGrid.RegisterInCell` (`RegisteredAtThingGridEvent.cs:13-35`) and
  `ThingGrid.DeregisterInCell` (`DeregisteredAtThingGridEvent.cs:13-50`) to notify the storage building
  after vanilla's own list `Add`/`Remove`, and postfixes `ListerMergeables.Notify_ThingStackChanged`
  (`NotifyItemStackChanged.cs:10-16`). All three feed `ThingCollection._validStoredThings`, a per-building
  `IntFishSet` (ASF's bundled Fishery hash set, `ThingCollection.cs:31`) recording which stored items the
  building currently accepts. HD never touches that set, those notifiers, or `ThingGrid` — it only places
  items through vanilla `GenPlace`, which is what runs ASF's transpiled register.
  **Known ASF-side bug (HD issue #236, not fixed by HD):** that set can desynchronise so that a later
  register throws `InvalidOperationException: Failed to find parent index in IntFishSet` from inside
  vanilla `ThingGrid.RegisterInCell` (`FishSet.cs:480-501` → `FishSet.ThrowHelper.cs:22-55`), which aborts
  the placement. *The throw site and the path that reaches it are verified; the underlying corruption is
  inferred and has not been reproduced* — the obvious guess (a slot whose `_tails` nibble says empty while
  the bucket still holds a live key) is likely wrong, since Fishery has a dedicated message for that shape
  and #236 reported the parent-walk one instead.
  This is ASF's to fix; HD ships **no workaround** (patching another mod's private
  collection is not something a hauling mod should do). HD does make the crash *more reachable*, because
  more haulers converge on the same storage cells, so the player-side mitigation is turning HD's
  **"Haul to stack"** setting off: that restores vanilla's destination-cell reservation, so only one pawn
  at a time targets a given cell. It narrows the window — it is a mitigation, not a fix. Since HD's
  exception breadcrumb now names an ancestor wrapper honestly (issue #236), a report of this crash points
  at ASF instead of at HD.
  HD's **pickup delay** (the per-stack pause with a progress bar before items enter a pawn's inventory,
  issue #121) also cannot collide with LWM's own "storing takes time" delay, verified against LWM's source:
  LWM's only active timing patch is a postfix on the toil factory `Toils_Haul.PlaceHauledThingInCell`
  (`Deep_Storage_Pause.cs`), which fires when placing INTO a cell whose slot-group parent carries
  `CompDeepStorage`; HD's pickup toils never call that factory (they `SplitOff` + `TryAdd` straight into
  `pawn.inventory`), and LWM applies no delay at all to taking items OUT of its units. On the extraction
  side, LWM's `Deep_Storage_RemoveFrom.cs` IS compiled, but its `[HarmonyPatch]` attribute is commented out
  so `PatchAll` never applies it, and it is not a delay anyway: it is a stack-fill transpiler on
  `StartCarryThing`'s delegate that tops up the picked stack from other stacks in the same deep-storage
  cell. The old storing-wait experiment, `Deep_Storage_Wait_NotUsed.cs` (the abandoned precursor of
  `Deep_Storage_Pause`, also targeting the place-into-cell toil), is the file excluded from LWM's csproj
  compile list entirely. So bulk-picking FROM a deep storage unit pays only HD's pickup delay, and
  depositing INTO one pays only LWM's storing delay: opposite phases, no double delay, no shared Harmony
  target.
- **Storage Network** (`BlackMouse.StorageNetwork`) — a *virtual* (Applied-Energistics-style) storage: its
  items live **despawned** inside server/terminal buildings, so they're invisible to HD's spawned-item
  sweep and HD falls back to vanilla one-stack loading by default. An **opt-in** setting ("Bulk-load from
  Storage Network", default off, shown only when SN is installed) lets HD bulk-load a transporter / portal /
  vehicle straight from the network: it adds the network's stored stacks to the load plan through a usable,
  reachable terminal and lets Storage Network materialize them on demand. Read-only during planning; bounded
  by the same claim / carry / mass budget; a stack SN can't hand over is skipped (never stranded); fully
  inert when off or SN absent.

### Loadout / inventory-stock mods vs. the "unload all surplus" option
The **"Also put away surplus inventory a pawn is carrying that HD did NOT pick up itself"** option (on by
default) makes a colonist at home unload *any* surplus it carries, not just HD-scooped loot. "Surplus"
respects every keep source vanilla itself respects — drug-policy `takeToInventory`, `inventoryStock`,
packable food, and the **Combat Extended** loadout — so those are never put away. The risk is a mod that
keeps items in a pawn's inventory through its **own** system rather than one of those:
- **Smart Medicine** (stock-up) and **sidearm mods (e.g. Simple Sidearms)** stash items in inventory via
  their own tracking. HD's surplus math can't see that intent, so with the option on it may haul those
  stashed items to storage. If you use such a mod and want the stash kept, **turn the option off** in
  HD's settings (the gizmo, the every-work-run/interval triggers, and the red alert still handle
  genuinely-stuck HD-scooped loot when it's off). CE loadouts are safe — HD reads the CE loadout as keep-stock.
- **Item Policy** (`RunningBugs.ItemPolicy`) — **auto-respected, no setting change needed.** It keeps a
  per-pawn "N of these defs in inventory" stock (re-fetched by its own `JobGiver_TakeItemForInventoryStock`).
  HD reads that per-pawn keep count (a reflection-only `ItemPolicyCompat` feeding HD's **count-aware** keep),
  so it keeps the policy amount and unloads only the genuine surplus — no fight with Item Policy's re-fetch,
  even with the "unload all surplus" option on. (The shim deliberately checks Item Policy's policy dictionary
  for the pawn *before* querying, so it never triggers Item Policy's create-on-read side effect. Inert
  without Item Policy.) This is the general pattern HD aims for: honor any per-pawn "keep N of def" intent
  through the count-aware keep rather than a per-mod special case.
- **Compositable Loadouts** (Wiri, Steam id 2679126859) — **auto-respected, no setting change needed.** HD
  reads each pawn's loadout as keep-stock (a reflection-only shim feeding the same count-aware keep), so a
  loadout item is never shipped off for Compositable Loadouts to fetch straight back. Two deliberate limits
  keep that protection from turning into a trap (#233): **clothing is not kept**, because Compositable
  Loadouts satisfies clothing by sending the colonist to *wear* a garment off the floor and never stocks a
  spare in the pack — so keeping one would only strand it; and a weapon the colonist is **already holding**
  counts towards its own loadout entry, so a spare of that weapon in the pack is put away rather than pinned.
  Inert without Compositable Loadouts.

### Vehicle Framework (`SmashPhil.VehicleFramework`) — composes; a vehicle is a foreign carrier HD respects
HD can bulk-load a vehicle's cargo in one trip (its own VF-aware load path) and otherwise treats a
`VehiclePawn` as a non-pawn carrier, not a colonist. All VF interop is **reflection-only** (inert without
VF), and HD's per-pawn logic that must skip vehicles does so via a single subclass-safe `IsVehicle` check.
A vehicle's cargo hold is the **player's** to manage, so HD never raids it: it does not source build
materials (build-from-inventory) or meals (meals-on-wheels) out of a parked, loaded vehicle, and never
bulk-unloads a vehicle. With HD's VF support toggle **off**, HD ignores vehicles entirely — it won't even
deposit into one via the pack-animal path. (The one Harmony patch that hooks VF's pack-vehicle work-giver
guards on the actual instance type, because VF's generic work-giver base shares JIT-compiled code across
sibling work-givers — without that guard a refuel/upgrade job would be hijacked into a cargo load.)

### Refuelable buildings — generators, drills, reactors (any mod, e.g. Advanced Power Plus)
HD's one-trip **bulk refuel** (a hauler sweeps several fuel stacks into its inventory and fills a
`CompRefuelable` in a single walk, instead of one stack per trip) works with refuelable buildings from any
mod, including large **impassable** ones. **Advanced Power Plus** (`yamabuki.sd.advpowergen`) is the
representative case: its uranium generators — the 6×6 advanced nuclear generator and the stirling
radioisotope generators — are ordinary `CompRefuelable` buildings on impassable footprints. HD now anchors
its fuel search at the hauler's own (always-passable) cell — the same cell vanilla's own refueling uses —
so it never trips on an impassable footprint and bulk-refuels these generators correctly. (Earlier HD
anchored the search at the building's own cell, which on an impassable footprint has no passable region and
made RimWorld's fuel finder throw, freezing colonists in a job-search loop and breaking the building's
right-click menu — issue #34; fixed, and the fix covers *any* impassable refuelable, not just APP's.)
Everything else APP adds is independent of HD with no shared patch surface — its buildings, its custom
solar-output comp (`sd_adv_powergen_CompAdvPowerPlantSolar`, render/output only), and its watermill
water-overlap cache postfixes. Build materials and uranium fuel haul exactly like vanilla.

### Adds storable content — all standard categories (no black-hole risk)
- **Melee Animation** — lassos (apparel, `ApparelUtility`) + a melee weapon. **Vanilla Expanded
  Framework** — a minified flower + a `VFEC_Shields` category parented under the default `Apparel`
  category. **Diagonal Walls 2**, **Replace Stuff** — buildings only. None use an empty/orphan
  top-level `thingCategory`, so all have a default stockpile and unload normally.
- **Modded resources are haulable exactly like vanilla.** Spot-checked by cloning **Vanilla Recycling
  Expanded**, **Alpha Biomes**, **DeepRim**, and **VFE-Mechanoid**: every resource/item/chunk def
  derives from a vanilla base (`ResourceBase` → `category=Item`, `alwaysHaulable=true`; modded chunks
  clone vanilla `ChunkBase` → `StoneChunks`, Dumping-stockpile-only). Zero `<alwaysHaulable>false</…>`
  and zero odd categories. HD's scoop / bulk-haul / pack-load all gate on `def.EverHaulable` (and
  `category == Item` for pack-loading) — the exact vanilla predicate — so any modded item the vanilla
  hauler picks up, HD picks up too, and any it skips, HD skips too. (Modded chunks ride HD's
  "no stockpile → desperate cell / dumping" path, same as vanilla rock chunks.)

### Non-human pawns — mechs, animals, robots (the "new hauling regime")
HD attaches its `CompHauledToInventory` the same way Pick Up And Haul does: a patch on
`ThingDef[thingClass="Pawn"]/comps` that hits the abstract `BasePawn` (which has `thingClass=Pawn` + a
`<comps>` node), so **every** pawn — colonists, mechs, animals, and most modded races — inherits the
comp. The comp alone is harmless; what matters is whether a pawn can be *loaded* by HD and then *not
unloaded*. HD's rule (see "Pawn eligibility" above): scoop, bulk-haul, and unload all gate on the same
`IsEligible` predicate.

- **Mechanoids** — an intended, `allowMechanoids`-gated target (default **on**). A colony hauler/lifter
  mech scoops, bulk-hauls (at its plain carry limit — the slowdown overload model is skipped for
  non-humanlikes), and auto-unloads coherently. `allowMechanoids = off` disables all of it.
- **Animals (vanilla + modded, e.g. Vanilla Animals Expanded)** — they *get* the comp but are
  structurally unreachable by HD's bulk haul: a trained-haul animal hauls via the animal think tree's
  `JobGiver_Haul → HaulAIUtility.HaulToStorageJob`, never through `WorkGiver_HaulGeneral.JobOnThing`
  (the only method HD patches); and the vanilla work scan needs `workSettings` (humanlikes + player
  mechs only) plus `IsColonist`. So an ordinary animal keeps vanilla single-stack hauling and HD never
  touches it. (Animals-Logic / "hardworking animals" just tune that same `JobGiver_Haul` path — still
  not HD's method.)
- **Robots / androids (modded)** — the two archetypes are safe by different mechanisms (verified by
  cloning): **Android Tiers Reforged** androids are `intelligence=Humanlike`, so HD treats them as
  colonists and auto-unloads them normally; **Misc. Robots / ++** uses a custom `thingClass`
  (`AIRobot.X2_AIRobot`) and a non-colonist custom work system, so it never reaches HD's haul method.
- **The one real edge case HD now guards against — an "animal worker" mod.** *HousekeeperAssistanceCat*
  (by the Animals-Logic author) is `intelligence=Animal` (non-humanlike) yet gives its cat a custom
  `JobGiver_Work` + `workSettings` + a Hauling work giver, **and** it inherits the comp. That combination
  reaches HD's bulk-haul postfix while being ineligible for HD's auto-unload — i.e. it *could* strand a
  swept load. HD closes this by gating bulk-haul (and pack-animal loading) on the **same** `IsEligible`
  predicate as scoop/unload: a non-humanlike, non-mech pawn is never swept, so it can never be stranded —
  it simply keeps vanilla single-stack hauling. (The cat's own author notes the comp-plus-haul combo "breaks
  Pick Up And Haul" — HD's symmetric gate is exactly the fix.) This makes HD robust to *any* current or
  future "plain-`Pawn` non-humanlike worker" race, not just the ones surveyed.

The remaining ~35 active mods are cosmetic / UI / render-only (Yayo's Animation, RimHUD, Camera+,
Bubbles, Quality Colors, Blood Animations, Bionic Icons, etc.) and never touch jobs, hauling, storage,
inventory, `GenPlace`, or `GenLeaving`.

## If you hit a problem
1. **Pawns carrying items forever?** You should see the red **"Cannot unload inventory"** alert — click
   it to jump to the pawn(s). It means there's no stockpile/dumping zone that accepts those items (add
   one — a Dumping Stockpile takes chunks), the storage is unreachable, or a mod is repeatedly cancelling
   the unload job.
2. **A mod keeps interrupting hauling/unloading?** Add `HaulersDream_UnloadInventory` to that mod's
   do-not-interrupt / excepted-jobs list. HD recovers either way, but it avoids wasted trips.
3. **Everyone idle, but still eating and sleeping — and forced orders still work?** Look for the
   **"Work giver disabled by another mod's error"** alert: another mod threw from a part of RimWorld's
   work selection that RimWorld does not guard, so HD switched that one kind of work off to keep the
   rest of the colony running. The alert names the mod — report it there, with your log. Restarting the
   game clears the list. (See the Smarter Construction section above for the worked example.)
4. **Right-clicking a colonist onto a drug gives "Value does not fall within the expected range" and
   no "Pick up" options?** RimWorld's own drug-policy lookup raises that (message-less) error when a
   colonist's drug policy holds no entry for that drug, before any HD code runs. HD now answers
   "keep it" — RimWorld's own default — so the menu builds and nothing gets dropped, and it logs the
   error once naming the drug. It does not repair the policy. If it persists, the log line names the
   drug; the "Modded drugs and the drug policy" section above lists what a report needs to pin down
   which mechanism produced the gap.
