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
        public void Forced_WhileAlreadyUnloading_StillSkips()
        {
            // Gizmo spam / repeated full-trigger hits must never double-queue the unload pass.
            Assert.That(Decide(forced: true, alreadyUnloading: true), Is.EqualTo(UnloadDecision.Skip));
        }

        [Test]
        public void TagsWithEmptyInventory_ClearsTracker()
        {
            // The worst desync: tags remain but the inventory is empty. Must prune (a stale tag would
            // otherwise be unprunable forever — a permanent phantom "Unload now" gizmo), not skip.
            Assert.That(Decide(carried: 3, inventory: 0), Is.EqualTo(UnloadDecision.ClearTracker));
        }

        [Test]
        public void Forced_DoesNotBypassDriftPrune()
        {
            // Even a forced unload must self-heal a drifted tracker first, never queue against phantom tags.
            Assert.That(Decide(forced: true, carried: 5, inventory: 2), Is.EqualTo(UnloadDecision.ClearTracker));
        }

        [Test]
        public void FullTrigger_TruthTable()
        {
            // The hit-the-carry-ceiling trigger: forced unload only when NOT strict AND auto-unload is on.
            // (Strict mode keeps working and leaves the surplus; markForUnload off means manual-only.)
            Assert.That(UnloadPolicy.FullTriggerAllowed(strictCarryWeight: false, markForUnload: true), Is.True);
            Assert.That(UnloadPolicy.FullTriggerAllowed(strictCarryWeight: true, markForUnload: true), Is.False);
            Assert.That(UnloadPolicy.FullTriggerAllowed(strictCarryWeight: false, markForUnload: false), Is.False);
            Assert.That(UnloadPolicy.FullTriggerAllowed(strictCarryWeight: true, markForUnload: false), Is.False);
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

        // --- EndOfRunUnloadAllowed: the work scan came up dry for a loaded pawn -> unload before
        // recreation/idle. Each gate is pinned individually off a passing baseline. ---

        private static bool EndOfRun(
            bool markForUnload = true, bool eligible = true, bool drafted = false,
            int tracked = 3, bool anyUnloadable = true, bool alreadyUnloading = false,
            int sinceIssue = 1000, int cooldown = 250)
            => UnloadPolicy.EndOfRunUnloadAllowed(markForUnload, eligible, drafted,
                tracked, anyUnloadable, alreadyUnloading, sinceIssue, cooldown);

        [Test]
        public void EndOfRun_LoadedPawnWorkDry_Allows()
        {
            Assert.That(EndOfRun(), Is.True);
        }

        [Test]
        public void EndOfRun_AutoUnloadOff_Blocks()
        {
            // markForUnload off = gizmo-only unloading; the think-tree trigger must stay silent.
            Assert.That(EndOfRun(markForUnload: false), Is.False);
        }

        [Test]
        public void EndOfRun_IneligibleOrDrafted_Blocks()
        {
            Assert.That(EndOfRun(eligible: false), Is.False);
            Assert.That(EndOfRun(drafted: true), Is.False);
        }

        [Test]
        public void EndOfRun_NothingTracked_Blocks()
        {
            Assert.That(EndOfRun(tracked: 0), Is.False);
        }

        [Test]
        public void EndOfRun_NothingUnloadable_Blocks()
        {
            // Every tracked stack out of inventory or reserved by another pawn: issuing would end
            // Incompletable instantly and re-issue every think cycle.
            Assert.That(EndOfRun(anyUnloadable: false), Is.False);
        }

        [Test]
        public void EndOfRun_AlreadyUnloading_Blocks()
        {
            Assert.That(EndOfRun(alreadyUnloading: true), Is.False);
        }

        [Test]
        public void EndOfRun_WithinCooldown_Blocks()
        {
            // A trip that failed mid-way must not re-issue in a tight loop.
            Assert.That(EndOfRun(sinceIssue: 100, cooldown: 250), Is.False);
            Assert.That(EndOfRun(sinceIssue: 250, cooldown: 250), Is.True);
        }

        [Test]
        public void EndOfRun_NoGraceGate()
        {
            // Deliberate: an empty work scan means the pickup stream is over by definition — the
            // trigger fires even right after the last scoop (there is no grace parameter at all).
            // This pins the signature staying grace-free; a future grace would need its own pin.
            Assert.That(EndOfRun(sinceIssue: 1000, cooldown: 0), Is.True);
        }
    }
}
