# Hauler's Dream — Mod Compatibility

How Hauler's Dream (HD) coexists with other mods, from a code-level investigation of a real 49-mod
load order (decompiled assemblies + XML patches cross-checked against HD's own patch surface).

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

### Can interrupt HD jobs (self-recovering)
- **Allow Tool** (`unlimitedhugs.allowtool`) — Shift + "Haul Urgently" calls `CheckForJobOverride` on
  colonists, which can cancel an in-progress HD bulk-haul / construction-tether / unload. User-initiated
  and self-recovering (HD re-issues). Urgent-haul targets are plain single-stack vanilla jobs and are
  **not** swept into inventory by HD's bulk haul — they coexist.
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

### Adds storable content — all standard categories (no black-hole risk)
- **Melee Animation** — lassos (apparel, `ApparelUtility`) + a melee weapon. **Vanilla Expanded
  Framework** — a minified flower + a `VFEC_Shields` category parented under the default `Apparel`
  category. **Diagonal Walls 2**, **Replace Stuff** — buildings only. None use an empty/orphan
  top-level `thingCategory`, so all have a default stockpile and unload normally.

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
