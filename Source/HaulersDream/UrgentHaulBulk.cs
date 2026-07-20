using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Haul Urgently" bulk backpack pickup (Allow Tool / Keyz' Allow Utilities soft-dep, see
    /// <see cref="UrgentHaulCompat"/>). When a pawn is sent to urgently haul an item, this pockets the OTHER
    /// urgent-marked stacks in a small radius into its inventory in ONE trip (urgent-first, nearest-first) instead
    /// of vanilla's one-stack-per-trip carry: the reported "pawns pick urgent items one by one, not into the
    /// backpack". When the player opts in it also folds in ordinary nearby haulables on the same trip.
    ///
    /// <para>It reuses HD's bulk machinery end to end: it emits the SAME <see cref="JobDriver_BulkHaul"/> job (NO
    /// new JobDef) with the urgent primary as the anchor (queue index 0) and the selected neighbours following, so
    /// the driver's multi-stack pocket-into-inventory walk + storage-aware unload service it exactly like an
    /// ordinary HD bulk haul (built the same way as <see cref="BulkHaul.BuildPickUpJob"/>). The pure selection
    /// (which neighbours, how many of each, in what order) lives in <see cref="UrgentVicinityPolicy"/>; this Verse
    /// layer only gathers + vets candidates and builds the job.</para>
    ///
    /// <para>KEY DIFFERENCE from the general sweep (<see cref="BulkHaul.TryBuildBulkJob"/>): the urgent-marked
    /// neighbours are swept even when they have NO strictly-better storage (urgent means "move it now"; the
    /// storage-aware unload re-homes them): the layer-2 fix for an urgent cluster with nowhere better collapsing
    /// to a single stack. Opted-in ordinary neighbours still keep the normal better-storage requirement.
    /// Independent of the general <c>bulkHaul</c> toggle. Inert when no urgent-haul mod is installed and when the
    /// feature setting is off.</para>
    ///
    /// <para>PURE PLANNING (no reservations, no designations, no world mutation) because the urgent WorkGiver's
    /// <c>JobOnThing</c> (whose result this replaces) is probed speculatively by the work scan; the driver
    /// re-clamps each take to live mass + Combat Extended room at pickup and reserves each stack there.</para>
    /// </summary>
    internal static class UrgentHaulBulk
    {
        // Total stacks per urgent sweep (primary + neighbours), matching BulkHaul.MaxStacks so the walk + unload
        // stay bounded. The primary is always queue index 0, so the vicinity is capped at one fewer.
        private const int MaxStacks = 24;

        // Hook-reachable build scratch, reused per call (this runs on the urgent work scan). [ThreadStatic] +
        // lazy-init matches this assembly's convention (see BulkHaul's scratch pools); each is Cleared at the point
        // of use, never trusted empty from a prior call, and never aliased into the job's own (fresh) queues.
        // SAFETY: TryBuild runs to completion with no nested urgent-JobOnThing probe, so single-thread reuse is sound.
        [ThreadStatic] private static List<Thing> scratchUrgentNear;
        [ThreadStatic] private static List<UrgentVicinityPolicy.Candidate> scratchCandidates;
        [ThreadStatic] private static Dictionary<int, Thing> scratchById;
        [ThreadStatic] private static HashSet<Thing> scratchOwnTargets;

        /// <summary>
        /// Build the urgent bulk pickup job for hauling <paramref name="primary"/>, or null when it doesn't apply
        /// (the vanilla single urgent haul then stands). See the type summary.
        /// </summary>
        /// <param name="pawn">The pawn the urgent WorkGiver assigned the haul to.</param>
        /// <param name="primary">The urgently-marked item the vanilla job hauls (always queue index 0).</param>
        /// <param name="vanillaJob">The urgent WorkGiver's own result; only a HaulToCell / HaulToContainer converts.</param>
        /// <param name="forced">The WorkGiver's forced flag (a player "prioritize" order). Selects the
        /// automatic-intake gate below: a forced order OVERRIDES the master / opt-out / bleeding / foreign / RimIOT
        /// gates, while swept neighbours are always vetted as non-forced. NOTE: this does NOT set
        /// <c>job.playerForced</c>: the vanilla dispatch (TryTakeOrderedJobPrioritizedWork) sets that downstream for
        /// a forced order, which is what lets the driver take a forbidden primary at queue index 0.</param>
        /// <param name="includeNonUrgent">Feature 2: also pocket ordinary nearby haulables (keeping their normal
        /// better-storage eligibility) alongside the urgent ones.</param>
        internal static Job TryBuild(Pawn pawn, Thing primary, Job vanillaJob, bool forced, bool includeNonUrgent)
        {
            if (pawn == null || primary == null || !primary.Spawned)
                return null;
            var s = HaulersDreamMod.Settings;
            var map = pawn.Map;
            if (s == null || map == null || !s.bulkHaulUrgent)
                return null;
            // Nothing is ever urgent unless a "Haul Urgently" mod defined its designation, the cheapest early-out.
            if (!UrgentHaulCompat.AnyUrgentDefResolved)
                return null;
            // Map gate, same as the general bulk sweep (BuildBulkJob): stand down where HD has no storage (a
            // non-home map with the toggle off). Unconditional (applies to forced too), like the general path.
            if (!MapGate.HdActiveOnMap(map))
                return null;
            if (pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger())
                return null;
            // Pawn-type gate unified with scoop + auto-unload (YieldRouter.IsEligible): only {humanlike colonists,
            // allowed colony mechs} sweep into inventory, so whatever this loads the unload side can service.
            if (!YieldRouter.IsEligible(pawn))
                return null;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null || pawn.inventory == null)
                return null;
            // The urgent WorkGiver hands back a plain single haul. Only a HaulToCell / HaulToContainer converts;
            // anything else (e.g. a genepack CarryToContainer) is left to vanilla. A container destination is
            // accepted like BulkHaul.BuildBulkJobForced does, the driver's storage-aware unload delivers it there.
            if (vanillaJob == null
                || (vanillaJob.def != JobDefOf.HaulToCell && vanillaJob.def != JobDefOf.HaulToContainer))
                return null;
            // Corpses keep their own vanilla urgent hauling flow (the sweep pool excludes them, and they don't
            // belong pocketed), so the single haul stands.
            if (primary is Corpse)
                return null;

            // AUTOMATIC-path intake gates: a player-FORCED urgent order (Allow Tool's prioritize) OVERRIDES all of
            // them, exactly mirroring BulkHaul.TryBuildBulkJob's !forced block so the automatic urgent scan honors
            // the same autonomous-intake rules as every other HD intake site (BulkHaul.cs:143-174, the work-spot
            // sweep, YieldRouter.IsCandidate):
            //  - C5 master switch off  -> HD initiates no autonomous bulk pocketing;
            //  - the per-pawn "Auto-haul yields" opt-out (the gizmo) -> a toggled-off pawn never auto-pockets;
            //  - C1 bleeding gate      -> a badly bleeding pawn gets treated, not loaded up;
            //  - a foreign per-item order on the primary (e.g. Recycle This) -> leave it (scooping it into inventory
            //    hides it from that mod's spawned-only WorkGiver; an urgent mark is whitelisted, so this only fires
            //    when the primary ALSO carries a non-haul designation);
            //  - a RimIOT-handled cell (#177/#184) -> cede it to RimIOT so the terminal haul/unload loop can't form.
            // A forced order skips this block, so the player can always force an urgent bulk pickup by hand.
            if (!forced)
            {
                if (!MasterEnable.Active)
                    return null;
                if (!comp.autoHaulYields)
                    return null;
                if (!YieldRouter.FitToStartHaul(pawn))
                    return null;
                if (ForeignOrderGuard.ClaimedByForeignOrder(primary))
                    return null;
                if (RimIOTCompat.IsPresent && RimIOTCompat.IsRimIOTHandledCell(map, primary.Position))
                    return null;
            }

            // The primary itself must fit in inventory under the carry ceiling; otherwise a hands-carry (no mass
            // limit) is the better plan and there's nothing to build on top of it. Same mass + CE clamp
            // BulkHaul.BuildPickUpJob prices the primary with.
            int primaryTake = BulkHaul.MassClampedTake(pawn, primary, primary.stackCount, s);
            if (primaryTake <= 0)
                return null;

            // The worth-it mass ceiling for this pawn (per-pawn base cap × the overload break-even ratio), the
            // exact ceiling BulkHaul plans against, and the running gear+inventory mass after committing the
            // primary's take, so the first neighbour is priced against the real remaining room.
            float baseCap = CarryMath.EffectiveCapacity(CarryCapacity.Of(pawn), s.carryLimitFraction);
            float ceiling = BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverloadFor(pawn, s), baseCap,
                OverloadGate.MaxCeilingKg(s));
            float running = MassUtility.GearAndInventoryMass(pawn) + primaryTake * primary.GetStatValue(StatDefOf.Mass);

            float radius = Math.Max(1, s.bulkHaulUrgentRadius); // slider is 1..12; floor at 1 defensively
            float radiusSq = radius * radius;

            var byId = scratchById ?? (scratchById = new Dictionary<int, Thing>());
            var candidates = scratchCandidates ?? (scratchCandidates = new List<UrgentVicinityPolicy.Candidate>());
            byId.Clear();
            candidates.Clear();
            var claimed = RouteSelection.ClaimedByOtherPawns(pawn);
            var ownTargets = scratchOwnTargets ?? (scratchOwnTargets = new HashSet<Thing>());
            FillOwnJobTargets(pawn, ownTargets);

            // Vet one neighbour and, if eligible, snapshot it as a candidate (recording id -> Thing for the mapping
            // back after selection). Mirrors BulkHaul.TakeNearestEligible's per-candidate gates, MINUS the
            // better-storage requirement for urgent items. A local function (never converted to a delegate) so it
            // captures the scratch/pawn/primary by ref with no per-call closure allocation on this warm scan path.
            void Vet(Thing t, bool isUrgent, bool requireBetterStorage)
            {
                if (t == null || t == primary || !t.Spawned || t.Map != map || t is Corpse)
                    return;
                if (t.def == null || !t.def.EverHaulable)
                    return;
                int distSq = (t.Position - primary.Position).LengthHorizontalSquared;
                if (distSq > radiusSq)
                    return;
                if (byId.ContainsKey(t.thingIDNumber))
                    return; // already added by the urgent pass, or a duplicate designation
                // RimIOT compat (#177/#184): a stack RimIOT owns (a logistic-network cell, or a powered interface's
                // ground apron) is never swept; else HD re-pockets the network's overflow into the endless loop.
                // The same check the general pool applies (BulkHaul.BuildPoolInto); inert without RimIOT.
                if (RimIOTCompat.IsPresent && RimIOTCompat.IsRimIOTHandledCell(map, t.Position))
                    return;
                // #187a: a keep-on-corpse tainted-apparel piece the keep policy says to leave (LeaveOnCorpse /
                // DropAndForbid) is never pocketed, the same gate BulkHaul's pool applies; inert for the Take-Smelt
                // defaults, non-apparel and untainted items, so this is byte-identical there.
                if (CorpseStripper.ShouldLeaveTaintedApparel(t, s))
                    return;
                // Swept extras are opportunistic (never forced): skip a forbidden neighbour, one another pawn is
                // already assigned to, one this pawn is itself already committed to elsewhere, and one a foreign
                // per-item order (e.g. Recycle This) owns, even when that item is ALSO marked urgent.
                if (t.IsForbidden(pawn) || claimed.Contains(t) || ownTargets.Contains(t)
                    || ForeignOrderGuard.ClaimedByForeignOrder(t))
                    return;
                // Bake CE fit (weight + bulk vs live inventory) into the stack count so the pure policy never plans
                // more than CE can hold, and a stack that can't fit even one unit is not a real cluster member.
                // MaxFitCount is int.MaxValue without CE, so the Min is a no-op then.
                int usableStack = Math.Min(t.stackCount, CECompat.MaxFitCount(pawn, t));
                if (usableStack <= 0)
                    return;
                // Reachable + legally haulable (extras are never forced), and never path a vacuum-/fire-concerned
                // pawn through a Deadly region for a swept extra, both exactly as BulkHaul's sweep.
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                    return;
                if (!ExtraSweepReach.Allows(pawn, t))
                    return;
                // Non-urgent neighbours keep the normal "has somewhere better to go" gate; urgent ones are swept
                // regardless (the layer-2 fix: the storage-aware unload re-homes them).
                if (requireBetterStorage
                    && !StoreUtility.TryFindBestBetterStorageFor(t, pawn, map, StoreUtility.CurrentStoragePriorityOf(t),
                            pawn.Faction, out _, out _, needAccurateResult: false))
                    return;

                byId[t.thingIDNumber] = t;
                candidates.Add(new UrgentVicinityPolicy.Candidate(
                    t.thingIDNumber, distSq, isUrgent, t.GetStatValue(StatDefOf.Mass), usableStack));
            }

            // Urgent-marked neighbours (the layer-2 fix): included regardless of whether they have better storage.
            var urgentNear = scratchUrgentNear ?? (scratchUrgentNear = new List<Thing>());
            UrgentHaulCompat.CollectUrgentNear(map, primary.Position, radiusSq, urgentNear);
            for (int i = 0; i < urgentNear.Count; i++)
                Vet(urgentNear[i], isUrgent: true, requireBetterStorage: false);

            // Feature 2: ordinary nearby haulables, keeping the NORMAL better-storage requirement. byId dedups an
            // item that is BOTH urgent and listed (it stays classified urgent, added above). Cast to the concrete
            // HashSet the lister returns so the foreach binds the struct enumerator and boxes nothing.
            if (includeNonUrgent)
            {
                var haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
                var set = haulables as HashSet<Thing>;
                if (set != null)
                {
                    foreach (var t in set)
                        Vet(t, isUrgent: false, requireBetterStorage: true);
                }
                else
                {
                    foreach (var t in haulables)
                        Vet(t, isUrgent: false, requireBetterStorage: true);
                }
            }

            var takes = UrgentVicinityPolicy.Select(candidates, radiusSq, includeNonUrgent, ceiling, running, MaxStacks - 1);
            if (takes.Count == 0)
            {
                // No vicinity cluster. Normally a lone primary hauls best in hands, so the vanilla single haul
                // stands, and that also keeps a lone CE-BULKY stack a hand-haul (#115): primaryTake is CE-clamped,
                // so for a stack CE fits fewer of than hands carry, primaryTake < handCap and it is NOT
                // oversized-worth below. EXCEPTION (bug-2 parity with the general path, BulkHaul.BuildBulkJob's
                // things.Count<2 else-branch): a lone OVERSIZED urgent stack (bigger than one armful, with inventory
                // able to carry more of it than hands) still rides inventory so the whole stack moves in ONE trip
                // instead of an armful per trip. deliverable is primaryTake (urgent items have no better-storage
                // clamp; the storage-aware unload re-homes the load): the CE clamp in it is what preserves #115.
                int handCap = pawn.carryTracker?.MaxStackSpaceEver(primary.def) ?? primary.def.stackLimit;
                if (!(s.haulOversizedInInventory
                      && BulkHaulPolicy.OversizedStackWorthInventory(primary.stackCount, handCap, primaryTake)))
                    return null; // vanilla single hand-haul stands (incl. the CE lone-bulky #115 case)
                // else: fall through and build a single-primary bulk (queue = [primary], one inventory trip).
            }

            // Build the bulk job exactly like BulkHaul.BuildPickUpJob: the urgent primary is the anchor (index 0,
            // taken with vanilla semantics, even forbidden when forced), the selected neighbours follow, count=1 is
            // the Job.count sentinel. The driver re-reads this identically, re-clamps every take to live mass + CE
            // at pickup, and reserves each stack there.
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkHaul, primary);
            job.targetQueueB = new List<LocalTargetInfo>(takes.Count + 1) { primary };
            job.countQueue = new List<int>(takes.Count + 1) { primaryTake };
            for (int i = 0; i < takes.Count; i++)
            {
                job.targetQueueB.Add(byId[takes[i].ThingId]);
                job.countQueue.Add(takes[i].Take);
            }
            job.count = 1;
            HDLog.Dbg($"UrgentHaul: {pawn} sweeping {job.targetQueueB.Count} urgent stack(s) "
                      + $"(includeNonUrgent={includeNonUrgent}, forced={forced}).");
            return job;
        }

        // The pawn's OWN current + queued job target things (Clearing `set` first), so the urgent sweep never
        // double-plans a stack this pawn is already committed to elsewhere. Small (bounded by the pawn's own queue);
        // mirrors the target collection RouteSelection uses for OTHER pawns.
        private static void FillOwnJobTargets(Pawn pawn, HashSet<Thing> set)
        {
            set.Clear();
            if (pawn?.jobs == null)
                return;
            AddJobTargets(set, pawn.CurJob);
            var q = pawn.jobs.jobQueue;
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                    AddJobTargets(set, q[i]?.job);
        }

        private static void AddJobTargets(HashSet<Thing> set, Job job)
        {
            if (job == null)
                return;
            if (job.targetA.Thing != null) set.Add(job.targetA.Thing);
            if (job.targetB.Thing != null) set.Add(job.targetB.Thing);
            var q = job.targetQueueB;
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                    if (q[i].Thing != null) set.Add(q[i].Thing);
        }
    }
}
