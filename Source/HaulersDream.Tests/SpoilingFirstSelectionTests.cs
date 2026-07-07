using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SpoilingFirstSelectionTests
    {
        // ---- Categorize: the two-toggle matrix, independence, corpse precedence ----

        // (isCorpse, isRottable, butcher, cook) -> kind
        [TestCase(true,  true,  true,  true,  ExpectedResult = IngredientSpoilKind.Corpse)] // corpse, both on
        [TestCase(true,  true,  false, true,  ExpectedResult = IngredientSpoilKind.None)]   // butcher off; cook does NOT rescue a corpse
        [TestCase(true,  true,  true,  false, ExpectedResult = IngredientSpoilKind.Corpse)] // corpse independent of cook toggle
        [TestCase(false, true,  true,  true,  ExpectedResult = IngredientSpoilKind.Food)]   // food, both on
        [TestCase(false, true,  true,  false, ExpectedResult = IngredientSpoilKind.None)]   // cook off; butcher does NOT rescue food
        [TestCase(false, false, true,  true,  ExpectedResult = IngredientSpoilKind.None)]   // steel: non-rottable, never applies
        [TestCase(true,  true,  false, false, ExpectedResult = IngredientSpoilKind.None)]   // both off -> corpse None
        [TestCase(false, true,  false, false, ExpectedResult = IngredientSpoilKind.None)]   // both off -> food None
        public IngredientSpoilKind Categorize_Matrix(bool isCorpse, bool isRottable, bool butcher, bool cook)
            => SpoilingFirstSelection.Categorize(isCorpse, isRottable, butcher, cook);

        [Test]
        public void Categorize_CorpsePrecedence_OverFood()
        {
            // A corpse that is also classified rottable, with butcher OFF and cook ON, must be None — proving
            // the corpse branch is checked BEFORE the food branch (otherwise it would wrongly become Food).
            Assert.That(SpoilingFirstSelection.Categorize(isCorpse: true, isRottable: true,
                butcherSpoilingFirst: false, cookSpoilingFirst: true), Is.EqualTo(IngredientSpoilKind.None));
        }

        [Test]
        public void Categorize_TogglesIndependent()
        {
            // butcher on / cook off -> corpse applies, food doesn't.
            Assert.That(SpoilingFirstSelection.Categorize(true,  true, true,  false), Is.EqualTo(IngredientSpoilKind.Corpse));
            Assert.That(SpoilingFirstSelection.Categorize(false, true, true,  false), Is.EqualTo(IngredientSpoilKind.None));
            // butcher off / cook on -> food applies, corpse doesn't.
            Assert.That(SpoilingFirstSelection.Categorize(false, true, false, true),  Is.EqualTo(IngredientSpoilKind.Food));
            Assert.That(SpoilingFirstSelection.Categorize(true,  true, false, true),  Is.EqualTo(IngredientSpoilKind.None));
        }

        // ---- IsEligible ----

        [TestCase(IngredientSpoilKind.None,   ExpectedResult = false)]
        [TestCase(IngredientSpoilKind.Corpse, ExpectedResult = true)]
        [TestCase(IngredientSpoilKind.Food,   ExpectedResult = true)]
        public bool IsEligible_ByKind(IngredientSpoilKind kind) => SpoilingFirstSelection.IsEligible(kind);

        // ---- Compare contract ----

        [Test]
        public void Compare_MostSpoiledFirst()
        {
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 100, 0, IngredientSpoilKind.Food, 500, 1), Is.LessThan(0));
        }

        [Test]
        public void Compare_EligibleBeforeNonEligible_RegardlessOfIndex()
        {
            // a spoiling food at index 5 still beats a non-rottable at index 0.
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 500, 5,
                IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots, 0), Is.LessThan(0));
        }

        [Test]
        public void Compare_NonEligibleKeepsVanillaOrder()
        {
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots, 0,
                IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots, 1), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots, 2,
                IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots, 1), Is.GreaterThan(0));
        }

        [Test]
        public void Compare_EqualTicksStability_FallsBackToIndex()
        {
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 200, 0, IngredientSpoilKind.Food, 200, 1), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 200, 1, IngredientSpoilKind.Food, 200, 0), Is.GreaterThan(0));
        }

        [Test]
        public void Compare_SelfIsZero()
        {
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 100, 3, IngredientSpoilKind.Food, 100, 3), Is.EqualTo(0));
        }

        [Test]
        public void Compare_CorpseVsFood_BothEligible_PurelyByTicks()
        {
            // a corpse 50 ticks beats a food 80 ticks (kind does not bias order, only eligibility does).
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Corpse, 50, 9, IngredientSpoilKind.Food, 80, 0), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Corpse, 90, 0, IngredientSpoilKind.Food, 40, 9), Is.GreaterThan(0));
        }

        [Test]
        public void Compare_FrozenSentinel_SinksToBackOfEligible_ButAheadOfNone()
        {
            // frozen-fresh (huge ticks) sorts after warm food but still ahead of a non-eligible candidate.
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 72000000, 0, IngredientSpoilKind.Food, 100, 1), Is.GreaterThan(0));
            Assert.That(SpoilingFirstSelection.Compare(
                IngredientSpoilKind.Food, 72000000, 0,
                IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots, 1), Is.LessThan(0));
        }

        [Test]
        public void Compare_Antisymmetry()
        {
            var reps = new (IngredientSpoilKind kind, int ticks, int idx)[]
            {
                (IngredientSpoilKind.None,   SpoilingFirstSelection.NeverRots, 0),
                (IngredientSpoilKind.Food,   100, 1),
                (IngredientSpoilKind.Food,   500, 2),
                (IngredientSpoilKind.Corpse, 50,  3),
                (IngredientSpoilKind.Food,   72000000, 4),
                (IngredientSpoilKind.None,   SpoilingFirstSelection.NeverRots, 5),
            };
            for (int i = 0; i < reps.Length; i++)
                for (int j = 0; j < reps.Length; j++)
                {
                    int ab = SpoilingFirstSelection.Compare(reps[i].kind, reps[i].ticks, reps[i].idx,
                                                            reps[j].kind, reps[j].ticks, reps[j].idx);
                    int ba = SpoilingFirstSelection.Compare(reps[j].kind, reps[j].ticks, reps[j].idx,
                                                            reps[i].kind, reps[i].ticks, reps[i].idx);
                    Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                        $"antisymmetry violated for ({i},{j})");
                }
        }

        // ---- Full-list List<T>.Sort stability (the load-bearing proof) ----

        private sealed class Triple
        {
            public IngredientSpoilKind Kind;
            public int Ticks;
            public int Index;
        }

        private static List<Triple> SortByComparator(List<Triple> input)
        {
            var copy = new List<Triple>(input);
            copy.Sort((a, b) => SpoilingFirstSelection.Compare(a.Kind, a.Ticks, a.Index, b.Kind, b.Ticks, b.Index));
            return copy;
        }

        [Test]
        public void Sort_FloatsFoodsByTicks_NonEligibleKeepRelativeOrder()
        {
            var input = new List<Triple>
            {
                new Triple { Kind = IngredientSpoilKind.None, Ticks = SpoilingFirstSelection.NeverRots, Index = 0 },
                new Triple { Kind = IngredientSpoilKind.Food, Ticks = 300, Index = 1 },
                new Triple { Kind = IngredientSpoilKind.None, Ticks = SpoilingFirstSelection.NeverRots, Index = 2 },
                new Triple { Kind = IngredientSpoilKind.Food, Ticks = 100, Index = 3 },
                new Triple { Kind = IngredientSpoilKind.None, Ticks = SpoilingFirstSelection.NeverRots, Index = 4 },
            };
            var sorted = SortByComparator(input);
            // expected index order [3, 1, 0, 2, 4]: foods float by ascending ticks; the three None keep 0,2,4.
            Assert.That(sorted.ConvertAll(t => t.Index), Is.EqualTo(new[] { 3, 1, 0, 2, 4 }));
        }

        [Test]
        public void Sort_AllNone_IsIdentityPermutation()
        {
            var input = new List<Triple>
            {
                new Triple { Kind = IngredientSpoilKind.None, Ticks = SpoilingFirstSelection.NeverRots, Index = 0 },
                new Triple { Kind = IngredientSpoilKind.None, Ticks = SpoilingFirstSelection.NeverRots, Index = 1 },
                new Triple { Kind = IngredientSpoilKind.None, Ticks = SpoilingFirstSelection.NeverRots, Index = 2 },
            };
            var sorted = SortByComparator(input);
            Assert.That(sorted.ConvertAll(t => t.Index), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void NeverRots_IsIntMaxValue()
        {
            Assert.That(SpoilingFirstSelection.NeverRots, Is.EqualTo(int.MaxValue));
        }

        // ---- CompareSpoilRank (the new pure primitive the AllowMix cook path chains value/distance after) ----

        [Test]
        public void CompareSpoilRank_EligibleBeforeNonEligible()
        {
            // Food (eligible) floats ahead of None, regardless of ticks.
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Food, 500, IngredientSpoilKind.None, 100), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.None, 100, IngredientSpoilKind.Food, 500), Is.GreaterThan(0));
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Corpse, 999, IngredientSpoilKind.None, 0), Is.LessThan(0));
        }

        [Test]
        public void CompareSpoilRank_BothEligible_AscendingTicks()
        {
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Food, 100, IngredientSpoilKind.Food, 500), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Food, 500, IngredientSpoilKind.Food, 100), Is.GreaterThan(0));
            // kind does not bias order among eligible — only ticks.
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Corpse, 50, IngredientSpoilKind.Food, 80), Is.LessThan(0));
        }

        [Test]
        public void CompareSpoilRank_EqualTicks_ReturnsZero_SoCallerDecides()
        {
            // Both eligible, equal ticks -> 0 (the cook path then applies value, then distance).
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Food, 200, IngredientSpoilKind.Food, 200), Is.EqualTo(0));
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.Corpse, 200, IngredientSpoilKind.Food, 200), Is.EqualTo(0));
        }

        [Test]
        public void CompareSpoilRank_BothNonEligible_ReturnsZero()
        {
            // Two non-eligible candidates tie on rank (different ticks must NOT leak in) -> caller's
            // tiebreak (value/distance or index) is the sole decider, preserving vanilla order.
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.None, 0, IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots),
                Is.EqualTo(0));
            Assert.That(SpoilingFirstSelection.CompareSpoilRank(
                IngredientSpoilKind.None, 999, IngredientSpoilKind.None, 1), Is.EqualTo(0));
        }

        [Test]
        public void CompareSpoilRank_Antisymmetry()
        {
            var reps = new (IngredientSpoilKind kind, int ticks)[]
            {
                (IngredientSpoilKind.None,   SpoilingFirstSelection.NeverRots),
                (IngredientSpoilKind.None,   0),
                (IngredientSpoilKind.Food,   100),
                (IngredientSpoilKind.Food,   500),
                (IngredientSpoilKind.Corpse, 50),
                (IngredientSpoilKind.Food,   72000000),
            };
            for (int i = 0; i < reps.Length; i++)
                for (int j = 0; j < reps.Length; j++)
                {
                    int ab = SpoilingFirstSelection.CompareSpoilRank(reps[i].kind, reps[i].ticks,
                                                                     reps[j].kind, reps[j].ticks);
                    int ba = SpoilingFirstSelection.CompareSpoilRank(reps[j].kind, reps[j].ticks,
                                                                     reps[i].kind, reps[i].ticks);
                    Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                        $"CompareSpoilRank antisymmetry violated for ({i},{j})");
                }
        }

        // ---- Compare delegates to CompareSpoilRank WITHOUT changing behaviour (NoMix/batch unaffected) ----

        /// <summary>The pre-refactor inline definition of Compare, kept here as the oracle. The refactored
        /// Compare must equal this for every input — proving the 385-baseline NoMix/batch paths are
        /// byte-identical after delegating to CompareSpoilRank.</summary>
        private static int OldCompare(
            IngredientSpoilKind aKind, int aTicks, int aIndex,
            IngredientSpoilKind bKind, int bTicks, int bIndex)
        {
            bool ae = SpoilingFirstSelection.IsEligible(aKind), be = SpoilingFirstSelection.IsEligible(bKind);
            if (ae != be) return ae ? -1 : 1;
            if (ae)
            {
                int c = aTicks.CompareTo(bTicks);
                if (c != 0) return c;
            }
            return aIndex.CompareTo(bIndex);
        }

        [Test]
        public void Compare_DelegationIsBehaviourPreserving()
        {
            var kinds = new[] { IngredientSpoilKind.None, IngredientSpoilKind.Corpse, IngredientSpoilKind.Food };
            var tickVals = new[] { 0, 100, 200, 500, 72000000, SpoilingFirstSelection.NeverRots };
            var idxVals = new[] { 0, 1, 2, 5 };

            foreach (var ak in kinds)
                foreach (var at in tickVals)
                    foreach (var ai in idxVals)
                        foreach (var bk in kinds)
                            foreach (var bt in tickVals)
                                foreach (var bi in idxVals)
                                {
                                    int expected = OldCompare(ak, at, ai, bk, bt, bi);
                                    int actual = SpoilingFirstSelection.Compare(ak, at, ai, bk, bt, bi);
                                    Assert.That(Math.Sign(actual), Is.EqualTo(Math.Sign(expected)),
                                        $"Compare diverged from old for ({ak},{at},{ai} | {bk},{bt},{bi})");
                                }
        }

        // ---- CompareCookRank (#137 opt-in most-stocked-first cook key, spoil rank as the tiebreak) ----

        [Test]
        public void CompareCookRank_StockOff_ReducesExactlyToSpoilRank()
        {
            // The backward-compat proof: with the most-stocked-first key OFF, CompareCookRank must equal
            // CompareSpoilRank for EVERY input (stock is ignored), so the existing cook path is byte-identical.
            var kinds = new[] { IngredientSpoilKind.None, IngredientSpoilKind.Corpse, IngredientSpoilKind.Food };
            var tickVals = new[] { 0, 100, 500, 72000000, SpoilingFirstSelection.NeverRots };
            var stockVals = new[] { 0, 1, 50, 9999 };

            foreach (var ak in kinds)
                foreach (var at in tickVals)
                    foreach (var asg in stockVals)
                        foreach (var bk in kinds)
                            foreach (var bt in tickVals)
                                foreach (var bsg in stockVals)
                                {
                                    int expected = SpoilingFirstSelection.CompareSpoilRank(ak, at, bk, bt);
                                    int actual = SpoilingFirstSelection.CompareCookRank(
                                        mostStockFirst: false, asg, ak, at, bsg, bk, bt);
                                    Assert.That(Math.Sign(actual), Is.EqualTo(Math.Sign(expected)),
                                        $"stock-off diverged from spoil rank for ({asg},{ak},{at} | {bsg},{bk},{bt})");
                                }
        }

        [Test]
        public void CompareCookRank_StockOn_HigherStockFirst()
        {
            // Descending stock is the primary key: the def the colony has more of goes first.
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                500, IngredientSpoilKind.Food, 100, 5, IngredientSpoilKind.Food, 100), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                5, IngredientSpoilKind.Food, 100, 500, IngredientSpoilKind.Food, 100), Is.GreaterThan(0));
        }

        [Test]
        public void CompareCookRank_StockOn_HigherStockBeatsMoreSpoiled()
        {
            // The headline #137 behaviour: an abundant frozen stack (huge ticks) is used BEFORE a scarce
            // nearly-rotting stack, because stock outranks spoilage. With the key OFF the same inputs flip
            // (the more-spoiled one wins), proving the toggle changes the outcome.
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                500, IngredientSpoilKind.Food, 72000000, 5, IngredientSpoilKind.Food, 100), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.CompareCookRank(false,
                500, IngredientSpoilKind.Food, 72000000, 5, IngredientSpoilKind.Food, 100), Is.GreaterThan(0));
        }

        [Test]
        public void CompareCookRank_StockOn_EqualStock_FallsToSpoilRank()
        {
            // Among stacks of the SAME abundant def (equal stock), the most-spoiling one still goes first.
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                500, IngredientSpoilKind.Food, 100, 500, IngredientSpoilKind.Food, 500), Is.LessThan(0));
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                500, IngredientSpoilKind.Food, 500, 500, IngredientSpoilKind.Food, 100), Is.GreaterThan(0));
        }

        [Test]
        public void CompareCookRank_StockOn_EqualStockEqualTicks_ReturnsZero_SoCallerDecides()
        {
            // Stock ties AND spoil rank ties -> 0, leaving the AllowMix path's value/distance keys to decide.
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                42, IngredientSpoilKind.Food, 200, 42, IngredientSpoilKind.Food, 200), Is.EqualTo(0));
            // Equal stock, both non-eligible -> spoil rank is 0 too, so the whole thing is 0.
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                7, IngredientSpoilKind.None, 0, 7, IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots),
                Is.EqualTo(0));
        }

        [Test]
        public void CompareCookRank_StockOn_NonEligibleStacksStillRankByStock()
        {
            // Even when neither candidate is rottable (e.g. rice vs corn a mixed-veggie meal both accept, both
            // frozen so None), the stock key still orders them: the more-stocked def is used up first.
            Assert.That(SpoilingFirstSelection.CompareCookRank(true,
                800, IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots,
                50, IngredientSpoilKind.None, SpoilingFirstSelection.NeverRots), Is.LessThan(0));
        }

        [Test]
        public void CompareCookRank_Antisymmetry_BothToggleStates()
        {
            var reps = new (int stock, IngredientSpoilKind kind, int ticks)[]
            {
                (0,   IngredientSpoilKind.None,   SpoilingFirstSelection.NeverRots),
                (50,  IngredientSpoilKind.Food,   100),
                (50,  IngredientSpoilKind.Food,   500),
                (500, IngredientSpoilKind.Corpse, 50),
                (500, IngredientSpoilKind.Food,   72000000),
                (999, IngredientSpoilKind.None,   0),
            };
            foreach (var stockOn in new[] { false, true })
                for (int i = 0; i < reps.Length; i++)
                    for (int j = 0; j < reps.Length; j++)
                    {
                        int ab = SpoilingFirstSelection.CompareCookRank(stockOn,
                            reps[i].stock, reps[i].kind, reps[i].ticks,
                            reps[j].stock, reps[j].kind, reps[j].ticks);
                        int ba = SpoilingFirstSelection.CompareCookRank(stockOn,
                            reps[j].stock, reps[j].kind, reps[j].ticks,
                            reps[i].stock, reps[i].kind, reps[i].ticks);
                        Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                            $"CompareCookRank antisymmetry violated (stockOn={stockOn}) for ({i},{j})");
                    }
        }
    }
}
