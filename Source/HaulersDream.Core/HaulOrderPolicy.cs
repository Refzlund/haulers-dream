namespace HaulersDream.Core
{
    /// <summary>Why an ordered Hauler's Dream HAULING task must not be offered for a pawn, or None.</summary>
    public enum HaulOrderBlock
    {
        /// <summary>The pawn may be ordered to haul.</summary>
        None = 0,

        /// <summary>Cannot physically pick anything up — no setting overrides this.</summary>
        Manipulation = 1,

        /// <summary>The Hauling WORK TYPE is disabled and allowIncapable is off.</summary>
        HaulingDisabled = 2,
    }

    /// <summary>
    /// The capability bar for a Hauler's Dream right-click order that makes a pawn do HAULING WORK
    /// (issue #229). Deliberately the same clause EligibilityPolicy applies on the automatic side
    /// (<c>!allowIncapable &amp;&amp; incapableOfHauling</c>) and NOTHING else: drafted / animal / mech /
    /// directed-activity gating belongs to the AUTONOMOUS gate (YieldRouter.IsEligible), which player
    /// orders must keep bypassing.
    ///
    /// <para>The <c>incapableOfHauling</c> input is a WORK-TYPE fact (Pawn.WorkTypeIsDisabled), never the
    /// WorkTags.Hauling bit: "incapable of dumb labor" disables the Hauling work TYPE through the
    /// ManualDumb/Commoner tags without ever setting that bit
    /// (Core/Defs/WorkTypeDefs/WorkTypes.xml:311-327, where <c>WorkTypeDef Hauling</c> carries
    /// <c>workTags = {ManualDumb, Hauling, Commoner, AllWork}</c> and BackstoryDef.AllowsWorkType is
    /// <c>(workDisables &amp; workType.workTags) == 0</c>), which is exactly the hole #229 reported.</para>
    /// </summary>
    public static class HaulOrderPolicy
    {
        /// <summary>
        /// Whether an ordered HD hauling task may be offered, and if not, why.
        /// </summary>
        /// <param name="capableOfManipulation">Whether the pawn still has the Manipulation capacity — i.e. can
        /// physically pick a stack up at all. False for a pawn with no working hands.</param>
        /// <param name="incapableOfHauling">Whether the HAULING WORK TYPE is disabled for the pawn
        /// (Pawn.WorkTypeIsDisabled), covering backstories, traits, genes, titles, roles and hediffs alike —
        /// NOT the WorkTags.Hauling bit, which a "dumb labor" backstory never sets.</param>
        /// <param name="allowIncapable">The player's "let pawns incapable of hauling haul anyway" setting.</param>
        /// <returns><see cref="HaulOrderBlock.None"/> when the order may be offered, else the blocking reason.</returns>
        public static HaulOrderBlock BlockFor(bool capableOfManipulation, bool incapableOfHauling, bool allowIncapable)
        {
            // Manipulation wins: no setting can make a pawn without hands carry a stack.
            if (!capableOfManipulation)
                return HaulOrderBlock.Manipulation;
            if (incapableOfHauling && !allowIncapable)
                return HaulOrderBlock.HaulingDisabled;
            return HaulOrderBlock.None;
        }
    }
}
