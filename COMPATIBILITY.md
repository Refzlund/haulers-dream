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
  (self-recovering). Three nuances:
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

### Can interrupt HD jobs (self-recovering)
- **Automatic Stump Chopping** (`arylice.rimworld.automaticstumpchopping`) — prepends a
  `CutPlant(stump)` job per felled tree; a big forest harvest can briefly front-load a cutter's queue,
  but it only prepends (never clears), so HD's queued unload/route work resumes.
- **Better Autocasting for VPE** — **not installed** in this load order. If added: an autocaster that
  interrupts the current job is survivable (HD re-issues). If it uses a *job-def exception list*, add
  `HaulersDream_UnloadInventory` (and optionally `HaulersDream_BillPrepGather` / `HaulersDream_BatchCraft`)
  to the **excepted** jobs so a deliberate in-hand/inventory load isn't dropped mid-trip.

### Overlap by design — composes
- **Smarter Deconstruction & Mining** (`mlie.smarterdeconstructionandmining`) — postfixes
  `JobDriver_Mine` / `RemoveBuilding` `MakeNewToils` to interleave roof-removal; does **not** replace
  the drivers or clear the queue, so HD's yield hook still fires and mine/deconstruct routes resume.
- **Smarter Construction** (`dhultgren.smarterconstruction`) — every destructive/cancel path is gated
  on `!playerForced`; HD's construction tether + delivery jobs set `playerForced=true`, so they're immune.
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
  and accept filters. So ASF's capacity rules apply *inside* HD's calls automatically, HD never
  over-fills a deep-storage cell, and ASF storage always resolves as a **cell** (HD takes the correct
  unload branch). ASF patches none of `JobDriver_HaulToCell` / `ReservationManager` / `HaulAIUtility` /
  the `TryFind*Storage*` finders, so HD's no-cell-reservation prefix has nothing to collide with. Neat
  Storage ships **no assembly** (pure ASF buildings), so it's covered transitively. (ASF + Neat verified
  by decompiling the installed assemblies; LWM/RimFridge/Reel's hit the same `GetMaxItemsAllowedInCell`
  path — expected to compose, untested.)

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
