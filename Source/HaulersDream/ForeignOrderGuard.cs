using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Respect other mods' per-item ORDERS so Hauler's Dream doesn't steal an item another mod has claimed
    /// (issue #5 — e.g. Recycle This / Recycle This (Continued), Simple Recycling, and the order-to-recycle family).
    ///
    /// <para>Those mods mark a specific item with a <see cref="Designation"/> and then a WorkGiver carries it to a
    /// workbench to smelt/scrap it. Crucially, that WorkGiver scans only SPAWNED designated things
    /// (<c>DesignationManager.SpawnedDesignationsOfDef</c> / <c>AnySpawnedDesignationOfDef</c>). HD's autonomous
    /// hauling intake scoops loose haulable items into a pawn's INVENTORY (which despawns them), so if it grabs a
    /// designated item before that WorkGiver runs, the order silently stalls — the item is no longer findable. This
    /// is HD-specific: vanilla hauling moves the item to STORAGE, where it stays spawned + designated + processable;
    /// only HD's into-inventory scoop hides it.</para>
    ///
    /// <para>So HD's autonomous inventory sweeps/pickups must LEAVE such items alone. We treat an item as claimed if
    /// it carries any designation OTHER than a HAUL designation: a haul designation (vanilla <c>Haul</c>, or
    /// "Haul Urgently" from Allow Tool or Keyz' Allow Utilities) means "haul this," which HD doing is correct; any
    /// other designation on an item means a mod has claimed it for its own action (recycle, destroy, mend, ...), so
    /// HD must not intercept it. This is a general rule (it costs HD nothing, those items were never HD's to take)
    /// and needs no per-mod knowledge.</para>
    ///
    /// <para>Cheap: the overwhelmingly common "no designation" case is a single O(1) dictionary miss
    /// (<c>DesignationManager.HasMapDesignationOn</c>, backed by a <c>Dictionary&lt;Thing, List&lt;Designation&gt;&gt;</c>),
    /// so this adds no measurable cost to the per-candidate haul gates.</para>
    /// </summary>
    internal static class ForeignOrderGuard
    {
        /// <summary>
        /// True when <paramref name="thing"/> carries a non-haul designation, i.e. another mod has claimed it for a
        /// specific action and HD's autonomous into-inventory intake must skip it. False (don't skip) when there is
        /// no designation, only a haul designation, or no map.
        /// </summary>
        internal static bool ClaimedByForeignOrder(Thing thing)
        {
            var map = thing?.Map;
            if (map == null)
                return false;
            var dm = map.designationManager;
            // O(1) fast path: the item has no designation at all (almost always). No allocation, no list scan.
            if (!dm.HasMapDesignationOn(thing))
                return false;
            EnsureHaulDefsResolved();
            var list = dm.AllDesignationsOn(thing);
            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i].def;
                // A HAUL designation means "haul this," which HD doing is correct, NOT a foreign claim: vanilla
                // "Haul", or "Haul Urgently" from Allow Tool / Keyz' Allow Utilities. The urgent defs come from
                // UrgentHaulCompat (the single source of truth, so the Keyz def is whitelisted here AND drives the
                // urgent bulk pickup, the two can never drift). Any OTHER designation is a mod's per-item order.
                if (def == null || def == haulDef || UrgentHaulCompat.IsUrgentDesignationDef(def))
                    continue;
                return true;  // any other designation → a mod's per-item order owns this item → leave it alone
            }
            return false;
        }

        // The vanilla "Haul" designation, resolved lazily by defName on first actual use (only when an item HAS a
        // designation, i.e. during gameplay, after defs load). Optional/version-dependent, so resolved silently by
        // NAME (null when absent) to avoid a hard reference. The "Haul Urgently" defs (Allow Tool + Keyz) are
        // resolved by UrgentHaulCompat, which the loop above delegates to (one resolver for both consumers).
        private static bool haulDefsResolved;
        private static DesignationDef haulDef;

        private static void EnsureHaulDefsResolved()
        {
            if (haulDefsResolved)
                return;
            haulDefsResolved = true;
            haulDef = DefDatabase<DesignationDef>.GetNamedSilentFail("Haul");
        }
    }
}
