namespace HaulersDream.Core
{
    /// <summary>What kind of spoilable a bill-ingredient candidate is, after the two toggles are
    /// applied. Corpse is decided BEFORE Food (a food-corpse is owned by the butcher toggle only,
    /// never double-gated). None = not rottable, or the governing toggle is off, or non-fresh.</summary>
    public enum IngredientSpoilKind { None, Corpse, Food }

    /// <summary>Pure, deterministic comparator that reorders bill-ingredient candidates so the
    /// most-spoiling rottable candidate is chosen first, WITHOUT changing recipe satisfaction.
    /// It only ever produces a STABLE refinement of the vanilla order: eligible candidates float
    /// forward by ascending ticks-until-rot; every non-eligible candidate (and every tie) keeps its
    /// original index, so a list with no eligible candidate is the identity permutation (non-food
    /// crafts are byte-identical). The runtime extracts the four primitives off each Thing and feeds
    /// them here; no Verse types cross this boundary.</summary>
    public static class SpoilingFirstSelection
    {
        /// <summary>The frozen / inactive / never-rots sentinel (matches CompRottable's 72000000),
        /// used as the ticks value for a candidate with no live CompRottable so it sorts last among
        /// any eligible bucket. (Such a candidate is normally None anyway.)</summary>
        public const int NeverRots = int.MaxValue;

        /// <summary>Classify a candidate. Corpse precedence is intentional (a rottable corpse that is
        /// also "food" is governed by the butcher toggle only). A candidate that is not rottable+Active+
        /// Fresh must be passed with isRottable=false so it classifies None and keeps its vanilla slot.</summary>
        public static IngredientSpoilKind Categorize(bool isCorpse, bool isRottable,
            bool butcherSpoilingFirst, bool cookSpoilingFirst)
        {
            if (isCorpse) return butcherSpoilingFirst ? IngredientSpoilKind.Corpse : IngredientSpoilKind.None;
            if (isRottable) return cookSpoilingFirst ? IngredientSpoilKind.Food : IngredientSpoilKind.None;
            return IngredientSpoilKind.None;
        }

        public static bool IsEligible(IngredientSpoilKind kind) => kind != IngredientSpoilKind.None;

        /// <summary>The spoiling RANK comparison only: eligible-before-non-eligible, then ascending
        /// ticks (most-spoiled first) among the eligible. Returns 0 on a spoil-rank tie — i.e. when
        /// both candidates are non-eligible, OR both are eligible with equal ticks — leaving the
        /// CALLER to apply its own tiebreak (the original list index for NoMix/batch, or the vanilla
        /// value/distance keys for the AllowMix cook path). Keeps Verse types out of Core.</summary>
        public static int CompareSpoilRank(
            IngredientSpoilKind aKind, int aTicks,
            IngredientSpoilKind bKind, int bTicks)
        {
            bool ae = IsEligible(aKind), be = IsEligible(bKind);
            if (ae != be) return ae ? -1 : 1;        // eligible floats to the front
            if (ae) return aTicks.CompareTo(bTicks); // both eligible: most-spoiling first
            return 0;                                 // both non-eligible: caller decides the tiebreak
        }

        /// <summary>Total deterministic order: eligible-before-non-eligible, then ascending ticks
        /// (most-spoiled first) among the eligible, then the candidate's ORIGINAL index (so the sort
        /// is a stable refinement and the non-eligible bucket preserves vanilla distance order).
        /// Delegates the rank to <see cref="CompareSpoilRank"/>, falling back to the index when the
        /// rank ties — behaviour-preserving with the prior inline form.</summary>
        public static int Compare(
            IngredientSpoilKind aKind, int aTicks, int aIndex,
            IngredientSpoilKind bKind, int bTicks, int bIndex)
        {
            int c = CompareSpoilRank(aKind, aTicks, bKind, bTicks);
            return c != 0 ? c : aIndex.CompareTo(bIndex);  // stable tiebreak == vanilla order for the rest
        }
    }
}
