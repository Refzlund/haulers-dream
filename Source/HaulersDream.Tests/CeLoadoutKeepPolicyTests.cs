using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CeLoadoutKeepPolicyTests
    {
        private static readonly object Beer = new object();
        private static readonly object WakeUp = new object();
        private static readonly object Medicine = new object();
        private static readonly object Weapon = new object();
        private static readonly object Ammo = new object();
        private static readonly object Drugs = new object();
        private static readonly object MedicineCategory = new object();
        private static readonly object AmmoCategory = new object();

        private static Dictionary<object, int> Allocate(
            CeLoadoutKeepPolicy.Stock[] stocks,
            params CeLoadoutKeepPolicy.Slot[] slots)
        {
            var output = new List<CeLoadoutKeepPolicy.Keep>();
            CeLoadoutKeepPolicy.Allocate(stocks, slots, Matches, output);
            var result = new Dictionary<object, int>();
            foreach (var keep in output)
                result[keep.Def] = keep.Count;
            return result;
        }

        private static bool Matches(object matcher, object def)
        {
            if (ReferenceEquals(matcher, Drugs))
                return ReferenceEquals(def, Beer) || ReferenceEquals(def, WakeUp);
            if (ReferenceEquals(matcher, MedicineCategory))
                return ReferenceEquals(def, Medicine);
            if (ReferenceEquals(matcher, AmmoCategory))
                return ReferenceEquals(def, Ammo);
            return false;
        }

        private static int CountOf(Dictionary<object, int> result, object def)
            => result.TryGetValue(def, out int count) ? count : 0;

        [Test]
        public void DropExcessGenericAloneContributesNoHdKeep()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, totalCount: 10, inventoryCount: 10),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, totalCount: 1, inventoryCount: 1),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10, CeLoadoutKeepPolicy.SlotMode.DropExcess));

            Assert.That(keep, Is.Empty,
                "GenericDrugs x10/dropExcess is a ceiling, not ten personal units for every matching drug");
        }

        [Test]
        public void DropExcessSlotDoesNotConsumeStockBeforeLaterPickupSlot()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 4, 4),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 4, 4),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 8, CeLoadoutKeepPolicy.SlotMode.DropExcess),
                CeLoadoutKeepPolicy.Slot.Exact(WakeUp, 2));

            Assert.That(CountOf(keep, Beer), Is.Zero,
                "a drop-only category ceiling contributes no HD keep of its own");
            Assert.That(CountOf(keep, WakeUp), Is.EqualTo(2),
                "skipping a drop-only slot must leave the ordered stock untouched for later refill slots");
        }

        [Test]
        public void PickupGeneric_UsesOneSharedBudgetAcrossDefs()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 7, 7),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 7, 7),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10));

            Assert.That(CountOf(keep, Beer), Is.EqualTo(7));
            Assert.That(CountOf(keep, WakeUp), Is.EqualTo(3));
        }

        [Test]
        public void PickupGeneric_RespectsCeStorageOrder()
        {
            var beerFirst = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 10, 10),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 1, 1),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10));
            var wakeFirst = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 1, 1),
                    new CeLoadoutKeepPolicy.Stock(Beer, 10, 10),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10));

            Assert.That(CountOf(beerFirst, Beer), Is.EqualTo(10));
            Assert.That(CountOf(beerFirst, WakeUp), Is.Zero);
            Assert.That(CountOf(wakeFirst, WakeUp), Is.EqualTo(1));
            Assert.That(CountOf(wakeFirst, Beer), Is.EqualTo(9));
        }

        [Test]
        public void PickupGeneric_UnderfilledKeepsOnlyUnitsActuallyHeld()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 4, 4),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 3, 3),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10));

            Assert.That(CountOf(keep, Beer) + CountOf(keep, WakeUp), Is.EqualTo(7));
        }

        [Test]
        public void AllocationRecomputesFromLiveStockWithoutStickyDefBinding()
        {
            var before = Allocate(
                new[] { new CeLoadoutKeepPolicy.Stock(Beer, 10, 10) },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10));
            var afterBeerGone = Allocate(
                new[] { new CeLoadoutKeepPolicy.Stock(WakeUp, 1, 1) },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 10));

            Assert.That(CountOf(before, Beer), Is.EqualTo(10));
            Assert.That(CountOf(afterBeerGone, WakeUp), Is.EqualTo(1));
        }

        [Test]
        public void ExactAndGenericSlotsConsumeTheSameRemainingListing()
        {
            var exactFirst = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 1, 1),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 1, 1),
                },
                CeLoadoutKeepPolicy.Slot.Exact(Beer, 1),
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 1));
            var genericFirst = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 1, 1),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 1, 1),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 1),
                CeLoadoutKeepPolicy.Slot.Exact(Beer, 1));

            Assert.That(CountOf(exactFirst, Beer), Is.EqualTo(1));
            Assert.That(CountOf(exactFirst, WakeUp), Is.EqualTo(1));
            Assert.That(CountOf(genericFirst, Beer), Is.EqualTo(1));
            Assert.That(CountOf(genericFirst, WakeUp), Is.Zero,
                "CE's greedy slot order must not be reordered into an idealized allocation");
        }

        [Test]
        public void EquipmentSatisfiesExactSlotBeforeInventoryDuplicate()
        {
            // CE storage contains two weapons, but only one is in the unloadable inventory; the other is equipped.
            var keep = Allocate(
                new[] { new CeLoadoutKeepPolicy.Stock(Weapon, totalCount: 2, inventoryCount: 1) },
                CeLoadoutKeepPolicy.Slot.Exact(Weapon, 1));

            Assert.That(CountOf(keep, Weapon), Is.Zero);
        }

        [Test]
        public void LoadedMagazineRoundsSatisfyPartOfAmmoSlot()
        {
            // 10 loaded + 20 loose, target 15 -> only five loose rounds are refill-protected.
            var keep = Allocate(
                new[] { new CeLoadoutKeepPolicy.Stock(Ammo, totalCount: 30, inventoryCount: 20) },
                CeLoadoutKeepPolicy.Slot.Exact(Ammo, 15));

            Assert.That(CountOf(keep, Ammo), Is.EqualTo(5));
        }

        [Test]
        public void ExternalRoundsSatisfyPartOfGenericAmmoSlot()
        {
            // 10 rounds are loaded in equipment/inventory weapons and 20 are loose. A generic target of 15
            // therefore protects only five loose rounds; generic allocation must use the same external-first
            // inventory projection as an exact slot.
            var keep = Allocate(
                new[] { new CeLoadoutKeepPolicy.Stock(Ammo, totalCount: 30, inventoryCount: 20) },
                CeLoadoutKeepPolicy.Slot.Generic(AmmoCategory, 15));

            Assert.That(CountOf(keep, Ammo), Is.EqualTo(5));
        }

        [Test]
        public void MultipleGenericSlotsCannotCoverOneUnitTwice()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 4, 4),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, 4, 4),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 3),
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 3));

            Assert.That(CountOf(keep, Beer), Is.EqualTo(4));
            Assert.That(CountOf(keep, WakeUp), Is.EqualTo(2));
        }

        [Test]
        public void DisjointGenericSlotsAllocateIndependently()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, 4, 4),
                    new CeLoadoutKeepPolicy.Stock(Medicine, 4, 4),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, 2),
                CeLoadoutKeepPolicy.Slot.Generic(MedicineCategory, 3));

            Assert.That(CountOf(keep, Beer), Is.EqualTo(2));
            Assert.That(CountOf(keep, Medicine), Is.EqualTo(3));
        }

        [Test]
        public void DropExcessExactAlsoContributesNoKeep()
        {
            var keep = Allocate(
                new[] { new CeLoadoutKeepPolicy.Stock(Beer, 5, 5) },
                CeLoadoutKeepPolicy.Slot.Exact(Beer, 5, CeLoadoutKeepPolicy.SlotMode.DropExcess));

            Assert.That(keep, Is.Empty);
        }

        [Test]
        public void InvalidCountsNeverCreateNegativeOrPhantomKeep()
        {
            var keep = Allocate(
                new[]
                {
                    new CeLoadoutKeepPolicy.Stock(Beer, totalCount: -2, inventoryCount: 5),
                    new CeLoadoutKeepPolicy.Stock(WakeUp, totalCount: 1, inventoryCount: -3),
                },
                CeLoadoutKeepPolicy.Slot.Generic(Drugs, -10));

            Assert.That(keep, Is.Empty);
        }

        [Test]
        public void NullOutputIsRejectedExplicitly()
        {
            Assert.Throws<ArgumentNullException>(() => CeLoadoutKeepPolicy.Allocate(
                Array.Empty<CeLoadoutKeepPolicy.Stock>(),
                Array.Empty<CeLoadoutKeepPolicy.Slot>(),
                Matches,
                null));
        }
    }
}
