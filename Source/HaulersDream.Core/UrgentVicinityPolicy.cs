using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure selection for the "Haul Urgently" bulk backpack pickup (no game types, unit-tested headlessly).
    ///
    /// Given the neighbours around the urgently-hauled primary, pick WHICH ones to pocket on the same trip and
    /// HOW MANY units of each: urgent-marked stacks first, then nearest-first, bounded by the pawn's worth-it
    /// carry-mass ceiling (the same <see cref="BulkHaulPolicy.CeilingKg"/> the general bulk haul plans against)
    /// and a hard stack cap. The order is a fixed total order (distance ties broken by the stable thing id) and
    /// there is no randomness, so every multiplayer client selects the identical set; the Verse layer may collect
    /// candidates in any (HashSet / designation-manager) order.
    ///
    /// The primary itself is NOT among the candidates: the Verse layer carries it separately as the job anchor
    /// (queue index 0) and prices its take with the game-side mass + Combat Extended math. This class owns only the
    /// vicinity arithmetic.
    /// </summary>
    public static class UrgentVicinityPolicy
    {
        /// <summary>
        /// One neighbour considered for the urgent sweep: a value SNAPSHOT so the selection stays pure (the Verse
        /// layer reads the live <c>Thing</c> once and hands over these primitives).
        /// </summary>
        public readonly struct Candidate
        {
            /// <summary>The thing's stable id (Verse <c>thingIDNumber</c>): the multiplayer-deterministic distance
            /// tiebreak, and the key the Verse layer maps back to the real <c>Thing</c> after selection.</summary>
            public readonly int ThingId;

            /// <summary>Squared horizontal distance from the primary/anchor, in cells. Compared against the squared
            /// vicinity radius (inclusive), and the nearest-first ranking key within a tier.</summary>
            public readonly float DistSqToAnchor;

            /// <summary>True when this neighbour itself carries a "Haul Urgently" designation. Urgent neighbours are
            /// always eligible and rank ahead of every non-urgent one; non-urgent neighbours are dropped entirely
            /// unless the caller opted them in.</summary>
            public readonly bool IsUrgent;

            /// <summary>Mass per unit, kg. 0 for a massless item (its whole stack is taken). Drives the ceiling take.</summary>
            public readonly float UnitMass;

            /// <summary>Units available to take from this stack, the upper bound on the take. The Verse layer has
            /// already clamped it to what Combat Extended can hold, so a take never exceeds live CE room.</summary>
            public readonly int StackCount;

            /// <summary>Snapshot a neighbour. See each field for meaning.</summary>
            public Candidate(int thingId, float distSqToAnchor, bool isUrgent, float unitMass, int stackCount)
            {
                ThingId = thingId;
                DistSqToAnchor = distSqToAnchor;
                IsUrgent = isUrgent;
                UnitMass = unitMass;
                StackCount = stackCount;
            }
        }

        /// <summary>A chosen neighbour and how many units to pocket from it. A named result (over a positional
        /// tuple) so both fields read at the use site and in the tests.</summary>
        public readonly struct UrgentTake
        {
            /// <summary>The selected thing's id (matches a <see cref="Candidate.ThingId"/>).</summary>
            public readonly int ThingId;

            /// <summary>Units to pocket, always &gt; 0 and never more than the candidate's stack held.</summary>
            public readonly int Take;

            /// <summary>Pair a selected thing id with its take.</summary>
            public UrgentTake(int thingId, int take)
            {
                ThingId = thingId;
                Take = take;
            }
        }

        /// <summary>
        /// Pick the urgent-sweep neighbours and their per-stack takes.
        /// </summary>
        /// <param name="candidates">The neighbours to consider (the primary is NOT among them). Any order, the
        /// result is deterministic regardless.</param>
        /// <param name="radiusSq">Squared vicinity radius, cells. A candidate is in range when its
        /// <see cref="Candidate.DistSqToAnchor"/> is &lt;= this (inclusive boundary).</param>
        /// <param name="includeNonUrgent">When false, non-urgent candidates are dropped entirely (an urgent trip
        /// carries only urgent-marked stacks). When true they are eligible, but still rank AFTER every urgent one.</param>
        /// <param name="ceilingKg">The worth-it total gear+inventory mass ceiling, kg.
        /// <see cref="float.PositiveInfinity"/> = no mass limit. Takes accumulate against it.</param>
        /// <param name="runningMassKg">The pawn's gear+inventory mass BEFORE the sweep, already including the
        /// primary's planned take, so the first neighbour is priced against the real remaining room.</param>
        /// <param name="maxStacks">Hard cap on how many neighbour stacks to pocket (bounds the job queue + walk).</param>
        /// <returns>The chosen (thing-id, take) pairs (urgent-first, then nearest-first, then lowest-id), each with
        /// a positive take, in selection order. Empty when nothing qualifies (the caller then leaves the vanilla
        /// single haul standing).</returns>
        public static List<UrgentTake> Select(
            IReadOnlyList<Candidate> candidates, float radiusSq, bool includeNonUrgent,
            float ceilingKg, float runningMassKg, int maxStacks)
        {
            var takes = new List<UrgentTake>();
            if (candidates == null || candidates.Count == 0 || maxStacks <= 0)
                return takes;

            // Filter to the in-range, opted-in candidates, then order deterministically. A private copy so the
            // caller's list is never mutated; the sort is a fixed total order (id is unique), so the result is
            // multiplayer-identical whatever order the Verse layer collected the candidates in.
            var ordered = new List<Candidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.DistSqToAnchor > radiusSq)
                    continue; // outside the vicinity
                if (!c.IsUrgent && !includeNonUrgent)
                    continue; // non-urgent dropped unless opted in
                if (c.StackCount <= 0)
                    continue; // nothing to take
                ordered.Add(c);
            }
            ordered.Sort(CompareUrgentFirstThenNearest);

            // Accumulate takes under the mass ceiling, stopping at the stack cap. A candidate too heavy for the
            // remaining room is SKIPPED (its take is 0) rather than ending the scan, mirroring
            // BulkHaul.TakeNearestEligible, a lighter later stack (or a massless one, which is free even at the
            // ceiling) may still fit. CountWithinCeiling returns 0 over the ceiling and the whole stack when
            // massless or under an infinite ceiling, so no separate break/epsilon is needed here.
            float running = runningMassKg;
            for (int i = 0; i < ordered.Count && takes.Count < maxStacks; i++)
            {
                var c = ordered[i];
                int take = BulkHaulPolicy.CountWithinCeiling(ceilingKg, running, c.UnitMass, c.StackCount);
                if (take <= 0)
                    continue; // too heavy for the room left; a lighter/massless later candidate may still fit
                takes.Add(new UrgentTake(c.ThingId, take));
                running += take * c.UnitMass;
            }
            return takes;
        }

        /// <summary>Total order for the urgent sweep: urgent stacks before non-urgent, then nearest first, then
        /// lowest id (a stable, multiplayer-deterministic tiebreak since ids are unique).</summary>
        private static int CompareUrgentFirstThenNearest(Candidate a, Candidate b)
        {
            if (a.IsUrgent != b.IsUrgent)
                return a.IsUrgent ? -1 : 1; // urgent tier first
            int byDist = a.DistSqToAnchor.CompareTo(b.DistSqToAnchor);
            if (byDist != 0)
                return byDist; // nearest first
            return a.ThingId.CompareTo(b.ThingId); // lowest id, deterministic
        }
    }
}
