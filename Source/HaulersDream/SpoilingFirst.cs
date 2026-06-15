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
        /// re-read the comp O(n log n) times). ticks defaults to the NeverRots sentinel.</summary>
        private struct Cand { public IngredientSpoilKind kind; public int ticks; public int index; }

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

        /// <summary>True iff this is an AllowMix bill that COOKS food and the cook toggle is on — the
        /// only case where <see cref="SortAllowMix"/> reorders. Gates on the recipe PRODUCT being
        /// ingestible (not on ingredients being rottable): that cleanly includes every meal/kibble/
        /// pemmican recipe and EXCLUDES Make_ChemfuelFromOrganics (consumes rottable food, produces
        /// non-food chemfuel) and Make_Patchleather (non-food). A recipe with no single produced def
        /// (special/multi products) yields null ProducedThingDef ⇒ false ⇒ not reordered (correct).
        /// The butcher toggle is never consulted here — butchery is NoMix — so toggle independence
        /// holds.</summary>
        public static bool IsCookSpoilingBill(Bill bill, HaulersDreamSettings s)
            => s != null
               && s.cookSpoilingFirst
               && bill?.recipe != null
               && bill.recipe.allowMixingIngredients
               && bill.recipe.ProducedThingDef?.IsIngestible == true;

        /// <summary>Spoiling-first sort for the vanilla AllowMix chooser. The transpiler forwards the
        /// SAME receiver list + the SAME two vanilla key selectors (value-per-unit asc, then squared
        /// distance asc) and adds <paramref name="bill"/>/<paramref name="s"/>.
        ///
        /// When this is NOT a cook-food bill (cook toggle off, non-ingestible product, etc.) we call
        /// the IDENTICAL vanilla sort verbatim — byte-for-byte the original order for chemfuel,
        /// patchleather, and every non-cook AllowMix recipe.
        ///
        /// When it IS, we precompute each Thing's (kind,ticks) ONCE, then sort by
        /// (spoilRank asc, value asc, distance asc): the most-perishable valid stack floats forward,
        /// but among candidates with EQUAL ticks (or all non-eligible) the order falls back to the
        /// exact vanilla value→distance keys, so the fill loop's nutrition/count accounting is
        /// unchanged. Spoiling only breaks ties differently in favour of perishables.</summary>
        public static void SortAllowMix(List<Thing> things, Func<Thing, float> valueKey,
            Func<Thing, int> distKey, Bill bill, HaulersDreamSettings s)
        {
            if (things == null) return;

            // Non-cook (or toggle off): replicate vanilla's two-key SortBy exactly. SortBy is a stable
            // ascending sort by (valueKey, then distKey) — mirror it with the same key precedence.
            if (things.Count < 2 || !IsCookSpoilingBill(bill, s))
            {
                things.SortBy(valueKey, distKey);
                return;
            }

            // Cook-food bill: precompute classification per Thing (never inside the Sort delegate).
            var cands = new Cand[things.Count];
            for (int i = 0; i < things.Count; i++)
                cands[i] = Classify(things[i], i, s);

            // Sort an index permutation so the per-Thing classification + vanilla keys are read once
            // per element, then materialise the Things in the new order. The comparator is a total
            // order: spoilRank (eligible-first, ascending ticks), then value asc, then distance asc.
            var order = new int[things.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) =>
            {
                int c = SpoilingFirstSelection.CompareSpoilRank(
                    cands[x].kind, cands[x].ticks, cands[y].kind, cands[y].ticks);
                if (c != 0) return c;
                c = valueKey(things[x]).CompareTo(valueKey(things[y]));
                if (c != 0) return c;
                c = distKey(things[x]).CompareTo(distKey(things[y]));
                if (c != 0) return c;
                return x.CompareTo(y);   // final stable tiebreak — total order for Array.Sort
            });
            var sorted = new Thing[things.Count];
            for (int i = 0; i < order.Length; i++) sorted[i] = things[order[i]];
            for (int i = 0; i < sorted.Length; i++) things[i] = sorted[i];
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
    }
}
