using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class MealsOnWheelsSelectionTests
    {
        private const int Awful   = MealsOnWheelsSelection.PrefMealAwful;     // 5
        private const int Lavish  = MealsOnWheelsSelection.PrefMealLavish;    // 9
        private const int Despr   = MealsOnWheelsSelection.PrefDesperateOnly; // 2
        private const int Window  = MealsOnWheelsSelection.RotRescueWindowTicks; // 30000
        private const int Never   = MealsOnWheelsSelection.NeverRots;         // int.MaxValue

        // ---- Classify ----

        [Test]
        public void Classify_NotAcceptable_IsNone()
        {
            // acceptable:false dominates regardless of every other field.
            Assert.That(MealsOnWheelsSelection.Classify(false, Lavish, true, 1),
                Is.EqualTo(MealCandidatePass.None));
        }

        [Test]
        public void Classify_RotRescue_HappyPath()
        {
            Assert.That(MealsOnWheelsSelection.Classify(true, Awful, true, Window - 1),
                Is.EqualTo(MealCandidatePass.RotRescue));
        }

        [Test]
        public void Classify_WindowEdge_IsExclusive()
        {
            // ticks == window is NOT < window -> Desperation.
            Assert.That(MealsOnWheelsSelection.Classify(true, Awful, true, Window),
                Is.EqualTo(MealCandidatePass.Desperation));
        }

        [Test]
        public void Classify_NotFreshActiveRottable_IsDesperation()
        {
            // even with great pref and tiny ticks, a non-fresh/inactive candidate is desperation at most.
            Assert.That(MealsOnWheelsSelection.Classify(true, Lavish, false, 100),
                Is.EqualTo(MealCandidatePass.Desperation));
        }

        [Test]
        public void Classify_LowPreferability_BlocksRescue()
        {
            // Fresh and rotting soon, but pref below MealAwful -> not rescued (still eaten via desperation).
            Assert.That(MealsOnWheelsSelection.Classify(true, Awful - 1, true, 100),
                Is.EqualTo(MealCandidatePass.Desperation));
        }

        [Test]
        public void Classify_NeverRotsSentinel_IsDesperation()
        {
            Assert.That(MealsOnWheelsSelection.Classify(true, Lavish, false, Never),
                Is.EqualTo(MealCandidatePass.Desperation));
        }

        [Test]
        public void Const_Sanity()
        {
            Assert.That(MealsOnWheelsSelection.RotRescueWindowTicks, Is.EqualTo(30000));
            Assert.That(MealsOnWheelsSelection.NeverRots, Is.EqualTo(int.MaxValue));
            Assert.That(Despr, Is.LessThan(Awful));
            Assert.That(Awful, Is.LessThan(Lavish));
        }

        // ---- IsCandidate ----

        [TestCase(MealCandidatePass.None,        ExpectedResult = false)]
        [TestCase(MealCandidatePass.RotRescue,   ExpectedResult = true)]
        [TestCase(MealCandidatePass.Desperation, ExpectedResult = true)]
        public bool IsCandidate_ByPass(MealCandidatePass pass) => MealsOnWheelsSelection.IsCandidate(pass);

        // ---- CompareRank ----

        [Test]
        public void CompareRank_RotRescue_Before_Desperation_Before_None()
        {
            // RotRescue beats Desperation even when its meal rots later / is on a far holder.
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.RotRescue, 29999, 99,
                MealCandidatePass.Desperation, 0, 0), Is.LessThan(0));
            // Desperation beats None.
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.Desperation, 0, 50,
                MealCandidatePass.None, 0, 0), Is.LessThan(0));
            // RotRescue beats None.
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.RotRescue, 0, 0,
                MealCandidatePass.None, 0, 0), Is.LessThan(0));
        }

        [Test]
        public void CompareRank_RotRescue_SoonestToRotFirst()
        {
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.RotRescue, 100, 0,
                MealCandidatePass.RotRescue, 500, 0), Is.LessThan(0));
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.RotRescue, 500, 0,
                MealCandidatePass.RotRescue, 100, 0), Is.GreaterThan(0));
        }

        [Test]
        public void CompareRank_Desperation_NearestHolderFirst()
        {
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.Desperation, 0, 3,
                MealCandidatePass.Desperation, 0, 9), Is.LessThan(0));
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.Desperation, 0, 9,
                MealCandidatePass.Desperation, 0, 3), Is.GreaterThan(0));
        }

        [Test]
        public void CompareRank_DistanceDoesNotLeakIntoRotRescue()
        {
            // Two RotRescue with EQUAL ticks but different distance -> tie (rescue ignores distance).
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.RotRescue, 200, 1,
                MealCandidatePass.RotRescue, 200, 9), Is.EqualTo(0));
        }

        [Test]
        public void CompareRank_TicksDoNotLeakIntoDesperation()
        {
            // Two Desperation with EQUAL dist but different ticks -> tie (desperation ignores ticks).
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.Desperation, 100, 4,
                MealCandidatePass.Desperation, 500, 4), Is.EqualTo(0));
        }

        [Test]
        public void CompareRank_WithinPassTie_ReturnsZero()
        {
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.RotRescue, 300, 0,
                MealCandidatePass.RotRescue, 300, 0), Is.EqualTo(0));
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.Desperation, 0, 7,
                MealCandidatePass.Desperation, 0, 7), Is.EqualTo(0));
            Assert.That(MealsOnWheelsSelection.CompareRank(
                MealCandidatePass.None, 0, 0,
                MealCandidatePass.None, 999, 999), Is.EqualTo(0));
        }

        // ---- Compare (total order with index tiebreak) ----

        [Test]
        public void Compare_IndexStableTiebreak_WithinPass()
        {
            // RotRescue equal ticks -> index decides.
            Assert.That(MealsOnWheelsSelection.Compare(
                MealCandidatePass.RotRescue, 200, 0, 0,
                MealCandidatePass.RotRescue, 200, 9, 1), Is.LessThan(0));
            // Desperation equal dist -> index decides.
            Assert.That(MealsOnWheelsSelection.Compare(
                MealCandidatePass.Desperation, 0, 5, 2,
                MealCandidatePass.Desperation, 0, 5, 0), Is.GreaterThan(0));
        }

        [Test]
        public void Compare_SelfIsZero()
        {
            Assert.That(MealsOnWheelsSelection.Compare(
                MealCandidatePass.RotRescue, 100, 3, 4,
                MealCandidatePass.RotRescue, 100, 3, 4), Is.EqualTo(0));
            Assert.That(MealsOnWheelsSelection.Compare(
                MealCandidatePass.Desperation, 50, 7, 2,
                MealCandidatePass.Desperation, 50, 7, 2), Is.EqualTo(0));
        }

        [Test]
        public void Compare_Antisymmetry()
        {
            var reps = new (MealCandidatePass pass, int ticks, int dist, int idx)[]
            {
                (MealCandidatePass.None,        Never, int.MaxValue, 0),
                (MealCandidatePass.RotRescue,   100,   0,            1),
                (MealCandidatePass.RotRescue,   500,   0,            2),
                (MealCandidatePass.Desperation, Never, 2,            3),
                (MealCandidatePass.Desperation, Never, 9,            4),
                (MealCandidatePass.None,        Never, int.MaxValue, 5),
            };
            for (int i = 0; i < reps.Length; i++)
                for (int j = 0; j < reps.Length; j++)
                {
                    int ab = MealsOnWheelsSelection.Compare(reps[i].pass, reps[i].ticks, reps[i].dist, reps[i].idx,
                                                            reps[j].pass, reps[j].ticks, reps[j].dist, reps[j].idx);
                    int ba = MealsOnWheelsSelection.Compare(reps[j].pass, reps[j].ticks, reps[j].dist, reps[j].idx,
                                                            reps[i].pass, reps[i].ticks, reps[i].dist, reps[i].idx);
                    Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                        $"antisymmetry violated for ({i},{j})");
                }
        }

        [Test]
        public void CompareRank_Antisymmetry()
        {
            var reps = new (MealCandidatePass pass, int ticks, int dist)[]
            {
                (MealCandidatePass.None,        Never, int.MaxValue),
                (MealCandidatePass.RotRescue,   100,   0),
                (MealCandidatePass.RotRescue,   500,   0),
                (MealCandidatePass.Desperation, Never, 2),
                (MealCandidatePass.Desperation, Never, 9),
            };
            for (int i = 0; i < reps.Length; i++)
                for (int j = 0; j < reps.Length; j++)
                {
                    int ab = MealsOnWheelsSelection.CompareRank(reps[i].pass, reps[i].ticks, reps[i].dist,
                                                                reps[j].pass, reps[j].ticks, reps[j].dist);
                    int ba = MealsOnWheelsSelection.CompareRank(reps[j].pass, reps[j].ticks, reps[j].dist,
                                                                reps[i].pass, reps[i].ticks, reps[i].dist);
                    Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                        $"CompareRank antisymmetry violated for ({i},{j})");
                }
        }

        // ---- End-to-end List<T>.Sort (the load-bearing proof the runtime list.Min picks the right meal) ----

        private sealed class Cand
        {
            public MealCandidatePass Pass;
            public int Ticks;
            public int Dist;
            public int Index;
        }

        private static List<Cand> SortByComparator(List<Cand> input)
        {
            var copy = new List<Cand>(input);
            copy.Sort((a, b) => MealsOnWheelsSelection.Compare(
                a.Pass, a.Ticks, a.Dist, a.Index, b.Pass, b.Ticks, b.Dist, b.Index));
            return copy;
        }

        [Test]
        public void Sort_RotRescueSoonestThenDesperationNearestThenNone()
        {
            var input = new List<Cand>
            {
                new Cand { Pass = MealCandidatePass.Desperation, Ticks = Never, Dist = 2, Index = 0 },
                new Cand { Pass = MealCandidatePass.RotRescue,   Ticks = 400,   Dist = 0, Index = 1 },
                new Cand { Pass = MealCandidatePass.None,        Ticks = Never, Dist = int.MaxValue, Index = 2 },
                new Cand { Pass = MealCandidatePass.RotRescue,   Ticks = 100,   Dist = 0, Index = 3 },
                new Cand { Pass = MealCandidatePass.Desperation, Ticks = Never, Dist = 9, Index = 4 },
            };
            var sorted = SortByComparator(input);
            // RotRescue soonest-first (idx3 ticks100, idx1 ticks400), then Desperation nearest-first
            // (idx0 dist2, idx4 dist9), then None (idx2).
            Assert.That(sorted.ConvertAll(c => c.Index), Is.EqualTo(new[] { 3, 1, 0, 4, 2 }));
        }

        [Test]
        public void Sort_AllNone_IsIdentityPermutation()
        {
            var input = new List<Cand>
            {
                new Cand { Pass = MealCandidatePass.None, Ticks = Never, Dist = int.MaxValue, Index = 0 },
                new Cand { Pass = MealCandidatePass.None, Ticks = Never, Dist = int.MaxValue, Index = 1 },
                new Cand { Pass = MealCandidatePass.None, Ticks = Never, Dist = int.MaxValue, Index = 2 },
            };
            var sorted = SortByComparator(input);
            Assert.That(sorted.ConvertAll(c => c.Index), Is.EqualTo(new[] { 0, 1, 2 }));
        }
    }
}
