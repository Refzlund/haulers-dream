using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Makes a construction-material delivery carry enough for MANY queued same-material needers in one
    /// trip — up to the pawn's HAND capacity (carrying in hands has no local move-speed penalty, so a
    /// bigger load is strictly better). Vanilla only batches needers within 8 tiles, so a pawn building a
    /// long fence shuttles ~9 wood at a time. We extend the vanilla <c>HaulToContainer</c> job's needer
    /// queue + count; the unmodified driver already delivers one hand-load to many needers (GotoBuild →
    /// Deposit → JumpToCarryToNextContainerIfPossible) and distributes the count across them via the
    /// enroute system. We only ever attach <see cref="IHaulEnroute"/> needers (frames / build blueprints),
    /// which take no hard reservation, so extending the queue can never create a reservation conflict.
    /// </summary>
    internal static class ConstructionBatch
    {
        // Only batch needers/resources within this many tiles of the build cluster / source. Keeps the
        // delivery run local (a far needer that gets walled off mid-trip would abort the job — vanilla's
        // FailOn only recovers from destroyed needers, not unreachable ones; the carried load is safely
        // DROPPED near the pawn in that case, never lost) and bounds the candidate sort for huge bases.
        private const int MaxBatchSpan = 40;
        private const int MaxBatchSpanSq = MaxBatchSpan * MaxBatchSpan;

        internal static void Expand(Pawn pawn, Job job, ThingDef def, bool forced)
        {
            if (pawn?.Map == null || job == null || def == null || pawn.carryTracker == null)
                return;
            if (!(job.targetB.Thing is IHaulEnroute) && !(job.targetC.Thing is IHaulEnroute))
                return; // primary needer must be enroute-tracked

            int handCap = pawn.carryTracker.MaxStackSpaceEver(def);
            if (handCap <= job.count)
                return; // already carrying a full hand-load

            var neederSet = new HashSet<Thing>();
            AddThing(neederSet, job.targetB.Thing);
            AddThing(neederSet, job.targetC.Thing);
            if (job.targetQueueB != null)
                foreach (var t in job.targetQueueB) AddThing(neederSet, t.Thing);

            var resourceSet = new HashSet<Thing>();
            int currentResource = AddResource(resourceSet, job.targetA.Thing);
            if (job.targetQueueA != null)
                foreach (var t in job.targetQueueA) currentResource += AddResource(resourceSet, t.Thing);

            // Anchor the batch on the build cluster (primary needer) and the wood source, so attached
            // needers/stacks stay local to the run rather than spanning the whole map.
            IntVec3 neederAnchor = job.targetB.Thing?.Position ?? job.targetC.Thing?.Position ?? pawn.Position;
            IntVec3 resourceAnchor = job.targetA.Thing?.Position ?? pawn.Position;

            var extraNeeders = new List<Thing>();
            var extraNeederSpaces = new List<int>();
            GatherNeeders(pawn, def, forced, neederSet, handCap - job.count, neederAnchor, extraNeeders, extraNeederSpaces);
            if (extraNeeders.Count == 0)
                return;

            var extraResources = new List<Thing>();
            var extraResourceCounts = new List<int>();
            GatherResources(pawn, def, resourceSet, handCap, resourceAnchor, extraResources, extraResourceCounts);

            ConstructionBatchMath.Plan(handCap, job.count, currentResource,
                extraNeederSpaces, extraResourceCounts,
                out int finalCount, out int neederTake, out int resourceTake);

            if (finalCount <= job.count || neederTake <= 0)
                return;

            int wasCount = job.count;
            if (job.targetQueueB == null) job.targetQueueB = new List<LocalTargetInfo>();
            for (int i = 0; i < neederTake && i < extraNeeders.Count; i++)
                job.targetQueueB.Add(extraNeeders[i]);
            if (resourceTake > 0)
            {
                if (job.targetQueueA == null) job.targetQueueA = new List<LocalTargetInfo>();
                for (int i = 0; i < resourceTake && i < extraResources.Count; i++)
                    job.targetQueueA.Add(extraResources[i]);
            }
            job.count = finalCount;
            HDLog.Dbg($"{pawn} batched {def.label} construction haul x{finalCount} (was x{wasCount}) across {neederSet.Count + neederTake} needers.");
        }

        private static void AddThing(HashSet<Thing> set, Thing t)
        {
            if (t != null) set.Add(t);
        }

        private static int AddResource(HashSet<Thing> set, Thing t)
            => (t != null && set.Add(t)) ? t.stackCount : 0;

        private static void GatherNeeders(Pawn pawn, ThingDef def, bool forced, HashSet<Thing> exclude,
            int spaceWanted, IntVec3 anchor, List<Thing> outNeeders, List<int> outSpaces)
        {
            var all = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Construction);
            var candidates = new List<Thing>();
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || exclude.Contains(t) || !(t is IConstructible) || !(t is IHaulEnroute))
                    continue;
                if (t.Faction != pawn.Faction || t.IsForbidden(pawn))
                    continue;
                if ((t.Position - anchor).LengthHorizontalSquared > MaxBatchSpanSq)
                    continue; // keep the batch local to the build cluster
                candidates.Add(t);
            }
            SortByDistance(candidates, pawn.Position);

            int got = 0;
            for (int i = 0; i < candidates.Count && got < spaceWanted; i++)
            {
                var t = candidates[i];
                // CanConstruct already enforces reachability (CanReach Touch, NormalMaxDanger unforced /
                // Deadly forced) for jobForReservation != null — so no extra CanReach is needed here.
                if (!GenConstruct.CanConstruct(t, pawn, checkSkills: false, forced: forced, JobDefOf.HaulToContainer))
                    continue;
                int space = forced
                    ? ((IConstructible)t).ThingCountNeeded(def)
                    : ((IHaulEnroute)t).GetSpaceRemainingWithEnroute(def, pawn);
                if (space <= 0)
                    continue;
                outNeeders.Add(t);
                outSpaces.Add(space);
                got += space;
            }
        }

        private static void GatherResources(Pawn pawn, ThingDef def, HashSet<Thing> exclude,
            int handCap, IntVec3 anchor, List<Thing> outResources, List<int> outCounts)
        {
            var all = pawn.Map.listerThings.ThingsOfDef(def);
            var candidates = new List<Thing>();
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || exclude.Contains(t) || t.IsForbidden(pawn))
                    continue;
                if ((t.Position - anchor).LengthHorizontalSquared > MaxBatchSpanSq)
                    continue;
                candidates.Add(t);
            }
            SortByDistance(candidates, pawn.Position);

            int got = 0;
            for (int i = 0; i < candidates.Count && got < handCap; i++)
            {
                var t = candidates[i];
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false) || !pawn.CanReserve(t))
                    continue;
                outResources.Add(t);
                outCounts.Add(t.stackCount);
                got += t.stackCount;
            }
        }

        private static void SortByDistance(List<Thing> things, IntVec3 from)
            => things.Sort((a, b) =>
                (a.Position - from).LengthHorizontalSquared.CompareTo((b.Position - from).LengthHorizontalSquared));
    }
}
