using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CremationStripPolicyTests
    {
        [Test]
        public void SettingOff_NeverStrips_RegardlessOfEverythingElse()
        {
            // The feature opt-in is the master gate: with it off, no combination of the other facts ever strips.
            foreach (bool autoStrips in new[] { false, true })
                foreach (bool anything in new[] { false, true })
                    foreach (bool playerCorpse in new[] { false, true })
                        foreach (bool stripColonists in new[] { false, true })
                            Assert.That(
                                CremationStripPolicy.ShouldStrip(false, autoStrips, anything, playerCorpse, stripColonists),
                                Is.False);
        }

        [Test]
        public void RecipeAutoStrips_NeverStrips_VanillaSeamAlreadyDid()
        {
            // Butchering (autoStripCorpses = true): vanilla's consume seam strips the body, so HD must not double it.
            Assert.That(CremationStripPolicy.ShouldStrip(true, true, true, false, false), Is.False);
        }

        [Test]
        public void NothingToStrip_NeverStrips()
        {
            // Already bare, or a haul-strip ran en route: no gear to salvage, and re-stripping is a pointless no-op.
            Assert.That(CremationStripPolicy.ShouldStrip(true, false, false, false, false), Is.False);
        }

        [Test]
        public void EnemyCremation_SettingOn_Strips()
        {
            // The headline case: an enemy corpse bound for a cremation (autoStripCorpses = false) bill, feature on.
            Assert.That(CremationStripPolicy.ShouldStrip(true, false, true, false, false), Is.True);
            // The enemy result is independent of the your-own-dead opt-in.
            Assert.That(CremationStripPolicy.ShouldStrip(true, false, true, false, true), Is.True);
        }

        [Test]
        public void OwnDead_NotStrippedUnlessColonistOptInAlsoOn()
        {
            // A player-faction corpse is left dressed with only the cremation-strip feature on...
            Assert.That(CremationStripPolicy.ShouldStrip(true, false, true, true, false), Is.False);
            // ...but stripped once the player also opted into stripping their own dead.
            Assert.That(CremationStripPolicy.ShouldStrip(true, false, true, true, true), Is.True);
        }
    }
}
