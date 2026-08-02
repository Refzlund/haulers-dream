using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The ONE place Hauler's Dream reads a <see cref="DrugPolicy"/> by drug (issue #232). Everything here goes
    /// through the policy's INTEGER indexer and its <c>Count</c>; the per-<c>ThingDef</c> indexer is never touched.
    ///
    /// <para>WHY. <c>DrugPolicy</c>'s <c>ThingDef</c> indexer walks the entry list and, on no match, ends in a bare
    /// <c>throw new ArgumentException();</c> — no message, which is why the game reports it as the otherwise
    /// baffling "Value does not fall within the expected range." A policy missing an entry for some def is
    /// supposed to be impossible (<c>DrugPolicy.InitializeIfNeeded</c> fills one in for every drug def), and yet
    /// issue #232 is a report of exactly that shape for a modded alcohol. HD cannot fix a policy it does not own,
    /// but it can stop asking questions that throw: every read here answers "no entry" instead.</para>
    ///
    /// <para>THE TWO MEMBERS ARE NOT INTERCHANGEABLE, and collapsing them would be a silent behaviour change.
    /// <see cref="EntryFor"/> returns the FIRST match, which is exactly what <c>DrugPolicy</c>'s own
    /// <c>ThingDef</c> indexer does, so the #229 addiction clause stays byte-identical for every def that has an
    /// entry. <see cref="TakeToInventoryTotal"/> SUMS every match, which is what
    /// <c>InventorySurplus.KeepCountOf</c> has always done — and a policy CAN carry duplicate entries for one def
    /// (<c>DrugPolicy.CopyFrom</c> clears and re-copies the list with no re-initialisation, so a malformed or
    /// mod-edited source propagates as-is), in which case a first-match keep count would be lower than the
    /// count the pawn's unload has been honouring. Preserve the sum.</para>
    ///
    /// <para>Read-only, always: HD never adds, removes or edits an entry. A <c>DrugPolicy</c> is shared, SCRIBED
    /// state, and one of vanilla's callers is the float-menu build on the clicking client's UI thread — writing to
    /// it there would be both a save mutation and a multiplayer desync, the same hazard
    /// <see cref="Patch_JobGiver_DropUnusedInventory_ShouldKeepDrug"/> documents for the tag-set self-heal.</para>
    /// </summary>
    internal static class DrugPolicyLookup
    {
        /// <summary>
        /// The policy's entry for one drug, or null when the policy holds none — the answer vanilla's
        /// <c>ThingDef</c> indexer throws instead of giving.
        /// </summary>
        /// <param name="policy">The pawn's current drug policy; null yields null.</param>
        /// <param name="def">The drug def to look up; null yields null.</param>
        /// <returns>The FIRST matching entry (vanilla's own semantics), or null.</returns>
        internal static DrugPolicyEntry EntryFor(DrugPolicy policy, ThingDef def)
        {
            if (policy == null || def == null)
                return null;
            for (int i = 0; i < policy.Count; i++)
            {
                var entry = policy[i];
                if (entry != null && entry.drug == def)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// How many units of one drug the policy wants kept in the pawn's inventory: the SUM of
        /// <c>takeToInventory</c> over every entry naming that def (see the class doc for why the sum, not the
        /// first match).
        /// </summary>
        /// <param name="policy">The pawn's current drug policy; null yields 0.</param>
        /// <param name="def">The drug def to total; null yields 0.</param>
        /// <returns>Total units to keep, 0 when the policy has no entry for the def (or wants none kept). Only
        /// positive per-entry values are added, matching the pre-existing keep-count read.</returns>
        internal static int TakeToInventoryTotal(DrugPolicy policy, ThingDef def)
        {
            if (policy == null || def == null)
                return 0;
            int total = 0;
            for (int i = 0; i < policy.Count; i++)
            {
                var entry = policy[i];
                if (entry != null && entry.drug == def && entry.takeToInventory > 0)
                    total += entry.takeToInventory;
            }
            return total;
        }
    }
}
