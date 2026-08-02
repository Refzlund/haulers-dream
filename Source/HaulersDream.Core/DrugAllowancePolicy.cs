namespace HaulersDream.Core
{
    /// <summary>
    /// Vanilla's "is this drug allowed for an addiction?" clause, restated so it can be answered for a drug the
    /// colonist's drug policy has NO ENTRY for (issue #232).
    ///
    /// <para>WHY THIS EXISTS. <c>DrugPolicy</c>'s per-<c>ThingDef</c> indexer walks its entry list and, finding no
    /// match, ends in a bare <c>throw new ArgumentException();</c> — no message, so .NET supplies "Value does not
    /// fall within the expected range." Every vanilla caller of that indexer therefore assumes an entry always
    /// exists. Issue #232 is a report where one did not: right-clicking a colonist onto a modded alcohol threw
    /// exactly that, from <c>Pawn_DrugPolicyTracker.AllowedToTakeScheduledEver</c> inside
    /// <c>JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory</c>, before any Hauler's Dream code ran. HD had
    /// its OWN copy of the same unguarded lookup in the #229 withdrawal-access scan, on a def taken from an
    /// arbitrary colonist's inventory; this policy is what that scan asks instead.</para>
    ///
    /// <para>THE MISSING-ENTRY ANSWER IS FAITHFUL, NOT CONVENIENT. See
    /// <see cref="MissingEntryAllowedForAddiction"/>: it reconstructs the value the entry would have carried had
    /// the policy been initialised for that def, and it is the only value the player cannot have overridden.</para>
    ///
    /// <para>Pure: the game layer extracts the primitives (does the pawn have a policy, is there an entry, what
    /// the entry says, the pawn's trait degree and mental state) and applies the effect.</para>
    /// </summary>
    public static class DrugAllowancePolicy
    {
        /// <summary>
        /// What "allowed for addiction" means for a drug the policy holds NO entry for.
        ///
        /// <para><c>true</c> is vanilla's own value, reconstructed: <c>DrugPolicy.InitializeIfNeeded</c> creates
        /// every entry as <c>new DrugPolicyEntry { drug = …, allowedForAddiction = true }</c>, so a def missing
        /// from the list is a def whose entry was never built — not a def the player set to "not allowed". It is
        /// also the only value the player CANNOT have overridden: a def with no entry has no row in the drug-policy
        /// dialog, so there is nothing there to have been switched off.</para>
        ///
        /// <para><c>false</c> would silently arm the player's rehab lever for a drug they never marked, which is
        /// exactly the surprise this constant exists to make impossible to introduce by accident — it is asserted
        /// by name in the tests, so flipping it fails the build rather than changing behaviour quietly.</para>
        /// </summary>
        public const bool MissingEntryAllowedForAddiction = true;

        /// <summary>
        /// The drug policy's "allowed for addiction" answer for one drug, safe for a def the policy has no entry
        /// for.
        /// </summary>
        /// <param name="entryPresent">Whether the policy actually holds an entry for this drug. False is the
        /// #232 case — vanilla's own <c>ThingDef</c> indexer throws there rather than answering.</param>
        /// <param name="entryAllowedForAddiction">That entry's <c>allowedForAddiction</c> flag. Ignored entirely
        /// when <paramref name="entryPresent"/> is false, so the caller may pass any value it likes.</param>
        /// <returns>The entry's flag when there is an entry, otherwise
        /// <see cref="MissingEntryAllowedForAddiction"/>.</returns>
        public static bool AllowedForAddiction(bool entryPresent, bool entryAllowedForAddiction)
            => entryPresent ? entryAllowedForAddiction : MissingEntryAllowedForAddiction;

        /// <summary>
        /// Whether the drug policy REFUSES this drug to a pawn seeking it for an addiction — vanilla's clause from
        /// <c>JobGiver_SatisfyChemicalNeed</c>'s private <c>DrugValidator</c> closure, restated verbatim except
        /// that the missing-entry case answers instead of throwing.
        ///
        /// <para>Vanilla's shape, for reference: <c>policy != null &amp;&amp; !policy[def].allowedForAddiction
        /// &amp;&amp; story != null &amp;&amp; traits.DegreeOfTrait(DrugDesire) &lt;= 0 &amp;&amp; (!InMentalState
        /// || !MentalStateDef.ignoreDrugPolicy)</c>. Every clause is preserved and the conjunction is unchanged;
        /// only the second one is answered through <see cref="AllowedForAddiction"/>.</para>
        /// </summary>
        /// <param name="hasPolicy">Whether the pawn has a current drug policy at all. No policy means no refusal —
        /// vanilla's own first clause.</param>
        /// <param name="entryPresent">Whether that policy holds an entry for this drug (see
        /// <see cref="AllowedForAddiction"/>).</param>
        /// <param name="entryAllowedForAddiction">That entry's flag; a don't-care when
        /// <paramref name="entryPresent"/> is false.</param>
        /// <param name="hasStory">Whether the pawn has a <c>story</c> (its trait set). Vanilla reads traits only
        /// behind this null check, and a pawn without one is never refused.</param>
        /// <param name="drugDesireDegree">The pawn's Drug Desire trait degree, 0 when it has no such trait. A
        /// POSITIVE degree (chemical interest / fascination) exempts the pawn from the policy, exactly as in
        /// vanilla; a negative degree (teetotaler) does not. Ignored when <paramref name="hasStory"/> is
        /// false.</param>
        /// <param name="mentalStateIgnoresPolicy">Whether the pawn is in a mental state whose
        /// <c>ignoreDrugPolicy</c> is set (binge and friends) — vanilla's other exemption.</param>
        /// <returns>True only when the policy genuinely refuses this drug for this addiction.</returns>
        public static bool BlocksAddictionUse(bool hasPolicy, bool entryPresent, bool entryAllowedForAddiction,
                                              bool hasStory, int drugDesireDegree, bool mentalStateIgnoresPolicy)
            => hasPolicy
               && !AllowedForAddiction(entryPresent, entryAllowedForAddiction)
               && hasStory
               && drugDesireDegree <= 0
               && !mentalStateIgnoresPolicy;

        /// <summary>
        /// Whether Hauler's Dream may ROUTE a withdrawing pawn to this drug (the #229 leg, hardened by #232).
        ///
        /// <para>THIS IS A DIFFERENT QUESTION FROM <see cref="BlocksAddictionUse"/>, AND MERGING THE TWO REOPENS
        /// THE BUG. <see cref="BlocksAddictionUse"/> answers <i>what the policy says</i>, and for a drug with no
        /// entry it correctly answers "not refused" — see <see cref="MissingEntryAllowedForAddiction"/>, which
        /// stays exactly as it is. This answers <i>whether the rest of the game can finish the job</i>, and there
        /// a missing entry is disqualifying. The take does not merely permit a drug: it MOVES a dose into the
        /// seeker's own inventory, and vanilla re-validates it there on the very next think through
        /// <c>JobGiver_SatisfyChemicalNeed</c>'s private <c>DrugValidator</c>, which performs the unguarded
        /// <c>drugPolicy[drug.def]</c> lookup that has no entry to find. Routing to such a drug would hand the
        /// addict a dose it can never ingest (that lookup throws every think, and the priority sorter then skips
        /// the whole drug-satisfaction node) and never shed (the drop loop keeps it — the pawn IS addicted).
        /// Standing down leaves the pawn exactly as it would be without Hauler's Dream, which is what an optional
        /// enhancement at a think-node seam owes its caller.</para>
        /// </summary>
        /// <param name="entryPresent">Whether the seeker's policy holds an entry for this drug. False disqualifies
        /// the route on its own, whatever the policy question answered, because vanilla's own re-validation of the
        /// taken dose would throw.</param>
        /// <param name="policyBlocks">The <see cref="BlocksAddictionUse"/> verdict for this drug and seeker — the
        /// player's rehab lever, unchanged.</param>
        /// <returns>True only when the policy permits the drug AND vanilla can evaluate it from an entry.</returns>
        public static bool MayRouteToDrug(bool entryPresent, bool policyBlocks)
            => entryPresent && !policyBlocks;
    }
}
