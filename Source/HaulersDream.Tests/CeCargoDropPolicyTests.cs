using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CeCargoDropPolicyTests
    {
        [Test]
        public void TaggedBeerDefersDifferentCeSelectedWakeUpStack()
        {
            // The pending HD cargo and CE-selected excess intentionally represent different defs. The policy has
            // no exact-tag-membership input, because GenericDrugs x10 is one cross-def category ceiling.
            bool pendingTaggedBeer = true;
            bool ceSelectedUntaggedWakeUpStillInInventory = true;

            Assert.That(CeCargoDropPolicy.ShouldVetoExcessDrop(
                ceSelectedExcess: true,
                unloadEverything: false,
                selectedStackStillInInventory: ceSelectedUntaggedWakeUpStillInInventory,
                hasAnyUnloadableHdCargo: pendingTaggedBeer), Is.True);
        }

        [TestCase(false, false, true, true)]
        [TestCase(true, true, true, true)]
        [TestCase(true, false, false, true)]
        [TestCase(true, false, true, false)]
        public void GuardReleasesOutsideTemporaryCargoWindow(
            bool ceSelectedExcess,
            bool unloadEverything,
            bool selectedStackStillInInventory,
            bool hasAnyUnloadableHdCargo)
        {
            Assert.That(CeCargoDropPolicy.ShouldVetoExcessDrop(
                ceSelectedExcess,
                unloadEverything,
                selectedStackStillInInventory,
                hasAnyUnloadableHdCargo), Is.False);
        }
    }
}
