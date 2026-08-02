using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /*
        ──────────────────────────────────────────
              Construction material hold
        ──────────────────────────────────────────
        "Don't ship a builder's leftover steel to the stockpile between two wall tiles."

        Reported (Steam, mod-disabling, reproduces at stock defaults): construction pawns appeared to
        "unload after every construction, running back to the stockpile after every single wall tile".
        The leftover from one frame's delivery is exactly what the NEXT frame eats, so the trip costs two
        walks and buys nothing. Vanilla has no such behaviour to inherit — carrying construction material in
        the pack is entirely this mod's, so the fix has to be here too.

        → KEY: this is a HOLD, never a drop. Nothing is dropped, destroyed or untagged. The material stays
          tagged in the pack and unloads on the very next trigger the moment ANY signal goes false.
        → GOTCHA: every caller must pass its own `forced` flag. A forced unload (the "Unload now" gizmo, a
          finish flush, the mech shed-before-charge) must never be held — the player asked for it.
    */

    /// <summary>
    /// Answers one question for the automatic-unload triggers: <b>is a tagged stack in this pawn's pack wanted by
    /// construction the pawn is about to do?</b> If so the automatic unload stands down and the pawn keeps the
    /// material for the build. The gating math is the pure <see cref="ConstructionHoldPolicy"/>; this gathers the
    /// live signals.
    ///
    /// <para>Mirrors the shape of the existing crafting guard (<c>PawnUnloadChecker.HoldsStockForActiveDoBill</c>):
    /// a bill about to consume carried stock already suppresses the automatic unload, and construction had no
    /// equivalent, so a builder's leftovers were fair game the instant a frame completed.</para>
    /// </summary>
    internal static class ConstructionMaterialHold
    {
        /// <summary>
        /// True when <paramref name="pawn"/> should KEEP the construction material it carries instead of making an
        /// automatic storage trip with it. Always false for a forced unload.
        /// </summary>
        /// <param name="pawn">The carrier. Null / comp-less / off-map pawns never hold.</param>
        /// <param name="comp">The pawn's tagged-cargo comp (read-only here — <c>PeekHashSet</c>, never the
        /// self-healing <c>GetHashSet</c>: this is a decision path, not a mutation path).</param>
        /// <param name="forced">The caller's forced flag; a forced unload never holds.</param>
        internal static bool HoldsMaterialForActiveConstruction(Pawn pawn, CompHauledToInventory comp, bool forced = false)
        {
            var inv = pawn?.inventory?.innerContainer;
            if (inv == null || comp == null || pawn.Map == null)
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            // Cheap half first: a forced unload, or a pawn that has not picked anything up for the whole
            // never-strand window, can be answered without touching the map.
            if (!ConstructionHoldPolicy.MayHoldAtAll(forced, now - comp.lastYieldTick))
                return false;
            var tagged = comp.PeekHashSet();
            if (tagged.Count == 0)
                return false;

            return ConstructionHoldPolicy.ShouldHoldMaterial(forced,
                ConstructionWantsHeldMaterial(pawn, tagged, inv), now - comp.lastYieldTick);
        }

        /// <summary>
        /// More construction work queued on <paramref name="pawn"/> (a route stop, a tethered build, another
        /// delivery)? Then carried material is the NEXT stop's, not a leftover. Hoisted out of
        /// <see cref="JobDriver_OverloadConstructDeliver"/> so its leftover-registration check and this guard read
        /// the same queue the same way. Deliberately NOT the only signal: an AUTONOMOUS builder's queue is empty
        /// between frames (it re-runs the work scan each time), which is precisely the reported case.
        /// </summary>
        internal static bool MoreConstructWorkQueued(Pawn pawn)
        {
            var q = pawn?.jobs?.jobQueue;
            if (q == null)
                return false;
            for (int i = 0; i < q.Count; i++)
            {
                // Indexed walk: JobQueue's enumerator boxes, its indexer does not (see UnloadPolicy.IsPendingRealWork).
                var def = q[i]?.job?.def;
                // HD's construct-deliver pair (HdJobDefSets — the single source of truth) OR vanilla's
                // FinishFrame (a vanilla def, not part of the HD pair, so it stays ORed here).
                if (def != null && (HdJobDefSets.ConstructDeliverJobs.Contains(def) || def == JobDefOf.FinishFrame))
                    return true;
            }
            return false;
        }

        /// <summary>Drop the per-(pawn, tick) memo — game-load hygiene, registered with <see cref="CacheRegistry"/>.</summary>
        internal static void Clear() => memo.Clear();

        // ---- signals -------------------------------------------------------------------------------------

        /// <summary>
        /// The three signals, ORed, memoised per <c>(pawn, tick)</c>: the answer is a pure read of the pawn's job
        /// queue and the map's construction sites, both stable within a tick, and the same pawn is asked several
        /// times per tick (the work-scan divert seam, the end-of-run trigger, the interval sweep).
        /// </summary>
        private static bool ConstructionWantsHeldMaterial(Pawn pawn, HashSet<Thing> tagged, ThingOwner<Thing> inv)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (memo.TryGet(tick, pawn.thingIDNumber, out bool cached))
                return cached;
            bool fresh = OnConstructionJobNow(pawn)
                         || MoreConstructWorkQueued(pawn)
                         || NearbySiteWantsHeldMaterial(pawn, tagged, inv);
            memo.Store(tick, pawn.thingIDNumber, fresh);
            return fresh;
        }

        /// <summary>Signal 1 — the pawn's CURRENT job is construction (finishing a frame, or one of HD's own
        /// inventory construct-deliveries). Deliberately def-agnostic about WHICH material, exactly like the queued
        /// signal: a pawn mid-build run keeps its whole load for the run.</summary>
        private static bool OnConstructionJobNow(Pawn pawn)
            => OpportunisticUnload.ClassifyJobDef(pawn.CurJobDef) == WorkRunKind.Construction;

        /// <summary>
        /// Signal 3, the load-bearing one — a reachable, non-forbidden, player-faction blueprint/frame near the
        /// pawn still needs the def of a tagged stack it is holding. This is what covers AUTONOMOUS construction,
        /// where the job queue is empty between frames because the pawn re-runs the whole work scan each time.
        /// Sites and radius come from the delivery clusterer (<see cref="InventoryConstructDelivery.CollectNeedersNear"/>),
        /// so "close enough to batch into one load" and "close enough to keep holding for" are the same distance.
        /// </summary>
        private static bool NearbySiteWantsHeldMaterial(Pawn pawn, HashSet<Thing> tagged, ThingOwner<Thing> inv)
        {
            var map = pawn.Map;
            // Cheap pre-gate: with nothing under construction anywhere on the map there is no site to hold for, and
            // this is the common case for most pawns most of the time. Two Count reads instead of the per-def
            // lister walks below — which every hauler carrying tagged loot would otherwise pay once a tick.
            if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == 0
                && map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count == 0)
                return false;

            var scan = scratchScan ?? (scratchScan = new List<Thing>());
            var defsSeen = scratchDefs ?? (scratchDefs = new HashSet<ThingDef>());
            defsSeen.Clear();

            // MP determinism: this is an OR over every held def, so the ANSWER does not depend on the set's
            // iteration order (unlike the capacity-bound loops elsewhere, which must sort by thingIDNumber because
            // WHICH stack they reach first changes the outcome). Only the order the work is done in varies.
            foreach (var t in tagged)
            {
                // Only material the pawn ACTUALLY still holds counts (a tag can outlive the stack for a tick), and
                // each def is scanned once however many stacks of it are tagged.
                if (t == null || t.def == null || !inv.Contains(t) || !defsSeen.Add(t.def))
                    continue;

                scan.Clear();
                InventoryConstructDelivery.CollectNeedersNear(map, t.def, pawn, pawn.Position, scan);
                for (int i = 0; i < scan.Count; i++)
                {
                    // Reachability is the expensive check, so it runs last and only until the first hit. Danger.Deadly
                    // matches how the delivery driver itself vets a queued needer, so a site the pawn would deliver to
                    // is a site it will hold material for.
                    if (pawn.CanReach(scan[i], PathEndMode.Touch, Danger.Deadly))
                    {
                        scan.Clear();
                        return true;
                    }
                }
            }
            scan.Clear();
            return false;
        }

        // ---- state ---------------------------------------------------------------------------------------

        // One memo per thread; the struct lazily creates its dictionary, so no field initializer is needed. Matches
        // PawnMassCache's convention — the work scan a threading mod may fan out reaches this.
        [ThreadStatic] private static TickKeyedMemo<bool> memo;

        // Hook-reachable scratch, per this assembly's convention (OpportunisticUnload.scratchRep): cleared at use,
        // never trusted empty, and never stored anywhere that outlives the call.
        [ThreadStatic] private static List<Thing> scratchScan;
        [ThreadStatic] private static HashSet<ThingDef> scratchDefs;

        // Self-register the per-session memo clear with the game-load hygiene sweep (see CacheRegistry). The static
        // ctor runs once, the first time any member here is touched.
        static ConstructionMaterialHold() => CacheRegistry.Register(Clear);
    }
}
