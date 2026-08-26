using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BulkUnloadTransporterPolicyTests
    {
        // --- MayOffer (offer/run gate: contents × load lord × caravan) ---

        [Test]
        public void Offer_LandedShuttleWithCargo_Offered()
        {
            // The headline case: a landed transporter with items in the hold, no load running, not in a caravan.
            Assert.That(BulkUnloadTransporterPolicy.MayOffer(hasPullableContents: true, loadLordActive: false, inCaravan: false), Is.True);
        }

        [Test]
        public void Offer_EmptyHold_NotOffered()
        {
            Assert.That(BulkUnloadTransporterPolicy.MayOffer(hasPullableContents: false, loadLordActive: false, inCaravan: false), Is.False);
        }

        [Test]
        public void Offer_ActiveLoadLord_NotOffered()
        {
            // Items are being loaded INTO the group, vanilla's cancel-load owns that state.
            Assert.That(BulkUnloadTransporterPolicy.MayOffer(hasPullableContents: true, loadLordActive: true, inCaravan: false), Is.False);
        }

        [Test]
        public void Offer_InCaravan_NotOffered()
        {
            // Caravan packing owns the hold during gather/arrival.
            Assert.That(BulkUnloadTransporterPolicy.MayOffer(hasPullableContents: true, loadLordActive: false, inCaravan: true), Is.False);
        }

        [Test]
        public void Offer_PassengersOnly_NothingToPull()
        {
            // A shuttle holding only pawns has no pullable stacks -> never offered (pawns leave on their own).
            Assert.That(BulkUnloadTransporterPolicy.MayOffer(hasPullableContents: false, loadLordActive: true, inCaravan: true), Is.False);
        }
    }
}
