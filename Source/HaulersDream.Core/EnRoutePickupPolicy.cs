using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// The pure "is this loose item worth grabbing on the way?" cascade — a faithful port of While You're
    /// Up's <c>OpportunityDetour.CanHaul</c> trip-ratio math (<c>OpportunityDetour.cs:210-300</c>) and its
    /// default thresholds (<c>Settings.cs ExposeData</c>, lines 465-472). A pawn about to start ANY job
    /// will detour to scoop a nearby floor haulable IFF the resulting trip
    /// <c>pawn → thing → store → job</c> stays within tight multiples of the direct <c>pawn → job</c>
    /// trip. The Verse layer feeds the straight-line leg distances (and, when a path checker beyond
    /// <see cref="EnRoutePathChecker.Vanilla"/> is selected, re-feeds the real A* path costs in the same
    /// shapes); this class owns only the arithmetic and the cheap-to-expensive short-circuit ORDER.
    ///
    /// <para>WHY two evaluation methods. WYU's <c>CanHaul</c> evaluates in two phases because the store
    /// cell is unknown until midway through:</para>
    /// <list type="number">
    ///   <item><see cref="CheckBeforeStore"/> — the cheap checks that need only the pawn, thing and job
    ///   positions (squared start→thing range, squared start→thing %-of-trip, then the unsquared
    ///   <c>start→thing + thing→job</c> total-trip pre-bound). WYU runs these FIRST so a candidate that is
    ///   obviously off-path is rejected before the costly <c>TryFindBestBetterStoreCellFor</c> store search.
    ///   (<c>OpportunityDetour.cs:216-226</c>.)</item>
    ///   <item><see cref="CheckAfterStore"/> — the checks that need the chosen store cell (squared
    ///   store→job range, squared store→job %-of-trip, then the unsquared <c>MaxNewLegs</c> and
    ///   <c>MaxTotalTrip</c> bounds). (<c>OpportunityDetour.cs:241-250</c>.)</item>
    /// </list>
    ///
    /// <para><b>Verse-side interleave contract</b> (must match WYU's cheap→expensive short-circuit so the
    /// expensive work runs in the most-optimistic order — <c>OpportunityDetour.cs:139-208</c>):</para>
    /// <list type="number">
    ///   <item>Snapshot the per-scan ranges with <see cref="MaxRanges.Reset"/> using the live settings; the
    ///   pawn does not move during one scan.</item>
    ///   <item>For each candidate <c>thing</c> in the haulables list, in the current range band, call
    ///   <see cref="CheckBeforeStore"/>. On <see cref="EnRouteResult.RangeFail"/>: in
    ///   <see cref="EnRoutePathChecker.Vanilla"/> treat it as <see cref="EnRouteResult.HardFail"/>
    ///   (remove + continue), otherwise SKIP this candidate this band (advance, keep it for a wider band).
    ///   On <see cref="EnRouteResult.HardFail"/>: remove the candidate from the list and continue. Only on
    ///   <see cref="EnRouteResult.Continue"/> proceed to the store search.</item>
    ///   <item>Apply the cross-cluster guards BEFORE the store search where they are cheapest — G2
    ///   anti-double-haul (skip if the thing is claimed by another pawn or already targeted by this pawn's
    ///   own job/queue), reservation/forbidden/PawnCanAutomaticallyHaulFast (WYU does these at
    ///   <c>OpportunityDetour.cs:227-229</c>, AFTER the total-trip pre-bound but BEFORE the store search).
    ///   These are pure Verse calls with no analogue here.</item>
    ///   <item>Find the best store cell, choosing the candidate cell CLOSEST to the thing↔job MIDWAY (rank
    ///   candidate cells with <see cref="MidwayDistanceSquared"/> / <see cref="MidwayBetter"/>; the Verse
    ///   layer enumerates the cells). On no store cell → <see cref="EnRouteResult.HardFail"/> (remove +
    ///   continue).</item>
    ///   <item>Call <see cref="CheckAfterStore"/> with the chosen store cell. Same RangeFail/HardFail
    ///   handling as step 2 (<see cref="EnRoutePathChecker.Vanilla"/> hard-fails a RangeFail; the others
    ///   skip-this-band).</item>
    ///   <item>Finally run the reachability/accuracy stage selected by <see cref="EnRoutePathChecker"/>:
    ///   <see cref="EnRoutePathChecker.Vanilla"/> = the bounded region-count flood; the others = re-feed the
    ///   real A* path costs back through <see cref="CheckAfterStore"/>'s ratio bounds (pawn→thing,
    ///   store→job, pawn→job, thing→store). A path-cost failure in
    ///   <see cref="EnRoutePathChecker.Default"/> ends the whole scan; in
    ///   <see cref="EnRoutePathChecker.Pathfinding"/> it hard-fails just this candidate.
    ///   (<c>OpportunityDetour.cs:294-299</c>.) On success the candidate is the chosen haul.</item>
    ///   <item>When the band is exhausted with no winner, multiply the ranges by
    ///   <see cref="MaxRanges.HeuristicRangeExpandFactor"/> and rescan — this is the expanding-range
    ///   heuristic that orders the expensive store searches optimistically
    ///   (<c>OpportunityDetour.cs:146-152</c>).</item>
    /// </list>
    ///
    /// <para>Allocation-light: every method is primitives in / value out; <see cref="MaxRanges"/> is a
    /// mutable struct so the per-scan band lives on the stack with no per-candidate allocation. The Verse
    /// layer calls these per candidate per job-start (see <c>EnRoutePickupPolicyPerfTests</c>).</para>
    /// </summary>
    public static class EnRoutePickupPolicy
    {
        // --- WYU default thresholds (Settings.cs ExposeData) ---------------------------------------------

        /// <summary>Max straight-line tiles pawn→thing (WYU <c>Opportunity_MaxStartToThing = 30f</c>,
        /// <c>Settings.cs:465</c>).</summary>
        public const float DefaultMaxStartToThing = 30f;

        /// <summary>Max pawn→thing as a fraction of the direct pawn→job trip (WYU
        /// <c>Opportunity_MaxStartToThingPctOrigTrip = 0.5f</c>, <c>Settings.cs:466</c>).</summary>
        public const float DefaultMaxStartToThingPctOrigTrip = 0.5f;

        /// <summary>Max straight-line tiles store→job (WYU <c>Opportunity_MaxStoreToJob = 50f</c>,
        /// <c>Settings.cs:467</c>).</summary>
        public const float DefaultMaxStoreToJob = 50f;

        /// <summary>Max store→job as a fraction of the direct pawn→job trip (WYU
        /// <c>Opportunity_MaxStoreToJobPctOrigTrip = 0.6f</c>, <c>Settings.cs:468</c>).</summary>
        public const float DefaultMaxStoreToJobPctOrigTrip = 0.6f;

        /// <summary>Max TOTAL trip (start→thing + thing→store + store→job) as a fraction of the direct
        /// pawn→job trip (WYU <c>Opportunity_MaxTotalTripPctOrigTrip = 1.7f</c>,
        /// <c>Settings.cs:469</c>).</summary>
        public const float DefaultMaxTotalTripPctOrigTrip = 1.7f;

        /// <summary>Max NEW legs (start→thing + store→job, i.e. the trip minus the thing→store carry) as a
        /// fraction of the direct pawn→job trip (WYU <c>Opportunity_MaxNewLegsPctOrigTrip = 1.0f</c>,
        /// <c>Settings.cs:470</c>).</summary>
        public const float DefaultMaxNewLegsPctOrigTrip = 1.0f;

        /// <summary>Region-flood cap pawn→thing (WYU
        /// <c>Opportunity_MaxStartToThingRegionLookCount = 25</c>, <c>Settings.cs:471</c>). Used by the
        /// Verse layer's <see cref="EnRoutePathChecker.Vanilla"/> region check; no pure analogue.</summary>
        public const int DefaultMaxStartToThingRegionLookCount = 25;

        /// <summary>Region-flood cap store→job (WYU
        /// <c>Opportunity_MaxStoreToJobRegionLookCount = 25</c>, <c>Settings.cs:472</c>). Used by the Verse
        /// layer's <see cref="EnRoutePathChecker.Vanilla"/> region check; no pure analogue.</summary>
        public const int DefaultMaxStoreToJobRegionLookCount = 25;

        /// <summary>WYU default reachability/accuracy mode (WYU
        /// <c>Opportunity_PathChecker = PathCheckerEnum.Default</c>, <c>Settings.cs:463</c>).</summary>
        public const EnRoutePathChecker DefaultPathChecker = EnRoutePathChecker.Default;

        /// <summary>
        /// Outcome of one <see cref="CheckBeforeStore"/> / <see cref="CheckAfterStore"/> phase, mapped 1:1
        /// to WYU's <c>CanHaulResult</c> (<c>OpportunityDetour.cs:106</c>) minus the <c>Success</c> case
        /// (which WYU only produces after the path/region stage, which lives in the Verse layer here).
        /// </summary>
        public enum EnRouteResult
        {
            /// <summary>A straight-line RANGE limit was exceeded (a squared start→thing / start→thing-%
            /// / store→job / store→job-% bound). WYU <c>CanHaulResult.RangeFail</c>. The Verse layer's
            /// handling depends on the path checker: <see cref="EnRoutePathChecker.Vanilla"/> treats it as
            /// <see cref="HardFail"/>; the others SKIP this candidate this band (it may pass in a wider
            /// band).</summary>
            RangeFail,

            /// <summary>A LEG-RATIO bound was exceeded (the unsquared total-trip pre-bound, or the
            /// <c>MaxNewLegs</c> / <c>MaxTotalTrip</c> bounds). WYU <c>CanHaulResult.HardFail</c>. The
            /// candidate can never qualify for this job regardless of band, so the Verse layer removes it
            /// from the candidate list.</summary>
            HardFail,

            /// <summary>This phase's bounds all passed — continue the cascade (proceed to the store search
            /// after <see cref="CheckBeforeStore"/>, or to the path/region stage after
            /// <see cref="CheckAfterStore"/>).</summary>
            Continue
        }

        /// <summary>
        /// Per-scan range band — a faithful port of WYU's <c>MaxRanges</c> struct
        /// (<c>OpportunityDetour.cs:108-134</c>). The four range limits start at the settings values and are
        /// multiplied by <see cref="HeuristicRangeExpandFactor"/> each time the candidate list is exhausted
        /// with no winner, so the expensive store/path searches run in the most-optimistic (closest-first)
        /// order. Mutable value type — copy it, never alias it across scans.
        /// </summary>
        public struct MaxRanges
        {
            /// <summary>WYU <c>MaxRanges.heuristicRangeExpandFactor = 2f</c>
            /// (<c>OpportunityDetour.cs:113</c>).</summary>
            public const float HeuristicRangeExpandFactor = 2f;

            /// <summary>How many times the band has been expanded this scan (0 on the first pass). WYU uses
            /// <c>expandCount == 0</c> to choose the fast/accurate store-search mode
            /// (<c>OpportunityDetour.cs:234</c>); exposed here so the Verse layer can mirror that.</summary>
            public int ExpandCount;

            /// <summary>Current pawn→thing tile cap (starts at <see cref="DefaultMaxStartToThing"/>).</summary>
            public float StartToThing;

            /// <summary>Current pawn→thing %-of-trip cap (starts at
            /// <see cref="DefaultMaxStartToThingPctOrigTrip"/>).</summary>
            public float StartToThingPctOrigTrip;

            /// <summary>Current store→job tile cap (starts at <see cref="DefaultMaxStoreToJob"/>).</summary>
            public float StoreToJob;

            /// <summary>Current store→job %-of-trip cap (starts at
            /// <see cref="DefaultMaxStoreToJobPctOrigTrip"/>).</summary>
            public float StoreToJobPctOrigTrip;

            /// <summary>Reset the band to the start (settings) values, expand count 0. Mirrors WYU
            /// <c>MaxRanges.Reset()</c> (<c>OpportunityDetour.cs:118-124</c>). Pass the live settings (or
            /// the defaults) so the Verse layer can drive it from the user knobs.</summary>
            public void Reset(
                float maxStartToThing = DefaultMaxStartToThing,
                float maxStartToThingPctOrigTrip = DefaultMaxStartToThingPctOrigTrip,
                float maxStoreToJob = DefaultMaxStoreToJob,
                float maxStoreToJobPctOrigTrip = DefaultMaxStoreToJobPctOrigTrip)
            {
                ExpandCount = 0;
                StartToThing = maxStartToThing;
                StartToThingPctOrigTrip = maxStartToThingPctOrigTrip;
                StoreToJob = maxStoreToJob;
                StoreToJobPctOrigTrip = maxStoreToJobPctOrigTrip;
            }

            /// <summary>Multiply every range by <paramref name="multiplier"/> and bump the expand count —
            /// WYU <c>MaxRanges.operator *</c> (<c>OpportunityDetour.cs:126-133</c>). Call after a fruitless
            /// pass over the whole candidate list (use <see cref="HeuristicRangeExpandFactor"/>).</summary>
            public void Expand(float multiplier = HeuristicRangeExpandFactor)
            {
                ExpandCount += 1;
                StartToThing *= multiplier;
                StartToThingPctOrigTrip *= multiplier;
                StoreToJob *= multiplier;
                StoreToJobPctOrigTrip *= multiplier;
            }
        }

        /// <summary>
        /// PHASE 1 — the cheap checks that need only the pawn, thing and job positions, run BEFORE the
        /// store search. Faithful port of <c>OpportunityDetour.cs:216-226</c>. All distances are
        /// straight-line tiles; the squared variants avoid a <c>Sqrt</c> exactly as WYU does (the
        /// <c>:Sqrt</c> note at <c>OpportunityDetour.cs:213-215</c>).
        /// </summary>
        /// <param name="pawnToThing">Straight-line tiles pawn→thing.</param>
        /// <param name="pawnToJob">Straight-line tiles pawn→job (the direct trip).</param>
        /// <param name="thingToJob">Straight-line tiles thing→job.</param>
        /// <param name="ranges">The current per-scan range band.</param>
        /// <param name="maxTotalTripPctOrigTrip">Total-trip cap fraction (default
        /// <see cref="DefaultMaxTotalTripPctOrigTrip"/>).</param>
        /// <returns>
        /// <see cref="EnRouteResult.RangeFail"/> if a squared range bound is exceeded;
        /// <see cref="EnRouteResult.HardFail"/> if the total-trip pre-bound is exceeded;
        /// <see cref="EnRouteResult.Continue"/> otherwise (proceed to the store search).
        /// </returns>
        public static EnRouteResult CheckBeforeStore(
            float pawnToThing, float pawnToJob, float thingToJob,
            in MaxRanges ranges,
            float maxTotalTripPctOrigTrip = DefaultMaxTotalTripPctOrigTrip)
        {
            // :Sqrt — squared range gate on pawn→thing (OpportunityDetour.cs:216-217).
            float startToThingSquared = pawnToThing * pawnToThing;
            if (startToThingSquared > Sq(ranges.StartToThing))
                return EnRouteResult.RangeFail;

            // Squared %-of-trip gate on pawn→thing (OpportunityDetour.cs:218-219).
            float pawnToJobSquared = pawnToJob * pawnToJob;
            if (startToThingSquared > pawnToJobSquared * Sq(ranges.StartToThingPctOrigTrip))
                return EnRouteResult.RangeFail;

            // Unsquared total-trip pre-bound: if start→thing + thing→job already exceeds the total-trip
            // budget, the post-store MaxTotalTrip check certainly will, so hard-fail now
            // (OpportunityDetour.cs:224-226).
            if (pawnToThing + thingToJob > pawnToJob * maxTotalTripPctOrigTrip)
                return EnRouteResult.HardFail;

            return EnRouteResult.Continue;
        }

        /// <summary>
        /// PHASE 2 — the checks that need the chosen store cell, run AFTER the store search. Faithful port
        /// of <c>OpportunityDetour.cs:241-250</c>. Reused by the Verse layer's path-cost stage by feeding
        /// the real A* path costs in place of the straight-line distances (range checks then become
        /// redundant and are still satisfied; WYU's <c>WithinPathCost</c> only applies the
        /// <c>MaxNewLegs</c> / <c>MaxTotalTrip</c> bounds, so when reused for path costs the caller passes
        /// range caps large enough to pass — or, simpler, calls the two leg-bound checks directly).
        /// </summary>
        /// <param name="pawnToThing">Straight-line tiles pawn→thing (the start→thing leg).</param>
        /// <param name="pawnToJob">Straight-line tiles pawn→job (the direct trip).</param>
        /// <param name="thingToStore">Straight-line tiles thing→store (the carry leg).</param>
        /// <param name="storeToJob">Straight-line tiles store→job.</param>
        /// <param name="ranges">The current per-scan range band.</param>
        /// <param name="maxNewLegsPctOrigTrip">New-legs cap fraction (default
        /// <see cref="DefaultMaxNewLegsPctOrigTrip"/>).</param>
        /// <param name="maxTotalTripPctOrigTrip">Total-trip cap fraction (default
        /// <see cref="DefaultMaxTotalTripPctOrigTrip"/>).</param>
        /// <returns>
        /// <see cref="EnRouteResult.RangeFail"/> if a squared store→job range bound is exceeded;
        /// <see cref="EnRouteResult.HardFail"/> if a leg-ratio (<c>MaxNewLegs</c> / <c>MaxTotalTrip</c>)
        /// bound is exceeded; <see cref="EnRouteResult.Continue"/> otherwise (proceed to the path/region
        /// stage selected by <see cref="EnRoutePathChecker"/>).
        /// </returns>
        public static EnRouteResult CheckAfterStore(
            float pawnToThing, float pawnToJob, float thingToStore, float storeToJob,
            in MaxRanges ranges,
            float maxNewLegsPctOrigTrip = DefaultMaxNewLegsPctOrigTrip,
            float maxTotalTripPctOrigTrip = DefaultMaxTotalTripPctOrigTrip)
        {
            // :Sqrt — squared range gate on store→job (OpportunityDetour.cs:241-242).
            float storeToJobSquared = storeToJob * storeToJob;
            if (storeToJobSquared > Sq(ranges.StoreToJob))
                return EnRouteResult.RangeFail;

            // Squared %-of-trip gate on store→job (OpportunityDetour.cs:243).
            float pawnToJobSquared = pawnToJob * pawnToJob;
            if (storeToJobSquared > pawnToJobSquared * Sq(ranges.StoreToJobPctOrigTrip))
                return EnRouteResult.RangeFail;

            // :MaxNewLeg — the two NEW legs (start→thing + store→job) must fit the new-legs budget
            // (OpportunityDetour.cs:245-247).
            if (pawnToThing + storeToJob > pawnToJob * maxNewLegsPctOrigTrip)
                return EnRouteResult.HardFail;

            // :MaxTotalTrip — the whole detour (start→thing + thing→store + store→job) must fit the
            // total-trip budget (OpportunityDetour.cs:248-250).
            if (pawnToThing + thingToStore + storeToJob > pawnToJob * maxTotalTripPctOrigTrip)
                return EnRouteResult.HardFail;

            return EnRouteResult.Continue;
        }

        /// <summary>
        /// The leg-ratio bounds ONLY (no range gates) — the exact pair of checks WYU's path-cost stage
        /// applies to the real A* path costs (<c>WithinPathCost</c>, <c>OpportunityDetour.cs:282-289</c>).
        /// The Verse layer calls this with path costs when <see cref="EnRoutePathChecker"/> is
        /// <see cref="EnRoutePathChecker.Default"/> / <see cref="EnRoutePathChecker.Pathfinding"/>.
        /// Returns true when BOTH the new-legs and total-trip bounds hold (i.e. the path is acceptable).
        /// </summary>
        /// <param name="pawnToThingCost">Path cost pawn→thing.</param>
        /// <param name="thingToStoreCost">Path cost thing→store.</param>
        /// <param name="storeToJobCost">Path cost store→job.</param>
        /// <param name="pawnToJobCost">Path cost pawn→job (the direct trip).</param>
        public static bool WithinPathLegBounds(
            float pawnToThingCost, float thingToStoreCost, float storeToJobCost, float pawnToJobCost,
            float maxNewLegsPctOrigTrip = DefaultMaxNewLegsPctOrigTrip,
            float maxTotalTripPctOrigTrip = DefaultMaxTotalTripPctOrigTrip)
        {
            // OpportunityDetour.cs:282-283 — new legs (pawn→thing + store→job).
            if (pawnToThingCost + storeToJobCost > pawnToJobCost * maxNewLegsPctOrigTrip)
                return false;
            // OpportunityDetour.cs:288-289 — total trip (pawn→thing + thing→store + store→job).
            if (pawnToThingCost + thingToStoreCost + storeToJobCost > pawnToJobCost * maxTotalTripPctOrigTrip)
                return false;
            return true;
        }

        // --- Midway store-cell ranking ------------------------------------------------------------------

        /// <summary>
        /// The MIDWAY between the thing and the job, as an integer cell — the point WYU measures candidate
        /// store cells against (<c>StoreUtility.cs:249</c>:
        /// <c>new IntVec3((target.x + thing.x) / 2, target.y, (target.z + thing.z) / 2)</c>). Integer
        /// division floors toward zero, matching WYU exactly (the Verse layer passes the thing's and job's
        /// X/Y/Z cell coords). Y is taken from the JOB target (single map level; WYU keeps the target's
        /// y).
        /// </summary>
        public static void Midway(
            int thingX, int thingY, int thingZ, int jobX, int jobY, int jobZ,
            out int midX, out int midY, out int midZ)
        {
            midX = (jobX + thingX) / 2;
            midY = jobY; // WYU uses the detour target's y (StoreUtility.cs:249)
            midZ = (jobZ + thingZ) / 2;
        }

        /// <summary>
        /// Horizontal squared distance from a candidate store cell to the thing↔job midway — the ranking
        /// key WYU uses to pick the store cell "closest to halfway to target"
        /// (<c>StoreUtility.cs:250-257</c>, <c>(position - cell).LengthHorizontalSquared</c>, X/Z only).
        /// Squared (no <c>Sqrt</c>); lower is better.
        /// </summary>
        public static int MidwayDistanceSquared(int cellX, int cellZ, int midX, int midZ)
        {
            int dx = cellX - midX;
            int dz = cellZ - midZ;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// True when <paramref name="candidateDistSquared"/> is STRICTLY closer to the midway than the
        /// running best — the replacement test WYU uses while scanning a slot group's cells
        /// (<c>StoreUtility.cs:256-257</c>: <c>if (distSquared &gt; closestDistSquared) continue;</c>, i.e.
        /// replace only on a strictly smaller distance, so the FIRST cell among equals wins — stable).
        /// The Verse layer enumerates the candidate cells; this is the comparison only.
        /// </summary>
        public static bool MidwayBetter(int candidateDistSquared, int bestDistSquared)
            => candidateDistSquared < bestDistSquared;

        private static float Sq(float v) => v * v;
    }
}
