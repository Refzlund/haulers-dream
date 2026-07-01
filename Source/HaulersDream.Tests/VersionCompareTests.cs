using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class VersionCompareTests
    {
        [Test]
        public void Outdated_WhenLatestIsHigher()
        {
            Assert.That(VersionCompare.IsOutdated("1.13.0", "1.14.0"), Is.True);
            Assert.That(VersionCompare.IsOutdated("1.13.0.0", "1.13.1"), Is.True);
            Assert.That(VersionCompare.IsOutdated("1.9.9", "2.0.0"), Is.True);
        }

        [Test]
        public void NotOutdated_WhenEqual_AcrossDifferentLengths()
        {
            // the running mod reports 4-part "1.13.0.0"; the published value is 3-part "1.13.0" -> equal
            Assert.That(VersionCompare.IsOutdated("1.13.0.0", "1.13.0"), Is.False);
            Assert.That(VersionCompare.IsOutdated("1.13.0", "1.13.0.0"), Is.False);
        }

        [Test]
        public void NotOutdated_WhenCurrentIsHigher()
        {
            Assert.That(VersionCompare.IsOutdated("1.14.0", "1.13.9"), Is.False);
            Assert.That(VersionCompare.IsOutdated("2.0.0", "1.99.99"), Is.False);
        }

        [Test]
        public void MissingOrEmpty_NeverWarns()
        {
            Assert.That(VersionCompare.IsOutdated("", "1.0.0"), Is.False);
            Assert.That(VersionCompare.IsOutdated("1.0.0", ""), Is.False);
            Assert.That(VersionCompare.IsOutdated("1.0.0", null), Is.False);
            Assert.That(VersionCompare.IsOutdated(null, "1.0.0"), Is.False);
        }

        [Test]
        public void Tolerates_NonNumericSuffix()
        {
            Assert.That(VersionCompare.IsOutdated("1.13.0", "1.13.1-beta"), Is.True);
            Assert.That(VersionCompare.Compare("1.13.0-rc1", "1.13.0"), Is.EqualTo(0));
        }

        [Test]
        public void Compare_Basics()
        {
            Assert.That(VersionCompare.Compare("1.2.3", "1.2.3"), Is.EqualTo(0));
            Assert.That(VersionCompare.Compare("1.2.3", "1.2.4"), Is.EqualTo(-1));
            Assert.That(VersionCompare.Compare("2.0", "1.9.9"), Is.EqualTo(1));
        }
    }
}
