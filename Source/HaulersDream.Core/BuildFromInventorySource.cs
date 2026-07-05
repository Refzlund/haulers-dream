using System;

namespace HaulersDream.Core
{
    /// <summary>Where a candidate inventory/floor stack of a needed construction material lives, relative to
    /// the constructing worker. The enum's declared order IS the source priority (lower = preferred):
    /// the worker's OWN inventory, then loose FLOOR stock, then OTHER colonists' inventories, then PACK
    /// animals' / caravan cargo. (BFI's order is own→floor→others; HD splits "others" into colonists vs
    /// pack animals so a teammate's carried steel is preferred over disturbing a loaded pack animal.)</summary>
    public enum BuildMaterialSource { Own = 0, Floor = 1, Other = 2, PackAnimal = 3 }

    /// <summary>Pure decisions for the Build-From-Inventory feature: (A) does enough material exist across
    /// inventory + floor to OFFER a construction job, honoring the partial-build gate; and (B) the
    /// own→floor→other→pack source-priority ordering of candidate stacks. The runtime extracts the
    /// primitives (counts per source, this-stack's source/distance) and feeds them here. No Verse types
    /// cross this boundary, so it is headless-NUnit-testable and MP-deterministic (no Rand).
    ///
    /// In HD's POSTFIX architecture the floor source always lost before the organic fallback runs (the
    /// deliver postfix only fires when vanilla's floor search returned null), so the runtime F3b path only
    /// ever compares organic candidates (own/other/pack). The full 4-tier order is encoded here for
    /// testability and for any future call site that ranks floor + organic together.</summary>
    public static class BuildFromInventorySource
    {
        // ---- (A) availability counting / gate ----

        /// <summary>Total units of a needed material visible to the build availability gate, summed across all
        /// four sources. The runtime passes each source's already-counted total. Saturating add so a
        /// pathological huge count can't overflow to negative; negative inputs are floored at 0.</summary>
        public static int TotalAvailable(int ownUnits, int floorUnits, int otherUnits, int packUnits)
        {
            long sum = (long)ownUnits + floorUnits + otherUnits + packUnits;
            return sum > int.MaxValue ? int.MaxValue : (sum < 0 ? 0 : (int)sum);
        }

        /// <summary>Does enough material exist to OFFER a construction job for <paramref name="amountNeeded"/>?
        /// FULL gate (partial OFF): need the whole amount available (vanilla's all-or-nothing). PARTIAL gate
        /// (partial ON): ANY unit available is enough — a frame can start from a partial stack (BFI semantics).
        /// amountNeeded &lt;= 0 is "needs nothing" → always available (true). availableUnits &lt;= 0 is never
        /// available (nothing to deliver). This single gate is BOTH "offer the job" and the partial switch.</summary>
        public static bool IsAvailable(int availableUnits, int amountNeeded, bool allowPartial)
        {
            if (amountNeeded <= 0) return true;
            if (availableUnits <= 0) return false;
            return allowPartial ? availableUnits >= 1 : availableUnits >= amountNeeded;
        }

        /// <summary>Convenience: count then gate in one call (the runtime availability-postfix shape).</summary>
        public static bool IsAvailable(int ownUnits, int floorUnits, int otherUnits, int packUnits,
            int amountNeeded, bool allowPartial)
            => IsAvailable(TotalAvailable(ownUnits, floorUnits, otherUnits, packUnits), amountNeeded, allowPartial);

        /// <summary>
        /// Whether a construction delivery should be served straight from the WORKER'S OWN carried stock
        /// instead of fetching a floor/shelf stack. True when the carried units cover at least one full
        /// delivery chunk, the lesser of the site's remaining need and one hand-load (deliveries move at
        /// most a hand-load per deposit either way, so carried stock that fills a whole chunk beats ANY
        /// fetch trip: same units moved, zero fetch walk). Below that threshold a floor fetch moves MORE
        /// units per trip, so vanilla's fetch stands. Previously overlooked (the Steam "goes to the nearest
        /// shelf instead" report): the planner only consulted inventory when the FLOOR was empty map-wide,
        /// so a pawn already carrying the material still walked to a shelf for it.
        /// </summary>
        /// <param name="ownUnits">Units of the needed material in the worker's own inventory.</param>
        /// <param name="neededUnits">Units the site still needs (space remaining, enroute-aware).</param>
        /// <param name="handStackCap">Units one hand-carry moves (<c>MaxStackSpaceEver</c>); the per-deposit chunk size.</param>
        public static bool OwnInventoryCoversDelivery(int ownUnits, int neededUnits, int handStackCap)
        {
            if (neededUnits <= 0 || handStackCap <= 0)
                return false;
            return ownUnits >= Math.Min(neededUnits, handStackCap);
        }

        // ---- (B) source-priority ordering ----

        /// <summary>The priority rank of a source (lower = preferred). == (int)source by construction; a named
        /// function so the contract is explicit and the enum's numeric values can't silently drift the order.</summary>
        public static int SourceRank(BuildMaterialSource source) => (int)source;

        /// <summary>Rank-only comparison of two candidate stacks: by SOURCE first (own&lt;floor&lt;other&lt;pack),
        /// then NEAREST holder/stack first within the same source. Returns 0 on a (source, distance) tie so the
        /// caller can apply its own stable index tiebreak. Mirrors MealsOnWheelsSelection.CompareRank.</summary>
        public static int CompareRank(BuildMaterialSource aSource, int aDist,
                                      BuildMaterialSource bSource, int bDist)
        {
            int ra = SourceRank(aSource), rb = SourceRank(bSource);
            if (ra != rb) return ra.CompareTo(rb);
            return aDist.CompareTo(bDist); // nearest first within a source (own is distance 0 by convention)
        }

        /// <summary>Total deterministic order (most-preferred FIRST; Compare(a,b)&lt;0 ⇒ a chosen over b):
        /// <see cref="CompareRank"/>, then a stable original-index tiebreak. Antisymmetric → List&lt;T&gt;.Sort-safe.</summary>
        public static int Compare(BuildMaterialSource aSource, int aDist, int aIndex,
                                  BuildMaterialSource bSource, int bDist, int bIndex)
        {
            int c = CompareRank(aSource, aDist, bSource, bDist);
            return c != 0 ? c : aIndex.CompareTo(bIndex);
        }
    }
}
