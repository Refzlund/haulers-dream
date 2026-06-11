using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class WorkTypePolicyTests
    {
        // Each type reads only its own toggle.
        [TestCase(HaulSourceType.Harvest)]
        [TestCase(HaulSourceType.Mining)]
        [TestCase(HaulSourceType.DeepDrill)]
        [TestCase(HaulSourceType.Deconstruct)]
        [TestCase(HaulSourceType.Animal)]
        [TestCase(HaulSourceType.Strip)]
        public void EachType_OnlyReadsItsOwnToggle(HaulSourceType type)
        {
            // all off except the one under test
            bool h = type == HaulSourceType.Harvest;
            bool m = type == HaulSourceType.Mining;
            bool d = type == HaulSourceType.DeepDrill;
            bool dc = type == HaulSourceType.Deconstruct;
            bool a = type == HaulSourceType.Animal;
            bool st = type == HaulSourceType.Strip;
            Assert.That(WorkTypePolicy.IsTypeEnabled(type, h, m, d, dc, a, st), Is.True, "its own toggle on => enabled");

            Assert.That(WorkTypePolicy.IsTypeEnabled(type, false, false, false, false, false, false), Is.False, "its own toggle off => disabled");
        }

        [Test]
        public void AllOn_EverythingEnabled()
        {
            foreach (HaulSourceType t in System.Enum.GetValues(typeof(HaulSourceType)))
                Assert.That(WorkTypePolicy.IsTypeEnabled(t, true, true, true, true, true, true), Is.True);
        }

        [Test]
        public void AllOff_NothingEnabled()
        {
            foreach (HaulSourceType t in System.Enum.GetValues(typeof(HaulSourceType)))
                Assert.That(WorkTypePolicy.IsTypeEnabled(t, false, false, false, false, false, false), Is.False);
        }

        [Test]
        public void MiningToggle_DoesNotLeakToHarvest()
        {
            // mining on, harvest off
            Assert.That(WorkTypePolicy.IsTypeEnabled(HaulSourceType.Harvest, false, true, false, false, false, false), Is.False);
            Assert.That(WorkTypePolicy.IsTypeEnabled(HaulSourceType.Mining, false, true, false, false, false, false), Is.True);
        }

        [Test]
        public void StripToggle_DoesNotLeakToOthers()
        {
            // strip on, everything else off
            Assert.That(WorkTypePolicy.IsTypeEnabled(HaulSourceType.Strip, false, false, false, false, false, true), Is.True);
            Assert.That(WorkTypePolicy.IsTypeEnabled(HaulSourceType.Deconstruct, false, false, false, false, false, true), Is.False);
        }
    }
}
