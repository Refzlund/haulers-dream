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
    ///  - the DECONSTRUCT capture (<see cref="BeginDeconstructCapture"/> / <see cref="EndDeconstructCapture"/>)
    ///    driven by a GenLeaving.DoLeavingsFor prefix+postfix. Deconstruct leavings ALSO travel through the
    ///    patched GenPlace overload (DoLeavingsFor places them via ThingOwner.TryDrop → GenDrop → GenPlace;
    ///    only detritus uses GenSpawn), but the prefix never ROUTES them because JobDriver_Deconstruct is
    ///    deliberately absent from TryGetWorkType. Instead, while DoLeavingsFor runs we CAPTURE the exact
    ///    item each placement produced (the GenPlace postfix feeds <see cref="CaptureDeconstructLeaving"/>),
    ///    so leavings are scooped wherever they land — even when Near-placement spills them outside the
    ///    building footprint, or they merge into a pre-existing ground stack. Never pre-existing items.
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
            // No producer from the prefix: this is a DECONSTRUCT leaving (FindWorker never matches a
            // deconstructor — JobDriver_Deconstruct is absent from TryGetWorkType). Hand it to the active
            // capture, which scoops all leavings once DoLeavingsFor completes. (When no capture is active,
            // a producerless placement is just an ordinary drop we don't touch.)
            if (dropPawn == null)
            {
                if (deconstructCapturePawn != null)
                    CaptureDeconstructLeaving(lastResultingThing);
                return;
            }
            if (lastResultingThing == null || !lastResultingThing.Spawned || lastResultingThing.Destroyed)
                return;
            // A yield that landed/merged INTO valid storage is already home — scooping it would pull stock back
            // OUT of a stockpile only to re-unload it later (a churn loop). Leave stored goods stored.
            if (lastResultingThing.IsInValidStorage())
                return;
            RecordSelfPickup(dropPawn, lastResultingThing);
        }

        /// <summary>Queue a fresh ground drop for the producer to scoop up (DropThenHaul mode).</summary>
        private static void RecordSelfPickup(Pawn pawn, Thing thing)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp == null || pawn.jobs == null)
                return;
            // Only queue the dropped yield for pickup if it can actually be delivered. With no storage
            // destination, leave it on the ground at the work spot (vanilla) rather than scooping it to a
            // desperate far/feet drop at unload. The nearby sweep below still grabs OTHER loose items that DO
            // have a destination, so a clean-up pass isn't lost just because this one yield has nowhere to go.
            if (HasScoopDestination(pawn, thing) && !comp.pendingSelfPickups.Contains(thing))
                comp.pendingSelfPickups.Add(thing);
            // Clean the surrounding area in the same pass: also queue nearby loose haulables for self-pickup
            // (cooldown-debounced, so this scans at most once per work spot — not once per dropped stack).
            MaybeSweepNearbyIntoPending(pawn, thing.Position);
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

        // Opportunistic "clean the area while you work" sweep bounds. Local (a work spot's immediate
        // surroundings), bounded per scan, and per-pawn cooldown-debounced so a fast work run doesn't
        // re-scan the lister on every single yield drop.
        private const float SweepRadius = 12f;          // "around the same place" — matches BulkHaul's search floor
        private const int SweepMaxStacks = 24;          // matches BulkHaul.MaxStacks — beyond is next cycle's work
        private const int SweepCooldownTicks = 250;     // ~4s: at most one scan per pawn per cooldown

        /// <summary>
        /// AREA CLEANUP: while a pawn is at a work spot scooping its OWN yields, also sweep OTHER loose
        /// haulable items lying nearby INTO its inventory — recorded as pending self-pickups, so the same
        /// self-pickup job grabs them and they ride out on the one consolidated unload trip. This makes
        /// deconstructing / mining / harvesting clear the surrounding clutter in passing, instead of leaving
        /// scattered stacks for separate hand-hauls. It is the bulk-haul sweep (which only fires on dedicated
        /// HAUL jobs) extended to WORK jobs; the items are picked up into INVENTORY, never hand-carried.
        ///
        /// Eligibility mirrors the bulk-haul sweep exactly so it never steals another hauler's target or pulls
        /// stock back out of storage: an item is swept only if it needs hauling (in the haul lister, not already
        /// stored), is not forbidden to this pawn, is not already claimed by another pawn's job, this pawn can
        /// legally haul it, there is room left under the overload ceiling, and it has a better storage to go to
        /// (else it would only strand in the pack). Per-pawn cooldown so a busy work run scans at most once per
        /// <see cref="SweepCooldownTicks"/>. No-op unless the pawn is scoop-eligible and the feature is enabled.
        /// </summary>
        internal static void MaybeSweepNearbyIntoPending(Pawn pawn, IntVec3 anchor)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.sweepNearbyWhileWorking)
                return;
            var map = pawn?.Map;
            if (map == null || pawn.jobs == null || !anchor.IsValid || !IsEligible(pawn))
                return;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null || !comp.autoHaulYields)
                return; // per-pawn opt-out: a toggled-off pawn never sweeps loose items into inventory
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - comp.lastSweepTick < SweepCooldownTicks)
                return;
            // Claim the cooldown whether or not anything is found, so a clean work area isn't re-scanned on
            // every drop. CountToPickUp below also naturally stops the sweep cold once the pawn is full.
            comp.lastSweepTick = now;

            var claimed = RouteSelection.ClaimedByOtherPawns(pawn);
            float radiusSq = SweepRadius * SweepRadius;
            int added = 0;

            // HOIST (HD-MASS): the pawn's capacity and current mass are INVARIANT across this whole sweep — it
            // only QUEUES pending pickups (comp.pendingSelfPickups.Add), it never adds to the pawn's inventory,
            // so the gear+inventory mass that gates "is the pawn full?" doesn't move between candidates. Read it
            // ONCE here (through the per-(pawn,tick) memo, so it's the SAME read the per-cell MoveSpeed StatPart
            // uses this tick) instead of re-walking apparel+equipment+inventory per candidate inside the gate.
            // Only the per-thing unit mass varies, which the primitive CountToPickUp overload reads per candidate.
            var mass = PawnMassCache.MassInfo(pawn);
            float sweepMaxCap = mass.Capacity;
            float sweepBaseCap = CarryMath.EffectiveCapacity(sweepMaxCap, s.carryLimitFraction);
            float sweepCur = mass.CurrentMass;

            // One candidate's full filter+queue step; returns true to keep sweeping, false to stop (cap reached).
            bool TryQueue(Thing t)
            {
                if (added >= SweepMaxStacks)
                    return false; // beyond the per-scan cap is simply the next work cycle's cleanup
                if (t == null || !t.Spawned || t.Map != map || t is Corpse)
                    return true; // corpses keep their own vanilla hauling flow (they don't belong in pockets)
                if (t.def == null || !t.def.EverHaulable)
                    return true; // same gate as BulkHaul.BuildPool — sweep exactly what a dedicated bulk haul would
                if ((t.Position - anchor).LengthHorizontalSquared > radiusSq)
                    return true; // only the immediate work area
                if (comp.pendingSelfPickups.Contains(t) || t.IsForbidden(pawn) || claimed.Contains(t) || t.IsInValidStorage())
                    return true;
                // Capacity first (pure arithmetic) — at/over the overload ceiling nothing more fits, so don't
                // pay the reachability/storage cost. cur mass doesn't change as we record (we only queue
                // pending), so the hoisted (sweepMaxCap/sweepBaseCap/sweepCur) read above gates the whole sweep
                // off once the pawn is full — identical decision to the live read, without re-walking mass.
                if (OverloadGate.CountToPickUp(pawn, t, s, sweepMaxCap, sweepBaseCap, sweepCur) <= 0)
                    return true;
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                    return true; // unreachable / can't legally haul it
                if (!StoreUtility.TryFindBestBetterStorageFor(t, pawn, map, StoreUtility.CurrentStoragePriorityOf(t),
                        pawn.Faction, out _, out _, needAccurateResult: false))
                    return true; // nowhere better to put it -> don't scoop it only to strand it in inventory
                comp.pendingSelfPickups.Add(t);
                added++;
                return true;
            }

            // Cast to the concrete HashSet<Thing> backing the lister (ThingsPotentiallyNeedingHauling returns the
            // ICollection<Thing> interface; the field is a HashSet<Thing>, decompile-verified) so the foreach binds
            // the struct enumerator and boxes nothing on this per-item-place sweep. `as` + null fallback to the
            // interface foreach future-proofs against a backing-type change (then degrades to the boxed enumerator).
            var haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
            var haulableSet = haulables as HashSet<Thing>;
            if (haulableSet != null)
            {
                foreach (var t in haulableSet)
                    if (!TryQueue(t))
                        break;
            }
            else
            {
                foreach (var t in haulables)
                    if (!TryQueue(t))
                        break;
            }

            if (added > 0)
            {
                HDLog.Dbg($"{pawn} area-sweep: queued {added} nearby loose stack(s) into self-pickup.");
                EnsureSelfPickupJob(pawn, comp);
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
            // On a non-home / temporary map there is no storage to unload to — divert the heavy load onto the
            // nearest owned pack animal instead (auto-divert), so the pawn doesn't keep working over-encumbered.
            if (pawn?.Map != null && !pawn.Map.IsPlayerHome)
            {
                PackAnimalLoad.MaybeAutoDivert(pawn, s);
                return;
            }
            // forced: bypass the post-pickup grace period — being full IS the signal to unload.
            PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true);
        }

        // NOTE: the old "unload as soon as over 100% capacity" trigger (MaybeUnloadBecauseOverEncumbered)
        // was REMOVED. It defeated the core overload design: the whole point is to keep scooping into
        // inventory PAST 100% — up to the smart-overload ceiling (MaxOverloadRatio, ~2x at "Fair"), where
        // carrying more stops paying off — and unload then (MaybeUnloadBecauseFull) or when the pawn is
        // genuinely DONE (the settle period; see UnloadPolicy/TryGetEndOfRunUnloadJob). Unloading at 100%
        // made pawns trip back and forth per item (mine one ore → carry → unload → repeat) instead of
        // accumulating a big load and making one trip.

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

        // ---- Deconstruct path (leaving CAPTURE) -------------------------------------------------
        //
        // We credit a deconstruct's leavings by CAPTURING each item DoLeavingsFor actually places — wherever
        // it lands — instead of snapshotting the building's footprint rect and diffing afterward. The old
        // footprint diff missed leavings in two real, in-game-observed cases: vanilla places leavings with
        // ThingPlaceMode.Near, which SPILLS them outside the (often 1-cell) footprint when it is blocked (a
        // wall hemmed in by full storage), and a leaving that MERGES into a pre-existing ground stack reads
        // as "already there" in the before-snapshot. The capture sees the exact placed/merged Thing via the
        // same patched GenPlace overload DoLeavingsFor routes through, so both cases are handled.

        // The deconstructor whose leavings are currently being placed + the exact stacks placed. Set for the
        // duration of one Deconstruct DoLeavingsFor; the GenPlace postfix feeds each placement here and
        // EndDeconstructCapture scoops them once placement finishes. [ThreadStatic] per this assembly's
        // convention for hook-reachable scratch state (matches `routing`).
        [ThreadStatic] private static Pawn deconstructCapturePawn;
        [ThreadStatic] private static List<Thing> deconstructCapturedLeavings;

        /// <summary>Begin crediting DoLeavingsFor's placements to the pawn deconstructing
        /// <paramref name="diedThing"/> (resolved here, while that pawn is still on its deconstruct toil).
        /// No-op when deconstruct hauling is off or no deconstructor is found (a mod/auto/explosion removal —
        /// those leavings fall to normal hauling).</summary>
        public static void BeginDeconstructCapture(CellRect area, Map map, Thing diedThing)
        {
            deconstructCapturePawn = null;
            deconstructCapturedLeavings = null;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.haulDeconstruct || map == null)
                return;
            var pawn = FindDeconstructor(area, map, diedThing);
            if (pawn == null)
                return;
            deconstructCapturePawn = pawn;
            deconstructCapturedLeavings = new List<Thing>();
        }

        /// <summary>The patched GenPlace postfix hands every item DoLeavingsFor places here while a capture is
        /// active (those placements carry no producer of their own). Records the exact placed/merged stack
        /// (deduped) to scoop once DoLeavingsFor finishes. (Edge: if Near-placement splits ONE leaving stack
        /// across multiple cells — partial-absorb into a near-full stack, remainder placed elsewhere — only the
        /// final resulting stack is captured; the partial-merge fragment stays loose-haulable and is picked up
        /// by normal hauling. No units are lost, and this is strictly better than the old footprint diff.)</summary>
        internal static void CaptureDeconstructLeaving(Thing placed)
        {
            var list = deconstructCapturedLeavings;
            if (list == null || placed == null)
                return;
            if (!list.Contains(placed))
                list.Add(placed);
        }

        /// <summary>Finish the capture: scoop every distinct leaving the deconstruct placed (wherever it
        /// landed), skipping anything that landed in valid storage or is forbidden. DropThenHaul records a
        /// self-pickup; DirectToInventory scoops inline — same as the producer's other yields.</summary>
        public static void EndDeconstructCapture()
        {
            var pawn = deconstructCapturePawn;
            var leavings = deconstructCapturedLeavings;
            deconstructCapturePawn = null;
            deconstructCapturedLeavings = null;
            if (pawn == null || leavings == null || leavings.Count == 0)
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return;
            int scooped = 0;
            for (int i = 0; i < leavings.Count; i++)
            {
                var t = leavings[i];
                if (t == null || !t.Spawned || t.Destroyed || t.def == null || t.def.category != ThingCategory.Item)
                    continue;
                // Never pull stored stock back out (it merged into a stockpile); respect per-pawn forbiddance.
                if (t.IsForbidden(pawn) || t.IsInValidStorage())
                    continue;
                if (s.pickupMode == PickupMode.DropThenHaul)
                    RecordSelfPickup(pawn, t); // leavings are already on the ground -> scoop them up afterward
                else
                    RouteIntoInventory(pawn, t, HaulSourceType.Deconstruct, out _, out _);
                scooped++;
            }
            if (scooped > 0)
                HDLog.Dbg($"{pawn} deconstruct: captured {scooped} leaving stack(s) for pickup.");
        }

        // ---- shared routing ---------------------------------------------------------------------

        /// <summary>
        /// True if <paramref name="thing"/> has a real (better) storage destination this pawn could haul it to —
        /// the same gate the nearby-sweep and bulk-haul pool use. Used so HD never scoops a yield it cannot
        /// deliver: an undeliverable yield left on the ground stays where vanilla hauling / listerHaulables see it,
        /// instead of riding the pack to a desperate far/feet drop at unload time (the "drops it at a random spot"
        /// bug). needAccurateResult:false consumes no Rand and does no pathfinding — cheap on the hot scoop path.
        /// </summary>
        internal static bool HasScoopDestination(Pawn pawn, Thing thing)
        {
            var map = pawn?.Map;
            if (map == null || thing?.def == null)
                return false;
            return StoreUtility.TryFindBestBetterStorageFor(thing, pawn, map,
                StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out _, out _, needAccurateResult: false);
        }

        private static bool RouteIntoInventory(Pawn pawn, Thing thing, HaulSourceType type, out Thing taken, out bool fullyConsumed)
        {
            taken = null;
            fullyConsumed = false;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;
            // Don't scoop a yield with nowhere to go — leave it on the ground at the work spot (vanilla behavior).
            // Otherwise it would ride the pack to a desperate far/feet drop at unload (the random-drop bug).
            if (!HasScoopDestination(pawn, thing))
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
                    // Tag the SPECIFIC scooped Thing for any non-stacking item (stackLimit 1 — every weapon and
                    // every quality/HP-bearing item). Those never merge, so `split` is always the exact loot we just
                    // picked up, NOT a same-def sidearm InventoryStackOfDef might otherwise return (which would ship
                    // the pawn's own 99%-quality sidearm to storage and keep a hauled 3% one). Stackable items
                    // (stackLimit > 1) are fungible and carry no per-instance quality, so keep the by-def relink
                    // unchanged — it tags the grown stack and re-notifies CE's HoldTracker of the merged delta via
                    // `moved` (which a tag on a folded-in residue would otherwise drop).
                    Thing held = split.def.stackLimit == 1
                        ? split
                        : (InventoryStackOfDef(owner, split.def, pawn) ?? (allMoved ? split : null));
                    if (held != null)
                        comp.RegisterHauledItem(held, moved);
                    comp.NotifyYieldPicked();
                    HDLog.Dbg($"{pawn} scooped x{before - split.stackCount} {split.def?.label} ({type}).");
                    // DirectToInventory: the yield went straight into the pack (no SelfPickup job from the drop),
                    // so clean the surrounding area from here — queue nearby loose haulables for a self-pickup
                    // sweep. Cooldown-debounced; safe under the `routing` guard (it places nothing, only records
                    // pending + enqueues a job).
                    MaybeSweepNearbyIntoPending(pawn, thing.Position);
                    // Keep accumulating: do NOT unload merely for crossing 100% here. Scooping continues up
                    // to the smart-overload ceiling (CountToPickUp returns 0 there → MaybeUnloadBecauseFull),
                    // and otherwise the load is unloaded only when the pawn is done for a while (settle) or on
                    // the interval / idle backstop. This is what makes mining/deconstruct one trip, not many.
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

        internal static Thing InventoryStackOfDef(ThingOwner owner, ThingDef def, Pawn pawn = null)
        {
            if (owner == null || def == null)
                return null;
            // Prefer a same-def stack that is NOT a genuine Simple Sidearms remembered sidearm, so the scoop's
            // unload-tag never lands on the pawn's own sidearm (weapons don't stack, so a sidearm of the scooped
            // weapon's def is a separate same-def stack that could be returned here as "first of def"). A remembered
            // sidearm is only returned as a last resort (all same-def stacks are sidearms — rare), so the caller
            // still has a stack to register. pawn==null keeps the old behavior for non-scoop callers.
            Thing fallback = null;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t?.def != def)
                    continue;
                if (pawn != null && SimpleSidearmsCompat.IsRememberedSidearm(pawn, t))
                {
                    if (fallback == null)
                        fallback = t;
                    continue;
                }
                return t;
            }
            return fallback;
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

        /// <summary>Total units of (def, stuff) across all of the owner's stacks. Stuff is compared with == so a
        /// null (stuffless, e.g. most ranged weapons) stuff matches a null remembered stuff — mirroring how
        /// <see cref="SimpleSidearmsCompat.RememberedCount"/> compares. Used for the count-aware sidearm keep.</summary>
        internal static int InventoryCountOfPair(ThingOwner owner, ThingDef def, ThingDef stuff)
        {
            if (owner == null || def == null)
                return 0;
            int total = 0;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t?.def == def && t.Stuff == stuff)
                    total += t.stackCount;
            }
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
                    // Resolve the work type BEFORE the candidate gate: an explicit player Strip order
                    // (the only source that overrides the per-pawn opt-out — OptOutOverridePolicy) must
                    // be recognized so IsCandidate bypasses a toggled-off pawn's "Auto-haul yields" flag.
                    // TryGetWorkType only reads the live driver (no side effects), so this reorder is free.
                    if (!(things[j] is Pawn p) || !TryGetWorkType(p.jobs?.curDriver, out var t)
                        || !IsCandidate(p, overrideOptOut: OptOutOverridePolicy.ExplicitOrderOverridesOptOut(t)))
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
        /// <param name="overrideOptOut">When true, the per-pawn "Auto-haul yields" opt-out is BYPASSED
        /// (the comp must still exist, and every other gate — faction, race eligibility, map — still
        /// applies). Set only for an EXPLICIT player order whose yield should be scooped regardless of the
        /// standing toggle: an explicit Strip order (a <c>JobDriver_Strip</c>, which is always
        /// player-ordered — see <see cref="Core.OptOutOverridePolicy"/>). Autonomous yields leave it
        /// false so the toggle governs them.</param>
        public static bool IsCandidate(Pawn p, bool overrideOptOut = false)
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
            var comp = p.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;
            // Per-pawn opt-out: a pawn toggled OFF never scoops/sweeps/self-picks AUTONOMOUS yields. An
            // explicit player order (overrideOptOut) bypasses the toggle — the player asked for it by hand,
            // so the dropped gear is still scooped+hauled. Unload paths don't read this flag either way, so
            // a toggled-off pawn always empties what it carries.
            return overrideOptOut || comp.autoHaulYields;
        }

        /// <summary>
        /// Drafted-agnostic RACE eligibility: humanlike, OR mechanoid when allowMechanoids, OR animal
        /// (non-humanlike non-mech) when allowAnimals — WITHOUT the drafted/incapable gating. This is the
        /// visibility test for the standing per-pawn auto-haul toggle (a drafted pawn must still be able to
        /// SET the preference, so the toggle can't vanish under the pauseWhileDrafted gate that
        /// <see cref="IsEligible"/> applies). It is strictly broader than IsEligible, so it never SHOWS the
        /// toggle on a pawn that could never scoop: the runtime scoop gates still call IsEligible and honor
        /// pauseWhileDrafted/allowIncapable, so showing the toggle while drafted never makes a drafted pawn
        /// actually scoop. Reuses the SAME Core policy as IsEligible with isDrafted/incapableOfHauling pinned
        /// false, so the two stay in lockstep on the race rules (only the race branches can pass).
        /// </summary>
        public static bool IsRaceEligible(Pawn p)
        {
            if (p?.RaceProps == null)
                return false;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return false;
            bool raceEligible = EligibilityPolicy.IsEligible(
                isMechanoid: p.RaceProps.IsMechanoid,
                isHumanlike: p.RaceProps.Humanlike,
                isDrafted: false,            // standing preference — not gated on the pawn's current drafted state
                incapableOfHauling: false,   // capability is a runtime scoop gate (IsEligible), not a visibility gate
                allowMechanoids: s.allowMechanoids,
                pauseWhileDrafted: s.pauseWhileDrafted,
                allowIncapable: s.allowIncapable,
                allowAnimals: s.allowAnimals);
            // #4 dead-gizmo fix (lockstep with IsEligible): an animal that can never use the Haul
            // feature (a cat) must not SHOW the auto-haul toggle either — CanScoopAsAnimal narrows only
            // the animal branch (humanlikes/mechs untouched), so with allowAnimals OFF the animal branch
            // is already false and this is byte-identical. Keeps the toggle visible exactly on the set of
            // animals the runtime scoop gate (IsEligible) will accept.
            return raceEligible && CanScoopAsAnimal(p);
        }

        public static bool IsEligible(Pawn p)
        {
            if (p?.RaceProps == null)
                return false;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return false;
            bool eligible = EligibilityPolicy.IsEligible(
                isMechanoid: p.RaceProps.IsMechanoid,
                isHumanlike: p.RaceProps.Humanlike,
                isDrafted: p.Drafted,
                // Type-level, not WorkTags.Hauling: "incapable of dumb labor" disables via
                // ManualDumb/Commoner without setting the Hauling tag, and the type check also
                // composes with the F23 all-pawns-can-haul override.
                incapableOfHauling: p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling),
                allowMechanoids: s.allowMechanoids,
                pauseWhileDrafted: s.pauseWhileDrafted,
                allowIncapable: s.allowIncapable,
                // #4 (opt-in, default OFF): a non-mech non-humanlike (colony animal) is eligible only
                // when allowAnimals is on — so with it off this whole arg is false and eligibility is
                // byte-identical to before (animals fall through EligibilityPolicy's animal branch to
                // false exactly as the absent-arg default did).
                allowAnimals: s.allowAnimals);
            // #4 dead-gizmo fix: even with allowAnimals ON, only an animal that can ACTUALLY use the
            // vanilla Haul behavior may scoop — a cat (not Haul-trainable) would otherwise get the
            // gizmo and scoop-eligibility while the toggle does nothing (colony animals haul via
            // JobGiver_Haul, gated on the Haul trainable being completed). CanScoopAsAnimal narrows
            // ONLY the animal branch; humanlikes/mechs are returned untouched, so allowAnimals=false
            // (animal branch already false) stays byte-identical — the gate short-circuits below.
            return eligible && CanScoopAsAnimal(p);
        }

        // The Haul trainable (defName "Haul") — not exposed on vanilla's TrainableDefOf, so looked up
        // by name (matching the codebase's GetNamedSilentFail convention, e.g. CECompat's "Bulk").
        // Cached after first resolve; null only on a game with the def stripped, handled fail-open below.
        private static TrainableDef haulTrainable;
        private static bool haulTrainableResolved;

        private static TrainableDef HaulTrainable()
        {
            if (!haulTrainableResolved)
            {
                haulTrainable = DefDatabase<TrainableDef>.GetNamedSilentFail("Haul");
                haulTrainableResolved = true;
            }
            return haulTrainable;
        }

        /// <summary>
        /// #4: gate the animal branch on actual Haul-feature capability. Returns true for every
        /// non-animal (humanlike / mechanoid) so it never changes their eligibility. For an animal it
        /// is true only when the pawn can really haul via the vanilla Haul trainable — i.e. the Haul
        /// row is assignable for this race (CanAssignToTrain: a cat fails "not smart enough", a husky/
        /// bear passes regardless of training progress) OR the animal has already learned Haul. This
        /// keeps a non-Haul-trainable animal (cat) from showing the auto-haul toggle / scooping where
        /// it would do nothing, while a Haul-trained husky/bear still qualifies. Fail-open if the
        /// training tracker or the Haul def is missing (so a modded edge never silently hides it).
        /// </summary>
        private static bool CanScoopAsAnimal(Pawn p)
        {
            // Only animals are narrowed; humanlikes and mechanoids are unaffected.
            if (p.RaceProps.IsMechanoid || p.RaceProps.Humanlike)
                return true;
            var training = p.training;
            var haul = HaulTrainable();
            if (training == null || haul == null)
                return true; // can't evaluate capability -> don't hide it (fail-open)
            return training.CanAssignToTrain(haul).Accepted || training.HasLearned(haul);
        }
    }
}
