using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class FitToHaulPolicyTests
    {
        // The WYU-parity default threshold (Source/OpportunityDetour.cs:91 -> Mod.cs:109 `> 0.001f`).
        const float Threshold = 0.001f;

        static bool Fit(bool gateEnabled, float bleedRate, float threshold = Threshold)
            => FitToHaulPolicy.FitToStartHaul(gateEnabled, bleedRate, threshold);

        [Test]
        public void GateOff_AlwaysFit()
        {
            // With the gate disabled, behavior is byte-identical to no health gate at all: always fit,
            // even for a pawn hemorrhaging far above the threshold.
            Assert.That(Fit(gateEnabled: false, bleedRate: 0f), Is.True);
            Assert.That(Fit(gateEnabled: false, bleedRate: 0.001f), Is.True);
            Assert.That(Fit(gateEnabled: false, bleedRate: 5f), Is.True);
        }

        [Test]
        public void NotBleeding_Fit()
        {
            // No bleeding -> fit to start a haul.
            Assert.That(Fit(gateEnabled: true, bleedRate: 0f), Is.True);
        }

        [Test]
        public void ExactlyAtThreshold_Fit()
        {
            // STRICT >: a pawn bleeding exactly AT the threshold is still fit (WYU parity — `> 0.001f`).
            Assert.That(Fit(gateEnabled: true, bleedRate: 0.001f), Is.True);
        }

        [Test]
        public void AboveThreshold_NotFit()
        {
            // One step past the threshold -> stand down (get treated instead of starting a sweep).
            Assert.That(Fit(gateEnabled: true, bleedRate: 0.0011f), Is.False);
            Assert.That(Fit(gateEnabled: true, bleedRate: 1f), Is.False);
        }

        [Test]
        public void JustBelowThreshold_Fit()
        {
            // Below the bar (a trivial scratch) is fit — only "bleeding badly" gates intake.
            Assert.That(Fit(gateEnabled: true, bleedRate: 0.0009f), Is.True);
        }

        [Test]
        public void CustomThreshold_StrictGreaterThan()
        {
            // The threshold is configurable; the strict > rule holds at any value.
            Assert.That(Fit(gateEnabled: true, bleedRate: 0.5f, threshold: 0.5f), Is.True);  // at the bar -> fit
            Assert.That(Fit(gateEnabled: true, bleedRate: 0.51f, threshold: 0.5f), Is.False); // past the bar -> not fit
        }
    }
}
