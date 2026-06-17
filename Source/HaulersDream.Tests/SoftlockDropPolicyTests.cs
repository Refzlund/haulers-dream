using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SoftlockDropPolicyTests
    {
        // A capable hauler (nothing disabled, not a stuck mech) keeps its cargo: it will unload on its own,
        // so never yank items out from under it.
        [Test]
        public void CapableHauler_NoDrop()
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: false,
                isMech: false, mechState: MechState.None,
                taggedCount: 3, runningHdJob: false), Is.False);
        }

        // Work tag disabled (e.g. an incapable-of-hauling pawn) → stranded cargo → drop.
        [Test]
        public void WorkDisabledHolder_Drops()
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: true,
                isMech: false, mechState: MechState.None,
                taggedCount: 1, runningHdJob: false), Is.True);
        }

        // REGRESSION (fix/drop-scoops): a Hauling work-type PRIORITY of 0 is NOT incapability. A pawn the player
        // set to "never haul" (a dedicated grower/crafter) still scoops its yields and still delivers them via
        // HD's own unload paths (which don't use the vanilla Hauling work giver), so its cargo is never stranded.
        // Force-dropping it caused the "pawn drops scooped items while it keeps working (e.g. sowing)" bug.
        // Priority is no longer an input to the policy, so a priority-0-but-capable pawn is just a capable hauler.
        [Test]
        public void PriorityZeroButCapable_NoDrop()
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: false,
                isMech: false, mechState: MechState.None,
                taggedCount: 5, runningHdJob: false), Is.False);
        }

        // A mech that is charging / self-shutdown / dormant holding tagged cargo → drop.
        [TestCase(MechState.Charging)]
        [TestCase(MechState.SelfShutdown)]
        [TestCase(MechState.Dormant)]
        public void StuckMechHolder_Drops(MechState state)
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: false,
                isMech: true, mechState: state,
                taggedCount: 2, runningHdJob: false), Is.True);
        }

        // An awake, working mech (MechState.None) is a capable hauler → no drop.
        [Test]
        public void AwakeWorkingMech_NoDrop()
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: false,
                isMech: true, mechState: MechState.None,
                taggedCount: 4, runningHdJob: false), Is.False);
        }

        // A mech-state classification is IGNORED for a non-mech: even a stray Charging value on a non-mech
        // pawn must not trigger a drop (the runtime only ever sets a non-None state for actual mechs, but the
        // policy is robust to a bad pairing).
        [TestCase(MechState.Charging)]
        [TestCase(MechState.SelfShutdown)]
        [TestCase(MechState.Dormant)]
        public void NonMechWithMechState_Ignored(MechState state)
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: false,
                isMech: false, mechState: state,
                taggedCount: 2, runningHdJob: false), Is.False);
        }

        // Running an HD load/unload/cleanup job → never drop, even when otherwise eligible (the job owns the
        // cargo and will resolve it; pulling items mid-job would corrupt the job's plan).
        [Test]
        public void RunningHdJob_NoDrop_EvenWhenEligible()
        {
            // Work disabled AND a stuck mech — every "incapable" signal is set, but the live HD job wins.
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: true,
                isMech: true, mechState: MechState.SelfShutdown,
                taggedCount: 7, runningHdJob: true), Is.False);
        }

        // Zero tagged items → nothing to free → no drop, even for an incapable pawn.
        [TestCase(0)]
        [TestCase(-1)]
        public void ZeroTagged_NoDrop(int count)
        {
            Assert.That(SoftlockDropPolicy.ShouldDrop(
                haulingDisabled: true,
                isMech: true, mechState: MechState.Dormant,
                taggedCount: count, runningHdJob: false), Is.False);
        }

        // The "incapable" classification, isolated from the tagged-count / running-job guards.
        [Test]
        public void IsHaulIncapable_DecisionTable()
        {
            // Fully capable.
            Assert.That(SoftlockDropPolicy.IsHaulIncapable(false, false, MechState.None), Is.False);
            Assert.That(SoftlockDropPolicy.IsHaulIncapable(false, true, MechState.None), Is.False, "awake mech is capable");
            // Each incapable signal independently flips it true.
            Assert.That(SoftlockDropPolicy.IsHaulIncapable(true, false, MechState.None), Is.True);
            Assert.That(SoftlockDropPolicy.IsHaulIncapable(false, true, MechState.Charging), Is.True);
            // A mech-state on a non-mech does not count.
            Assert.That(SoftlockDropPolicy.IsHaulIncapable(false, false, MechState.Dormant), Is.False);
        }
    }
}
