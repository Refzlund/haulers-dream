using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class HarvestSectionPolicyTests
    {
        [Test]
        public void CountBelowSection_Unchanged()
        {
            Assert.That(HarvestSectionPolicy.Cap(1, 8), Is.EqualTo(1));
            Assert.That(HarvestSectionPolicy.Cap(3, 8), Is.EqualTo(3));
            Assert.That(HarvestSectionPolicy.Cap(7, 8), Is.EqualTo(7));
        }

        [Test]
        public void CountEqualSection_Unchanged()
        {
            Assert.That(HarvestSectionPolicy.Cap(8, 8), Is.EqualTo(8));
        }

        [Test]
        public void CountAboveSection_CappedToSection()
        {
            Assert.That(HarvestSectionPolicy.Cap(9, 8), Is.EqualTo(8));
            Assert.That(HarvestSectionPolicy.Cap(40, 8), Is.EqualTo(8));
        }

        [Test]
        public void ZeroCount_Zero()
        {
            Assert.That(HarvestSectionPolicy.Cap(0, 8), Is.EqualTo(0));
        }

        [Test]
        public void NonPositiveSection_DisablesCap()
        {
            // An invalid/disabled section size must never trim the queue away — return the full count.
            Assert.That(HarvestSectionPolicy.Cap(40, 0), Is.EqualTo(40));
            Assert.That(HarvestSectionPolicy.Cap(40, -1), Is.EqualTo(40));
        }

        [Test]
        public void CollectNow_NonPlantWork_AlwaysImmediate()
        {
            // Mining / deconstruct / animal / etc. collect each drop immediately regardless of cluster/pending.
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: false, clustered: true, pendingCount: 1, sectionSize: 8), Is.True);
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: false, clustered: false, pendingCount: 0, sectionSize: 8), Is.True);
        }

        [Test]
        public void CollectNow_IsolatedPlantWork_AlwaysImmediate()
        {
            // A lone harvest (not clustered) is collected right away so it isn't dropped-and-abandoned.
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: false, pendingCount: 1, sectionSize: 8), Is.True);
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: false, pendingCount: 5, sectionSize: 8), Is.True);
        }

        [Test]
        public void CollectNow_ClusteredPlantWork_HoldsBelowSection()
        {
            // Within a cluster, drops pile up: no collection until a full section has accumulated.
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: true, pendingCount: 1, sectionSize: 8), Is.False);
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: true, pendingCount: 7, sectionSize: 8), Is.False);
        }

        [Test]
        public void CollectNow_ClusteredPlantWork_FiresAtSection()
        {
            // Exactly a section (and beyond) triggers the sweep of the accumulated cluster.
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: true, pendingCount: 8, sectionSize: 8), Is.True);
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: true, pendingCount: 9, sectionSize: 8), Is.True);
        }

        [Test]
        public void CollectNow_ClusteredPlantWork_NonPositiveSection_DisablesHold()
        {
            // A disabled section size reverts clustered plant work to immediate collection (never strands a drop).
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: true, pendingCount: 1, sectionSize: 0), Is.True);
            Assert.That(HarvestSectionPolicy.ShouldCollectNow(isPlantWork: true, clustered: true, pendingCount: 1, sectionSize: -1), Is.True);
        }

        [Test]
        public void Clustered_NoPrevious_False()
        {
            // The first harvest of a run (or after load) has no previous marker -> isolated.
            Assert.That(HarvestSectionPolicy.IsClustered(hasPrevious: false, dxAbs: 0, dzAbs: 0, ticksSincePrevious: 0, radius: 2, recencyTicks: 600), Is.False);
        }

        [Test]
        public void Clustered_WithinRadiusAndRecent_True()
        {
            // Adjacent (and up to the radius) + recent -> continuing a cluster.
            Assert.That(HarvestSectionPolicy.IsClustered(true, 1, 0, 100, 2, 600), Is.True);
            Assert.That(HarvestSectionPolicy.IsClustered(true, 2, 2, 599, 2, 600), Is.True);
            Assert.That(HarvestSectionPolicy.IsClustered(true, 0, 0, 0, 2, 600), Is.True);
        }

        [Test]
        public void Clustered_OutsideRadius_False()
        {
            // More than the radius away in either axis -> isolated, even if recent.
            Assert.That(HarvestSectionPolicy.IsClustered(true, 3, 0, 10, 2, 600), Is.False);
            Assert.That(HarvestSectionPolicy.IsClustered(true, 0, 3, 10, 2, 600), Is.False);
        }

        [Test]
        public void Clustered_Stale_False()
        {
            // Nearby but the previous harvest is too old (or a negative delta from a clock reset) -> isolated.
            Assert.That(HarvestSectionPolicy.IsClustered(true, 1, 1, 601, 2, 600), Is.False);
            Assert.That(HarvestSectionPolicy.IsClustered(true, 1, 1, -5, 2, 600), Is.False);
        }
    }
}
