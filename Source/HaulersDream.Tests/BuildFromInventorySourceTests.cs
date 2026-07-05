using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BuildFromInventorySourceTests
    {
        private const BuildMaterialSource Own   = BuildMaterialSource.Own;
        private const BuildMaterialSource Floor = BuildMaterialSource.Floor;
        private const BuildMaterialSource Other = BuildMaterialSource.Other;
        private const BuildMaterialSource Pack  = BuildMaterialSource.PackAnimal;

        // ---- TotalAvailable ----

        [Test]
        public void TotalAvailable_SumsAllFour()
        {
            Assert.That(BuildFromInventorySource.TotalAvailable(3, 5, 7, 11), Is.EqualTo(26));
        }

        [Test]
        public void TotalAvailable_AllZero_IsZero()
        {
            Assert.That(BuildFromInventorySource.TotalAvailable(0, 0, 0, 0), Is.EqualTo(0));
        }

        [Test]
        public void TotalAvailable_Saturates_NoOverflowToNegative()
        {
            Assert.That(BuildFromInventorySource.TotalAvailable(int.MaxValue, 1, 0, 0),
                Is.EqualTo(int.MaxValue));
            Assert.That(BuildFromInventorySource.TotalAvailable(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
                Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void TotalAvailable_NegativeClampedToZero()
        {
            // Defensive: the runtime never passes negatives, but a saturating sum must floor at 0.
            Assert.That(BuildFromInventorySource.TotalAvailable(-5, 0, 0, 0), Is.EqualTo(0));
            Assert.That(BuildFromInventorySource.TotalAvailable(int.MinValue, int.MinValue, 0, 0), Is.EqualTo(0));
        }

        // ---- IsAvailable (the partial truth table — the heart of the feature) ----

        [Test]
        public void IsAvailable_Full_ExactlyEnough_True()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(10, 10, allowPartial: false), Is.True);
        }

        [Test]
        public void IsAvailable_Full_OneShort_False()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(9, 10, allowPartial: false), Is.False);
        }

        [Test]
        public void IsAvailable_Full_Surplus_True()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(20, 10, allowPartial: false), Is.True);
        }

        [Test]
        public void IsAvailable_Full_Zero_False()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(0, 10, allowPartial: false), Is.False);
        }

        [Test]
        public void IsAvailable_Partial_AnyUnit_True()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(1, 10, allowPartial: true), Is.True);
        }

        [Test]
        public void IsAvailable_Partial_Zero_False()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(0, 10, allowPartial: true), Is.False);
        }

        [Test]
        public void IsAvailable_Partial_ExactlyEnough_True()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(10, 10, allowPartial: true), Is.True);
        }

        [Test]
        public void IsAvailable_Partial_Surplus_True()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(50, 10, allowPartial: true), Is.True);
        }

        // need <= 0 -> always available regardless of (partial, avail).
        [TestCase(0, true, 0)]
        [TestCase(0, false, 0)]
        [TestCase(0, true, 5)]
        [TestCase(0, false, 5)]
        [TestCase(-3, true, 0)]
        [TestCase(-3, false, 5)]
        public void IsAvailable_NeedsNothing_AlwaysTrue(int need, bool partial, int avail)
        {
            Assert.That(BuildFromInventorySource.IsAvailable(avail, need, partial), Is.True);
        }

        [Test]
        public void IsAvailable_NegativeAvail_ClampedFalse()
        {
            Assert.That(BuildFromInventorySource.IsAvailable(-3, 1, allowPartial: false), Is.False);
            Assert.That(BuildFromInventorySource.IsAvailable(-3, 1, allowPartial: true), Is.False);
        }

        // Partial is a SUPERSET of full: anything the FULL gate accepts, the PARTIAL gate also accepts
        // (for the same avail/need). Sweep a grid to prove the implication.
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(5, 1)]
        [TestCase(0, 10)]
        [TestCase(1, 10)]
        [TestCase(9, 10)]
        [TestCase(10, 10)]
        [TestCase(20, 10)]
        [TestCase(0, 0)]
        [TestCase(-3, 10)]
        public void IsAvailable_PartialIsSupersetOfFull(int avail, int need)
        {
            bool full = BuildFromInventorySource.IsAvailable(avail, need, allowPartial: false);
            bool partial = BuildFromInventorySource.IsAvailable(avail, need, allowPartial: true);
            if (full)
                Assert.That(partial, Is.True, $"partial must accept everything full accepts (avail={avail}, need={need})");
        }

        [Test]
        public void IsAvailable_FourArgOverload_MatchesTwoArg()
        {
            // The (own,floor,other,pack,...) overload equals IsAvailable(TotalAvailable(...), need, partial).
            foreach (var (o, f, ot, p, need, partial) in new[]
            {
                (3, 5, 7, 11, 20, false),
                (1, 0, 0, 0, 10, true),
                (0, 0, 0, 0, 5, false),
                (4, 4, 4, 4, 16, false),
            })
            {
                int total = BuildFromInventorySource.TotalAvailable(o, f, ot, p);
                Assert.That(BuildFromInventorySource.IsAvailable(o, f, ot, p, need, partial),
                    Is.EqualTo(BuildFromInventorySource.IsAvailable(total, need, partial)),
                    $"overload mismatch for ({o},{f},{ot},{p}) need={need} partial={partial}");
            }
        }

        // ---- SourceRank + Const_Sanity (priority own→floor→other→pack) ----

        [Test]
        public void Const_Sanity()
        {
            // Pin the enum values so a silent reorder can't regress "prefer own over disturbing a pack animal".
            Assert.That((int)BuildMaterialSource.Own, Is.EqualTo(0));
            Assert.That((int)BuildMaterialSource.Floor, Is.EqualTo(1));
            Assert.That((int)BuildMaterialSource.Other, Is.EqualTo(2));
            Assert.That((int)BuildMaterialSource.PackAnimal, Is.EqualTo(3));
        }

        [Test]
        public void SourceRank_Order_OwnFloorOtherPack()
        {
            Assert.That(BuildFromInventorySource.SourceRank(Own),
                Is.LessThan(BuildFromInventorySource.SourceRank(Floor)));
            Assert.That(BuildFromInventorySource.SourceRank(Floor),
                Is.LessThan(BuildFromInventorySource.SourceRank(Other)));
            Assert.That(BuildFromInventorySource.SourceRank(Other),
                Is.LessThan(BuildFromInventorySource.SourceRank(Pack)));
        }

        // ---- CompareRank ----

        [Test]
        public void CompareRank_SourceBeatsDistance()
        {
            // A far OWN stack beats a near FLOOR stack (source dominates distance).
            Assert.That(BuildFromInventorySource.CompareRank(Own, 999, Floor, 0), Is.LessThan(0));
            Assert.That(BuildFromInventorySource.CompareRank(Floor, 0, Own, 999), Is.GreaterThan(0));
        }

        [Test]
        public void CompareRank_Floor_Before_Other()
        {
            Assert.That(BuildFromInventorySource.CompareRank(Floor, 0, Other, 0), Is.LessThan(0));
        }

        [Test]
        public void CompareRank_Other_Before_Pack()
        {
            Assert.That(BuildFromInventorySource.CompareRank(Other, 0, Pack, 0), Is.LessThan(0));
        }

        [Test]
        public void CompareRank_SameSource_NearestFirst()
        {
            Assert.That(BuildFromInventorySource.CompareRank(Other, 3, Other, 9), Is.LessThan(0));
            Assert.That(BuildFromInventorySource.CompareRank(Other, 9, Other, 3), Is.GreaterThan(0));
        }

        [Test]
        public void CompareRank_SameSourceSameDist_Tie_IsZero()
        {
            Assert.That(BuildFromInventorySource.CompareRank(Pack, 5, Pack, 5), Is.EqualTo(0));
            Assert.That(BuildFromInventorySource.CompareRank(Own, 0, Own, 0), Is.EqualTo(0));
        }

        [Test]
        public void CompareRank_DistanceDoesNotLeakAcrossSource()
        {
            // A near FLOOR stack does NOT beat a far OWN stack despite the distance gap.
            Assert.That(BuildFromInventorySource.CompareRank(Own, 9, Floor, 0), Is.LessThan(0));
        }

        [Test]
        public void CompareRank_Antisymmetry()
        {
            var reps = new (BuildMaterialSource src, int dist)[]
            {
                (Own, 0), (Own, 5),
                (Floor, 0), (Floor, 3),
                (Other, 2), (Other, 9),
                (Pack, 1), (Pack, 7),
            };
            for (int i = 0; i < reps.Length; i++)
                for (int j = 0; j < reps.Length; j++)
                {
                    int ab = BuildFromInventorySource.CompareRank(reps[i].src, reps[i].dist, reps[j].src, reps[j].dist);
                    int ba = BuildFromInventorySource.CompareRank(reps[j].src, reps[j].dist, reps[i].src, reps[i].dist);
                    Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                        $"CompareRank antisymmetry violated for ({i},{j})");
                }
        }

        // ---- Compare (total order + index tiebreak) ----

        [Test]
        public void Compare_IndexStableTiebreak_WithinSourceAndDist()
        {
            // Same source+dist -> lower index first.
            Assert.That(BuildFromInventorySource.Compare(Other, 5, 0, Other, 5, 1), Is.LessThan(0));
            Assert.That(BuildFromInventorySource.Compare(Pack, 2, 3, Pack, 2, 1), Is.GreaterThan(0));
        }

        [Test]
        public void Compare_SelfIsZero()
        {
            Assert.That(BuildFromInventorySource.Compare(Other, 5, 2, Other, 5, 2), Is.EqualTo(0));
            Assert.That(BuildFromInventorySource.Compare(Own, 0, 0, Own, 0, 0), Is.EqualTo(0));
        }

        [Test]
        public void Compare_Antisymmetry()
        {
            var reps = new (BuildMaterialSource src, int dist, int idx)[]
            {
                (Own, 0, 0), (Own, 5, 1),
                (Floor, 0, 2), (Floor, 3, 3),
                (Other, 2, 4), (Other, 2, 5),
                (Pack, 1, 6), (Pack, 7, 7),
            };
            for (int i = 0; i < reps.Length; i++)
                for (int j = 0; j < reps.Length; j++)
                {
                    int ab = BuildFromInventorySource.Compare(reps[i].src, reps[i].dist, reps[i].idx,
                                                              reps[j].src, reps[j].dist, reps[j].idx);
                    int ba = BuildFromInventorySource.Compare(reps[j].src, reps[j].dist, reps[j].idx,
                                                              reps[i].src, reps[i].dist, reps[i].idx);
                    Assert.That(Math.Sign(ab), Is.EqualTo(-Math.Sign(ba)),
                        $"Compare antisymmetry violated for ({i},{j})");
                }
        }

        // ---- End-to-end List<T>.Sort (the load-bearing proof the runtime picks own→floor→other→pack) ----

        private sealed class Cand
        {
            public BuildMaterialSource Source;
            public int Dist;
            public int Index;
        }

        private static List<Cand> SortByComparator(List<Cand> input)
        {
            var copy = new List<Cand>(input);
            copy.Sort((a, b) => BuildFromInventorySource.Compare(
                a.Source, a.Dist, a.Index, b.Source, b.Dist, b.Index));
            return copy;
        }

        [Test]
        public void Sort_OwnFloorOtherPack_ThenNearestThenIndex()
        {
            var input = new List<Cand>
            {
                new Cand { Source = Pack,  Dist = 1, Index = 0 },
                new Cand { Source = Other, Dist = 9, Index = 1 },
                new Cand { Source = Other, Dist = 3, Index = 2 },
                new Cand { Source = Floor, Dist = 4, Index = 3 },
                new Cand { Source = Own,   Dist = 0, Index = 4 },
                new Cand { Source = Own,   Dist = 0, Index = 5 }, // same source+dist as idx4 -> index tiebreak
            };
            var sorted = SortByComparator(input);
            // Own (idx4 then idx5 by index), Floor (idx3), Other nearest-first (idx2 dist3, idx1 dist9), Pack (idx0).
            Assert.That(sorted.ConvertAll(c => c.Index), Is.EqualTo(new[] { 4, 5, 3, 2, 1, 0 }));
        }

        [Test]
        public void Sort_AllSameSource_NearestThenIndex()
        {
            var input = new List<Cand>
            {
                new Cand { Source = Other, Dist = 5, Index = 0 },
                new Cand { Source = Other, Dist = 2, Index = 1 },
                new Cand { Source = Other, Dist = 5, Index = 2 }, // ties idx0 on (source,dist) -> index breaks
                new Cand { Source = Other, Dist = 1, Index = 3 },
            };
            var sorted = SortByComparator(input);
            Assert.That(sorted.ConvertAll(c => c.Index), Is.EqualTo(new[] { 3, 1, 0, 2 }));
        }

        // ---- OwnInventoryCoversDelivery (the "walks to a shelf while carrying the material" fix) ----
        // Threshold contract: fire exactly when carried stock covers one full delivery chunk,
        // min(neededUnits, handStackCap). At or above it, delivering from inventory moves the same
        // units per deposit as any fetch would, with zero fetch walk; below it, a floor fetch moves
        // strictly more per trip, so vanilla must stand.

        [Test]
        public void OwnCovers_WholeNeed_True()
        {
            // The Steam report: a wall needs 5, the pawn carries plenty.
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(70, 5, 75), Is.True);
        }

        [Test]
        public void OwnCovers_ExactNeed_True()
        {
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(5, 5, 75), Is.True);
        }

        [Test]
        public void OwnCovers_OneShortOfNeed_False()
        {
            // Partial coverage below one chunk: a single floor fetch delivers the whole need in one
            // trip, so splitting it (deliver 4, then fetch 1) would cost a trip.
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(4, 5, 75), Is.False);
        }

        [Test]
        public void OwnCovers_FullHandChunkOfBigNeed_True()
        {
            // Need exceeds one hand-load: carrying a full chunk (75) is as good as any fetch trip,
            // so deliver it now and let the next scan handle the remainder.
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(75, 110, 75), Is.True);
        }

        [Test]
        public void OwnCovers_PartialChunkOfBigNeed_False()
        {
            // 40 carried against a 110 need with 75-unit hands: a fetch trip moves 75, the carried
            // stock only 40, so the fetch wins.
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(40, 110, 75), Is.False);
        }

        [Test]
        public void OwnCovers_NothingCarried_False()
        {
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(0, 5, 75), Is.False);
        }

        [Test]
        public void OwnCovers_NoNeed_False()
        {
            // Nothing to deliver: never offer a job for it.
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(70, 0, 75), Is.False);
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(70, -1, 75), Is.False);
        }

        [Test]
        public void OwnCovers_NoHandCap_False()
        {
            // A pawn that cannot move a single unit by hand cannot deposit chunks either.
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(70, 5, 0), Is.False);
            Assert.That(BuildFromInventorySource.OwnInventoryCoversDelivery(70, 5, -1), Is.False);
        }
    }
}
