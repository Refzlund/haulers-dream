using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OptOutOverridePolicyTests
    {
        [Test]
        public void Strip_IsTheOnlyExplicitOrderThatOverridesTheOptOut()
        {
            // An explicit player Strip order must scoop+haul even when the worker's per-pawn
            // "Auto-haul yields" toggle is OFF (JobDriver_Strip is always player-ordered).
            Assert.That(OptOutOverridePolicy.ExplicitOrderOverridesOptOut(HaulSourceType.Strip), Is.True);
        }

        [Test]
        public void AutonomousSources_DoNotOverrideTheOptOut()
        {
            // Every non-strip source is ordinary autonomous work; the standing toggle still governs it.
            Assert.That(OptOutOverridePolicy.ExplicitOrderOverridesOptOut(HaulSourceType.Harvest), Is.False);
            Assert.That(OptOutOverridePolicy.ExplicitOrderOverridesOptOut(HaulSourceType.Mining), Is.False);
            Assert.That(OptOutOverridePolicy.ExplicitOrderOverridesOptOut(HaulSourceType.DeepDrill), Is.False);
            Assert.That(OptOutOverridePolicy.ExplicitOrderOverridesOptOut(HaulSourceType.Deconstruct), Is.False);
            Assert.That(OptOutOverridePolicy.ExplicitOrderOverridesOptOut(HaulSourceType.Animal), Is.False);
        }

        [Test]
        public void ExactlyOneSourceOverrides_GuardsAgainstFutureEnumDrift()
        {
            // Lock the contract: precisely one source type (Strip) overrides the opt-out. If a new
            // HaulSourceType is added, this fails until its override behavior is deliberately decided.
            int overriding = 0;
            foreach (HaulSourceType t in Enum.GetValues(typeof(HaulSourceType)))
                if (OptOutOverridePolicy.ExplicitOrderOverridesOptOut(t))
                    overriding++;
            Assert.That(overriding, Is.EqualTo(1));
        }
    }
}
