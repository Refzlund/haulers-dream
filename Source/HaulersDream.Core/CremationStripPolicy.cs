namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for "strip before cremation" (#222): when a corpse is a bill INGREDIENT about to be
    /// consumed by a recipe that would DESTROY its gear (cremation, a modded incinerator: any corpse recipe
    /// with autoStripCorpses OFF), should HD strip the body first so its weapons/apparel/carried items drop on
    /// the cremation tile and get salvaged instead of burned? Unit-tested headlessly; the Verse glue
    /// (CorpseStripper.MaybeStripForCremation) supplies the live facts and performs the drop.
    /// </summary>
    public static class CremationStripPolicy
    {
        /// <summary>
        /// Decide whether a cremation-bound corpse should be stripped onto the bill tile before it is consumed.
        /// </summary>
        /// <param name="settingOn">The player opt-in (stripBeforeCremation). Off means never strip, since the
        /// whole point of cremating dressed is to destroy the gear, so the feature is off by default.</param>
        /// <param name="recipeAutoStrips">The recipe's autoStripCorpses flag. True (e.g. butchering) means the
        /// vanilla consume seam ALREADY strips the body itself, so HD must stay out or it would strip twice.</param>
        /// <param name="anythingToStrip">The corpse's IStrippable.AnythingToStrip(). False means the body is
        /// already bare (or a haul-strip ran en route): nothing to salvage, and re-stripping is a pointless
        /// no-op that only risks churn.</param>
        /// <param name="isPlayerFactionCorpse">True when the dead pawn belonged to the player's faction (a
        /// colonist / colony animal): your own dead, which are not treated as loot by default.</param>
        /// <param name="stripColonistCorpses">The separate opt-in to strip your own dead too. When false, a
        /// player-faction corpse is left dressed even with the cremation-strip feature on.</param>
        /// <returns>True iff HD should strip this corpse onto the cremation tile now.</returns>
        public static bool ShouldStrip(bool settingOn, bool recipeAutoStrips, bool anythingToStrip,
            bool isPlayerFactionCorpse, bool stripColonistCorpses)
            => settingOn
               && !recipeAutoStrips
               && anythingToStrip
               && (!isPlayerFactionCorpse || stripColonistCorpses);
    }
}
