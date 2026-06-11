using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Walks the bulk-haul pickup plan (see <see cref="BulkHaul"/>): visit each planned stack in chain order,
    /// load the planned count into INVENTORY (tagged in <see cref="CompHauledToInventory"/>), and when the
    /// chain is done force the single storage-aware unload pass — one trip to storage for the whole sweep.
    ///
    /// Safety by construction: this job only MOVES things (SplitOff + TryAdd, with a place-back fallback) and
    /// tags them, so anything loaded is reclaimed by the unload pass no matter how the job ends — interrupted
    /// by a threat, a stack sniped mid-walk, whatever. Per-stack validity is re-checked at each step (skip,
    /// never fail the whole job), and the live mass ceiling is re-applied at pickup time so a mid-job change
    /// (gear picked up, settings changed) can't over-load past the worth-it point.
    ///
    /// Stacks are added WITHOUT merging into existing inventory stacks: tagging a merged stack would also
    /// flag the pawn's own pre-existing stock (a packed lunch of the same meal def) for unload. A separate
    /// entry keeps the sweep's stock exactly distinguishable; the unload pass consolidates at placement.
    /// </summary>
    public class JobDriver_BulkHaul : JobDriver
    {
        private const TargetIndex PrimaryInd = TargetIndex.A; // the clicked/assigned haulable (report anchor)
        private const TargetIndex StackInd = TargetIndex.B;   // scratch: the stack currently being walked to

        private int loadIndex;
        private bool loadedAnything;

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdBulkLoadIndex", 0);
            Scribe_Values.Look(ref loadedAnything, "hdBulkLoadedAnything", false);
        }

        public override string GetReport() => "HaulersDream.BulkHaul.Report".Translate();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // The primary must be ours (it's what the work scan / order assigned); the rest of the sweep is
            // best-effort — a stack another pawn reserved first is simply skipped by the per-step validity.
            var queue = job.GetTargetQueue(StackInd);
            if (queue == null || queue.Count == 0)
                return false;
            if (!pawn.Reserve(queue[0], job, 1, -1, null, errorOnFailed))
                return false;
            pawn.ReserveAsManyAsPossible(queue, job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            // Whatever way the job ends — completed, interrupted, target gone — the swept stock is tagged, so
            // flush it to storage now ("when done THEN unload"). With nothing loaded this is a cheap no-op.
            AddFinishAction(delegate
            {
                if (loadedAnything)
                    PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true);
            });

            Toil end = Toils_General.Label();

            Toil loadDecide = ToilMaker.MakeToil("HD_Bulk_LoadDecide");
            loadDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                float ceiling = CeilingKgLive(HaulersDreamMod.Settings);
                bool roomLeft = float.IsPositiveInfinity(ceiling)
                                || MassUtility.GearAndInventoryMass(pawn) < ceiling - 0.0001f;
                while (roomLeft && queue != null && loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    // The playerForced primary may be forbidden (that's what forcing means); swept extras are
                    // never taken while forbidden. Stacks in someone's inventory (claimed mid-walk) are gone.
                    bool forbiddenOk = t != null && !t.IsForbidden(pawn);
                    if (!forbiddenOk && loadIndex == 0 && job.playerForced)
                        forbiddenOk = true;
                    bool valid = t != null && t.Spawned && forbiddenOk
                                 && !(t.ParentHolder is Pawn_InventoryTracker)
                                 && counts != null && loadIndex < counts.Count && counts[loadIndex] > 0
                                 && (pawn.CanReserve(t) || pawn.Map.reservationManager.ReservedBy(t, pawn, job));
                    if (valid)
                        break;
                    loadIndex++;
                }
                if (!roomLeft || queue == null || loadIndex >= queue.Count) { JumpToToil(end); return; }
                job.SetTarget(StackInd, queue[loadIndex].Thing);
            };
            loadDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadDecide;

            Toil loadGoto = ToilMaker.MakeToil("HD_Bulk_LoadGoto");
            loadGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(loadDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            loadGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return loadGoto;

            Toil take = ToilMaker.MakeToil("HD_Bulk_Take");
            take.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                var counts = job.countQueue;
                int planned = counts != null && loadIndex < counts.Count ? counts[loadIndex] : 0;
                if (t == null || !t.Spawned || planned <= 0) { loadIndex++; JumpToToil(loadDecide); return; }

                // Re-clamp the planned count to the LIVE remaining room (mass may have shifted since planning).
                int count = BulkHaulPolicy.CountWithinCeiling(CeilingKgLive(HaulersDreamMod.Settings),
                    MassUtility.GearAndInventoryMass(pawn), t.GetStatValue(StatDefOf.Mass),
                    System.Math.Min(planned, t.stackCount));
                // Under Combat Extended also clamp to CE's live weight+bulk fit (exact — CE's inventory cache
                // updates after every add, so the plan's optimism self-corrects here).
                count = System.Math.Min(count, CECompat.MaxFitCount(pawn, t));
                if (count <= 0) { loadIndex++; JumpToToil(loadDecide); return; }

                // SplitOff with count >= stackCount despawns the thing itself (the full-stack pickup path);
                // a partial split returns a fresh unspawned thing. Either way TryAdd takes the plain-add path.
                var split = t.SplitOff(count);
                var inv = Inv;
                if (inv != null && inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    if (comp != null)
                    {
                        comp.RegisterHauledItem(split);
                        comp.NotifyYieldPicked();
                    }
                    // Unspawned splits carry a default (0,0,0) position; the shared-inventory chooser ranks
                    // carried stock by position, so stamp the pawn's cell (a plain field write when unspawned).
                    if (!split.Spawned)
                        split.Position = pawn.Position;
                    loadedAnything = true;
                }
                else if (split != null && !split.Destroyed && !split.Spawned)
                {
                    // Add failed (shouldn't happen — pawn inventories are effectively unbounded): put it back
                    // on the ground rather than ever letting an item vanish.
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
                loadIndex++;
                JumpToToil(loadDecide);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;

            yield return end;
        }

        // The live worth-it mass ceiling for THIS pawn (per-pawn base cap × the overload break-even ratio).
        private float CeilingKgLive(HaulersDreamSettings s)
        {
            if (s == null)
                return float.PositiveInfinity;
            float baseCap = CarryMath.EffectiveCapacity(MassUtility.Capacity(pawn), s.carryLimitFraction);
            return BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverload(s), baseCap);
        }
    }
}
