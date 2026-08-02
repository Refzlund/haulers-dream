using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the honesty rules behind issue #243: when another mod is doing the ingredient gathering, HD's own
    /// controls must admit it rather than claim credit for a behaviour they cannot stop.
    /// </summary>
    [TestFixture]
    public class GatherOwnershipPolicyTests
    {
        // --- the reporter's exact configuration (#243) ---

        [Test] // Common Sense gathering with HD's own gather also enabled: the switch does NOT govern what the
               // player is watching, and the notice must name the foreign mod rather than HD's own settings.
        public void ForeignGathererWithHdGatherOn_IsTheReportedCase()
        {
            Assert.That(GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(foreignOwnsGatherFlow: true, plainGatherEnabled: true),
                Is.False);
            Assert.That(GatherOwnershipPolicy.ResolveNotice(foreignModGathersNow: true, plainGatherEnabled: true),
                Is.EqualTo(BenchGatherNotice.ForeignModGathers));
        }

        [Test] // The same player then turns HD's crafting checkboxes off and the collecting continues: still the
               // foreign mod's doing, so the notice must not switch to blaming HD's own (now irrelevant) setting.
        public void ForeignGathererWins_OverGlobalGatherOff()
            => Assert.That(GatherOwnershipPolicy.ResolveNotice(foreignModGathersNow: true, plainGatherEnabled: false),
                Is.EqualTo(BenchGatherNotice.ForeignModGathers));

        // --- the other two outcomes ---

        [Test] // HD owns the gather and it is enabled: the control means exactly what its description says.
        public void HdOwnsGatherAndItIsOn_NoNotice()
        {
            Assert.That(GatherOwnershipPolicy.ResolveNotice(foreignModGathersNow: false, plainGatherEnabled: true),
                Is.EqualTo(BenchGatherNotice.None));
            Assert.That(GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(foreignOwnsGatherFlow: false, plainGatherEnabled: true),
                Is.True);
        }

        [Test] // No foreign gatherer, but the player switched HD's one-sweep gather off globally: the bench switch
               // still governs batch crafting there, so it is not inert — it just no longer governs the one-sweep
               // gather its description describes.
        public void HdGatherSwitchedOffGlobally_GlobalGatherOff()
        {
            Assert.That(GatherOwnershipPolicy.ResolveNotice(foreignModGathersNow: false, plainGatherEnabled: false),
                Is.EqualTo(BenchGatherNotice.GlobalGatherOff));
            Assert.That(GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(foreignOwnsGatherFlow: false, plainGatherEnabled: false),
                Is.False);
        }

        // --- the two entry points must never disagree ---

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GovernsIsExactlyTheNoNoticeCase(bool foreign, bool plainOn)
            => Assert.That(GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(foreign, plainOn),
                Is.EqualTo(GatherOwnershipPolicy.ResolveNotice(foreign, plainOn) == BenchGatherNotice.None));
    }
}
