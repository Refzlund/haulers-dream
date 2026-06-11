using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class RouteOrderingTests
    {
        // A candidate: (marked?, squared distance to anchor, unique id).
        private struct Cand
        {
            public bool Marked;
            public long DistSq;
            public int Id;
            public Cand(bool m, long d, int id) { Marked = m; DistSq = d; Id = id; }
            public override string ToString() => $"({(Marked ? "M" : "u")},d{DistSq},#{Id})";
        }

        private static void Sort(List<Cand> xs) =>
            xs.Sort((a, b) => RouteOrdering.CompareMarkedFirst(a.Marked, a.DistSq, a.Id, b.Marked, b.DistSq, b.Id));

        [Test]
        public void MarkedFirst_AllMarkedComeBeforeAnyUnmarked()
        {
            var xs = new List<Cand>
            {
                new Cand(false, 5, 1), new Cand(true, 30, 2), new Cand(false, 1, 3), new Cand(true, 10, 4),
            };
            Sort(xs);
            int lastMarked = xs.FindLastIndex(c => c.Marked);
            int firstUnmarked = xs.FindIndex(c => !c.Marked);
            Assert.That(lastMarked, Is.LessThan(firstUnmarked), "every marked candidate must precede every unmarked one");
        }

        [Test]
        public void MarkedFirst_WithinAGroupSortsByDistanceThenId()
        {
            var xs = new List<Cand>
            {
                new Cand(true, 20, 9), new Cand(true, 10, 8), new Cand(true, 20, 7), // two at d20 → id tiebreak
            };
            Sort(xs);
            Assert.That(xs.Select(c => c.Id), Is.EqualTo(new[] { 8, 7, 9 }), "nearest first; equal distance breaks by id");
        }

        [Test]
        public void AddingUnmarked_DoesNotReorderTheMarkedPrefix_Monotonic()
        {
            // NOTE: this pins a PROPERTY OF THE PRIMITIVE (marked-first), not the live route behaviour. The route
            // path deliberately NO LONGER sorts marked-first (it sorts purely by distance so "the nearest few" is
            // honoured for unmarked plants too) — see RouteOrdering's doc. Kept because the primitive is still
            // correct and may be reused.
            // The property: with marked-first, the marked-only set is a literal PREFIX of marked+unmarked, so the
            // marked order is preserved and only unmarked stops are appended.
            var marked = new List<Cand>
            {
                new Cand(true, 30, 1), new Cand(true, 10, 2), new Cand(true, 20, 3), new Cand(true, 20, 4),
            };
            var unmarked = new List<Cand>
            {
                new Cand(false, 5, 5), new Cand(false, 15, 6), new Cand(false, 25, 7),
            };

            var off = new List<Cand>(marked);
            Sort(off);

            var on = new List<Cand>(marked);
            on.AddRange(unmarked);
            Sort(on);

            // (a) the marked-only subsequence of the ON order equals the OFF order
            var onMarked = on.Where(c => c.Marked).Select(c => c.Id).ToList();
            Assert.That(onMarked, Is.EqualTo(off.Select(c => c.Id).ToList()), "marked order changed when unmarked were added");

            // (b) the OFF order is a literal prefix of the ON order
            for (int i = 0; i < off.Count; i++)
                Assert.That(on[i].Id, Is.EqualTo(off[i].Id), $"ON order diverges from the OFF prefix at index {i}");
        }

        [Test]
        public void NonHarvest_BothFalse_SortsPurelyByDistance()
        {
            var xs = new List<Cand>
            {
                new Cand(false, 30, 1), new Cand(false, 10, 2), new Cand(false, 20, 3),
            };
            Sort(xs);
            Assert.That(xs.Select(c => c.Id), Is.EqualTo(new[] { 2, 3, 1 }));
        }
    }
}
