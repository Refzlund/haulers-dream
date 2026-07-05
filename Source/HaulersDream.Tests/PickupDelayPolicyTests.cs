using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class PickupDelayPolicyTests
    {
        // ── TicksPerStack: the clamp from a raw (untrusted) config value to the effective wait ─────────

        [Test]
        public void Zero_IsInstant()
        {
            Assert.That(PickupDelayPolicy.TicksPerStack(0), Is.EqualTo(0));
        }

        [Test]
        public void Negative_ClampsToInstant()
        {
            // Only reachable via a hand-edited config; must degrade to instant, never a negative toil duration.
            Assert.That(PickupDelayPolicy.TicksPerStack(-120), Is.EqualTo(0));
        }

        [Test]
        public void OneTick_PassesThrough()
        {
            // The smallest non-instant value is honored as-is (boundary just above the instant gate).
            Assert.That(PickupDelayPolicy.TicksPerStack(1), Is.EqualTo(1));
        }

        [Test]
        public void Vanilla_PassesThroughUnchanged()
        {
            Assert.That(PickupDelayPolicy.TicksPerStack(PickupDelayPolicy.VanillaDelayTicks),
                Is.EqualTo(PickupDelayPolicy.VanillaDelayTicks));
        }

        [Test]
        public void SliderMax_PassesThroughUnchanged()
        {
            Assert.That(PickupDelayPolicy.TicksPerStack(PickupDelayPolicy.SliderMaxTicks),
                Is.EqualTo(PickupDelayPolicy.SliderMaxTicks));
        }

        [Test]
        public void CeilingBoundary_PassesThrough_JustAboveClamps()
        {
            Assert.That(PickupDelayPolicy.TicksPerStack(PickupDelayPolicy.MaxDelayTicks),
                Is.EqualTo(PickupDelayPolicy.MaxDelayTicks));
            Assert.That(PickupDelayPolicy.TicksPerStack(PickupDelayPolicy.MaxDelayTicks + 1),
                Is.EqualTo(PickupDelayPolicy.MaxDelayTicks));
        }

        [Test]
        public void AbsurdConfigValue_ClampsToCeiling()
        {
            // "Extra zero" typo in the config XML: 6000 ticks must degrade to the 600-tick ceiling, not a
            // multi-minute stall per stack.
            Assert.That(PickupDelayPolicy.TicksPerStack(6000), Is.EqualTo(PickupDelayPolicy.MaxDelayTicks));
        }

        // ── The documented constants: lock the vanilla evidence and the slider/ceiling relationship ────

        [Test]
        public void VanillaConstant_MatchesDecompiledFloatMenuValue()
        {
            // FloatMenuOptionProvider_PickUpItem sets job.takeInventoryDelay = 120 in all three player
            // "Pick up" options (decompiled 1.6). If this ever changes upstream, the default should be
            // re-verified, not silently drifted.
            Assert.That(PickupDelayPolicy.VanillaDelayTicks, Is.EqualTo(120));
        }

        [Test]
        public void Slider_CoversVanilla_AndStaysWithinCeiling()
        {
            // The UI must be able to land exactly on the vanilla value, and must never offer a value the
            // clamp would silently reduce.
            Assert.That(PickupDelayPolicy.SliderMaxTicks, Is.GreaterThanOrEqualTo(PickupDelayPolicy.VanillaDelayTicks));
            Assert.That(PickupDelayPolicy.SliderMaxTicks, Is.LessThanOrEqualTo(PickupDelayPolicy.MaxDelayTicks));
        }
    }
}
