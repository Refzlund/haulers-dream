using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class InferencePolicyTests
    {
        // ---- IsRoutablePlacement ----------------------------------------------------------------

        [Test]
        public void RoutablePlacement_RequiresNotReentrant_Near_Item()
        {
            Assert.That(InferencePolicy.IsRoutablePlacement(reentrant: false, nearMode: true, isItem: true), Is.True);
        }

        [Test]
        public void RoutablePlacement_RejectsReentrant()
        {
            Assert.That(InferencePolicy.IsRoutablePlacement(true, true, true), Is.False);
        }

        [Test]
        public void RoutablePlacement_RejectsNonNearMode()
        {
            Assert.That(InferencePolicy.IsRoutablePlacement(false, false, true), Is.False);
        }

        [Test]
        public void RoutablePlacement_RejectsNonItem()
        {
            Assert.That(InferencePolicy.IsRoutablePlacement(false, true, false), Is.False);
        }

        // ---- IsTrueProducer ---------------------------------------------------------------------

        [Test]
        public void TrueProducer_PawnStandingOnCell()
        {
            // plants / animals / deep-drill: the worker is on the placement cell
            Assert.That(InferencePolicy.IsTrueProducer(5, 5, hasJobTarget: false, 0, 0, 5, 5), Is.True);
        }

        [Test]
        public void TrueProducer_JobTargetIsCell()
        {
            // mining: the miner is adjacent (3,5) but its target (the rock) is the placement cell (4,5)
            Assert.That(InferencePolicy.IsTrueProducer(3, 5, hasJobTarget: true, 4, 5, 4, 5), Is.True);
        }

        [Test]
        public void NotTrueProducer_WhenNeitherMatches()
        {
            // adjacent pawn whose target is elsewhere -> only a fallback candidate, not the true producer
            Assert.That(InferencePolicy.IsTrueProducer(3, 5, hasJobTarget: true, 9, 9, 4, 5), Is.False);
        }

        [Test]
        public void NotTrueProducer_TargetMatchesButNoJob()
        {
            // hasJobTarget false means the target coords must be ignored even if they happen to match
            Assert.That(InferencePolicy.IsTrueProducer(3, 5, hasJobTarget: false, 4, 5, 4, 5), Is.False);
        }

        [Test]
        public void DropSafety_BystanderWorkerNearADropIsNotTheProducer()
        {
            // Deletion-safety invariant: an item DROPPED at cell (10,10) while a colonist harvests a
            // plant at (20,20) and stands at (21,20) must NOT be attributed to that colonist. With no
            // fallback, FindWorker returns null for this drop, so it lands on the ground (never
            // diverted, never deleted). This is the regression guard for the Harvest-And-Haul /
            // Pick Up And Haul collision.
            int dropCellX = 10, dropCellZ = 10;
            int harvesterX = 21, harvesterZ = 20;     // standing next to its plant, NOT the drop
            int harvesterTargetX = 20, harvesterTargetZ = 20; // the plant it's harvesting
            Assert.That(
                InferencePolicy.IsTrueProducer(harvesterX, harvesterZ, true, harvesterTargetX, harvesterTargetZ, dropCellX, dropCellZ),
                Is.False);
        }

        // ---- ShouldScoopLeaving -----------------------------------------------------------------

        [Test]
        public void ScoopLeaving_FreshUnforbiddenUnstoredItem()
        {
            Assert.That(InferencePolicy.ShouldScoopLeaving(isItem: true, wasPresentBefore: false, forbidden: false, inValidStorage: false), Is.True);
        }

        [Test]
        public void DontScoop_NonItem()
        {
            Assert.That(InferencePolicy.ShouldScoopLeaving(false, false, false, false), Is.False);
        }

        [Test]
        public void DontScoop_PreExistingGroundItem()
        {
            // the storage-protection / "don't grab unrelated items" guarantee
            Assert.That(InferencePolicy.ShouldScoopLeaving(true, wasPresentBefore: true, forbidden: false, inValidStorage: false), Is.False);
        }

        [Test]
        public void DontScoop_Forbidden()
        {
            Assert.That(InferencePolicy.ShouldScoopLeaving(true, false, forbidden: true, inValidStorage: false), Is.False);
        }

        [Test]
        public void DontScoop_AlreadyInValidStorage()
        {
            Assert.That(InferencePolicy.ShouldScoopLeaving(true, false, false, inValidStorage: true), Is.False);
        }
    }
}
