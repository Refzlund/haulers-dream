using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class HarvestSectionPolicyTests
    {
        [Test]
        public void CountBelowSection_Unchanged()
        {
            Assert.That(HarvestSectionPolicy.Cap(1, 8), Is.EqualTo(1));
            Assert.That(HarvestSectionPolicy.Cap(3, 8), Is.EqualTo(3));
            Assert.That(HarvestSectionPolicy.Cap(7, 8), Is.EqualTo(7));
        }

        [Test]
        public void CountEqualSection_Unchanged()
        {
            Assert.That(HarvestSectionPolicy.Cap(8, 8), Is.EqualTo(8));
        }

        [Test]
        public void CountAboveSection_CappedToSection()
        {
            Assert.That(HarvestSectionPolicy.Cap(9, 8), Is.EqualTo(8));
            Assert.That(HarvestSectionPolicy.Cap(40, 8), Is.EqualTo(8));
        }

        [Test]
        public void ZeroCount_Zero()
        {
            Assert.That(HarvestSectionPolicy.Cap(0, 8), Is.EqualTo(0));
        }

        [Test]
        public void NonPositiveSection_DisablesCap()
        {
            // An invalid/disabled section size must never trim the queue away — return the full count.
            Assert.That(HarvestSectionPolicy.Cap(40, 0), Is.EqualTo(40));
            Assert.That(HarvestSectionPolicy.Cap(40, -1), Is.EqualTo(40));
        }
    }
}
