using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class UnloadPolicyTests
    {
        private static UnloadDecision Decide(
            bool eligible = true, int carried = 3, int inventory = 3, bool alreadyUnloading = false,
            bool forced = false, bool hasPendingWork = false, int ticksSinceYield = 1000, int grace = 60)
            => UnloadPolicy.Decide(eligible, carried, inventory, alreadyUnloading, forced, hasPendingWork, ticksSinceYield, grace);

        [Test]
        public void NormalLoadedPawn_Queues()
        {
            Assert.That(Decide(), Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void NotEligible_Skips()
        {
            Assert.That(Decide(eligible: false), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void NothingCarried_Skips()
        {
            Assert.That(Decide(carried: 0), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void EmptyInventory_Skips()
        {
            Assert.That(Decide(inventory: 0, carried: 0), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void AlreadyUnloading_Skips()
        {
            Assert.That(Decide(alreadyUnloading: true), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void TrackerHasMoreThanInventory_ClearsTracker()
        {
            // 5 tracked but only 2 actually in inventory => drifted
            Assert.That(Decide(carried: 5, inventory: 2), Is.EqualTo(UnloadDecision.ClearTracker));
        }

        [Test]
        public void WithinGrace_Skips()
        {
            Assert.That(Decide(ticksSinceYield: 30, grace: 60), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void PastGrace_Queues()
        {
            Assert.That(Decide(ticksSinceYield: 60, grace: 60), Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void Forced_BypassesGrace()
        {
            Assert.That(Decide(forced: true, ticksSinceYield: 0, grace: 60), Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void Forced_StillSkipsWhenNothingToUnload()
        {
            // forced can't conjure items
            Assert.That(Decide(forced: true, carried: 0), Is.EqualTo(UnloadDecision.Skip));
            Assert.That(Decide(forced: true, inventory: 0, carried: 0), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void ZeroGrace_NeverBlocks()
        {
            Assert.That(Decide(ticksSinceYield: 0, grace: 0), Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void SyncCheckBeatsGrace()
        {
            // a drifted tracker clears even within the grace window
            Assert.That(Decide(carried: 5, inventory: 2, ticksSinceYield: 0, grace: 60),
                Is.EqualTo(UnloadDecision.ClearTracker));
        }

        // --- pending-work guard: an automatic unload must not preempt a queued harvest route ---

        [Test]
        public void PendingWork_NonForced_Skips()
        {
            // The bug repro: a loaded, eligible pawn past grace BUT with queued harvests -> don't unload.
            Assert.That(Decide(hasPendingWork: true, ticksSinceYield: 1000, grace: 60),
                Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void PendingWork_Forced_Queues()
        {
            // The gizmo / full-trigger / debug override pending work and unload now.
            Assert.That(Decide(forced: true, hasPendingWork: true), Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void NoPendingWork_NonForced_Queues()
        {
            // End-of-run / idle: the queue has drained -> the consolidated unload runs.
            Assert.That(Decide(hasPendingWork: false, ticksSinceYield: 1000), Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void PendingWork_Drift_StillClearsTracker()
        {
            // Tracker drift self-heals even mid-route (drift check precedes the pending-work skip).
            Assert.That(Decide(carried: 5, inventory: 2, hasPendingWork: true),
                Is.EqualTo(UnloadDecision.ClearTracker));
        }

        [Test]
        public void PendingWork_AlreadyUnloading_Skips()
        {
            // The already-unloading short-circuit precedes the new branch -> still Skip (no double-queue).
            Assert.That(Decide(alreadyUnloading: true, hasPendingWork: true), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void PendingWork_NothingCarried_Skips()
        {
            Assert.That(Decide(carried: 0, hasPendingWork: true), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void PendingWork_Forced_WithinGrace_Queues()
        {
            // Forced bypasses both the pending-work guard and the grace window.
            Assert.That(Decide(forced: true, hasPendingWork: true, ticksSinceYield: 0, grace: 60),
                Is.EqualTo(UnloadDecision.Queue));
        }

        [Test]
        public void NoPendingWork_WithinGrace_NonForced_Skips()
        {
            // Grace still applies independently when there is no pending work.
            Assert.That(Decide(hasPendingWork: false, ticksSinceYield: 30, grace: 60),
                Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void PendingWork_NonForced_ZeroGrace_Skips()
        {
            // The pending-work skip is independent of grace (fires even with grace disabled).
            Assert.That(Decide(hasPendingWork: true, ticksSinceYield: 0, grace: 0),
                Is.EqualTo(UnloadDecision.Skip));
        }

        // --- HasPendingRealWork: the mod's own housekeeping jobs must NOT count as pending work ---

        private const string SelfPickup = "HaulersDream_SelfPickup";
        private const string Unload = "HaulersDream_UnloadInventory";

        [Test]
        public void RealWork_RealHarvestJob_IsPending()
        {
            Assert.That(UnloadPolicy.HasPendingRealWork(new[] { "HarvestDesignated" }, SelfPickup, Unload), Is.True);
        }

        [Test]
        public void RealWork_OnlySelfPickup_IsNotPending()
        {
            // The livelock guard: a queue holding ONLY the mod's self-pickup is not "pending work",
            // so the auto-unload still fires (and breaks the strict-mode stranding loop).
            Assert.That(UnloadPolicy.HasPendingRealWork(new[] { SelfPickup }, SelfPickup, Unload), Is.False);
        }

        [Test]
        public void RealWork_OnlyHousekeeping_IsNotPending()
        {
            Assert.That(UnloadPolicy.HasPendingRealWork(new[] { SelfPickup, Unload }, SelfPickup, Unload), Is.False);
        }

        [Test]
        public void RealWork_HarvestMixedWithSelfPickup_IsPending()
        {
            // The screenshot scenario mid-route: [selfpickup, harvest2, ...] -> still pending (defer the unload).
            Assert.That(UnloadPolicy.HasPendingRealWork(new[] { SelfPickup, "HarvestDesignated" }, SelfPickup, Unload), Is.True);
        }

        [Test]
        public void RealWork_EmptyQueue_IsNotPending()
        {
            Assert.That(UnloadPolicy.HasPendingRealWork(new string[0], SelfPickup, Unload), Is.False);
            Assert.That(UnloadPolicy.HasPendingRealWork(null, SelfPickup, Unload), Is.False);
        }

        [Test]
        public void RealWork_NullDefNamesSkipped()
        {
            Assert.That(UnloadPolicy.HasPendingRealWork(new[] { (string)null, SelfPickup }, SelfPickup, Unload), Is.False);
            Assert.That(UnloadPolicy.HasPendingRealWork(new[] { (string)null, "Mine" }, SelfPickup, Unload), Is.True);
        }
    }
}
