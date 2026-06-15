using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class HaulAfterKillPolicyTests
    {
        // Hunt reads ONLY haulWildKills (independent of the tamed toggle).
        [TestCase(true,  true,  ExpectedResult = true)]
        [TestCase(true,  false, ExpectedResult = true)]
        [TestCase(false, true,  ExpectedResult = false)]
        [TestCase(false, false, ExpectedResult = false)]
        public bool Hunt_FollowsWildToggleOnly(bool haulWild, bool haulTamed)
            => HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, haulWild, haulTamed);

        // Slaughter reads ONLY haulTamedSlaughter (independent of the wild toggle).
        [TestCase(true,  true,  ExpectedResult = true)]
        [TestCase(false, true,  ExpectedResult = true)]
        [TestCase(true,  false, ExpectedResult = false)]
        [TestCase(false, false, ExpectedResult = false)]
        public bool Slaughter_FollowsTamedToggleOnly(bool haulWild, bool haulTamed)
            => HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Slaughter, haulWild, haulTamed);

        [Test]
        public void DefaultOn_BothKindsHaul()
        {
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, true, true), Is.True);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Slaughter, true, true), Is.True);
        }

        [Test]
        public void BothTogglesOff_NeitherKindHauls()
        {
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, false, false), Is.False);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Slaughter, false, false), Is.False);
        }

        [Test]
        public void TogglesAreIndependent()
        {
            // Tamed-on, Wild-off: slaughter hauls, hunt doesn't.
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Slaughter, false, true), Is.True);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt,      false, true), Is.False);
            // Wild-on, Tamed-off: hunt hauls, slaughter doesn't.
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt,      true, false), Is.True);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Slaughter, true, false), Is.False);
        }

        // The WILD-kill path is a JobDriver_Hunt finish action that fires only on a NON-clean hunt (an
        // interrupted-after-kill hunt vanilla wouldn't self-haul), feeding HaulKillSource.Hunt — so the pure
        // policy is unchanged: a hunt-classified kill is gated SOLELY by haulWildKills, never by the tamed toggle.
        [Test]
        public void HuntClassification_GatedSolelyByWildToggle()
        {
            // haulWildKills decides Hunt regardless of the tamed toggle's value.
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, true,  true),  Is.True);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, true,  false), Is.True);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, false, true),  Is.False);
            Assert.That(HaulAfterKillPolicy.ShouldHaul(HaulKillSource.Hunt, false, false), Is.False);
        }
    }
}
