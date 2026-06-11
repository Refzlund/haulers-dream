using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class ConstructionBatchMathTests
    {
        // Healthy colonist: 75 wood per hand-trip; a fence costs 1 wood (so each needer space = 1).
        const int HandCap = 75;

        static List<int> Ones(int n) => Enumerable.Repeat(1, n).ToList();

        static (int count, int needers, int resources) Plan(
            int handCap, int currentCount, int currentResource,
            IReadOnlyList<int> needers, IReadOnlyList<int> resources)
        {
            ConstructionBatchMath.Plan(handCap, currentCount, currentResource, needers, resources,
                out int fc, out int nt, out int rt);
            return (fc, nt, rt);
        }

        [Test]
        public void BatchesNeedersUpToCarryableLoad_WhenSourcePileSuffices()
        {
            // The reported bug: vanilla count 9, but a 64-wood pile is already attached and many fences
            // are queued. We should raise the trip to the 64 wood we have (limited by the pile, not 9).
            var r = Plan(HandCap, currentCount: 9, currentResource: 64, Ones(100), new List<int>());
            Assert.That(r.count, Is.EqualTo(64));     // carry the whole 64-pile, not 9
            Assert.That(r.needers, Is.EqualTo(66));   // 9 + 66 = 75 (filled demand to hand cap)
            Assert.That(r.resources, Is.EqualTo(0));  // the existing pile covers it
        }

        [Test]
        public void DemandLimited_CarriesExactlyWhatIsQueued()
        {
            // Only 50 more fences queued → carry 9 + 50 = 59, no extra wood needed.
            var r = Plan(HandCap, currentCount: 9, currentResource: 64, Ones(50), new List<int> { 20, 20 });
            Assert.That(r.count, Is.EqualTo(59));
            Assert.That(r.needers, Is.EqualTo(50));
            Assert.That(r.resources, Is.EqualTo(0));
        }

        [Test]
        public void AttachesExtraResource_WhenSourcePileTooSmall()
        {
            // Source has only 20 wood but demand fills to 75 → pull in nearby wood stacks to reach it.
            var r = Plan(HandCap, currentCount: 9, currentResource: 20, Ones(100), new List<int> { 15, 30, 40 });
            Assert.That(r.count, Is.EqualTo(75));    // capped at hand capacity
            Assert.That(r.needers, Is.EqualTo(66));
            Assert.That(r.resources, Is.EqualTo(3)); // 20 + 15 + 30 + 40 covers 75
        }

        [Test]
        public void ResourceTake_StopsAtFirstSufficientStack()
        {
            var r = Plan(HandCap, currentCount: 9, currentResource: 20, Ones(100), new List<int> { 100 });
            Assert.That(r.count, Is.EqualTo(75));
            Assert.That(r.resources, Is.EqualTo(1)); // one big stack is enough
        }

        [Test]
        public void HandsAlreadyFull_NoChange()
        {
            var r = Plan(HandCap, currentCount: 75, currentResource: 75, Ones(50), new List<int> { 50 });
            Assert.That(r.needers, Is.EqualTo(0));
            Assert.That(r.resources, Is.EqualTo(0));
        }

        [Test]
        public void NoExtraNeeders_NoChange()
        {
            var r = Plan(HandCap, currentCount: 9, currentResource: 64, new List<int>(), new List<int> { 30 });
            Assert.That(r.count, Is.EqualTo(9));
            Assert.That(r.needers, Is.EqualTo(0));
            Assert.That(r.resources, Is.EqualTo(0));
        }

        [Test]
        public void NoMoreResourceReachableAndSourceMatchesCount_NoChange()
        {
            // Carrying all 9 available wood already, nothing more reachable → can't improve.
            var r = Plan(HandCap, currentCount: 9, currentResource: 9, Ones(100), new List<int>());
            Assert.That(r.count, Is.EqualTo(9));
            Assert.That(r.needers, Is.EqualTo(0));
            Assert.That(r.resources, Is.EqualTo(0));
        }

        [Test]
        public void FinalCount_NeverBelowCurrent_NeverAboveHandCapOrResource()
        {
            foreach (var (cc, cr, needers, res) in new[]
            {
                (9, 64, 100, 0), (1, 5, 3, 0), (40, 200, 100, 0), (9, 10, 100, 0),
            })
            {
                ConstructionBatchMath.Plan(HandCap, cc, cr, Ones(needers),
                    res == 0 ? new List<int>() : Ones(res), out int fc, out int nt, out int rt);
                Assert.That(fc, Is.GreaterThanOrEqualTo(cc));
                Assert.That(fc, Is.LessThanOrEqualTo(HandCap));
                Assert.That(fc, Is.LessThanOrEqualTo(cr).Or.EqualTo(cc)); // bounded by available resource
                Assert.That(nt, Is.LessThanOrEqualTo(needers));
            }
        }
    }
}
