using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class YieldLeashPolicyTests
    {
        [Test]
        public void BelowThreshold_NeverCollects()
        {
            // Even with the cooldown long elapsed, a pile under the threshold is left to keep growing.
            Assert.That(YieldLeashPolicy.ShouldCollectNow(0, 8, 100000, 300), Is.False);
            Assert.That(YieldLeashPolicy.ShouldCollectNow(7, 8, 100000, 300), Is.False);
        }

        [Test]
        public void AtOrAboveThreshold_CooldownElapsed_Collects()
        {
            Assert.That(YieldLeashPolicy.ShouldCollectNow(8, 8, 300, 300), Is.True);   // both bounds exactly met
            Assert.That(YieldLeashPolicy.ShouldCollectNow(8, 8, 301, 300), Is.True);
            Assert.That(YieldLeashPolicy.ShouldCollectNow(40, 8, 5000, 300), Is.True);
        }

        [Test]
        public void AtThreshold_CooldownNotElapsed_Waits()
        {
            // Pile is big enough, but the pawn was interrupted too recently -> let it drill on.
            Assert.That(YieldLeashPolicy.ShouldCollectNow(8, 8, 299, 300), Is.False);
            Assert.That(YieldLeashPolicy.ShouldCollectNow(20, 8, 0, 300), Is.False);
        }

        [Test]
        public void NeverInterrupted_LargeSinceValue_Collects()
        {
            // The runtime passes int.MaxValue when a pawn has no recorded interrupt yet.
            Assert.That(YieldLeashPolicy.ShouldCollectNow(8, 8, int.MaxValue, 300), Is.True);
        }
    }
}
