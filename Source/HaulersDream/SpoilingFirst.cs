using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>Runtime bridge for Spoiling-First Selection: reads CompRottable / is-Corpse off a
    /// candidate Thing, routes the two toggles, and delegates ranking to the pure Core comparator.
    /// Returns "no change" when both toggles are off (so the caller falls back to its exact vanilla
    /// path -> non-food bills byte-identical). No game types cross into Core.</summary>
    public static class SpoilingFirst
    {
        /// <summary>Per-candidate primitives, computed ONCE (never inside the Sort delegate, which would
        /// re-read the comp O(n log n) times). ticks defaults to the NeverRots sentinel; stock is the def's
        /// colony stockpile count, filled only for the most-stocked-first cook path (#137) and 0 otherwise.</summary>
        private struct Cand { public IngredientSpoilKind kind; public int ticks; public int index; public int stock; }

        private static Cand Classify(Thing t, int index, HaulersDreamSettings s)
        {
            bool isCorpse = t is Corpse;
            var rot = t.TryGetComp<CompRottable>();
            // Only a live, Fresh CompRottable participates: a Rotting/Dessicated stack reports
            // TicksUntilRot 0 and would falsely sort first; an inactive (e.g. mid-incubation hatcher)
            // comp must not be treated as spoiling food.
            bool isRottable = rot != null && rot.Active && rot.Stage == RotStage.Fresh;
            int ticks = (rot != null && rot.Active) ? rot.TicksUntilRotAtCurrentTemp
                                                    : SpoilingFirstSelection.NeverRots;
            var kind = SpoilingFirstSelection.Categorize(isCorpse, isRottable,
                s.butcherSpoilingFirst, s.cookSpoilingFirst);
            return new Cand { kind = kind, ticks = ticks, index = index };
        }

        /// <summary>Both toggles off ⇒ feature is a pure no-op. Cheap gate every caller uses first.</summary>
        public static bool AnyToggleOn(HaulersDreamSettings s)
            => s != null && (s.butcherSpoilingFirst || s.cookSpoilingFirst);

        /// <summary>True iff this is an AllowMix bill that COOKS food (its produced def is ingestible). This
        /// is the recipe/product test BOTH opt-in cook keys share (spoiling-first and #137's most-stocked-
        /// first), so a bill is classified once. Gates on the recipe PRODUCT being ingestible (not on
        /// ingredients being rottable): that cleanly includes every meal/kibble/pemmican recipe and EXCLUDES
        /// Make_ChemfuelFromOrganics (consumes rottable food, produces non-food chemfuel) and Make_Patchleather
        /// (non-food). A recipe with no single produced def (special/multi products) yields null
        /// ProducedThingDef ⇒ false ⇒ not reordered (correct). Butchery is NoMix and never reaches here.</summary>
        public static bool IsCookBill(Bill bill, HaulersDreamSettings s)
            => s != null
               && bill?.recipe != null
               && bill.recipe.allowMixingIngredients
               && bill.recipe.ProducedThingDef?.IsIngestible == true;

        /// <summary>True iff this is a cook-food bill AND the spoiling-first cook toggle is on: the case where
        /// <see cref="SortAllowMix"/> reorders by perishability. (The most-stocked-first key, #137, gates on
        /// <see cref="HaulersDreamSettings.cookMostStockFirst"/> against the same <see cref="IsCookBill"/>.)</summary>
        public static bool IsCookSpoilingBill(Bill bill, HaulersDreamSettings s)
            => s != null && s.cookSpoilingFirst && IsCookBill(bill, s);

        /// <summary>A def's total in the colony's stockpiles (the resource-readout count) on <paramref name="map"/>,
        /// for the most-stocked-first cook key (#137). 0 when the def is null, uncounted, or there is no map, so an
        /// uncounted candidate ranks as least-stocked (used last). Cheap: the ResourceCounter keeps this cached per
        /// def (GetCount returns 0 for an uncounted def without erroring; TryGetValue would throw on a null def, so
        /// the null guard is load-bearing).</summary>
        /// <param name="def">The ingredient def to count. Null yields 0.</param>
        /// <param name="map">The map whose colony stockpiles to sum. Null yields 0.</param>
        public static int StockOfDef(ThingDef def, Map map)
            => (def != null && map?.resourceCounter != null) ? map.resourceCounter.GetCount(def) : 0;

        /// <summary>The candidate's def stock on the map it is on or held on. See <see cref="StockOfDef"/>.</summary>
        private static int StockOf(Thing t) => StockOfDef(t?.def, t?.MapHeld);

        /// <summary>Opt-in cook-ingredient sort for the vanilla AllowMix chooser. The transpiler forwards the
        /// SAME receiver list + the SAME two vanilla key selectors (value-per-unit asc, then squared distance
        /// asc) and adds <paramref name="bill"/>/<paramref name="s"/>.
        ///
        /// Two independent cook keys layer on top of the vanilla order, both only for a cook-food bill
        /// (<see cref="IsCookBill"/>): the MOST-STOCKED-FIRST key (#137, PRIMARY when on) prefers the def the
        /// colony has the most of, so surplus is used up and scarce ingredients are preserved; the
        /// SPOILING-FIRST key (secondary) floats the most-perishable valid stack forward. The final order is
        /// (stock desc, spoil rank, value asc, distance asc): stock chooses the def, spoiling breaks ties among
        /// stacks of that def, and the vanilla value→distance keys break the rest so the fill loop's
        /// nutrition/count accounting is unchanged.
        ///
        /// When this is NOT a cook-food bill, or BOTH cook toggles are off, we call the IDENTICAL vanilla sort
        /// verbatim: byte-for-byte the original order for chemfuel, patchleather, every non-cook AllowMix
        /// recipe, and cooking with the feature disabled.</summary>
        public static void SortAllowMix(List<Thing> things, Func<Thing, float> valueKey,
            Func<Thing, int> distKey, Bill bill, HaulersDreamSettings s)
        {
            if (things == null) return;

            // A cook-food bill may be reordered by either opt-in cook key; both share the same food-bill test.
            bool cook = IsCookBill(bill, s);
            bool applyStock = cook && s.cookMostStockFirst;
            bool applySpoil = cook && s.cookSpoilingFirst;

            // Non-cook, or neither cook key on: replicate vanilla's two-key SortBy exactly (stable ascending by
            // valueKey then distKey): byte-identical for chemfuel, patchleather, every non-cook AllowMix
            // recipe, and cooking with the feature off.
            if (things.Count < 2 || (!applyStock && !applySpoil))
            {
                things.SortBy(valueKey, distKey);
                return;
            }

            // Cook-food bill: precompute each Thing's (kind, ticks, stock) ONCE (never inside the Sort
            // delegate). Reuse a [ThreadStatic] scratch Cand[] across calls (the sort runs single-threaded
            // inside one JobOnThing scan; cands is fully consumed before any reentrant scan can start). Stock is
            // read only when its key is on (its per-def resource count is otherwise unneeded).
            int n = things.Count;
            var cands = RentCands(n);
            bool anyEligible = false;
            for (int i = 0; i < n; i++)
            {
                cands[i] = Classify(things[i], i, s);
                if (applyStock) cands[i].stock = StockOf(things[i]);
                if (SpoilingFirstSelection.IsEligible(cands[i].kind)) anyEligible = true;
            }
            // With the stock key OFF and no candidate rottable, spoiling never breaks a tie ⇒ the comparator
            // degrades to the exact vanilla (value asc, distance asc) order, so use the cheap vanilla SortBy
            // (mirrors ReorderInPlace's !anyEligible early-out). The stock key, when on, always reorders.
            if (!applyStock && !anyEligible)
            {
                things.SortBy(valueKey, distKey);
                return;
            }

            // Sort an index permutation so the per-Thing classification + vanilla keys are read once per
            // element, then materialise the Things in the new order. The comparator is a total order:
            // stock desc (when on), then spoil rank, then value asc, then distance asc, then index. Hoisted
            // into a struct IComparer<int> holding the inputs in fields, so there is no capturing closure and no
            // per-call delegate allocation.
            var order = RentOrder(n);
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, 0, n, new AllowMixComparer(applyStock, cands, things, valueKey, distKey));
            var sorted = RentSorted(n);
            for (int i = 0; i < n; i++) sorted[i] = things[order[i]];
            for (int i = 0; i < n; i++) things[i] = sorted[i];
            // Release the Thing references the scratch buffer holds (cands/order hold only value types
            // / ints; only the Thing[] scratch needs clearing to avoid pinning a dead candidate).
            Array.Clear(sorted, 0, n);
        }

        /// <summary>Allocation-free index comparator for the AllowMix cook path: stock desc (when the
        /// most-stocked-first key is on) → spoilRank (eligible-first, ascending ticks) → vanilla value asc →
        /// vanilla distance asc → original index (a total order, required by Array.Sort). Holds the per-call
        /// inputs in fields so the sort allocates no closure or delegate.</summary>
        private struct AllowMixComparer : IComparer<int>
        {
            private readonly bool mostStockFirst;
            private readonly Cand[] cands;
            private readonly List<Thing> things;
            private readonly Func<Thing, float> valueKey;
            private readonly Func<Thing, int> distKey;

            public AllowMixComparer(bool mostStockFirst, Cand[] cands, List<Thing> things,
                Func<Thing, float> valueKey, Func<Thing, int> distKey)
            {
                this.mostStockFirst = mostStockFirst;
                this.cands = cands;
                this.things = things;
                this.valueKey = valueKey;
                this.distKey = distKey;
            }

            public int Compare(int x, int y)
            {
                int c = SpoilingFirstSelection.CompareCookRank(mostStockFirst,
                    cands[x].stock, cands[x].kind, cands[x].ticks,
                    cands[y].stock, cands[y].kind, cands[y].ticks);
                if (c != 0) return c;
                c = valueKey(things[x]).CompareTo(valueKey(things[y]));
                if (c != 0) return c;
                c = distKey(things[x]).CompareTo(distKey(things[y]));
                if (c != 0) return c;
                return x.CompareTo(y);   // final stable tiebreak — total order for Array.Sort
            }
        }

        // [ThreadStatic] scratch buffers reused across reorder calls. Each sort runs single-threaded
        // within one JobOnThing scan and fully consumes the buffers before returning, so a per-thread
        // pool is safe (matches the repo's planCache / scratchPool per-thread idioms). Buffers grow as
        // needed and are sized >= the request; only the [0,n) prefix is ever used.
        [System.ThreadStatic] private static Cand[] scratchCands;
        [System.ThreadStatic] private static int[] scratchOrder;
        [System.ThreadStatic] private static Thing[] scratchSorted;

        private static Cand[] RentCands(int n)
        {
            if (scratchCands == null || scratchCands.Length < n)
                scratchCands = new Cand[n];
            return scratchCands;
        }

        private static int[] RentOrder(int n)
        {
            if (scratchOrder == null || scratchOrder.Length < n)
                scratchOrder = new int[n];
            return scratchOrder;
        }

        private static Thing[] RentSorted(int n)
        {
            if (scratchSorted == null || scratchSorted.Length < n)
                scratchSorted = new Thing[n];
            return scratchSorted;
        }

        /// <summary>Stable in-place reorder of the vanilla chooser's candidate list (NoMix only).
        /// Precomputes a (kind,ticks,index) triple per Thing, then sorts by the Core comparator.
        /// Returns false (no change) when both toggles off OR no candidate is eligible (so the list is
        /// left exactly as vanilla left it — non-food/steel bills untouched, no alreadySorted flip).</summary>
        public static bool ReorderInPlace(List<Thing> availableThings, HaulersDreamSettings s)
        {
            if (!AnyToggleOn(s) || availableThings == null || availableThings.Count < 2)
                return false;
            var cands = new Cand[availableThings.Count];
            bool anyEligible = false;
            for (int i = 0; i < availableThings.Count; i++)
            {
                cands[i] = Classify(availableThings[i], i, s);
                if (SpoilingFirstSelection.IsEligible(cands[i].kind)) anyEligible = true;
            }
            if (!anyEligible) return false;   // identity permutation — leave vanilla order + sort flag alone
            // Sort an index permutation by the Core comparator (which carries the original index as the
            // stable tiebreak), then materialise the Things in the new order.
            var order = new int[availableThings.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (x, y) => SpoilingFirstSelection.Compare(
                cands[x].kind, cands[x].ticks, cands[x].index,
                cands[y].kind, cands[y].ticks, cands[y].index));
            var sorted = new Thing[availableThings.Count];
            for (int i = 0; i < order.Length; i++) sorted[i] = availableThings[order[i]];
            for (int i = 0; i < sorted.Length; i++) availableThings[i] = sorted[i];
            return true;
        }

        /// <summary>For the batch path: rank candidate <paramref name="b"/> against the current best
        /// <paramref name="a"/>. Returns true iff b should replace a. distA/distB are the squared distances
        /// used as the index-equivalent tiebreak (so equal-eligibility ties pick the nearer stack — the
        /// batch loop's existing nearest-first behaviour). Callers gate on <see cref="AnyToggleOn"/> first.</summary>
        public static bool BetterThan(Thing b, int distB, Thing a, int distA, HaulersDreamSettings s)
        {
            var cb = Classify(b, distB, s);   // index slot carries distance for the tiebreak
            var ca = Classify(a, distA, s);
            return SpoilingFirstSelection.Compare(cb.kind, cb.ticks, cb.index,
                                                  ca.kind, ca.ticks, ca.index) < 0;
        }

        /// <summary>Cook-path variant of <see cref="BetterThan"/> for HD's batch-cook ingredient pickers
        /// (issue #137). Returns true iff candidate <paramref name="b"/> should rank before the current best
        /// <paramref name="a"/> under (stock desc when on, spoil rank, distance): the most-stocked def wins first,
        /// then the more-perishable stack, then the nearer one (distA/distB are the squared distances used as the
        /// index-equivalent tiebreak, matching BetterThan).
        ///
        /// <paramref name="applyStock"/> MUST already fold in the cook-food + toggle gate
        /// (<see cref="IsCookBill"/> AND <see cref="HaulersDreamSettings.cookMostStockFirst"/>) so non-food batch
        /// crafts never rank by stock. When it is false this is BYTE-IDENTICAL to <see cref="BetterThan"/>:
        /// <see cref="SpoilingFirstSelection.CompareCookRank"/> reduces to the spoil rank, and the 0-tie falls to
        /// the same distance tiebreak, so the existing batch spoiling behaviour is unchanged.</summary>
        public static bool CookBetterThan(Thing b, int distB, Thing a, int distA, bool applyStock, HaulersDreamSettings s)
        {
            var cb = Classify(b, distB, s);
            var ca = Classify(a, distA, s);
            int sb = applyStock ? StockOf(b) : 0;
            int sa = applyStock ? StockOf(a) : 0;
            int c = SpoilingFirstSelection.CompareCookRank(applyStock, sb, cb.kind, cb.ticks, sa, ca.kind, ca.ticks);
            return c != 0 ? c < 0 : distB < distA;   // distance breaks a stock+spoil tie (== BetterThan's index tiebreak)
        }
    }
}
