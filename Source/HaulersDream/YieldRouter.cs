using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Routes fresh work-yields into the producing pawn's inventory at the moment they are created.
    /// Two entry points, both reusing <see cref="RouteIntoInventory"/>:
    ///  - <see cref="OnTryPlaceThing"/> from a GenPlace.TryPlaceThing prefix (plants, mining,
    ///    deep-drill, animals — all place via GenPlace; decompilation-verified). The responsible
    ///    pawn is inferred from who is working at/adjacent to the placement cell.
    ///  - <see cref="OnDeconstructLeavings"/> from a GenLeaving.DoLeavingsFor postfix. Deconstruct
    ///    leavings ALSO travel through the patched GenPlace overload (DoLeavingsFor places them via
    ///    ThingOwner.TryDrop → GenDrop → GenPlace; only detritus uses GenSpawn), but the prefix never
    ///    routes them because JobDriver_Deconstruct is deliberately absent from TryGetWorkType. Only
    ///    the leavings produced by THIS call are scooped (snapshot diff), never pre-existing ground items.
    /// Work types that aren't tracked (surgery, bench work, fermenting, fishing, …) never match, so
    /// they're excluded automatically.
    ///
    /// Consuming the placement (prefix returns false) is safe for every producer: all pass a null
    /// placedAction except mining, whose placedAction only forbids NON-player yields (we only ever
    /// intercept player pawns), and every producer resets its own accounting independently of the
    /// placement result (verified against decompiled Assembly-CSharp).
    /// </summary>
    public static class YieldRouter
    {
        [ThreadStatic] private static bool routing;        // re-entrancy guard (RouteIntoInventory's own inner placements)

        // ---- GenPlace path (plants / mining / deep drill / animals) -----------------------------

        /// <returns>true to let vanilla place the (remaining) thing; false to fully consume it.</returns>
        /// <param name="dropPawn">The producer awaiting the postfix (DropThenHaul), carried per-invocation
        /// via Harmony's <c>__state</c> — a static handoff would be cleared by a NESTED TryPlaceThing call
        /// (e.g. a modded comp spawning a side product mid-placement), losing the outer scoop.</param>
        public static bool OnTryPlaceThing(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode,
            ref Thing lastResultingThing, ref bool result, out Pawn dropPawn)
        {
            dropPawn = null;
            if (map == null || thing == null || thing.Destroyed || thing.def == null)
                return true;
            if (!InferencePolicy.IsRoutablePlacement(routing, mode == ThingPlaceMode.Near, thing.def.category == ThingCategory.Item))
                return true;

            var s = HaulersDreamMod.Settings;
            if (s == null)
                return true;

            var pawn = FindWorker(center, map, out var type);
            if (pawn == null || !s.IsTypeEnabled(type))
                return true;

            // Strip drops ALWAYS take the drop-then-scoop path, regardless of pickup mode: unlike the
            // other producers (which place freshly-created things), strip drops come out of ThingOwner
            // containers via the GenDrop chain, whose callers run bookkeeping on the placed result —
            // consuming the placement is unverified there, and letting the clothes hit the floor before
            // the scoop is the natural reading of a strip anyway.
            if (s.pickupMode == PickupMode.DropThenHaul || type == HaulSourceType.Strip)
            {
                // Realistic mode: let the yield land on the ground; the postfix (OnTryPlaceThingPost)
                // records the just-placed thing so the producer scoops it up afterward.
                dropPawn = pawn;
                return true;
            }

            // DirectToInventory: take it straight into inventory (no ground touch).
            if (RouteIntoInventory(pawn, thing, type, out var taken, out var fullyConsumed) && fullyConsumed)
            {
                lastResultingThing = taken;
                result = true;
                return false; // we took the whole stack — skip vanilla placement
            }
            return true; // nothing taken, or partial: vanilla places the remainder
        }

        /// <summary>Postfix side of the GenPlace hook: in DropThenHaul mode, record the dropped yield.
        /// <paramref name="dropPawn"/> is the prefix's out value, delivered through Harmony <c>__state</c>.</summary>
        public static void OnTryPlaceThingPost(Thing lastResultingThing, Pawn dropPawn)
        {
            var pawn = dropPawn;
            if (pawn == null || lastResultingThing == null || !lastResultingThing.Spawned || lastResultingThing.Destroyed)
                return;
            // A yield that landed/merged INTO valid storage is already home — scooping it would pull stock back
            // OUT of a stockpile only to re-unload it later (a churn loop). Leave stored goods stored.
            if (lastResultingThing.IsInValidStorage())
                return;
            RecordSelfPickup(pawn, lastResultingThing);
        }

        /// <summary>Queue a fresh ground drop for the producer to scoop up (DropThenHaul mode).</summary>
        private static void RecordSelfPickup(Pawn pawn, Thing thing)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp == null || pawn.jobs == null)
                return;
            if (!comp.pendingSelfPickups.Contains(thing))
                comp.pendingSelfPickups.Add(thing);
            EnsureSelfPickupJob(pawn, comp);
        }

        /// <summary>
        /// Make sure the producer has a self-pickup job queued for its pending drops. Gates on the pawn's
        /// ACTUAL job state (current or queued) rather than a stored flag — the job queue can be cleared
        /// without the job ever running (drafting, a forced/prioritized job, a mental break, an
        /// interruption), which would otherwise strand a "queued" flag set true forever and silently
        /// stop the pawn from ever scooping its drops again. This is self-correcting: any new drop, or the
        /// idle backstop, re-queues it.
        /// </summary>
        internal static void EnsureSelfPickupJob(Pawn pawn, CompHauledToInventory comp = null)
        {
            if (pawn?.jobs == null)
                return;
            comp = comp ?? pawn.GetComp<CompHauledToInventory>();
            if (comp == null || comp.pendingSelfPickups.Count == 0 || HasSelfPickupJob(pawn))
                return;

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_SelfPickup);
            if (job.TryMakePreToilReservations(pawn, false))
            {
                pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
                HDLog.Dbg($"{pawn} queued self-pickup ({comp.pendingSelfPickups.Count} pending drops).");
            }
        }

        /// <summary>
        /// The pawn just hit its carry limit while scooping. In the default mode it breaks off to run the
        /// consolidated unload pass now (so it doesn't keep working overweight forever); in strict mode it
        /// keeps working and the surplus is left for normal hauling.
        /// </summary>
        internal static void MaybeUnloadBecauseFull(Pawn pawn, HaulersDreamSettings s)
        {
            if (s == null || !Core.UnloadPolicy.FullTriggerAllowed(s.strictCarryWeight, s.markForUnload))
                return;
            // forced: bypass the post-pickup grace period — being full IS the signal to unload.
            PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true);
        }

        /// <summary>
        /// A scoop just landed: if it pushed the pawn OVER-ENCUMBERED (above 100% carry capacity — the
        /// point where vanilla starts slowing the pawn down), queue the unload trip now instead of
        /// waiting for the smart-overload ceiling (~2x capacity). Smart overload deliberately lets a
        /// pawn scoop past its capacity so yields aren't stranded on the ground — but carrying that
        /// surplus around for hours taxes everything else the pawn does; better to finish the current
        /// job and make the trip. Queued (never interrupts), forced (a route mid-run is worth pausing
        /// when the pawn is this heavy). Same gate family as the full-trigger: never in strict mode
        /// (strict can't exceed capacity) and never with auto-unload off.
        ///
        /// Design note: this deliberately trades away some of smart-overload's "fewer trips across a
        /// multi-job run" benefit for heavy materials — the pawn still overloads WITHIN one job (the
        /// unload is queued, so it runs at the job boundary), but it no longer carries the surplus across
        /// many jobs to the ~2x ceiling. That's the point of the fix: a pawn shouldn't lug steel around
        /// all day. Trip frequency stays bounded (one trip per capacity-worth of scooping), and the
        /// queued-unload dedup (alreadyUnloading) means repeated over-cap scoops never stack up unloads.
        /// </summary>
        internal static void MaybeUnloadBecauseOverEncumbered(Pawn pawn, HaulersDreamSettings s)
        {
            if (pawn != null && MassUtility.IsOverEncumbered(pawn))
                MaybeUnloadBecauseFull(pawn, s);
        }

        private static bool HasSelfPickupJob(Pawn pawn)
        {
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_SelfPickup)
                return true;
            var queue = pawn.jobs.jobQueue;
            if (queue != null)
                foreach (var qj in queue)
                    if (qj?.job?.def == HaulersDreamDefOf.HaulersDream_SelfPickup)
                        return true;
            return false;
        }

        // ---- Deconstruct path -------------------------------------------------------------------

        /// <summary>Snapshot the items in <paramref name="area"/> before DoLeavingsFor runs.</summary>
        public static HashSet<Thing> SnapshotItems(CellRect area, Map map)
        {
            var set = new HashSet<Thing>();
            foreach (var c in area.Cells)
            {
                if (!c.InBounds(map))
                    continue;
                var things = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < things.Count; i++)
                    if (things[i].def?.category == ThingCategory.Item)
                        set.Add(things[i]);
            }
            return set;
        }

        /// <summary>Scoop only the leavings that appeared in <paramref name="area"/> (not in <paramref name="before"/>).</summary>
        public static void OnDeconstructLeavings(CellRect area, Map map, HashSet<Thing> before)
            => OnDeconstructLeavings(area, map, before, null);

        /// <summary>Scoop only the leavings that appeared in <paramref name="area"/> (not in <paramref name="before"/>).
        /// <paramref name="diedThing"/> is DoLeavingsFor's subject — it pins the credit on the pawn whose
        /// job actually targets it, instead of any adjacent deconstructor.</summary>
        public static void OnDeconstructLeavings(CellRect area, Map map, HashSet<Thing> before, Thing diedThing)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.haulDeconstruct || map == null)
                return;

            var pawn = FindDeconstructor(area, map, diedThing);
            if (pawn == null)
                return;

            foreach (var c in area.Cells)
            {
                if (!c.InBounds(map))
                    continue;
                var things = map.thingGrid.ThingsListAtFast(c);
                for (int i = things.Count - 1; i >= 0; i--) // backward: RouteIntoInventory may remove the current item
                {
                    var t = things[i];
                    if (t.def == null)
                        continue;
                    bool isItem = t.def.category == ThingCategory.Item;
                    if (!InferencePolicy.ShouldScoopLeaving(isItem, before.Contains(t), isItem && t.IsForbidden(pawn), isItem && t.IsInValidStorage()))
                        continue;
                    if (s.pickupMode == PickupMode.DropThenHaul)
                        RecordSelfPickup(pawn, t); // leavings are already on the ground -> scoop them up afterward
                    else
                        RouteIntoInventory(pawn, t, HaulSourceType.Deconstruct, out _, out _);
                }
            }
        }

        // ---- shared routing ---------------------------------------------------------------------

        private static bool RouteIntoInventory(Pawn pawn, Thing thing, HaulSourceType type, out Thing taken, out bool fullyConsumed)
        {
            taken = null;
            fullyConsumed = false;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;

            var s = HaulersDreamMod.Settings;
            int count = OverloadGate.CountToPickUp(pawn, thing, s);
            if (count <= 0)
            {
                // Pawn is full (at its carry/overload limit). Head off to unload now (unless strict mode,
                // where it keeps working and leaves the surplus for normal hauling). The yield stays on the
                // ground either way.
                MaybeUnloadBecauseFull(pawn, s);
                return false;
            }

            routing = true;
            try
            {
                bool fully = count >= thing.stackCount;
                // Always SplitOff (see JobDriver_SelfPickup): for a SPAWNED input (deconstruct leavings),
                // SplitOff(full) despawns it and clears holdingOwner so TryAddOrTransfer can TryAdd it; passing
                // the spawned thing directly would hit the "Can't transfer to/from Maps" guard and move nothing.
                // For the GenPlace path the input is unspawned, so SplitOff(full) returns it unchanged (no-op).
                Thing split = thing.SplitOff(count);
                if (split == null)
                    return false;

                // TryAddOrTransfer handles both a spawned ground item (deconstruct) and a not-yet-
                // placed thing (GenPlace). It can MERGE part of the stack into an existing inventory
                // stack and still report false (some units moved, some didn't), so we track by the
                // count delta rather than the bool — never leaving moved units untracked or lost.
                var owner = pawn.inventory.GetDirectlyHeldThings();
                int before = split.stackCount;
                bool allMoved = owner.TryAddOrTransfer(split, canMergeWithExistingStacks: true);

                if (allMoved || split.stackCount < before)
                {
                    // Some/all units landed in inventory. Register one stack of this def so the unload
                    // pass collects it (the unload driver relinks the real stacks by def). Pass the moved
                    // count so a merge into an already-tagged stack re-notifies CE's HoldTracker.
                    int moved = allMoved ? before : before - split.stackCount;
                    Thing held = InventoryStackOfDef(owner, split.def) ?? (allMoved ? split : null);
                    if (held != null)
                        comp.RegisterHauledItem(held, moved);
                    comp.NotifyYieldPicked();
                    HDLog.Dbg($"{pawn} scooped x{before - split.stackCount} {split.def?.label} ({type}).");
                    // Direct mode lands yields mid-work-job: if this one tipped the pawn over capacity,
                    // queue the unload now — it runs when the current job ends (alreadyUnloading dedups
                    // the repeats while the pawn keeps scooping the rest of this job's yields).
                    MaybeUnloadBecauseOverEncumbered(pawn, s);
                }

                if (allMoved)
                {
                    taken = split;
                    fullyConsumed = fully;
                    return true;
                }

                // Anything still un-moved is folded back into `thing` so vanilla places it on the
                // ground (the item is never lost — worst case it just isn't picked up this time).
                if (!fully && split.stackCount > 0 && !thing.Destroyed)
                    thing.TryAbsorbStack(split, respectStackLimit: false);
                return false;
            }
            finally
            {
                routing = false;
            }
        }

        internal static Thing InventoryStackOfDef(ThingOwner owner, ThingDef def)
        {
            if (owner == null || def == null)
                return null;
            for (int i = 0; i < owner.Count; i++)
                if (owner[i]?.def == def)
                    return owner[i];
            return null;
        }

        /// <summary>Total units of <paramref name="def"/> across all of the owner's stacks.</summary>
        internal static int InventoryCountOfDef(ThingOwner owner, ThingDef def)
        {
            if (owner == null || def == null)
                return 0;
            int total = 0;
            for (int i = 0; i < owner.Count; i++)
                if (owner[i]?.def == def)
                    total += owner[i].stackCount;
            return total;
        }

        // ---- pawn inference ---------------------------------------------------------------------

        /// <summary>
        /// Find the pawn that produced a yield placed at <paramref name="center"/>. Prefers the true
        /// producer — the worker standing on the cell (plants/animals/deep-drill) or whose job target
        /// is the cell (mining) — before falling back to any tracked worker in the 3×3.
        /// </summary>
        private static Pawn FindWorker(IntVec3 center, Map map, out HaulSourceType type)
        {
            type = default;
            var cells = GenAdj.AdjacentCellsAndInside; // 9-cell 3x3 block; the (0,0,0) CENTER offset is LAST (index 8)
            // The CENTER cell is checked FIRST (i == -1): the pawn STANDING on the drop cell is the
            // truest producer, but vanilla's table orders the center offset last, which would credit an
            // adjacent pawn whose job targets the center before the pawn standing on it. The loop bound
            // then skips the table's trailing center entry (decompile-verified layout).
            for (int i = -1; i < cells.Length - 1; i++)
            {
                IntVec3 c = i < 0 ? center : center + cells[i];
                if (!c.InBounds(map))
                    continue;
                var things = map.thingGrid.ThingsListAtFast(c);
                for (int j = 0; j < things.Count; j++)
                {
                    if (!(things[j] is Pawn p) || !IsCandidate(p) || !TryGetWorkType(p.jobs?.curDriver, out var t))
                        continue;

                    // ONLY the true producer is routed — the pawn standing on the cell
                    // (plants/animals/deep-drill) or whose current job target IS the cell (mining).
                    // There is deliberately NO fallback to "any nearby worker": that is what makes us
                    // immune to the Harvest-And-Haul / Pick Up And Haul "dropped item gets deleted"
                    // collision. An ordinary inventory drop (vanilla, our own unload's drop-at-feet,
                    // or another hauling mod) has no producer at its cell, so it is never intercepted
                    // and always lands on the ground. We also never destroy items, only move them.
                    IntVec3 pos = p.Position;
                    bool hasJob = p.CurJob != null;
                    IntVec3 tgt = hasJob ? p.CurJob.targetA.Cell : default;
                    if (InferencePolicy.IsTrueProducer(pos.x, pos.z, hasJob, tgt.x, tgt.z, center.x, center.z))
                    {
                        type = t;
                        return p;
                    }
                }
            }
            return null;
        }

        private static Pawn FindDeconstructor(CellRect area, Map map, Thing diedThing)
        {
            Pawn fallback = null;
            foreach (var c in area.ExpandedBy(1).Cells)
            {
                if (!c.InBounds(map))
                    continue;
                var things = map.thingGrid.ThingsListAtFast(c);
                for (int j = 0; j < things.Count; j++)
                {
                    if (!(things[j] is Pawn p) || !IsCandidate(p) || !(p.jobs?.curDriver is JobDriver_Deconstruct))
                        continue;
                    // PREFER the pawn whose job actually targets the died thing — two pawns deconstructing
                    // side by side must not mis-credit each other's leavings to whoever scans first.
                    if (diedThing != null && p.CurJob?.targetA.Thing == diedThing)
                        return p;
                    // The first-found fallback only applies when the caller has NO died thing (legacy
                    // 3-arg overload). Vanilla always passes it, and a pawn-less instant deconstruct
                    // (a cancelled frame, a zero-work building, god mode) must NOT credit a bystander
                    // deconstructing something else — those leavings stay for normal hauling.
                    if (diedThing == null && fallback == null)
                        fallback = p;
                }
            }
            return fallback;
        }

        private static bool TryGetWorkType(JobDriver driver, out HaulSourceType type)
        {
            // GUARD: never add JobDriver_Deconstruct here — its leavings also flow through the patched
            // GenPlace overload, so the prefix would consume them AND the DoLeavingsFor postfix would
            // scoop them: every leaving double-processed.
            switch (driver)
            {
                case JobDriver_PlantWork _: type = HaulSourceType.Harvest; return true;
                case JobDriver_Mine _: type = HaulSourceType.Mining; return true;
                case JobDriver_OperateDeepDrill _: type = HaulSourceType.DeepDrill; return true;
                case JobDriver_GatherAnimalBodyResources _: type = HaulSourceType.Animal; return true;
                // A strip order's removed gear is a work yield like any other: it drops at the TARGET's
                // position (which is the stripper's job target cell, so the true-producer check matches)
                // and the stripper scoops the pile right after the strip completes.
                case JobDriver_Strip _: type = HaulSourceType.Strip; return true;
                default: type = default; return false;
            }
        }

        /// <summary>Player-owned, eligible race, on an allowed map, with the tracking comp.</summary>
        public static bool IsCandidate(Pawn p)
        {
            if (p == null || p.Faction != Faction.OfPlayerSilentFail)
                return false;
            if (!IsEligible(p))
                return false;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return false;
            if (p.Map != null && !s.enableOnNonHomeMaps && !p.Map.IsPlayerHome)
                return false;
            return p.GetComp<CompHauledToInventory>() != null;
        }

        public static bool IsEligible(Pawn p)
        {
            if (p?.RaceProps == null)
                return false;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return false;
            return EligibilityPolicy.IsEligible(
                isMechanoid: p.RaceProps.IsMechanoid,
                isHumanlike: p.RaceProps.Humanlike,
                isDrafted: p.Drafted,
                // Type-level, not WorkTags.Hauling: "incapable of dumb labor" disables via
                // ManualDumb/Commoner without setting the Hauling tag, and the type check also
                // composes with the F23 all-pawns-can-haul override.
                incapableOfHauling: p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling),
                allowMechanoids: s.allowMechanoids,
                pauseWhileDrafted: s.pauseWhileDrafted,
                allowIncapable: s.allowIncapable);
        }
    }
}
