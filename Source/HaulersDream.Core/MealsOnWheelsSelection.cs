namespace HaulersDream.Core
{
    /// <summary>Which pass a carried-food candidate falls in, AFTER the runtime has already gated it
    /// through FoodUtility.BestFoodInInventory (acceptable == willEat && nutrition && pref window).
    /// None = the runtime's acceptability gate rejected it (not eatable by this eater at all).</summary>
    public enum MealCandidatePass { None, RotRescue, Desperation }

    /// <summary>Pure, deterministic decision for the "Meals On Wheels" feature: a hungry free colonist
    /// with no food found by vanilla eats acceptable food carried in ANOTHER player-faction pawn's (or
    /// pack animal's) inventory. The runtime patches FoodUtility.TryFindBestFoodSourceFor (postfix, only
    /// when vanilla found nothing), scans eligible carriers' inventories via FoodUtility.BestFoodInInventory
    /// (which folds in food policy / ideology / royal title / teetotaler / preferability window /
    /// nutrition*stack), extracts the primitives below per stack, and picks the best candidate by
    /// <see cref="Compare"/>.
    ///
    /// Acceptability is NOT re-derived here — it crosses the boundary as the single <c>acceptable</c> bool
    /// (the non-null result of BestFoodInInventory). This type owns ONLY the two-pass priority order:
    /// prefer a Fresh carried meal about to spoil (anti-waste), otherwise any acceptable carried food by
    /// nearest holder. Mirrors <see cref="SpoilingFirstSelection"/>'s shape; no Verse types cross this
    /// boundary, so it is headless-NUnit-testable and MP-deterministic (no Rand).</summary>
    public static class MealsOnWheelsSelection
    {
        /// <summary>Half an in-game day in ticks (GenDate.TicksPerDay/2 == 60000/2). A Fresh carried meal
        /// rotting within this window is rescued first. Matches vanilla's own-inventory rot rescue (the
        /// literal 30000 in FoodUtility.TryFindBestFoodSourceFor) and the reference mod's
        /// GenDate.TicksPerDay/2. The window is EXCLUSIVE (ticks &lt; 30000).</summary>
        public const int RotRescueWindowTicks = 30000;

        /// <summary>Sort-last sentinel ticks for a candidate with no live/active CompRottable (frozen,
        /// inactive, or non-rottable like pemmican/packaged survival meal). Such a candidate never
        /// qualifies for RotRescue (it isn't spoiling) and, being Desperation, is ordered by distance.</summary>
        public const int NeverRots = int.MaxValue;

        // FoodPreferability int values (stable since RimWorld 1.0); the runtime passes
        // (int)def.ingestible.preferability. Only the window edges + the rescue floor matter to Core.
        public const int PrefDesperateOnly = 2;   // FoodPreferability.DesperateOnly
        public const int PrefMealAwful     = 5;   // FoodPreferability.MealAwful  (the rot-rescue floor)
        public const int PrefMealLavish    = 9;   // FoodPreferability.MealLavish

        /// <summary>Classify one ALREADY-acceptable candidate into its pass. RotRescue requires a Fresh+active
        /// rottable stack whose <paramref name="ticksUntilRot"/> is within the rescue window AND preferability
        /// ≥ MealAwful (the reference mod's pass-1 floor); everything else acceptable is Desperation. A
        /// non-acceptable candidate (BestFoodInInventory returned null) must be passed acceptable:false and
        /// classifies None.</summary>
        public static MealCandidatePass Classify(
            bool acceptable, int preferabilityRank, bool isFreshActiveRottable, int ticksUntilRot)
        {
            if (!acceptable) return MealCandidatePass.None;
            if (isFreshActiveRottable
                && preferabilityRank >= PrefMealAwful
                && ticksUntilRot < RotRescueWindowTicks)
                return MealCandidatePass.RotRescue;
            return MealCandidatePass.Desperation;
        }

        /// <summary>True iff this pass should be eaten (anything other than None).</summary>
        public static bool IsCandidate(MealCandidatePass pass) => pass != MealCandidatePass.None;

        /// <summary>Rank-only (no index tiebreak): RotRescue &lt; Desperation &lt; None; within RotRescue
        /// ascending ticks (soonest-to-rot first); within Desperation ascending holder distance (nearest
        /// first). Returns 0 on a within-pass tie so the caller can apply its own index tiebreak. Mirrors
        /// SpoilingFirstSelection.CompareSpoilRank's split. NOTE: distance does NOT leak into RotRescue
        /// ordering (rescue is anti-waste-over-distance) and ticks do NOT leak into Desperation.</summary>
        public static int CompareRank(
            MealCandidatePass aPass, int aTicks, int aDist,
            MealCandidatePass bPass, int bTicks, int bDist)
        {
            int ra = Rank(aPass), rb = Rank(bPass);
            if (ra != rb) return ra.CompareTo(rb);
            switch (aPass)
            {
                case MealCandidatePass.RotRescue:   return aTicks.CompareTo(bTicks); // soonest-to-rot first
                case MealCandidatePass.Desperation: return aDist.CompareTo(bDist);   // nearest holder first
                default:                            return 0;                        // None vs None: caller decides
            }
        }

        /// <summary>Total deterministic order (most-preferred FIRST, i.e. Compare(a,b) &lt; 0 means a is chosen
        /// over b): <see cref="CompareRank"/>, then a stable original-index tiebreak. Antisymmetric →
        /// List&lt;T&gt;.Sort-safe.</summary>
        public static int Compare(
            MealCandidatePass aPass, int aTicks, int aDist, int aIndex,
            MealCandidatePass bPass, int bTicks, int bDist, int bIndex)
        {
            int c = CompareRank(aPass, aTicks, aDist, bPass, bTicks, bDist);
            return c != 0 ? c : aIndex.CompareTo(bIndex);
        }

        // None must sort LAST despite enum value 0, so map to a priority rank.
        private static int Rank(MealCandidatePass p) => p == MealCandidatePass.None ? 2
            : p == MealCandidatePass.Desperation ? 1 : 0;
    }
}
