namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for letting CORPSES take part in the bulk-haul sweep (unit-tested headlessly).
    ///
    /// <para>Vanilla splits hauling across two work givers: <c>WorkGiver_HaulGeneral</c>, which returns null for
    /// anything that is a corpse, and <c>WorkGiver_HaulCorpses</c>, which returns null for anything that is not.
    /// HD only ever hooked the first, so a corpse was invisible to the sweep from both directions — a haul
    /// ORDERED on a corpse picked up nothing else, and a corpse lying beside another haul was never picked up on
    /// the way past. That is one bug with two faces, and this type is the switch that closes both.</para>
    ///
    /// <para>The two faces need SEPARATE predicates because they fail differently. A PRIMARY that turns out not
    /// to fit the hauler's carry ceiling costs nothing: the bulk plan declines and vanilla's own hand-haul stands,
    /// so the corpse still moves. A NEIGHBOUR that doesn't fit is simply skipped and left for its own haul. Only
    /// the primary decision can suppress a haul that would otherwise have happened, so keeping them apart makes
    /// that asymmetry explicit at every call site instead of hiding it behind one shared boolean.</para>
    ///
    /// <para>WHAT THIS DELIBERATELY DOES NOT CHANGE: how much a hauler can carry. A humanlike corpse weighs
    /// around 60 kg against a default ceiling near 96 kg, so bodies still move one at a time — that is the carry
    /// model working as designed, not a defect. Small animals (a hare at roughly 24 kg, a squirrel at 12) are
    /// what actually gain: several of them now ride home together, which they never could before.</para>
    /// </summary>
    public static class CorpseSweepPolicy
    {
        /// <summary>
        /// May a corpse be the ANCHOR of a bulk haul — the thing the player (or the work scan) picked, which the
        /// sweep then builds its neighbourhood around? Both switches must be on: the bulk sweep itself, and the
        /// corpse opt-in.
        ///
        /// <para>Saying yes only makes the bulk plan ELIGIBLE to be built. Everything downstream still applies —
        /// carry weight, storage space, reachability — and a plan that fails any of those returns null, leaving
        /// vanilla's single hand-haul in place. So a false here is a guarantee of the old behaviour, while a true
        /// is only a permission to try.</para>
        /// </summary>
        /// <param name="bulkHaulEnabled">The master bulk-haul setting. Off means no sweep of any kind, so the
        /// corpse opt-in cannot revive one on its own.</param>
        /// <param name="bulkHaulCorpses">The corpse opt-in. Off restores the pre-fix behaviour exactly: corpse
        /// hauls stay vanilla's own single-body carry.</param>
        public static bool CanAnchorSweep(bool bulkHaulEnabled, bool bulkHaulCorpses)
            => bulkHaulEnabled && bulkHaulCorpses;

        /// <summary>
        /// May a corpse be SWEPT UP as a neighbour of some other haul — pocketed on the way past, whether the
        /// anchor is another corpse or an ordinary item? Same two switches as <see cref="CanAnchorSweep"/>,
        /// because a player who wants corpses left out of bulk hauling wants them left out of both roles; a
        /// setting that stopped corpses anchoring but still let them be pocketed would be the more surprising
        /// half of the feature, not a useful middle ground.
        ///
        /// <para>Kept as its own method rather than an alias so the two candidate-filter call sites read as what
        /// they are, and so a future asymmetry (say, sweeping neighbours only under an explicit order) has an
        /// obvious place to live without touching the anchor decision.</para>
        /// </summary>
        /// <param name="bulkHaulEnabled">The master bulk-haul setting.</param>
        /// <param name="bulkHaulCorpses">The corpse opt-in.</param>
        public static bool CanSweepAsNeighbor(bool bulkHaulEnabled, bool bulkHaulCorpses)
            => bulkHaulEnabled && bulkHaulCorpses;
    }
}
