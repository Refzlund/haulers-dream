using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

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
        /// <param name="immediate">#215: when true (a plain left-click of the "Unload now" gizmo), END the pawn's
        /// current job after queuing the unload so it runs NOW instead of after the current job. Only meaningful on
        /// the EnqueueFirst path (the gizmo always passes behindQueuedWork:false), so a queued-behind-work unload is
        /// never force-interrupted. Defaults false, so every automatic caller (interval, idle backstop, bulk finish)
        /// keeps the prior queue-behind-current behavior.</param>
        public static void CheckIfShouldUnload(Pawn pawn, bool forced = false, bool behindQueuedWork = false,
            bool immediate = false)
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

            // Never queue an AUTOMATIC unload onto a pawn resting for medical care / in bed: it would be dequeued
            // the moment LayDown re-evaluates and yank the patient upright, then the medical think tree lays it back
            // down — the reported "patient waiting for treatment stands up and lies back down" thrash. A FORCED
            // unload (the "unload now" gizmo, the bulk-haul finish flush) is a deliberate override and still runs.
            // This single chokepoint covers both the interval loop and the idle backstop. See ProtectedWork.
            if (!forced && ProtectedWork.IsRestingPatient(pawn))
                return;

            // #4 Lord-activity stand-down (AUTOMATIC only): a pawn under a Lord is in a directed group activity —
            // a ritual (its offering, e.g. bioferrite for an Anomaly psychic ritual, sits in inventory ON PURPOSE
            // and a gather toil reads pawn.inventory directly), caravan forming, a party / marriage / gathering, a
            // quest lord. An automatic unload queued now would ship that purposeful inventory off to storage before
            // the activity consumes it (the reported "pawns empty their inventory before the ritual → ritual fails"
            // bug). YieldRouter.IsEligible also returns false for a Lord pawn (so the `eligible` gate below stands
            // the autonomous adopt+unload down independently), but this explicit early-out makes the intent
            // unmissable and also suppresses an autonomous unload of ALREADY-TAGGED loot mid-activity. A FORCED
            // unload (the "unload now" gizmo, the bulk-haul finish flush) is a deliberate override and still runs.
            if (!forced && InDirectedActivity(pawn))
                return;

            // A pawn mid bill-prep-gather is CARRYING INGREDIENTS TO A BENCH ON PURPOSE — an auto-unload queued now
            // would run before the bill re-scan (queued jobs precede work) and dump the whole gathered load back to
            // storage, wasting the entire sweep. Only the explicit gizmo (forced) may override.
            if (!forced && pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BillPrepGather)
                return;

            // Belt-and-suspenders (CS-agnostic): while the pawn's CURRENT or a QUEUED job is a vanilla DoBill that
            // can consume the tagged stock the pawn is carrying, don't auto-unload that stock to storage — doing so
            // would recreate the floor stacks and (with a DoBill-rewriting mod like Common Sense) re-trigger the
            // gather->bench->unload loop. The recipe is about to consume it from inventory in place. The gizmo
            // (forced) still dumps everything.
            if (!forced && HoldsStockForActiveDoBill(pawn, comp))
                return;

            // On a non-home / temporary map (a caravan / encounter site) there is no player storage to unload
            // to, so the storage-unload pass is never appropriate there. We DON'T bail here, though: the same
            // eligibility / grace / pending-work / surplus gating below decides WHEN to commit (so the caravan
            // offload keeps the home accumulate-during-work timing — F38), and the Queue branch then offloads
            // onto a PACK ANIMAL instead of storage (PackAnimalLoad.TryGetOpportunisticLoadJob). When no usable
            // pack animal is reachable that returns null and the loot rides home in inventory (the F34 fallback).

            // A FORCED unload (the gizmo, an end-of-batch flush) is RECOVERY, not work — it must function even
            // for a pawn that became scoop-ineligible (hauling-incapable after a settings flip), or the recovery
            // button is silently dead while tagged stock strands in inventory. (Drafted pawns never get here —
            // the draft gate above wins; the gizmo shows a disabled reason for them.)
            bool eligible = pawn.Faction == Faction.OfPlayerSilentFail
                            && (forced || YieldRouter.IsEligible(pawn))
                            && pawn.inventory?.innerContainer != null;

            // Before reading the tracked set, ADOPT (tag) any inventory the pawn is carrying that HD never scooped
            // but that is surplus above its keep-stock — so foreign trade / mod / manual stock unloads exactly like
            // HD-scooped loot through the same tag-scoped pass below (and shows the gizmo, fires the alert if stuck,
            // etc., with no other code changes). Two ways in: the global "unload all surplus" toggle (adopts every
            // surplus stack), or — even with that off — a def carrying an explicit per-item rule that produces
            // surplus (keep-at-most / always-unload), a deliberate surgical opt-in. Gated to eligible (humanlike
            // colonists / allowed mechs — the same predicate as scoop/unload) and NOT mid caravan-loading
            // (IsFormingCaravan inventory is deliberate). Keep-stock (food / drugs / inventoryStock / CE loadout)
            // has SurplusOf==0, so it is never adopted; once a stack is unloaded out of inventory the def's tag
            // self-prunes in CompHauledToInventory.GetHashSet.
            bool adoptAll = settings.unloadAllSurplus;
            if (eligible && !pawn.IsFormingCaravan() && (adoptAll || settings.HasAnySurplusProducingRule))
                AdoptSurplusInventory(pawn, comp, adoptAll);

            var carried = comp.GetHashSet();
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

            // Accumulate window, EXCEPT when the pawn is ALREADY in a downtime job it should unload before —
            // rest / recreation / eating, per the toggles. Then drop the load now instead of holding it through
            // the window (a pawn napping or eating shouldn't be sitting on a full pack). We use the STATE check
            // (not the need-based "entering" one): this runs from the hourly interval for EVERY pawn, including
            // one mid-mining-run that merely happens to be tired — that pawn is still working, so it must keep
            // accumulating. A forced unload ignores grace anyway, so this only changes the automatic path.
            int effectiveGrace = OpportunisticUnload.IsInDowntimeJob(pawn, settings)
                ? 0 : settings.unloadGraceTicks;

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
                    hasPendingWork, ticksSinceYield, effectiveGrace, anyUnloadable);

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
                        // Caravan / away map with NO player storage — offload onto a pack animal instead. The Decide
                        // gates above already applied the SAME eligibility / grace / pending-work / surplus
                        // timing as the home storage path (so F38's accumulate-during-work holds);
                        // TryGetOpportunisticLoadJob adds the caravan toggle + carrier gate and builds the
                        // deposit-only load job (null -> no usable carrier, loot just rides home in inventory).
                        // Any non-home map WITH player storage (a Vehicle Framework RV interior) is the
                        // exception: ShouldUnloadToStorage is true there, so it falls through to the storage-unload
                        // driver below — which delivers to the RV's shelves (and keeps the load tagged in inventory,
                        // never looping, if they are full). Without this an RV pawn forked here, found no reachable
                        // carrier, and looped picking up / dropping forever.
                        if (pawn.Map != null && !MapGate.ShouldUnloadToStorage(pawn.Map))
                        {
                            var loadJob = PackAnimalLoad.TryGetOpportunisticLoadJob(pawn);
                            if (loadJob != null && pawn.jobs != null)
                            {
                                if (behindQueuedWork && hasPendingWork)
                                    pawn.jobs.jobQueue.EnqueueLast(loadJob, JobTag.Misc);
                                else
                                    pawn.jobs.jobQueue.EnqueueFirst(loadJob, JobTag.Misc);
                                HDLog.Dbg($"{pawn} queued caravan pack-animal load ({carried.Count} tracked).");
                                // Same one-trip ordering as the storage path: scoop pending fresh drops first.
                                YieldRouter.EnsureSelfPickupJob(pawn);
                                // #215 left-click: run the just-queued load NOW (only on the EnqueueFirst path).
                                if (immediate && !(behindQueuedWork && hasPendingWork))
                                    InterruptCurrentJob(pawn);
                            }
                            return;
                        }
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
                            // #215 left-click: interrupt the current job so this [SelfPickup, Unload] runs NOW.
                            // Only on the EnqueueFirst path (the gizmo passes behindQueuedWork:false), never when
                            // the unload was deliberately queued behind pending work. Gated on anyUnloadable so a
                            // forced click that would enqueue a NO-OP unload (the gizmo shows for a tagged stack
                            // that is now all keep-stock, surplus 0) doesn't yank the pawn off its work for nothing;
                            // the no-op unload still queues (the forced-always-proceeds invariant) but runs behind
                            // the current job, exactly as before #215.
                            if (immediate && anyUnloadable && !(behindQueuedWork && hasPendingWork))
                                InterruptCurrentJob(pawn);
                        }
                        return;

                    default:
                        return;
                }
            }
        }

        /// <summary>
        /// #215 (left-click "Unload now"): end the pawn's CURRENT job so an unload / self-pickup / pack-animal-load
        /// just <c>EnqueueFirst</c>'d in front of it runs immediately, instead of after the current job finishes.
        /// <see cref="JobCondition.InterruptForced"/> is the same condition vanilla's own "order this now"
        /// (<c>TryTakeOrderedJob</c> without queueing) uses to replace the current job, so the pawn drops what it was
        /// doing and starts the queued housekeeping at once. Guarded: with no current job the queued work starts on
        /// its own, and if the current job IS one of HD's housekeeping jobs (a second click while it already runs)
        /// there is nothing to interrupt. Runs inside the same synced command as the enqueue, so it is
        /// MP-deterministic.
        /// </summary>
        internal static void InterruptCurrentJob(Pawn pawn)
        {
            var cur = pawn?.jobs?.curJob;
            if (cur == null)
                return;
            var def = cur.def;
            if (def == HaulersDreamDefOf.HaulersDream_UnloadInventory
                || def == HaulersDreamDefOf.HaulersDream_SelfPickup
                || def == HaulersDreamDefOf.HaulersDream_LoadPackAnimal)
                return;
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        /// <summary>True if the pawn is engaged in a DIRECTED activity HD's autonomous inventory manipulation must
        /// stand down for (a ritual/ceremony, caravan forming, a party/gathering/marriage, a quest lord). Two signals,
        /// EITHER sufficient:
        /// <list type="bullet">
        /// <item><c>GetLord()!=null</c> — a member of a Lord's ownedPawns. Subsumes
        /// <see cref="CaravanFormingUtility.IsFormingCaravan"/> (itself a GetLord + LordJob check). Covers
        /// participants/gatherers (a pawn carrying a ritual offering ON PURPOSE).</item>
        /// <item><c>mindState.duty!=null</c> — driven by a PawnDuty even if NOT in the Lord's ownedPawns. This is
        /// the load-bearing addition for a ritual TARGET: an Anomaly psychic-ritual target is given a ritual duty
        /// (e.g. DeliverPawnToPsychicRitualCell) and must not be emptied out from under the ritual — the previous
        /// <c>GetLord()</c>-only gate let a hauler bulk-unload such a target and FAIL the ritual (the reported bug).
        /// A normal idle/working colonist has a NULL duty, so this never over-suppresses ordinary hauling (the same
        /// signal <see cref="OpportunisticUnload"/> already uses).</item>
        /// </list>
        /// Vanilla + DLC + modded directed activities are covered without enumerating LordJob/duty types. The pawn's
        /// inventory is purposeful (an offering, hand-loaded cargo) and must not be scooped/adopted/emptied. Explicit
        /// player (forced) orders bypass this at each call site.</summary>
        internal static bool InDirectedActivity(Pawn p) => p != null && (p.GetLord() != null || p.mindState?.duty != null);

        /// <summary>
        /// Surplus adoption: tag inventory stacks with surplus above the pawn's keep-stock, so stock HD never
        /// scooped (trade / mod / manual) is unloaded by the normal tag-scoped pass. <paramref name="adoptAll"/>
        /// (the global "unload all surplus" toggle) adopts EVERY surplus stack; otherwise only defs with an
        /// explicit surplus-producing per-item rule (keep-at-most / always-unload) are adopted — a deliberate
        /// surgical opt-in that works with the global toggle off. Bounded to surplus (keep-stock has
        /// SurplusOf==0 → never tagged), so it can't strip food / drugs / inventoryStock / CE loadout.
        /// RegisterHauledItem is idempotent (a HashSet add) and notifies CE's HoldTracker so a CE loadout doesn't
        /// floor-drop the adopted stock before the unload trip runs. Callers gate on eligibility + !IsFormingCaravan.
        /// </summary>
        internal static void AdoptSurplusInventory(Pawn pawn, CompHauledToInventory comp, bool adoptAll)
        {
            var inner = pawn.inventory?.innerContainer;
            if (inner == null || comp == null)
                return;
            // #4 Directed-activity stand-down (REQUIRED here even though the autonomous caller is already gated): the
            // FORCED unload path reaches AdoptSurplusInventory with eligible==true (forced) and only !IsFormingCaravan
            // — so a ritual pawn (a Lord/duty, but NOT forming a caravan) would have its untagged offering ADOPTED
            // (tagged) and then shipped off. Guarding here makes adoption never touch ANY directed pawn's purposeful
            // inventory, regardless of caller or forced flag. (InDirectedActivity subsumes the IsFormingCaravan gate
            // at the call site and also covers a ritual target driven by a duty without Lord membership.)
            if (InDirectedActivity(pawn))
                return;
            var settings = HaulersDreamMod.Settings;
            int adopted = 0;
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t == null || t.Destroyed)
                    continue;
                // With the global toggle off, adopt ONLY defs the player explicitly set to keep-at-most / always-
                // unload (RuleProducesSurplus). KeepAll/Default defs are left to the normal (tagged-only) path.
                if (!adoptAll && (settings == null || !settings.RuleProducesSurplus(t.def)))
                    continue;
                // #222: never ADOPT (tag) a Simple Sidearms remembered sidearm or a Grab Your Tool carried tool,
                // UNLESS the player set an explicit per-def "Unload always" rule on it. The keep exclusion itself is
                // the SAME one the scoop self-heal applies (CompHauledToInventory.cs:191-193, excludeFromTag) and
                // YieldRouter.InventoryStackOfDef uses; adoption was the ONE tagging path that lacked it. SurplusOf
                // is per (def,stuff) PAIR, so a pawn carrying its remembered sidearm PLUS a same-pair LOOTED
                // duplicate (HD strips enemy corpses) gets SurplusOf > 0 for BOTH Things (pairHave 2 minus pairKeep
                // 1 = 1); without the guard adoption tagged the remembered sidearm too, and the unload driver (which
                // unloads by ascending thingIDNumber) then shipped the pawn's OWN older sidearm to storage: the
                // reported "pawns unload their sword/knife" bug. IsRememberedSidearm is a precise (def,stuff) match
                // that ignores HD's tag, so it also skips the looted duplicate here; that copy is already
                // scoop-tagged, so it still unloads.
                //
                // forcedUnload mirrors SurplusOf's OWN precedence (InventorySurplus.cs:68-70, and the documented
                // GrabYourToolCompat "Unload always still wins" override): an explicit UnloadAlways rule makes the
                // whole stack surplus BEFORE the SS/GYT keep is consulted. Adoption is the ONLY path that could tag a
                // remembered sidearm/tool, so without this deferral that documented per-def override would be a
                // silent no-op (a backward-incompatible change). KeepAtMost/KeepAll are NOT deferred: their excess
                // still unloads via the already-tagged looted copy while the specific remembered sidearm stays kept.
                // Both predicates are read-only and inert when SS/GYT are absent.
                bool forcedUnload = settings != null && settings.TryGetItemRule(t.def, out var rule)
                                    && rule.mode == ItemUnloadMode.UnloadAlways;
                if (!forcedUnload
                    && (SimpleSidearmsCompat.IsRememberedSidearm(pawn, t) || GrabYourToolCompat.IsCarriedTool(pawn, t)))
                    continue;
                // Only adopt surplus we can actually DELIVER. Adopting a stack with no storage destination would
                // tag it and the unload pass would then relocate it to a desperate far/feet cell (the "drops it at
                // a random spot" bug). Leave a no-destination stack UNTAGGED instead — it stays where it is, and
                // Alert_CannotUnloadInventory (Condition A, tag-independent) still surfaces it as a real black hole.
                if (InventorySurplus.SurplusOf(pawn, t) > 0 && InventorySurplus.HasUnloadDestination(pawn, t))
                {
                    int before = comp.PeekHashSet().Count;
                    comp.RegisterHauledItem(t);
                    if (comp.PeekHashSet().Count > before)
                        adopted++;
                }
            }
            if (adopted > 0)
                HDLog.Dbg($"{pawn} adopted {adopted} surplus inventory stack(s) it did not scoop (adoptAll={adoptAll}).");
        }

        /// <summary>True if at least one tracked stack still in the pawn's inventory has surplus above the
        /// pawn's personal keep-stock — i.e. the unload pass would actually move something. Uses the SAME
        /// surplus math as the unload driver and the cannot-unload alert (<see cref="InventorySurplus"/>), so
        /// the three never disagree.</summary>
        internal static bool AnyUnloadable(Pawn pawn, HashSet<Thing> carried)
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

        /// <summary>True if the pawn's current OR any queued job is a vanilla DoBill whose bill can consume a
        /// tagged stack the pawn still carries (InventoryShare.IsUsableForBill). The recipe will consume that
        /// stock from inventory in place, so the automatic unload must not ship it to storage first.</summary>
        private static bool HoldsStockForActiveDoBill(Pawn pawn, CompHauledToInventory comp)
        {
            var inv = pawn?.inventory?.innerContainer;
            if (inv == null || comp == null)
                return false;
            if (MatchesActiveDoBill(pawn.CurJob, comp, inv))
                return true;
            var queue = pawn.jobs?.jobQueue;
            if (queue != null)
                foreach (var qj in queue)
                    if (MatchesActiveDoBill(qj?.job, comp, inv))
                        return true;
            return false;
        }

        /// <summary>True iff <paramref name="job"/> is a vanilla DoBill whose bill matches a tagged stack still
        /// in the pawn's inventory. Read-only (PeekHashSet — no GetHashSet self-heal/CE-notify on a decision
        /// path); the inv.Contains guard excludes anything no longer held. HD's own gather drivers aren't
        /// JobDefOf.DoBill, so they're naturally excluded.</summary>
        private static bool MatchesActiveDoBill(Job job, CompHauledToInventory comp, ThingOwner<Thing> inv)
        {
            if (job == null || job.def != JobDefOf.DoBill || job.bill?.recipe == null)
                return false;
            foreach (var tagged in comp.PeekHashSet())
                if (tagged != null && inv.Contains(tagged) && InventoryShare.IsUsableForBill(tagged, job.bill))
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
            // Allocation-free: indexed walk over the JobQueue (its enumerator boxes; the indexer does not) +
            // a reference compare of each queued JobDef against the two housekeeping defs. No List<string> and
            // no params string[] are materialised — the per-tick allocation HD-UNLWORK removes.
            var selfPickup = HaulersDreamDefOf.HaulersDream_SelfPickup;
            var unload = HaulersDreamDefOf.HaulersDream_UnloadInventory;
            for (int i = 0; i < queue.Count; i++)
                if (UnloadPolicy.IsPendingRealWork(queue[i]?.job?.def, selfPickup, unload))
                    return true;
            return false;
        }
    }
}
