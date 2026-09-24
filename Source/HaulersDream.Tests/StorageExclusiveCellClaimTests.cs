using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class StorageExclusiveCellClaimTests
    {
        [Test]
        public void ExclusiveNativeCellIsNotSubtractedAgainFromGroupCapacity()
        {
            var pawn = new object(); var other = new object(); var group = new object(); var def = new object(); var allocation = new object();
            var rows = StorageClaimLedger.Add(null, pawn, group, def, 7, allocation);
            rows = StorageClaimLedger.Add(rows, other, group, def, 3);
            StorageClaimEvidence evidence = (p,d) => 100;
            Assert.Multiple(() =>
            {
                Assert.That(rows.Length, Is.EqualTo(2));
                Assert.That(rows[0].ExclusiveCellAllocation, Is.SameAs(allocation));
                Assert.That(StorageClaimLedger.ClaimedTotal(rows, group, def, evidence), Is.EqualTo(3));
                Assert.That(StorageClaimLedger.ClaimedByOthers(rows, group, def, other, evidence), Is.Zero);
                Assert.That(StorageClaimLedger.ClaimedByPawn(rows, group, def, pawn, evidence), Is.Zero);
            });
        }

        [Test]
        public void ExclusiveTripReplacesSameDefGroupIntentAndReconcilesWithoutEvidence()
        {
            var pawn = new object(); var oldGroup = new object(); var group = new object(); var def = new object(); var allocation = new object();
            var before = StorageClaimLedger.Add(null, pawn, oldGroup, def, 50);
            var after = StorageClaimLedger.Add(before, pawn, group, def, 7, allocation);
            Assert.Multiple(() =>
            {
                Assert.That(before.Length, Is.EqualTo(1));
                Assert.That(before[0].Group, Is.SameAs(oldGroup));
                Assert.That(after.Length, Is.EqualTo(1));
                Assert.That(after[0].Group, Is.SameAs(group));
                Assert.That(after[0].Units, Is.EqualTo(7));
                Assert.That(StorageClaimLedger.Reconcile(after, (p,d) => 0, g => true), Is.Empty);
            });
        }

        [Test]
        public void OtherDefsOrdinaryClaimsRemainCountedWhileExclusiveCellsArePhysicalExclusions()
        {
            var group = new object(); var steel = new object(); var wood = new object();
            var rows = StorageClaimLedger.Add(null, new object(), group, steel, 7, new object());
            rows = StorageClaimLedger.Add(rows, new object(), group, wood, 20);
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, group, steel, (p,d) => 100), Is.Zero);
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, group, wood, (p,d) => 100), Is.EqualTo(20));
        }

        [Test]
        public void SelectedShelfLowerBoundKeepsOrdinarySameDefAndOwnPickupClaims()
        {
            var group=new object();var steel=new object();var asker=new object();var other=new object();
            var rows=StorageClaimLedger.Add(null,other,group,steel,20);
            rows=StorageClaimLedger.Add(rows,asker,group,steel,5);
            Assert.That(SelectedShelfCapacity.Available(3,1,75,rows,group,steel,asker,false,(p,d)=>100,d=>75),Is.EqualTo(53));
            Assert.That(SelectedShelfCapacity.Available(3,1,75,rows,group,steel,asker,true,(p,d)=>100,d=>75),Is.EqualTo(58));
        }

        [Test]
        public void SelectedShelfLowerBoundChargesForeignDefsOnceInVacantSlotsOnly()
        {
            var group=new object();var steel=new object();var wood=new object();
            var rows=StorageClaimLedger.Add(null,new object(),group,wood,50);
            rows=StorageClaimLedger.Add(rows,new object(),group,wood,25);
            // Two rows total one native stack/slot, leaving the other slot and Steel-only deficits.
            Assert.That(SelectedShelfCapacity.Available(3,2,75,rows,group,steel,new object(),false,(p,d)=>100,d=>75),Is.EqualTo(78));
            Assert.That(SelectedShelfCapacity.Available(3,0,75,rows,group,steel,new object(),false,(p,d)=>100,d=>75),Is.EqualTo(3));
        }

        [Test]
        public void SelectedShelfLowerBoundDoesNotDoubleSubtractExclusiveOrStaleClaims()
        {
            var group=new object();var steel=new object();var wood=new object();var stale=new object();
            var rows=StorageClaimLedger.Add(null,new object(),group,wood,75,new object());
            rows=StorageClaimLedger.Add(rows,stale,group,steel,75);
            Assert.That(SelectedShelfCapacity.Available(0,1,75,rows,group,steel,new object(),false,(p,d)=>ReferenceEquals(p,stale)?0:100,d=>75),Is.EqualTo(75));
            Assert.That(SelectedShelfCapacity.Available(0,0,75,rows,group,steel,new object(),false,(p,d)=>100,d=>75),Is.Zero);
        }

        [Test]
        public void SelectedShelfLowerBoundRefusesFullyCommittedOrUnpriceableCapacity()
        {
            var group=new object();var steel=new object();var wood=new object();
            var same=StorageClaimLedger.Add(null,new object(),group,steel,75);
            var foreign=StorageClaimLedger.Add(null,new object(),group,wood,1);
            Assert.That(SelectedShelfCapacity.Available(0,1,75,same,group,steel,new object(),false,(p,d)=>100,d=>75),Is.Zero);
            Assert.That(SelectedShelfCapacity.Available(0,1,75,foreign,group,steel,new object(),false,(p,d)=>100,d=>75),Is.Zero);
            Assert.That(SelectedShelfCapacity.Available(0,1,75,foreign,group,steel,new object(),false,(p,d)=>100,d=>0),Is.Zero);
        }
    }
}
