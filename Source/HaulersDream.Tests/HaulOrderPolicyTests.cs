using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the capability bar Hauler's Dream's right-click HAUL orders apply (issue #229): a pawn whose
    /// HAULING WORK TYPE is disabled must not be offered one unless the player opted in, and no setting can
    /// override missing manipulation.
    /// </summary>
    [TestFixture]
    public class HaulOrderPolicyTests
    {
        // ── the three outcomes ───────────────────────────────────────────────────────────────────

        [Test]
        public void CapableHauler_NotBlocked()
        {
            Assert.That(HaulOrderPolicy.BlockFor(true, incapableOfHauling: false, allowIncapable: false),
                Is.EqualTo(HaulOrderBlock.None));
        }

        [Test]
        public void NoManipulation_BlockedEvenWhenIncapableIsAllowed()
        {
            // "Let incapable pawns haul anyway" is about WORK assignment, never about physical ability: a pawn
            // with no working hands still cannot pick a stack up.
            Assert.That(HaulOrderPolicy.BlockFor(false, incapableOfHauling: false, allowIncapable: true),
                Is.EqualTo(HaulOrderBlock.Manipulation));
        }

        [Test]
        public void NoManipulation_WinsOverHaulingDisabled()
        {
            // Both blocks apply; the reported one is the one no setting can lift, so the message never suggests
            // a toggle that would not help.
            Assert.That(HaulOrderPolicy.BlockFor(false, incapableOfHauling: true, allowIncapable: false),
                Is.EqualTo(HaulOrderBlock.Manipulation));
        }

        [Test]
        public void HaulingDisabled_BlockedByDefault()
        {
            // The #229 case: an "incapable of dumb labor" backstory. Vanilla greys "Prioritize hauling" out, so
            // HD's orders must be hidden too.
            Assert.That(HaulOrderPolicy.BlockFor(true, incapableOfHauling: true, allowIncapable: false),
                Is.EqualTo(HaulOrderBlock.HaulingDisabled));
        }

        [Test]
        public void HaulingDisabled_AllowedWhenPlayerOptedIn()
        {
            Assert.That(HaulOrderPolicy.BlockFor(true, incapableOfHauling: true, allowIncapable: true),
                Is.EqualTo(HaulOrderBlock.None));
        }

        [Test]
        public void CapableHauler_IsSettingIndependent()
        {
            // A pawn vanilla WILL give hauling work to is never affected by the opt-in either way.
            Assert.That(HaulOrderPolicy.BlockFor(true, incapableOfHauling: false, allowIncapable: false),
                Is.EqualTo(HaulOrderBlock.None));
            Assert.That(HaulOrderPolicy.BlockFor(true, incapableOfHauling: false, allowIncapable: true),
                Is.EqualTo(HaulOrderBlock.None));
        }

        // ── lockstep with the automatic side ─────────────────────────────────────────────────────

        [Test]
        public void OrderedBar_MatchesAutomaticIncapableClause_ForEveryCombination()
        {
            // The ordered gate (HaulOrderPolicy) and the automatic gate (EligibilityPolicy, via
            // YieldRouter.IsEligible) must apply the SAME incapable clause, or an order could hand a pawn cargo
            // the automatic unload then refuses to shed. Compare across all four (incapable, allowIncapable)
            // combinations for an otherwise-unremarkable colonist: humanlike, not a mech, not drafted.
            foreach (bool incapable in new[] { false, true })
            {
                foreach (bool allowIncapable in new[] { false, true })
                {
                    bool orderedAllows =
                        HaulOrderPolicy.BlockFor(true, incapable, allowIncapable) == HaulOrderBlock.None;
                    bool automaticAllows = EligibilityPolicy.IsEligible(
                        isMechanoid: false,
                        isHumanlike: true,
                        isDrafted: false,
                        incapableOfHauling: incapable,
                        allowMechanoids: false,
                        pauseWhileDrafted: true,
                        allowIncapable: allowIncapable);
                    Assert.That(orderedAllows, Is.EqualTo(automaticAllows),
                        $"ordered vs automatic gate drifted at incapable={incapable}, allowIncapable={allowIncapable}");
                }
            }
        }
    }
}
