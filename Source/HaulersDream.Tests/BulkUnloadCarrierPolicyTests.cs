using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BulkUnloadCarrierPolicyTests
    {
        // --- HasEnoughBackpackRoom (room-gate boundary) ---

        [Test]
        public void Room_EmptyPawn_HasRoom()
        {
            // 0% encumbered, want 50% free -> 0 <= 0.5 -> room.
            Assert.That(BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(0f, 0.5f), Is.True);
        }

        [Test]
        public void Room_AtBoundary_HasRoom()
        {
            // Exactly at the threshold (50% encumbered, want 50% free): 0.5 <= 0.5 -> still room (inclusive).
            Assert.That(BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(0.5f, 0.5f), Is.True);
        }

        [Test]
        public void Room_JustOverBoundary_NoRoom()
        {
            // 60% encumbered, want 50% free -> 0.6 <= 0.5 is false -> no room.
            Assert.That(BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(0.6f, 0.5f), Is.False);
        }

        [Test]
        public void Room_LooseRequirement_AllowsHeavyPawn()
        {
            // Only want 10% free: an 85%-encumbered pawn still qualifies (0.85 <= 0.9).
            Assert.That(BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(0.85f, 0.1f), Is.True);
            // ...but a 95%-encumbered one does not (0.95 <= 0.9 is false).
            Assert.That(BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(0.95f, 0.1f), Is.False);
        }

        // --- PullCountWithinFreeSpace (massless / heavy / partial pull) ---

        [Test]
        public void Pull_FitsWithinFreeSpace()
        {
            // 10 kg free, 1 kg/unit, 100 in the stack -> 10 fit.
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(10f, 1f, 100), Is.EqualTo(10));
        }

        [Test]
        public void Pull_ClampsToStackCount()
        {
            // Plenty of room, only 5 in the stack.
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(1000f, 1f, 5), Is.EqualTo(5));
        }

        [Test]
        public void Pull_MasslessTakenInFull()
        {
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(0f, 0f, 7), Is.EqualTo(7));
        }

        [Test]
        public void Pull_NoFreeSpace_TakesNone()
        {
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(0f, 1f, 50), Is.EqualTo(0));
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(-3f, 1f, 50), Is.EqualTo(0));
        }

        [Test]
        public void Pull_PartialUnitRoundsDown()
        {
            // 2.9 kg free, 1 kg/unit -> 2 fit (never over-fill by rounding up).
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(2.9f, 1f, 50), Is.EqualTo(2));
        }

        [Test]
        public void Pull_HeavyUnit_NoneFit()
        {
            // 5 kg free, each unit weighs 8 kg -> 0 fit (routes to hands in the ladder).
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(5f, 8f, 3), Is.EqualTo(0));
        }

        [Test]
        public void Pull_ZeroStack_TakesNone()
        {
            Assert.That(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(100f, 1f, 0), Is.EqualTo(0));
        }

        // --- PlanNextPull (the selection ladder) ---

        private static BulkUnloadCarrierPolicy.CarrierStack Stack(int index, float unitMass, int count)
            => new BulkUnloadCarrierPolicy.CarrierStack(index, unitMass, count);

        [Test]
        public void Plan_EmptyCarrier_ReturnsNone()
        {
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(new List<BulkUnloadCarrierPolicy.CarrierStack>(), 100f);
            Assert.That(plan.ChosenIndex, Is.LessThan(0));
            Assert.That(plan.Count, Is.EqualTo(0));
        }

        [Test]
        public void Plan_NullStacks_ReturnsNone()
        {
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(null, 100f);
            Assert.That(plan.ChosenIndex, Is.LessThan(0));
        }

        [Test]
        public void Plan_BackpackFirst_PullsFullStackWhenRoom()
        {
            // 100 kg free, two 1 kg/unit stacks of 30 each -> first stack pulled whole into the backpack.
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 1f, 30), Stack(1, 1f, 40) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 100f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(0));
            Assert.That(plan.Count, Is.EqualTo(30));
            Assert.That(plan.ToHands, Is.False);
        }

        [Test]
        public void Plan_BackpackFirst_PartialWhenLimitedRoom()
        {
            // Only 12 kg free, the first stack is 1 kg/unit x 30 -> pull 12 into the backpack (partial).
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 1f, 30) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 12f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(0));
            Assert.That(plan.Count, Is.EqualTo(12));
            Assert.That(plan.ToHands, Is.False);
        }

        [Test]
        public void Plan_SkipsTooHeavyStack_PullsLighterIntoBackpack()
        {
            // 5 kg free: stack 0 is 8 kg/unit (doesn't fit), stack 1 is 1 kg/unit -> pull stack 1 into the backpack.
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 8f, 3), Stack(1, 1f, 10) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 5f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(1));
            Assert.That(plan.Count, Is.EqualTo(5));
            Assert.That(plan.ToHands, Is.False);
        }

        [Test]
        public void Plan_LastStackToHands_WhenNoBackpackRoom()
        {
            // No free carry mass and ONE stack left that won't fit the backpack -> take it whole to hands.
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 8f, 3) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 0f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(0));
            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan.ToHands, Is.True);
        }

        [Test]
        public void Plan_FallbackOneToHands_WhenNearFullAndManyStacks()
        {
            // Backpack full (0 kg free), several heavy stacks -> take the FIRST whole stack to hands (progress).
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack>
            {
                Stack(0, 8f, 3), Stack(1, 8f, 5), Stack(2, 8f, 2)
            };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 0f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(0));
            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan.ToHands, Is.True);
        }

        [Test]
        public void Plan_SkipsEmptyStacks()
        {
            // Stack 0 is empty (already pulled), stack 1 has goods and room -> pick stack 1.
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 1f, 0), Stack(1, 1f, 20) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 100f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(1));
            Assert.That(plan.Count, Is.EqualTo(20));
            Assert.That(plan.ToHands, Is.False);
        }

        [Test]
        public void Plan_AllStacksEmpty_ReturnsNone()
        {
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 1f, 0), Stack(1, 1f, 0) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 100f);
            Assert.That(plan.ChosenIndex, Is.LessThan(0));
        }

        [Test]
        public void Plan_MasslessGoesToBackpackEvenAtZeroFreeSpace()
        {
            // A massless stack fits the backpack at any free space -> backpack-first, never to hands.
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 0f, 9) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 0f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(0));
            Assert.That(plan.Count, Is.EqualTo(9));
            Assert.That(plan.ToHands, Is.False);
        }

        // --- CE-clamp re-plan contract ---
        // When Combat Extended is active, JobDriver_UnloadCarrierInBulk discovers via CECompat.MaxFitCount that
        // CE's weight+bulk capacity is exhausted even though the vanilla-mass planner saw backpack room (CE's bulk
        // dimension is invisible to this pure policy). It then RE-PLANS with freeSpace forced to 0 so the ladder
        // routes the overflow to HANDS — the carry tracker is exempt from the soft ceiling. These pin the exact
        // re-plan-at-zero contract that fallback relies on (massive stack -> hands; massless -> still backpack and
        // re-clamped to 0 by CE at the transfer, a safe terminal end-of-visit, never a spin).

        [Test]
        public void Plan_ReplanAtZeroFreeSpace_RoutesHeavyStackToHands()
        {
            // CE bulk exhausted (the JobDriver forces freeSpace=0): a non-empty heavy stack goes whole to hands.
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack> { Stack(0, 5f, 8) };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 0f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(0));
            Assert.That(plan.Count, Is.EqualTo(8));
            Assert.That(plan.ToHands, Is.True);
        }

        [Test]
        public void Plan_ReplanAtZeroFreeSpace_PicksFirstNonEmptyToHands()
        {
            // Several stacks remain after CE bulk fills the backpack -> the first non-empty goes to hands (progress).
            var stacks = new List<BulkUnloadCarrierPolicy.CarrierStack>
            {
                Stack(0, 2f, 0), Stack(1, 3f, 4), Stack(2, 1f, 6)
            };
            var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 0f);
            Assert.That(plan.ChosenIndex, Is.EqualTo(1));
            Assert.That(plan.Count, Is.EqualTo(4));
            Assert.That(plan.ToHands, Is.True);
        }
    }
}
