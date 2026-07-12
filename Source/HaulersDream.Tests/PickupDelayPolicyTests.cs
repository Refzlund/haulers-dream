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

        // ── ShouldPause: WHICH pickups the delay applies to (the vanilla-faithful scope) ────────────────

        // (context, onHauling, onLoading) -> should the pause show?
        // ManualCarry: ALWAYS, whatever the toggles (vanilla's own delayed pickup order).
        [TestCase(PickupDelayContext.ManualCarry, false, false, ExpectedResult = true)]
        [TestCase(PickupDelayContext.ManualCarry, true, true, ExpectedResult = true)]
        [TestCase(PickupDelayContext.ManualCarry, false, true, ExpectedResult = true)]
        // AutoHaul: only when hauling is opted in; the loading toggle must NOT affect it.
        [TestCase(PickupDelayContext.AutoHaul, false, false, ExpectedResult = false)]
        [TestCase(PickupDelayContext.AutoHaul, true, false, ExpectedResult = true)]
        [TestCase(PickupDelayContext.AutoHaul, false, true, ExpectedResult = false)]
        // Loading: only when loading is opted in; the hauling toggle must NOT affect it.
        [TestCase(PickupDelayContext.Loading, false, false, ExpectedResult = false)]
        [TestCase(PickupDelayContext.Loading, false, true, ExpectedResult = true)]
        [TestCase(PickupDelayContext.Loading, true, false, ExpectedResult = false)]
        public bool ShouldPause_Matrix(PickupDelayContext context, bool onHauling, bool onLoading)
            => PickupDelayPolicy.ShouldPause(context, onHauling, onLoading);

        [Test]
        public void ShouldPause_DefaultScope_IsVanillaFaithful_OnlyManualCarry()
        {
            // Both toggles off (the shipped default): only the deliberate carry order pauses; automatic hauling
            // and loading are instant, exactly like vanilla (floor-removal debris is swept without a delay).
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.ManualCarry, false, false), Is.True);
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.AutoHaul, false, false), Is.False);
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.Loading, false, false), Is.False);
        }

        [Test]
        public void ShouldPause_BothOptIns_RestorePauseEverywhere()
        {
            // Turning both opt-ins on is the pre-scope "delay everywhere" feel: every context pauses.
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.ManualCarry, true, true), Is.True);
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.AutoHaul, true, true), Is.True);
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.Loading, true, true), Is.True);
        }

        // DirectHarvest: an isolated ordered harvest collected on the spot. Paces on its OWN opt-in alone
        // (independent of the hauling/loading toggles); the base magnitude (ticks > 0) is gated separately.
        // (context, onHauling, onLoading, onDirectHarvest) -> should the pause show?
        [TestCase(false, false, false, ExpectedResult = false)] // direct-harvest off -> instant
        [TestCase(true, false, false, ExpectedResult = false)]  // direct-harvest off, hauling on -> still instant
        [TestCase(false, false, true, ExpectedResult = true)]   // direct-harvest on, hauling off -> pace (independent)
        [TestCase(true, true, true, ExpectedResult = true)]     // direct-harvest on -> pace
        public bool ShouldPause_DirectHarvest_FollowsItsOwnToggle(bool onHauling, bool onLoading, bool onDirectHarvest)
            => PickupDelayPolicy.ShouldPause(PickupDelayContext.DirectHarvest, onHauling, onLoading, onDirectHarvest);

        [Test]
        public void ShouldPause_DirectHarvest_HaulingAndLoadingIrrelevant()
        {
            // Only the direct-harvest opt-in decides a direct harvest; the hauling and loading toggles must not.
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.DirectHarvest, true, true, false), Is.False);
            Assert.That(PickupDelayPolicy.ShouldPause(PickupDelayContext.DirectHarvest, false, false, true), Is.True);
        }
    }
}
