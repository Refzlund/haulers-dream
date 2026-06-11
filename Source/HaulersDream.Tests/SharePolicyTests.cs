using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SharePolicyTests
    {
        // The load-bearing rule: a worker's OWN stock bypasses the reach + radius gates (it's already at the
        // bench/site), but everyone still passes the reservation opt-out and the recipe/material filter.

        [Test]
        public void Self_BypassesReachAndRadius()
        {
            // Even unreachable + outside radius, the worker's own reservable, usable stock is included.
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: true, reachable: false, canReserve: true,
                isUsable: true, withinRadius: false), Is.True);
        }

        [Test]
        public void Self_NotReservable_Excluded()
        {
            // The opt-out still applies to self: a stack already committed to another job is skipped.
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: true, reachable: true, canReserve: false,
                isUsable: true, withinRadius: true), Is.False);
        }

        [Test]
        public void Self_NotUsable_Excluded()
        {
            // The recipe/material filter still applies to self.
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: true, reachable: true, canReserve: true,
                isUsable: false, withinRadius: true), Is.False);
        }

        [Test]
        public void Other_Unreachable_Excluded()
        {
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: false, reachable: false, canReserve: true,
                isUsable: true, withinRadius: true), Is.False);
        }

        [Test]
        public void Other_OutsideRadius_Excluded()
        {
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: false, reachable: true, canReserve: true,
                isUsable: true, withinRadius: false), Is.False);
        }

        [Test]
        public void Other_AllConditionsMet_Included()
        {
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: false, reachable: true, canReserve: true,
                isUsable: true, withinRadius: true), Is.True);
        }

        [Test]
        public void Other_NotReservable_Excluded()
        {
            Assert.That(SharePolicy.ShouldIncludeStack(isSelf: false, reachable: true, canReserve: false,
                isUsable: true, withinRadius: true), Is.False);
        }
    }
}
