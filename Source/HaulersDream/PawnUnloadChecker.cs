using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Queues the single unload pass when appropriate. Called from the idle backstop, the interval
    /// GameComponent, and the "unload now" gizmo (forced). Honors the unload grace period so a pawn
    /// doesn't unload in the middle of a stream of pickups.
    /// </summary>
    public static class PawnUnloadChecker
    {
        /// <param name="behindQueuedWork">Queue the unload BEHIND any pending real work instead of in front
        /// of it. The bulk-haul finish flush passes true: a player order that interrupted the sweep (vanilla
        /// TryTakeOrderedJob EnqueueFirst's the order, then ends the job — our finish action runs after) must
        /// be obeyed first; the load still flushes right after (forced stays true, so strict/grace/markForUnload
        /// can't strand it).</param>
        public static void CheckIfShouldUnload(Pawn pawn, bool forced = false, bool behindQueuedWork = false)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp == null)
                return;

            // Unconditional, like every other call site in the mod — settings is dereferenced below
            // (unloadGraceTicks) regardless of the branch that used to null-check it.
            var settings = HaulersDreamMod.Settings;
            if (settings == null)
                return;

            // A pawn mid bill-prep-gather is CARRYING INGREDIENTS TO A BENCH ON PURPOSE — an auto-unload queued now
            // would run before the bill re-scan (queued jobs precede work) and dump the whole gathered load back to
            // storage, wasting the entire sweep. Only the explicit gizmo (forced) may override.
            if (!forced && pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BillPrepGather)
                return;

            // On a map the mod is configured to leave alone (enableOnNonHomeMaps off + a temporary/encounter
            // map), an automatic unload would just dump the tagged load at the pawn's feet — there's no storage
            // there. Keep carrying; the load unloads at home. The explicit gizmo still works.
            if (!forced && !settings.enableOnNonHomeMaps
                && pawn.Map != null && !pawn.Map.IsPlayerHome)
                return;

            var carried = comp.GetHashSet();
            // A FORCED unload (the gizmo, an end-of-batch flush) is RECOVERY, not work — it must function even
            // for a pawn that became scoop-ineligible (drafted with pauseWhileDrafted, hauling-incapable after a
            // settings flip), or the recovery button is silently dead while tagged stock strands in inventory.
            bool eligible = pawn.Faction == Faction.OfPlayerSilentFail
                            && (forced || YieldRouter.IsEligible(pawn))
                            && pawn.inventory?.innerContainer != null;
            int inventoryCount = pawn.inventory?.innerContainer?.Count ?? 0;
            bool alreadyUnloading = pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory
                                    || HasQueuedUnload(pawn);
            int ticksSinceYield = (Find.TickManager?.TicksGame ?? 0) - comp.lastYieldTick;
            // Pending REAL work = a queued job that is the pawn's actual work (e.g. a shift-prioritized
            // harvest route). An automatic unload must not be EnqueueFirst'd ahead of it; only a forced
            // unload (gizmo / full / debug) may. We deliberately EXCLUDE our OWN housekeeping jobs
            // (self-pickup / unload): the idle backstop EnqueueFirst's a self-pickup BEFORE calling this, so
            // counting it as "work" would make the auto-unload skip every time — a livelock that strands
            // goods in strict mode (where the full-trigger never fires to break it).
            bool hasPendingWork = HasPendingRealWork(pawn);

            // All the gating logic lives in the (unit-tested) pure policy.
            var decision = UnloadPolicy.Decide(eligible, carried.Count, inventoryCount, alreadyUnloading, forced,
                hasPendingWork, ticksSinceYield, settings.unloadGraceTicks);

            switch (decision)
            {
                case UnloadDecision.ClearTracker:
                    // Targeted prune, NOT a whole-set Clear: during a craft the tagged ingredients legitimately
                    // move inventory→hands→bench (inventoryCount dips below the tracked count), and wiping every
                    // tag then would permanently strand the pawn's OTHER tagged stock in inventory. Removing only
                    // entries no longer in the inventory keeps valid tags; destroyed ones self-prune in GetHashSet.
                    HDLog.Dbg($"{pawn} tracker out of sync ({inventoryCount} < {carried.Count}); pruning stale tags.");
                    var inv = pawn.inventory?.innerContainer;
                    carried.RemoveWhere(t => t == null || t.Destroyed || inv == null || !inv.Contains(t));
                    return;

                case UnloadDecision.Queue:
                    // The unload driver sets its own A/B targets in its toils, so no initial target.
                    var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory);
                    if (pawn.jobs != null && job.TryMakePreToilReservations(pawn, false))
                    {
                        if (behindQueuedWork && hasPendingWork)
                            pawn.jobs.jobQueue.EnqueueLast(job, JobTag.Misc);
                        else
                            pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
                        HDLog.Dbg($"{pawn} queued unload ({carried.Count} tracked, forced={forced}).");
                        // Both EnqueueFirst, so the queue reads [SelfPickup, Unload]: pending fresh drops are
                        // scooped BEFORE the unload runs — one trip regardless of which trigger queued the
                        // unload (the interval firing mid-long-job otherwise yields [Unload, SelfPickup]: a
                        // second trip). EnsureSelfPickupJob dedups, no-ops without pendings, and never calls
                        // back into this checker. On the behindQueuedWork path the scoop still lands ahead of
                        // the queued work (acceptable: it's quick and at the pawn's feet) while the unload
                        // trip waits at the back.
                        YieldRouter.EnsureSelfPickupJob(pawn);
                    }
                    return;

                default:
                    return;
            }
        }

        private static bool HasQueuedUnload(Pawn pawn)
        {
            var queue = pawn.jobs?.jobQueue;
            if (queue != null)
                foreach (var qj in queue)
                    if (qj?.job?.def == HaulersDreamDefOf.HaulersDream_UnloadInventory)
                        return true;
            return false;
        }

        /// <summary>
        /// True if the pawn has a queued job that is its OWN work (anything but the mod's self-pickup /
        /// unload housekeeping jobs) — i.e. it's mid-run and an automatic unload should defer behind it.
        /// Delegates to the unit-tested pure <see cref="UnloadPolicy.HasPendingRealWork"/>.
        /// </summary>
        private static bool HasPendingRealWork(Pawn pawn)
        {
            var queue = pawn.jobs?.jobQueue;
            if (queue == null)
                return false;
            var defNames = new List<string>();
            foreach (var qj in queue)
                if (qj?.job?.def != null)
                    defNames.Add(qj.job.def.defName);
            return UnloadPolicy.HasPendingRealWork(defNames,
                HaulersDreamDefOf.HaulersDream_SelfPickup.defName,
                HaulersDreamDefOf.HaulersDream_UnloadInventory.defName);
        }
    }
}
