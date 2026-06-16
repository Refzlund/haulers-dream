using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OpportunisticLoadPolicyTests
    {
        // ===== ShouldConsider (the top-level gate) =====

        // Feature OFF → never a candidate, regardless of everything else (byte-inert default).
        [Test]
        public void Consider_FeatureOff_No()
        {
            Assert.That(OpportunisticLoadPolicy.ShouldConsider(
                featureEnabled: false, carriedSurplusUnits: 50, alreadyOnHigherPriorityHdJob: false), Is.False);
        }

        // Nothing carried (no tagged surplus) → no candidate (nothing to opportunistically shed).
        [TestCase(0)]
        [TestCase(-5)]
        public void Consider_NothingCarried_No(int carried)
        {
            Assert.That(OpportunisticLoadPolicy.ShouldConsider(
                featureEnabled: true, carriedSurplusUnits: carried, alreadyOnHigherPriorityHdJob: false), Is.False);
        }

        // Already running a higher-priority HD/forced job owning the cargo → never divert off it.
        [Test]
        public void Consider_AlreadyOnHdJob_No()
        {
            Assert.That(OpportunisticLoadPolicy.ShouldConsider(
                featureEnabled: true, carriedSurplusUnits: 50, alreadyOnHigherPriorityHdJob: true), Is.False);
        }

        // Feature on, carrying surplus, not busy → a candidate.
        [Test]
        public void Consider_EligibleCarryingPawn_Yes()
        {
            Assert.That(OpportunisticLoadPolicy.ShouldConsider(
                featureEnabled: true, carriedSurplusUnits: 1, alreadyOnHigherPriorityHdJob: false), Is.True);
        }

        // ===== DepositCount (per-(target,def) clamp) =====

        // Carried < target need → deposit all carried.
        [Test]
        public void Deposit_CarriedBindsLow()
        {
            Assert.That(OpportunisticLoadPolicy.DepositCount(carriedSurplusOfDef: 20, targetAvailableForDef: 75), Is.EqualTo(20));
        }

        // Target need < carried → deposit only what the target can take (clamped).
        [Test]
        public void Deposit_TargetBindsLow()
        {
            Assert.That(OpportunisticLoadPolicy.DepositCount(carriedSurplusOfDef: 75, targetAvailableForDef: 20), Is.EqualTo(20));
        }

        // Equal → that amount.
        [Test]
        public void Deposit_Equal()
        {
            Assert.That(OpportunisticLoadPolicy.DepositCount(30, 30), Is.EqualTo(30));
        }

        // Target wants none of this def (already fully claimed / satisfied) → 0 (no matching need → no divert).
        [TestCase(40, 0)]
        [TestCase(40, -3)]
        public void Deposit_NoTargetNeed_Zero(int carried, int avail)
        {
            Assert.That(OpportunisticLoadPolicy.DepositCount(carried, avail), Is.EqualTo(0));
        }

        // Nothing carried of this def → 0.
        [TestCase(0, 50)]
        [TestCase(-2, 50)]
        public void Deposit_NothingCarriedOfDef_Zero(int carried, int avail)
        {
            Assert.That(OpportunisticLoadPolicy.DepositCount(carried, avail), Is.EqualTo(0));
        }

        // ===== ShouldDivertTo (combined: gate + radius + count) =====

        // Feature OFF → no divert even with a perfect needy target right next to the pawn.
        [Test]
        public void DivertTo_FeatureOff_NoDivert()
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: false, carriedSurplusOfDef: 40, targetAvailableForDef: 40,
                distanceToTarget: 1f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(0));
        }

        // Needy target IN radius with matching cargo → divert, count CLAMPED to the smaller of carried/need.
        [Test]
        public void DivertTo_NeedyInRadius_DivertsClamped()
        {
            // carried 40, target wants 25, distance 10 within radius 30 → deposit 25.
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 40, targetAvailableForDef: 25,
                distanceToTarget: 10f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(25));
        }

        // A target exactly AT the radius is in-range (≤, inclusive — matches IntVec3.InHorDistOf).
        [Test]
        public void DivertTo_AtRadiusEdge_InRange()
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 10, targetAvailableForDef: 10,
                distanceToTarget: 30f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(10));
        }

        // Target OUT of radius → no divert (even though it needs exactly what the pawn carries).
        [Test]
        public void DivertTo_OutOfRadius_NoDivert()
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 40, targetAvailableForDef: 40,
                distanceToTarget: 31f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(0));
        }

        // In radius but the target needs NONE of the carried def → no divert.
        [Test]
        public void DivertTo_NoMatchingNeed_NoDivert()
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 40, targetAvailableForDef: 0,
                distanceToTarget: 5f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(0));
        }

        // Nothing carried → no divert.
        [Test]
        public void DivertTo_NothingCarried_NoDivert()
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 0, targetAvailableForDef: 40,
                distanceToTarget: 5f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(0));
        }

        // Already busy with a higher-priority HD/forced job → no divert.
        [Test]
        public void DivertTo_AlreadyOnHdJob_NoDivert()
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 40, targetAvailableForDef: 40,
                distanceToTarget: 5f, scanRadius: 30f, alreadyOnHigherPriorityHdJob: true);
            Assert.That(n, Is.EqualTo(0));
        }

        // A misconfigured non-positive radius diverts nowhere (defensive).
        [TestCase(0f)]
        [TestCase(-10f)]
        public void DivertTo_NonPositiveRadius_NoDivert(float radius)
        {
            int n = OpportunisticLoadPolicy.ShouldDivertTo(
                featureEnabled: true, carriedSurplusOfDef: 40, targetAvailableForDef: 40,
                distanceToTarget: 0f, scanRadius: radius, alreadyOnHigherPriorityHdJob: false);
            Assert.That(n, Is.EqualTo(0));
        }
    }
}
