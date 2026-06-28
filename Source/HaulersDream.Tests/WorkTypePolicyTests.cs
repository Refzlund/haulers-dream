using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class WorkTypePolicyTests
    {
        // A distinct YieldBehavior per argument slot so a wrong-slot wiring bug is caught: each call passes a
        // unique value for the type under test and a DIFFERENT value everywhere else, then asserts the returned
        // value is exactly the one routed to that type's slot.
        [TestCase(HaulSourceType.Harvest)]
        [TestCase(HaulSourceType.Logging)]
        [TestCase(HaulSourceType.Mining)]
        [TestCase(HaulSourceType.Chunks)]
        [TestCase(HaulSourceType.DeepDrill)]
        [TestCase(HaulSourceType.Deconstruct)]
        [TestCase(HaulSourceType.Animal)]
        [TestCase(HaulSourceType.Strip)]
        [TestCase(HaulSourceType.Uninstall)]
        [TestCase(HaulSourceType.Fishing)]
        public void BehaviorFor_RoutesEachTypeToItsOwnSlot(HaulSourceType type)
        {
            // The slot for `type` gets DirectToInventory; every other slot gets Disabled. The result must be the
            // slot's value, proving no cross-wiring between adjacent switch arms.
            YieldBehavior S(HaulSourceType t) => t == type ? YieldBehavior.DirectToInventory : YieldBehavior.Disabled;
            var got = WorkTypePolicy.BehaviorFor(type,
                S(HaulSourceType.Harvest), S(HaulSourceType.Logging), S(HaulSourceType.Mining), S(HaulSourceType.Chunks),
                S(HaulSourceType.DeepDrill), S(HaulSourceType.Deconstruct), S(HaulSourceType.Animal), S(HaulSourceType.Strip),
                S(HaulSourceType.Uninstall), S(HaulSourceType.Fishing));
            Assert.That(got, Is.EqualTo(YieldBehavior.DirectToInventory), $"{type} must read its own slot");
        }

        [Test]
        public void BehaviorFor_FishingReadsTheFishingSlot()
        {
            // Explicit per-type assertion (mirrors the per-slot pattern above): Fishing must return exactly the
            // `fishing` argument and nothing else, even when every other slot carries a different value.
            var got = WorkTypePolicy.BehaviorFor(HaulSourceType.Fishing,
                YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled,
                YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled,
                YieldBehavior.Disabled, fishing: YieldBehavior.DirectToInventory);
            Assert.That(got, Is.EqualTo(YieldBehavior.DirectToInventory));
        }

        [Test]
        public void BehaviorFor_PassesTheValueThrough()
        {
            // Each type returns whatever value sits in its slot (here: all DropThenHaul).
            foreach (HaulSourceType t in System.Enum.GetValues(typeof(HaulSourceType)))
                Assert.That(WorkTypePolicy.BehaviorFor(t,
                    YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul,
                    YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul,
                    YieldBehavior.DropThenHaul, YieldBehavior.DropThenHaul), Is.EqualTo(YieldBehavior.DropThenHaul), t.ToString());
        }

        [Test]
        public void BehaviorFor_AllDisabled_EveryTypeDisabled()
        {
            foreach (HaulSourceType t in System.Enum.GetValues(typeof(HaulSourceType)))
                Assert.That(WorkTypePolicy.BehaviorFor(t,
                    YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled,
                    YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled, YieldBehavior.Disabled,
                    YieldBehavior.Disabled, YieldBehavior.Disabled), Is.EqualTo(YieldBehavior.Disabled), t.ToString());
        }

        [Test]
        public void BehaviorFor_UnknownType_ReturnsDisabled()
        {
            // An out-of-range cast (no such enum member) must hit the default arm -> Disabled (safe).
            var bogus = (HaulSourceType)999;
            Assert.That(WorkTypePolicy.BehaviorFor(bogus,
                YieldBehavior.DirectToInventory, YieldBehavior.DirectToInventory, YieldBehavior.DirectToInventory,
                YieldBehavior.DirectToInventory, YieldBehavior.DirectToInventory, YieldBehavior.DirectToInventory,
                YieldBehavior.DirectToInventory, YieldBehavior.DirectToInventory, YieldBehavior.DirectToInventory,
                YieldBehavior.DirectToInventory),
                Is.EqualTo(YieldBehavior.Disabled));
        }

        // ----- the pure one-time legacy migration mapping (shared by ExposeData's MigrateLegacyYieldSettings) -----

        [Test]
        public void MapLegacyYield_DisabledToggle_AlwaysDisabled()
        {
            // A false toggle maps to Disabled regardless of the old global pickup mode or the force-drop flag.
            Assert.That(WorkTypePolicy.MapLegacyYield(false, PickupMode.DropThenHaul, false), Is.EqualTo(YieldBehavior.Disabled));
            Assert.That(WorkTypePolicy.MapLegacyYield(false, PickupMode.DirectToInventory, false), Is.EqualTo(YieldBehavior.Disabled));
            Assert.That(WorkTypePolicy.MapLegacyYield(false, PickupMode.DirectToInventory, true), Is.EqualTo(YieldBehavior.Disabled));
        }

        [Test]
        public void MapLegacyYield_EnabledToggle_FollowsGlobalPickupMode()
        {
            Assert.That(WorkTypePolicy.MapLegacyYield(true, PickupMode.DropThenHaul, false), Is.EqualTo(YieldBehavior.DropThenHaul));
            Assert.That(WorkTypePolicy.MapLegacyYield(true, PickupMode.DirectToInventory, false), Is.EqualTo(YieldBehavior.DirectToInventory));
        }

        [Test]
        public void MapLegacyYield_ForceDropOnly_StripIgnoresGlobalMode()
        {
            // The Strip special case: enabled + forceDropOnly -> DropThenHaul even if the global mode was Direct.
            Assert.That(WorkTypePolicy.MapLegacyYield(true, PickupMode.DirectToInventory, true), Is.EqualTo(YieldBehavior.DropThenHaul));
            Assert.That(WorkTypePolicy.MapLegacyYield(true, PickupMode.DropThenHaul, true), Is.EqualTo(YieldBehavior.DropThenHaul));
        }

        [Test]
        public void MapLegacyYield_LegacyDefaults_MapToNewDefault()
        {
            // The old defaults (toggle on, global pickup DropThenHaul) must map to the new field default
            // (DropThenHaul) for both the normal and the force-drop categories — so a fresh state is unchanged.
            Assert.That(WorkTypePolicy.MapLegacyYield(true, PickupMode.DropThenHaul, false), Is.EqualTo(YieldBehavior.DropThenHaul));
            Assert.That(WorkTypePolicy.MapLegacyYield(true, PickupMode.DropThenHaul, true), Is.EqualTo(YieldBehavior.DropThenHaul));
        }
    }
}
