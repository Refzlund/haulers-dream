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

            // A drafted pawn must never start (or queue) an unload — forced and automatic alike. Vanilla's
            // Humanlike think tree places ThinkNode_QueuedJob BEFORE the wait-while-drafted branch and that
            // node has NO draft gate (decompile-verified), so a queued unload WOULD be dequeued and executed
            // while drafted: the pawn marches off to storage mid-raid instead of standing to orders. (The
            // bulk-haul finish flush hits exactly this: drafting runs ClearQueuedJobs BEFORE EndCurrentJob,
            // so our finish action would enqueue into the freshly emptied queue.) The gizmo is a no-op while
            // drafted too; after undrafting, the idle backstop / interval / a fresh gizmo press recovers.
            if (pawn.Drafted)
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
            // for a pawn that became scoop-ineligible (hauling-incapable after a settings flip), or the recovery
            // button is silently dead while tagged stock strands in inventory. (Drafted pawns never get here —
            // the draft gate above wins; the gizmo shows a disabled reason for them.)
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

            // Up to two passes: a ClearTracker outcome PRUNES and then RE-DECIDES with the fresh counts
            // instead of consuming the trigger occurrence. Without the second pass, a pawn whose tagged
            // meal is momentarily in its HANDS (Toils_Ingest moves an inventory meal to the carry tracker
            // while the tag persists) swallowed every trigger that landed during the meal — for a pawn
            // whose inventory is all scooped goods, that silently forfeited entire interval boundaries.
            // The loop always terminates: after the prune, carried ⊆ inventory, so the second Decide can
            // never return ClearTracker again.
            for (int pass = 0; pass < 2; pass++)
            {
                // Is there anything ABOVE keep-stock to actually unload? Recomputed each pass (a ClearTracker
                // prune changes the set). Keeps an all-keep-stock pawn (whose surplus tags we deliberately
                // retain) from re-queuing a no-op unload every cycle; a forced unload ignores this in Decide.
                bool anyUnloadable = AnyUnloadable(pawn, carried);
                // All the gating logic lives in the (unit-tested) pure policy.
                var decision = UnloadPolicy.Decide(eligible, carried.Count, inventoryCount, alreadyUnloading, forced,
                    hasPendingWork, ticksSinceYield, settings.unloadGraceTicks, anyUnloadable);

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
                        inventoryCount = pawn.inventory?.innerContainer?.Count ?? 0;
                        continue;

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
        }

        /// <summary>True if at least one tracked stack still in the pawn's inventory has surplus above the
        /// pawn's personal keep-stock — i.e. the unload pass would actually move something. Uses the SAME
        /// surplus math as the unload driver and the cannot-unload alert (<see cref="InventorySurplus"/>), so
        /// the three never disagree.</summary>
        private static bool AnyUnloadable(Pawn pawn, HashSet<Thing> carried)
        {
            var inner = pawn.inventory?.innerContainer;
            if (inner == null || carried == null)
                return false;
            foreach (var t in carried)
                if (t != null && inner.Contains(t) && InventorySurplus.SurplusOf(pawn, t) > 0)
                    return true;
            return false;
        }

        internal static bool HasQueuedUnload(Pawn pawn)
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
