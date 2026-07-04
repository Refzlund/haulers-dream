using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CarryCapacityPolicyTests
    {
        // ── player mechs: base on the UI CarryingCapacity stat (issue #1) ──────────────────────────────
        [Test]
        public void Mech_VanillaLifter_UsesCarryingCapacityStat_Not24point5()
        {
            // A vanilla lifter: UI carrying capacity 52, MassUtility.Capacity 24.5. The whole point of #1.
            float cap = CarryCapacityPolicy.BaseCapacity(
                isPlayerMech: true, ceActive: false,
                statCarryingCapacity: 52f, massUtilityCapacity: 24.5f, mechMultiplier: 1f);
            Assert.That(cap, Is.EqualTo(52f));
        }

        [Test]
        public void Mech_ModdedLoader_TracksItsLargerStat()
        {
            // A modded advanced loader: UI carrying capacity 158, same tiny MassUtility.Capacity 24.5.
            float cap = CarryCapacityPolicy.BaseCapacity(
                isPlayerMech: true, ceActive: false,
                statCarryingCapacity: 158f, massUtilityCapacity: 24.5f, mechMultiplier: 1f);
            Assert.That(cap, Is.EqualTo(158f));
        }

        [Test]
        public void Mech_MultiplierLayersOnTopOfTheStat()
        {
            float cap = CarryCapacityPolicy.BaseCapacity(
                isPlayerMech: true, ceActive: false,
                statCarryingCapacity: 52f, massUtilityCapacity: 24.5f, mechMultiplier: 2f);
            Assert.That(cap, Is.EqualTo(104f));
        }

        [Test]
        public void Mech_NonPositiveMultiplier_TreatedAsOne()
        {
            Assert.That(CarryCapacityPolicy.BaseCapacity(true, false, 52f, 24.5f, 0f), Is.EqualTo(52f));
            Assert.That(CarryCapacityPolicy.BaseCapacity(true, false, 52f, 24.5f, -3f), Is.EqualTo(52f));
        }

        // ── humanlikes / animals: unchanged (MassUtility.Capacity) ─────────────────────────────────────
        [Test]
        public void NonMech_KeepsMassUtilityCapacity_IgnoringStatAndMultiplier()
        {
            float cap = CarryCapacityPolicy.BaseCapacity(
                isPlayerMech: false, ceActive: false,
                statCarryingCapacity: 75f, massUtilityCapacity: 35f, mechMultiplier: 5f);
            Assert.That(cap, Is.EqualTo(35f));
        }

        // ── Combat Extended owns the model: always MassUtility.Capacity, even for a mech ───────────────
        [Test]
        public void CE_Active_AlwaysDefersToMassUtilityCapacity_EvenForMech()
        {
            float cap = CarryCapacityPolicy.BaseCapacity(
                isPlayerMech: true, ceActive: true,
                statCarryingCapacity: 158f, massUtilityCapacity: 40f, mechMultiplier: 3f);
            Assert.That(cap, Is.EqualTo(40f));
        }

        // ── #118: the CE mech-carry override is OPT-IN above ×1.0 (default = inert, CE owns carry weight) ──
        [Test]
        public void CeMechCarryOverride_InertAtDefaultMultiplier()
        {
            // ×1.0 (default) and any value at or below it → HD does not touch the mech's CE carry weight.
            Assert.That(CarryCapacityPolicy.CeMechCarryOverrideActive(1.0f), Is.False);
            Assert.That(CarryCapacityPolicy.CeMechCarryOverrideActive(0.5f), Is.False);
        }

        [Test]
        public void CeMechCarryOverride_ActiveOnlyWhenRaisedAboveDefault()
        {
            // The player raised the slider → HD sets the mech's CE carry weight to carrying capacity × multiplier.
            Assert.That(CarryCapacityPolicy.CeMechCarryOverrideActive(1.1f), Is.True);
            Assert.That(CarryCapacityPolicy.CeMechCarryOverrideActive(5.0f), Is.True);
        }
    }
}
