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
            bool allowMechanoids = true, bool pauseWhileDrafted = true, bool allowIncapable = false)
            => EligibilityPolicy.IsEligible(isMechanoid, isHumanlike, isDrafted, incapableOfHauling,
                allowMechanoids, pauseWhileDrafted, allowIncapable);

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
