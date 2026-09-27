using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class StorageResourceAllocationTests
    {
        private sealed class Cargo
        {
            internal string Def, Attribute;
            internal Cargo(string def, string attribute = "ordinary") { Def = def; Attribute = attribute; }
        }
        private static bool Stack(object target, object incoming) => target is Cargo a && incoming is Cargo b
            && a.Def == b.Def && a.Attribute == b.Attribute;
        private static StorageAllocationRequest Request(object subject, int units = 7, int limit = 75,
            string[] cells = null, string[] stacks = null, object owner = null, object parcel = null)
            => new StorageAllocationRequest(owner ?? new object(), parcel ?? new object(), subject, units, limit,
                cells ?? new[] { "shelf" }, stacks);
        private static StorageAllocationCell Cell(int vacant = 1, params StorageAllocationStack[] stacks)
            => new StorageAllocationCell("shelf", vacant, stacks);
        private static StorageAllocationResult Allocate(StorageAllocationCell[] cells, StorageAllocationState baseline,
            params StorageAllocationRequest[] requests)
            => StorageResourceAllocator.Allocate(cells, baseline, requests, Stack, s => true);

        [Test]
        public void OccupiedThreeSlotShelfHasOneSharedVacancyForTwoDifferentDefs()
        {
            var cloth = Request(new Cargo("cloth")); var uranium = Request(new Cargo("uranium"));
            // Two full physical residents contribute no deficit. Their cell's ONE vacant slot
            // is the real shared resource; no def gets a private 75-unit copy of it.
            var cells = new[] { Cell(1, new StorageAllocationStack("steel", new Cargo("steel"), 0),
                new StorageAllocationStack("wood", new Cargo("wood"), 0)) };
            var first = Allocate(cells, StorageAllocationState.Empty, cloth);
            var second = Allocate(cells, first.State, uranium);
            Assert.Multiple(() =>
            {
                Assert.That(first.AdmittedUnits(cloth), Is.EqualTo(7));
                Assert.That(second.Status, Is.EqualTo(StorageAllocationStatus.CapacityLimited));
                Assert.That(second.AdmittedUnits(uranium), Is.Zero);
                Assert.That(second.State, Is.SameAs(first.State));
                Assert.That(second.State.UnitsFor(cloth.Owner, cloth.Parcel), Is.EqualTo(7));
            });
        }

        [Test]
        public void CompatibleIncomingParcelsShareOneTailButCannotExceedItsStackLimit()
        {
            var first = Request(new Cargo("steel"), 30); var second = Request(new Cargo("steel"), 20);
            var third = Request(new Cargo("steel"), 50);
            var result = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first, second, third);
            Assert.Multiple(() =>
            {
                Assert.That(result.AdmittedUnits(first), Is.EqualTo(30));
                Assert.That(result.AdmittedUnits(second), Is.EqualTo(20));
                Assert.That(result.AdmittedUnits(third), Is.EqualTo(25));
                Assert.That(result.State.Slices.Select(s => s.ResourceKey).Distinct().Count(), Is.EqualTo(1));
                Assert.That(result.State.Slices.Sum(s => s.Units), Is.EqualTo(75));
            });
        }

        [Test]
        public void SameDefDifferentAttributesDoNotShareVirtualTailOrResidentDeficit()
        {
            var ordinary = Request(new Cargo("medicine", "ordinary"));
            var different = Request(new Cargo("medicine", "different"), stacks: new[] { "resident" });
            var cells = new[] { Cell(1, new StorageAllocationStack("resident", new Cargo("medicine", "ordinary"), 60)) };
            var first = Allocate(cells, StorageAllocationState.Empty, ordinary);
            var second = Allocate(cells, first.State, different);
            Assert.That(second.AdmittedUnits(different), Is.Zero);
        }

        [Test]
        public void DirectionalCompatibilityUsesRealTargetFirst()
        {
            var host = new object(); var incoming = new object(); var calls = new List<(object, object)>();
            var a = Request(host, 4, 10); var b = Request(incoming, 6, 10);
            bool Direction(object target, object subject)
            { calls.Add((target, subject)); return ReferenceEquals(target, host) && ReferenceEquals(subject, incoming); }
            var result = StorageResourceAllocator.Allocate(new[] { Cell() }, StorageAllocationState.Empty,
                new[] { a, b }, Direction, s => true);
            Assert.That(result.AdmittedUnits(b), Is.EqualTo(6));
            Assert.That(calls.Any(p => ReferenceEquals(p.Item1, host) && ReferenceEquals(p.Item2, incoming)), Is.True);
            var reverse = StorageResourceAllocator.Allocate(new[] { Cell() }, StorageAllocationState.Empty,
                new[] { b, a }, Direction, s => true);
            Assert.That(reverse.AdmittedUnits(a), Is.Zero);
        }

        [Test]
        public void FullShelfCountsOnlyEligibleCompatibleExistingStack()
        {
            var subject = new Cargo("steel", "a");
            var cells = new[] { Cell(0, new StorageAllocationStack("compatible", new Cargo("steel", "a"), 3),
                new StorageAllocationStack("wrong-attribute", new Cargo("steel", "b"), 50),
                new StorageAllocationStack("provider-invalid", new Cargo("steel", "a"), 50)) };
            var request = Request(subject, 100, stacks: new[] { "compatible", "wrong-attribute" });
            var result = Allocate(cells, StorageAllocationState.Empty, request);
            Assert.That(result.AdmittedUnits(request), Is.EqualTo(3));
            Assert.That(result.State.Slices.Single().ResourceKey, Is.EqualTo("compatible"));
        }

        [Test]
        public void ExistingTopUpsCanBeReassignedForRestrictedNeighbor()
        {
            var a = new Cargo("steel"); var b = new Cargo("steel");
            var cells = new[] { Cell(0, new StorageAllocationStack("x", new Cargo("steel"), 7),
                new StorageAllocationStack("y", new Cargo("steel"), 7)) };
            var flexible = Request(a, stacks: new[] { "x", "y" }); var restricted = Request(b, stacks: new[] { "x" });
            var result = Allocate(cells, StorageAllocationState.Empty, flexible, restricted);
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.Complete));
                Assert.That(result.State.SlicesFor(flexible.Owner).Single().ResourceKey, Is.EqualTo("y"));
                Assert.That(result.State.SlicesFor(restricted.Owner).Single().ResourceKey, Is.EqualTo("x"));
            });
        }

        [Test]
        public void HeterogeneousCellEligibilityDoesNotInheritFirstParcelsSubset()
        {
            var cells = new[] { new StorageAllocationCell("x", 1), new StorageAllocationCell("y", 3) };
            var flexible = Request(new Cargo("steel"), 75, cells: new[] { "x", "y" });
            var restricted = Request(new Cargo("wood"), 75, cells: new[] { "x" });
            var result = Allocate(cells, StorageAllocationState.Empty, flexible, restricted);
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.Complete));
            Assert.That(result.State.SlicesFor(restricted.Owner).Single().CellKey, Is.EqualTo("x"));
            Assert.That(result.State.SlicesFor(flexible.Owner).Single().CellKey, Is.EqualTo("y"));
        }

        [Test]
        public void NewSlotsCanBeReassignedWhenEqualSizeEligibilitySetsWouldOtherwiseFail()
        {
            var cells = new[] { new StorageAllocationCell("a", 1), new StorageAllocationCell("b", 1),
                new StorageAllocationCell("c", 1) };
            var first = Request(new Cargo("first"), cells: new[] { "a", "c" });
            var second = Request(new Cargo("second"), cells: new[] { "a", "b" });
            var third = Request(new Cargo("third"), cells: new[] { "a", "b" });
            var result = Allocate(cells, StorageAllocationState.Empty, first, second, third);
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.Complete));
            Assert.That(result.State.SlicesFor(first.Owner).Single().CellKey, Is.EqualTo("c"));
            Assert.That(result.State.Slices.Select(s => s.CellKey).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void CommittedForeignSlotNeverMovesToMakeNewAdmissionSucceed()
        {
            var cells = new[] { new StorageAllocationCell("a", 1), new StorageAllocationCell("b", 1) };
            var first = Request(new Cargo("first"), cells: new[] { "a", "b" });
            var initial = Allocate(cells, StorageAllocationState.Empty, first);
            var restricted = Request(new Cargo("second"), cells: new[] { "a" });
            var result = Allocate(cells, initial.State, restricted);
            Assert.That(result.AdmittedUnits(restricted), Is.Zero);
            Assert.That(result.State, Is.SameAs(initial.State));
        }

        [Test]
        public void AbsentCandidateDefStillConsumesItsActualCommittedResource()
        {
            var foreign = Request(new Cargo("gold"), 1);
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, foreign);
            var candidate = Request(new Cargo("cloth"), 1);
            var result = Allocate(new[] { Cell() }, initial.State, candidate);
            Assert.That(result.AdmittedUnits(candidate), Is.Zero);
        }

        [Test]
        public void RepeatedSameOwnerParcelAdmissionIsIdempotent()
        {
            var request = Request(new Cargo("steel"));
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, request);
            var repeated = Allocate(new[] { Cell() }, initial.State, request);
            Assert.That(repeated.State, Is.SameAs(initial.State));
            Assert.That(repeated.AdmittedUnits(request), Is.EqualTo(7));
        }

        [Test]
        public void SameOwnerDifferentParcelsHaveIndependentQuantitiesAndRelease()
        {
            var owner = new object(); var first = Request(new Cargo("steel"), owner: owner);
            var second = Request(new Cargo("steel"), owner: owner);
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first, second);
            var after = StorageResourceAllocator.Release(initial.State, owner, first.Parcel);
            Assert.Multiple(() =>
            {
                Assert.That(initial.State.UnitsFor(owner), Is.EqualTo(14));
                Assert.That(after.UnitsFor(owner, first.Parcel), Is.Zero);
                Assert.That(after.UnitsFor(owner, second.Parcel), Is.EqualTo(7));
                Assert.That(StorageResourceAllocator.Release(after, new object()), Is.SameAs(after));
            });
        }

        [Test]
        public void EqualTextIsNotAnOwnershipIdentity()
        {
            object ownerA = new string(new[] { 'x' }), ownerB = new string(new[] { 'x' });
            var first = Request(new Cargo("steel"), owner: ownerA); var second = Request(new Cargo("steel"), owner: ownerB);
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first, second);
            var after = StorageResourceAllocator.Release(initial.State, ownerA);
            Assert.That(after.UnitsFor(ownerB), Is.EqualTo(7));
        }

        [Test]
        public void PartialDepositRebindsRemainingTailToPhysicalStackWithoutDoubleCharging()
        {
            var first = Request(new Cargo("steel"), 30); var second = Request(new Cargo("steel"), 20);
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first, second);
            var slot = initial.State.Slices.First().ResourceKey;
            var target = new Cargo("steel");
            var reduced = StorageResourceAllocator.Reduce(initial.State, first.Owner, first.Parcel, 30);
            var bound = StorageResourceAllocator.RebindVirtualSlot(reduced, slot, "shelf", "physical", target);
            var third = Request(new Cargo("steel"), 50, stacks: new[] { "physical" });
            var result = Allocate(new[] { Cell(0, new StorageAllocationStack("physical", target, 45)) }, bound, third);
            Assert.Multiple(() =>
            {
                Assert.That(result.State.UnitsFor(second.Owner), Is.EqualTo(20));
                Assert.That(result.AdmittedUnits(third), Is.EqualTo(25));
                Assert.That(initial.State.UnitsFor(first.Owner), Is.EqualTo(30));
                Assert.That(result.State.Slices.All(s => s.Kind == StorageAllocationResourceKind.ExistingStack), Is.True);
            });
        }

        [Test]
        public void ChangedPhysicalTargetMustReconcileIncompatibleSharedTailBeforePublishing()
        {
            var plannedTarget = new object(); var firstDepositor = new object(); var waitingCargo = new object();
            // A accepts both B and C, but the physical stack created when B arrives first accepts A,
            // not C. Compatibility with the former virtual target cannot be inherited transitively.
            bool Direction(object target, object incoming) =>
                ReferenceEquals(target, plannedTarget)
                    && (ReferenceEquals(incoming, firstDepositor) || ReferenceEquals(incoming, waitingCargo))
                || ReferenceEquals(target, firstDepositor) && ReferenceEquals(incoming, plannedTarget);
            var creator = Request(plannedTarget, 10);
            var depositor = Request(firstDepositor, 20);
            var waiting = Request(waitingCargo, 30);
            var initial = StorageResourceAllocator.Allocate(new[] { Cell() }, StorageAllocationState.Empty,
                new[] { creator, depositor, waiting }, Direction, s => true);
            Assert.That(initial.Status, Is.EqualTo(StorageAllocationStatus.Complete));
            Assert.That(initial.State.Slices.Select(s => s.ResourceKey).Distinct().Count(), Is.EqualTo(1));

            var slot = initial.State.Slices[0].ResourceKey;
            var reduced = StorageResourceAllocator.Reduce(initial.State, depositor.Owner, depositor.Parcel, 20, slot);
            var rebound = StorageResourceAllocator.RebindVirtualSlot(reduced, slot, "shelf", "physical", firstDepositor);
            var next = Request(plannedTarget, 5, stacks: new[] { "physical" });
            var physical = new[] { Cell(0, new StorageAllocationStack("physical", firstDepositor, 55)) };
            var rejected = StorageResourceAllocator.Allocate(physical, rebound, new[] { next }, Direction, s => true);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Status, Is.EqualTo(StorageAllocationStatus.NeedsReconciliation));
                Assert.That(rejected.CanPublish, Is.False);
                Assert.That(rejected.State, Is.SameAs(rebound));
                Assert.That(rejected.AdmittedUnits(next), Is.Zero);
                Assert.That(rejected.State.UnitsFor(creator.Owner), Is.EqualTo(10));
                Assert.That(rejected.State.UnitsFor(waiting.Owner), Is.EqualTo(30));
                Assert.That(rejected.State.UnitsFor(depositor.Owner), Is.Zero);
                Assert.That(initial.State.UnitsFor(depositor.Owner), Is.EqualTo(20));
            });

            // Once the caller actually reconciles the incompatible outstanding parcel, the valid
            // owner and freshly compatible incoming cargo may use the observed physical deficit.
            var reconciled = StorageResourceAllocator.Release(rebound, waiting.Owner, waiting.Parcel);
            var accepted = StorageResourceAllocator.Allocate(physical, reconciled, new[] { next }, Direction, s => true);
            Assert.Multiple(() =>
            {
                Assert.That(accepted.Status, Is.EqualTo(StorageAllocationStatus.Complete));
                Assert.That(accepted.CanPublish, Is.True);
                Assert.That(accepted.AdmittedUnits(next), Is.EqualTo(5));
                Assert.That(accepted.State.UnitsFor(creator.Owner), Is.EqualTo(10));
                Assert.That(accepted.State.UnitsFor(waiting.Owner), Is.Zero);
            });
        }

        [Test]
        public void PartialReductionCannotReleaseMoreThanTheExactParcelOwns()
        {
            var first = Request(new Cargo("steel")); var second = Request(new Cargo("steel"));
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first, second);
            var result = StorageResourceAllocator.Reduce(initial.State, first.Owner, first.Parcel, 3);
            Assert.That(result.UnitsFor(first.Owner), Is.EqualTo(4));
            Assert.That(result.UnitsFor(second.Owner), Is.EqualTo(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => StorageResourceAllocator.Reduce(result, first.Owner, first.Parcel, 5));
        }

        [Test]
        public void DepositMustNameItsResourceWhenParcelSpansSeveralSlots()
        {
            var request = Request(new Cargo("steel"), 100);
            var initial = Allocate(new[] { Cell(2) }, StorageAllocationState.Empty, request);
            var first = initial.State.Slices[0]; var second = initial.State.Slices[1];
            Assert.Throws<ArgumentException>(() => StorageResourceAllocator.Reduce(initial.State, request.Owner, request.Parcel, 7));
            var reduced = StorageResourceAllocator.Reduce(initial.State, request.Owner, request.Parcel, 7, second.ResourceKey);
            Assert.That(reduced.Slices.Single(s => s.ResourceKey == first.ResourceKey).Units, Is.EqualTo(first.Units));
            Assert.That(reduced.Slices.Single(s => s.ResourceKey == second.ResourceKey).Units, Is.EqualTo(second.Units - 7));
        }

        [Test]
        public void ReleasingVirtualHostDoesNotGiveAwayRemainingTailOwnersSlot()
        {
            var first = Request(new Cargo("steel")); var tailOwner = Request(new Cargo("steel"));
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first, tailOwner);
            var released = StorageResourceAllocator.Release(initial.State, first.Owner);
            var unrelated = Request(new Cargo("wood"));
            var result = Allocate(new[] { Cell() }, released, unrelated);
            Assert.That(result.AdmittedUnits(unrelated), Is.Zero);
            Assert.That(result.State.UnitsFor(tailOwner.Owner), Is.EqualTo(7));
        }

        [Test]
        public void ForeignRealTopUpDoesNotConsumeAnUnrelatedVacantSlot()
        {
            var target = new Cargo("steel");
            var first = Request(new Cargo("steel"), stacks: new[] { "resident" });
            var cells = new[] { Cell(1, new StorageAllocationStack("resident", target, 7)) };
            var initial = Allocate(cells, StorageAllocationState.Empty, first);
            var different = Request(new Cargo("wood"), 75);
            var result = Allocate(cells, initial.State, different);
            Assert.That(result.AdmittedUnits(different), Is.EqualTo(75));
            Assert.That(result.State.SlicesFor(first.Owner).Single().Kind, Is.EqualTo(StorageAllocationResourceKind.ExistingStack));
        }

        [Test]
        public void RepeatedLargerDesiredTotalAllocatesOnlyItsIncrease()
        {
            var first = Request(new Cargo("steel"));
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first);
            var larger = Request(first.Subject, 40, owner: first.Owner, parcel: first.Parcel);
            var result = Allocate(new[] { Cell() }, initial.State, larger);
            Assert.That(result.AdmittedUnits(larger), Is.EqualTo(40));
            Assert.That(initial.State.UnitsFor(first.Owner), Is.EqualTo(7));
        }

        [Test]
        public void UnknownResourceBehindAnExistingLeaseDoesNotAuthorizeOtherKnownSpace()
        {
            var first = Request(new Cargo("steel"), cells: new[] { "unobserved" });
            var initial = Allocate(new[] { new StorageAllocationCell("unobserved", 1) }, StorageAllocationState.Empty, first);
            var other = Request(new Cargo("wood"));
            var result = StorageResourceAllocator.Allocate(new[] { Cell() }, initial.State, new[] { other },
                Stack, s => true, new StorageAllocationOptions(observationComplete: false));
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.NeedsReconciliation));
            Assert.That(result.State, Is.SameAs(initial.State));
        }

        [Test]
        public void LimitShrinkOrInvalidatedBaselineRequiresRepairWithoutPublishingNewClaims()
        {
            var first = Request(new Cargo("steel"));
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first);
            var other = Request(new Cargo("wood"));
            var shrunk = Allocate(new[] { Cell(0) }, initial.State, other);
            var invalid = StorageResourceAllocator.Allocate(new[] { Cell() }, initial.State, new[] { other }, Stack, s => false);
            Assert.Multiple(() =>
            {
                Assert.That(shrunk.Status, Is.EqualTo(StorageAllocationStatus.NeedsReconciliation));
                Assert.That(shrunk.State, Is.SameAs(initial.State));
                Assert.That(invalid.Status, Is.EqualTo(StorageAllocationStatus.NeedsReconciliation));
                Assert.That(invalid.CanPublish, Is.False);
                Assert.That(invalid.State, Is.SameAs(initial.State));
            });
        }

        [Test]
        public void PhysicalTopUpMutationInSameTickCannotReuseAnEarlierQuantity()
        {
            var target = new Cargo("steel"); var request = Request(new Cargo("steel"), 7, stacks: new[] { "resident" });
            var initial = Allocate(new[] { Cell(0, new StorageAllocationStack("resident", target, 7)) }, StorageAllocationState.Empty, request);
            var afterDeposit = Allocate(new[] { Cell(0, new StorageAllocationStack("resident", target, 0)) }, initial.State);
            Assert.That(afterDeposit.Status, Is.EqualTo(StorageAllocationStatus.NeedsReconciliation));
            Assert.That(afterDeposit.State, Is.SameAs(initial.State));
        }

        [Test]
        public void PhysicalSnapshotsRequestsAndResultsAreDefensivelyCopied()
        {
            var stacks = new List<StorageAllocationStack> { new StorageAllocationStack("resident", new Cargo("steel"), 7) };
            var keys = new List<string> { "shelf" }; var stackKeys = new List<string> { "resident" };
            var cell = new StorageAllocationCell("shelf", 0, stacks);
            var request = new StorageAllocationRequest(new object(), new object(), new Cargo("steel"), 7, 75, keys, stackKeys);
            stacks.Clear(); keys.Clear(); stackKeys.Clear();
            var result = Allocate(new[] { cell }, StorageAllocationState.Empty, request);
            Assert.That(result.AdmittedUnits(request), Is.EqualTo(7));
            Assert.Throws<NotSupportedException>(() => ((IList<StorageResourceAllocation>)result.State.Slices).Clear());
        }

        [Test]
        public void LargeIntegerCapacityDoesNotOverflowOrExpandSlotsPerUnit()
        {
            var request = Request(new Cargo("steel"), int.MaxValue, int.MaxValue);
            var result = Allocate(new[] { Cell(int.MaxValue) }, StorageAllocationState.Empty, request);
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.Complete));
            Assert.That(result.AdmittedUnits(request), Is.EqualTo(int.MaxValue));
            Assert.That(result.State.Slices.Count, Is.EqualTo(1));
        }

        [Test]
        public void SlotWorkExhaustionRollsBackTheWholeProposalAndPreservesBaseline()
        {
            var request = Request(new Cargo("one-per-stack"), int.MaxValue, 1);
            var result = StorageResourceAllocator.Allocate(new[] { Cell(int.MaxValue) }, StorageAllocationState.Empty,
                new[] { request }, Stack, s => true, new StorageAllocationOptions(maximumNewSlots: 4));
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.BudgetExhausted));
            Assert.That(result.State, Is.SameAs(StorageAllocationState.Empty));
            Assert.That(result.CanPublish, Is.False);
        }

        [Test]
        public void WorkBudgetExhaustionRetainsCommittedOwnershipWithoutPublishingTentativeClaims()
        {
            var cells = new[] { Cell(2) };
            var committed = Request(new Cargo("steel"));
            var initial = Allocate(cells, StorageAllocationState.Empty, committed);
            var before = initial.State.Slices.ToArray();
            var candidate = Request(new Cargo("wood"), cells: new[] { "shelf" }
                .Concat(Enumerable.Range(0, 1000).Select(i => "unobserved-" + i)).ToArray());
            var complete = Allocate(cells, initial.State, candidate);
            Assert.That(complete.Status, Is.EqualTo(StorageAllocationStatus.Complete));
            Assert.That(complete.AdmittedUnits(candidate), Is.EqualTo(7));

            // Exercise both rejection while ingesting a large eligibility list and exhaustion at
            // the successful proposal's final work boundary, without pinning internal step counts.
            foreach (int budget in new[] { 8, complete.Work - 1 })
            {
                var limited = StorageResourceAllocator.Allocate(cells, initial.State, new[] { candidate },
                    Stack, s => true, new StorageAllocationOptions(maximumWork: budget));
                Assert.Multiple(() =>
                {
                    Assert.That(limited.Status, Is.EqualTo(StorageAllocationStatus.BudgetExhausted), "budget " + budget);
                    Assert.That(limited.Work, Is.LessThanOrEqualTo(budget));
                    Assert.That(limited.CanPublish, Is.False);
                    Assert.That(limited.State, Is.SameAs(initial.State));
                    Assert.That(limited.AdmittedUnits(candidate), Is.Zero);
                    Assert.That(limited.State.UnitsFor(committed.Owner, committed.Parcel), Is.EqualTo(7));
                    Assert.That(initial.State.Slices, Is.EqualTo(before));
                });
            }
        }

        [Test]
        public void IncompleteObservationOffersOnlyWitnessedResourcesAndSaysItIsIncomplete()
        {
            var request = Request(new Cargo("steel"), 100);
            var result = StorageResourceAllocator.Allocate(new[] { Cell() }, StorageAllocationState.Empty,
                new[] { request }, Stack, s => true, new StorageAllocationOptions(observationComplete: false));
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.IncompleteObservation));
            Assert.That(result.AdmittedUnits(request), Is.EqualTo(75));
            Assert.That(result.CanPublish, Is.True);
        }

        [Test]
        public void EmptyIncompleteObservationDoesNotInventInfinityOrProveFull()
        {
            var request = Request(new Cargo("steel"), 7);
            var result = StorageResourceAllocator.Allocate(Array.Empty<StorageAllocationCell>(), StorageAllocationState.Empty,
                new[] { request }, Stack, s => true, new StorageAllocationOptions(observationComplete: false));
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.IncompleteObservation));
            Assert.That(result.AdmittedUnits(request), Is.Zero);
        }

        [Test]
        public void DuplicateParcelRequestDoesNotDoubleBookItsQuantity()
        {
            var request = Request(new Cargo("steel"));
            var result = Allocate(new[] { Cell() }, StorageAllocationState.Empty, request, request);
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.InvalidRequest));
            Assert.That(result.State, Is.SameAs(StorageAllocationState.Empty));
        }

        [Test]
        public void SingleUnitHeterogeneousSlotsMatchSmallExhaustiveOracle()
        {
            var random = new Random(270);
            for (int trial = 0; trial < 120; trial++)
            {
                var capacities = Enumerable.Range(0, 4).Select(_ => random.Next(0, 3)).ToArray();
                var cells = capacities.Select((n, i) => new StorageAllocationCell("cell" + i, n)).ToArray();
                var edges = Enumerable.Range(0, 6).Select(_ => Enumerable.Range(0, 4).Where(i => random.Next(2) == 1).ToArray()).ToArray();
                var requests = edges.Select((e, i) => Request(new Cargo("distinct" + i), 1, 1,
                    e.Select(c => "cell" + c).ToArray())).ToArray();
                int Oracle(int next)
                {
                    if (next == edges.Length) return 0;
                    int best = Oracle(next + 1);
                    foreach (int c in edges[next])
                    {
                        if (capacities[c] <= 0) continue;
                        capacities[c]--; best = Math.Max(best, 1 + Oracle(next + 1)); capacities[c]++;
                    }
                    return best;
                }
                int expected = Oracle(0);
                var result = Allocate(cells, StorageAllocationState.Empty, requests);
                Assert.That(result.State.Slices.Sum(s => s.Units), Is.EqualTo(expected), "trial " + trial);
                foreach (var group in result.State.Slices.GroupBy(s => s.CellKey))
                    Assert.That(group.Select(s => s.ResourceKey).Distinct().Count(),
                        Is.LessThanOrEqualTo(cells.Single(c => c.Key == group.Key).VacantSlots), "trial " + trial);
                foreach (var request in requests)
                    Assert.That(result.State.SlicesFor(request.Owner).All(s => request.EligibleCells.Contains(s.CellKey)), Is.True);
            }
        }

        [Test]
        public void ExceptionInDirectionalPredicateCannotMutateCommittedState()
        {
            var first = Request(new Cargo("steel"));
            var initial = Allocate(new[] { Cell() }, StorageAllocationState.Empty, first);
            var before = initial.State.Slices.ToArray();
            Assert.Throws<InvalidOperationException>(() => StorageResourceAllocator.Allocate(new[] { Cell() }, initial.State,
                new[] { Request(new Cargo("steel")) }, (a, b) => throw new InvalidOperationException("native callback"), s => true));
            Assert.That(initial.State.Slices, Is.EqualTo(before));
        }
    }
}
