using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Keep X in inventory": walk to the clicked ground stack and take it into the pawn's inventory as a KEPT item
    /// (registered on <see cref="CompHauledToInventory"/> via <see cref="CompHauledToInventory.RegisterKept"/>), so
    /// HD's unload never hauls it away and vanilla's drop-unused never sheds it. The counterpart to
    /// <see cref="JobDriver_BulkHaul"/>'s "pick up to haul" — here the item is HELD, not stored: single-target, no
    /// nearby sweep, and NO forced unload. The player releases a kept item simply by consuming it or dropping it from
    /// the pawn's gear tab (the comp's heal then forgets it). The take is mass/CE-clamped live and added
    /// canMerge:false so a kept stack never folds into — and thus never wrongly keeps — the pawn's personal or
    /// HD-hauled stock. Ordered via TryTakeOrderedJob (multiplayer auto-syncs the order), and the registration runs
    /// inside the driver on every client, so the kept set stays deterministic.
    /// </summary>
    public class JobDriver_KeepInInventory : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.GetTarget(ItemInd), job, 1, -1, null, errorOnFailed);

        public override string GetReport() => "HaulersDream.Keep.Report".Translate();

        public override IEnumerable<Toil> MakeNewToils()
        {
            // A forced player order may target a FORBIDDEN stack (same leniency as "Pick up X"), so only fail when the
            // stack is gone, not when it is forbidden. Job-bound reservations release at job end (CleanupCurrentJob).
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(ItemInd);

            Toil take = ToilMaker.MakeToil("HD_Keep_Take");
            take.initAction = delegate
            {
                var t = job.GetTarget(ItemInd).Thing;
                if (t == null || !t.Spawned)
                    return;
                var s = HaulersDreamMod.Settings;
                var comp = pawn.GetComp<CompHauledToInventory>();
                var inv = pawn.inventory?.innerContainer;
                if (s == null || comp == null || inv == null)
                    return;
                int planned = job.count > 0 ? job.count : t.stackCount;
                // Re-clamp to LIVE mass/CE room at pickup (the plan-time count can be stale — gear picked up, settings
                // changed since). Same clamp the "Pick up X" builder/driver use.
                int count = BulkHaul.MassClampedTake(pawn, t, planned, s);
                if (count <= 0)
                    return;
                // SplitOff with count >= stackCount despawns the thing itself (full-stack take); a partial split
                // returns a fresh unspawned thing. Either way TryAdd takes the plain-add path.
                var split = t.SplitOff(count);
                if (split == null || split.Destroyed || split.stackCount <= 0)
                    return;
                // canMerge:false keeps the kept stack ISOLATED from personal / HD-hauled stock, so keeping never
                // accidentally protects the pawn's own kit or a scooped haul (mirrors the bulk driver's isolation).
                if (inv.TryAdd(split, canMergeWithExistingStacks: false))
                    comp.RegisterKept(split);
                else if (!split.Destroyed)
                    // Never let the split vanish if inventory somehow refuses it — put it back on the ground.
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;
        }
    }
}
