using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class EligibilityPolicyTests
    {
        // Convenience wrapper with sensible defaults so each test states only what it cares about.
        private static bool Eligible(
            bool isMechanoid = false, bool isHumanlike = true, bool isDrafted = false, bool incapableOfHauling = false,
            bool allowMechanoids = true, bool pauseWhileDrafted = true, bool allowIncapable = false,
            bool allowAnimals = false)
            => EligibilityPolicy.IsEligible(isMechanoid, isHumanlike, isDrafted, incapableOfHauling,
                allowMechanoids, pauseWhileDrafted, allowIncapable, allowAnimals);

        [Test]
        public void PlainHumanlikeColonist_IsEligible()
        {
            Assert.That(Eligible(), Is.True);
        }

        [Test]
        public void Mechanoid_FollowsAllowMechanoidsSetting()
        {
            Assert.That(Eligible(isMechanoid: true, allowMechanoids: true), Is.True);
            Assert.That(Eligible(isMechanoid: true, allowMechanoids: false), Is.False);
        }

        [Test]
        public void Mechanoid_IgnoresDraftedAndIncapableGating()
        {
            // Drafted + incapable would block a humanlike, but a mech only checks allowMechanoids.
            Assert.That(Eligible(isMechanoid: true, isDrafted: true, incapableOfHauling: true,
                allowMechanoids: true, pauseWhileDrafted: true, allowIncapable: false), Is.True);
        }

        [Test]
        public void NonHumanlikeNonMech_NeverEligible()
        {
            Assert.That(Eligible(isHumanlike: false), Is.False);
        }

        [Test]
        public void Animal_FollowsAllowAnimalsSetting()
        {
            // Non-humanlike, non-mech = animal: eligible only when allowAnimals is on.
            Assert.That(Eligible(isHumanlike: false, allowAnimals: true), Is.True);
            Assert.That(Eligible(isHumanlike: false, allowAnimals: false), Is.False);
        }

        [Test]
        public void Animal_AllowAnimals_IgnoresDraftedAndIncapableGating()
        {
            // Like mechs, an allowed animal gates only on its allow-toggle; the drafted /
            // incapable-of-hauling gating (meaningful only for colonists) does not block it.
            Assert.That(Eligible(isHumanlike: false, allowAnimals: true,
                isDrafted: true, incapableOfHauling: true,
                pauseWhileDrafted: true, allowIncapable: false), Is.True);
        }

        [Test]
        public void Humanlike_UnaffectedByAllowAnimals()
        {
            // A humanlike colonist's eligibility never depends on the animal toggle, either way.
            Assert.That(Eligible(isHumanlike: true, allowAnimals: true), Is.True);
            Assert.That(Eligible(isHumanlike: true, allowAnimals: false), Is.True);
        }

        [Test]
        public void Mech_UnaffectedByAllowAnimals()
        {
            // A mech still follows only allowMechanoids regardless of the animal toggle.
            Assert.That(Eligible(isMechanoid: true, allowMechanoids: true, allowAnimals: false), Is.True);
            Assert.That(Eligible(isMechanoid: true, allowMechanoids: true, allowAnimals: true), Is.True);
            Assert.That(Eligible(isMechanoid: true, allowMechanoids: false, allowAnimals: true), Is.False);
        }

        [Test]
        public void Drafted_BlockedOnlyWhenPauseWhileDrafted()
        {
            Assert.That(Eligible(isDrafted: true, pauseWhileDrafted: true), Is.False);
            Assert.That(Eligible(isDrafted: true, pauseWhileDrafted: false), Is.True);
        }

        [Test]
        public void IncapableOfHauling_BlockedUnlessAllowed()
        {
            Assert.That(Eligible(incapableOfHauling: true, allowIncapable: false), Is.False);
            Assert.That(Eligible(incapableOfHauling: true, allowIncapable: true), Is.True);
        }

        [Test]
        public void DraftedTakesPrecedenceOverIncapableAllowance()
        {
            // even if incapable is allowed, a paused-drafted pawn is still blocked
            Assert.That(Eligible(isDrafted: true, pauseWhileDrafted: true, incapableOfHauling: true, allowIncapable: true), Is.False);
        }
    }
}
