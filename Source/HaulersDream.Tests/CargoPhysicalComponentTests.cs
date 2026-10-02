using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CargoPhysicalComponentTests
    {
        private const long Work = 1000000;
        private static readonly Guid Session = Guid.Parse("5e30e880-df0c-49af-8eb6-77aa950ecb3c");
        private static readonly Guid Component = Guid.Parse("0c8a349d-8115-42a8-9d31-681b6d39b1c5");
        private static CargoPhysicalObservation O(string key, long quantity, string context = "floor")
            => new CargoPhysicalObservation(key, quantity, context);
        private static CargoPhysicalComponent Create(params CargoPhysicalObservation[] census)
        {
            var result = CargoPhysicalComponent.TryCreate(Session, Component, census, Work, out var owner);
            Assert.That(result.Status, Is.EqualTo(CargoPhysicalStatus.Created), result.Detail);
            return owner;
        }
        private static CargoPhysicalSnapshot Snap(CargoPhysicalComponent owner)
        {
            var result = owner.CaptureSnapshot(Work);
            Assert.That(result.Status, Is.EqualTo(CargoPhysicalStatus.SnapshotCaptured), result.Detail);
            return result.Snapshot;
        }
        private static CargoPhysicalEntityVersion V(CargoPhysicalComponent owner, string key)
            => Snap(owner).FrontierEntities.Single(x => x.Observation.EntityKey == key);
        private static int Origin(CargoPhysicalComponent owner, string key)
            => Snap(owner).Nodes.Single(n => n.Kind == CargoPhysicalNodeKind.ObservedBirth && n.EntityKey == key).OriginId;
        private static CargoPhysicalResult Applied(CargoPhysicalComponent owner, CargoPhysicalReceipt receipt)
        {
            var result = owner.Apply(receipt, Work);
            Assert.That(result.Status, Is.EqualTo(CargoPhysicalStatus.Applied), result.Detail);
            Assert.That(owner.IsCurrent(result.Stamp), Is.True);
            return result;
        }
        private static CargoPhysicalFlowView View(CargoPhysicalComponent owner, int selection, bool cohort, params int[] targets)
        {
            var stamp = Snap(owner).Stamp;
            var result = cohort ? owner.CompileCohort(stamp, selection, targets, Work)
                : owner.CompileOrigin(stamp, selection, targets, Work);
            Assert.That(result.Status, Is.EqualTo(CargoPhysicalStatus.ViewCompiled), result.Detail);
            return result.View;
        }
        private static void Bounds(CargoPhysicalFlowView view, long minimum, long maximum)
        {
            var result = CargoFlowBounds.Measure(view.Nodes.ToArray(), view.Edges.ToArray(), Work);
            Assert.That(result.Status, Is.EqualTo(CargoFlowStatus.Complete), result.Detail);
            Assert.That(new[] { result.Minimum, result.Maximum }, Is.EqualTo(new long?[] { minimum, maximum }));
        }
        private static string Physical(CargoPhysicalSnapshot s) => string.Join("|", s.Nodes.Select(n =>
            $"{n.Id}/{n.Kind}/{n.EntityKey}/{n.ContextKey}/{n.Quantity}/{n.BirthQuantity}/{n.OriginId}/{n.CreatedSequence}/{n.Suboperation}/{n.Frontier}"))
            + ";" + string.Join("|", s.Edges.Select(e => $"{e.From}/{e.To}/{e.Quantity}"))
            + ";" + string.Join("|", s.Cohorts.Select(c => $"{c.Id}/{c.OperationId}/{c.Sequence}/{c.Boundary}/{c.SelectedNodeId}/{c.NextNodeId}/{c.Quantity}:" + string.Join(",", c.CompleteCut)))
            + $";{s.TotalObservedBirthQuantity}/{s.AcceptedReceiptCount}";
        private static void RejectedAtomically(CargoPhysicalComponent owner, CargoPhysicalSnapshot before,
            CargoPhysicalResult result, CargoPhysicalStatus expected)
        {
            Assert.That(result.Status, Is.EqualTo(expected), result.Detail);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.CohortId, Is.Null);
            Assert.That(result.Stamp.Complete, Is.False);
            Assert.That(owner.IsCurrent(before.Stamp), Is.False);
            var after = Snap(owner);
            Assert.That(Physical(after), Is.EqualTo(Physical(before)), "An observed failure must not commit a partial graph/cut/receipt.");
            Assert.That(after.Stamp.GraphVersion, Is.EqualTo(before.Stamp.GraphVersion));
            Assert.That(after.Stamp.CoverageRevision, Is.EqualTo(before.Stamp.CoverageRevision + 1));
            Assert.That(after.CoverageReason, Is.EqualTo(expected));
            Assert.That(owner.CompileOrigin(after.Stamp, 1, Array.Empty<int>(), Work).Status,
                Is.EqualTo(CargoPhysicalStatus.CoverageInvalidated));
        }

        [Test]
        public void InitialCensus_PartialPickupKeepsUntouchedOriginAndCapturesOnlyMovedQuantity()
        {
            var owner = Create(O("source", 12), O("other", 7));
            var before = Snap(owner);
            var applied = Applied(owner, CargoPhysicalReceipt.Split(Session, Component, 1, 10, V(owner, "source"),
                O("source", 8), O("picked", 4, "inventory"), true));
            var after = Snap(owner);
            Assert.That(after.TotalObservedBirthQuantity, Is.EqualTo(19));
            Assert.That(after.Nodes.Count(n => n.BirthQuantity > 0), Is.EqualTo(2));
            Assert.That(after.FrontierEntities.Count, Is.EqualTo(3));
            Assert.That(applied.Outputs.Select(v => v.Observation.Quantity), Is.EqualTo(new long[] { 8, 4 }));
            Assert.That(owner.IsCurrent(before.Stamp), Is.False);
            Assert.That(after.Cohorts.Single().Boundary, Is.EqualTo(CargoCohortBoundary.AfterSplit));
            Bounds(View(owner, Origin(owner, "source"), false, V(owner, "source").VersionId), 8, 8);
            Bounds(View(owner, applied.CohortId.Value, true, V(owner, "source").VersionId), 0, 0);
            Bounds(View(owner, applied.CohortId.Value, true, V(owner, "picked").VersionId), 4, 4);
            Bounds(View(owner, applied.CohortId.Value, true, after.Nodes.Where(n => n.Frontier).Select(n => n.Id).ToArray()), 4, 4);
            Assert.That(after.Receipts.Single().Sequence, Is.EqualTo(10));
        }

        [Test]
        public void PartialAbsorb_UsesDirectionalTransferCutAndRetainsSameReceiptTargetMerge()
        {
            var owner = Create(O("source", 2), O("target", 2, "inventory"));
            int sourceOrigin = Origin(owner, "source"), targetOrigin = Origin(owner, "target");
            int oldTarget = V(owner, "target").VersionId;
            var result = Applied(owner, CargoPhysicalReceipt.Absorb(Session, Component, 1, 1,
                V(owner, "source"), V(owner, "target"), O("source", 1), O("target", 3, "inventory"), true));
            var snapshot = Snap(owner); var cut = snapshot.Cohorts.Single();
            int remainder = V(owner, "source").VersionId, target = V(owner, "target").VersionId;
            var transfer = snapshot.Nodes.Single(n => n.Kind == CargoPhysicalNodeKind.MeasuredTransfer);
            Assert.That(cut.Boundary, Is.EqualTo(CargoCohortBoundary.AfterSourceSeparationBeforeAbsorb));
            Assert.That(cut.CompleteCut, Is.EquivalentTo(new[] { oldTarget, remainder, transfer.Id }));
            Assert.That(cut.NextNodeId, Is.EqualTo(target));
            Assert.That(transfer.EntityKey, Is.Null);
            Assert.That(transfer.BirthQuantity, Is.Zero);
            Assert.That(transfer.Frontier, Is.False);
            Assert.That(snapshot.Nodes.Count(n => n.BirthQuantity > 0), Is.EqualTo(2));
            Bounds(View(owner, targetOrigin, false, remainder), 0, 0);
            Bounds(View(owner, targetOrigin, false, target), 2, 2);
            Bounds(View(owner, sourceOrigin, false, remainder), 1, 1);
            Bounds(View(owner, sourceOrigin, false, target), 1, 1);
            var cohort = View(owner, result.CohortId.Value, true, target);
            Assert.That(cohort.OwnedNodeIds, Does.Contain(target));
            Assert.That(cohort.OwnedNodeIds, Does.Contain(oldTarget));
            Bounds(cohort, 1, 1);
            Bounds(View(owner, result.CohortId.Value, true, remainder), 0, 0);
        }

        [Test]
        public void CohortView_IncludesEarlierSinksLaterBirthsAndEveryLaterTransformation()
        {
            var owner = Create(O("source", 8), O("old", 2));
            Applied(owner, CargoPhysicalReceipt.Sink(Session, Component, 1, 1, V(owner, "old"), null, 2, "consumed"));
            int oldSink = Snap(owner).Nodes.Single(n => n.Kind == CargoPhysicalNodeKind.AccountedSink).Id;
            var picked = Applied(owner, CargoPhysicalReceipt.Split(Session, Component, 2, 2,
                V(owner, "source"), O("source", 5), O("picked", 3, "inventory"), true));
            Applied(owner, CargoPhysicalReceipt.Birth(Session, Component, 3, 3, O("new", 4, "inventory")));
            Applied(owner, CargoPhysicalReceipt.Absorb(Session, Component, 4, 4,
                V(owner, "picked"), V(owner, "new"), null, O("new", 7, "inventory")));
            Applied(owner, CargoPhysicalReceipt.Sink(Session, Component, 5, 5,
                V(owner, "new"), O("new", 5, "inventory"), 2, "recipe"));
            int newSink = Snap(owner).Nodes.Last(n => n.Kind == CargoPhysicalNodeKind.AccountedSink).Id;
            var snapshot = Snap(owner);
            Assert.That(snapshot.Cohorts.Single().CompleteCut, Does.Contain(oldSink));
            var all = View(owner, picked.CohortId.Value, true, snapshot.Nodes.Where(n => n.Frontier).Select(n => n.Id).ToArray());
            Assert.That(all.Nodes.Where(n => n.Frontier).Sum(n => n.Quantity), Is.EqualTo(14));
            Assert.That(all.OwnedNodeIds, Does.Contain(oldSink));
            Bounds(all, 3, 3);
            Bounds(View(owner, picked.CohortId.Value, true, oldSink), 0, 0);
            Bounds(View(owner, picked.CohortId.Value, true, newSink), 0, 2);
            Bounds(View(owner, picked.CohortId.Value, true, V(owner, "new").VersionId, newSink), 3, 3);
            Bounds(View(owner, picked.CohortId.Value, true, V(owner, "source").VersionId), 0, 0);
        }

        [Test]
        public void MixedHistory_OwnerViewsMatchIndependentFeasibleAllocations()
        {
            // Physical choices: split mixed A2+B2 into X2/rest2, then transfer one from rest
            // into unrelated C2. Enumerate possible B counts without assigning hidden labels.
            var owner = Create(O("a", 2), O("b", 2), O("c", 2));
            int selected = Origin(owner, "b"), cOrigin = Origin(owner, "c");
            Applied(owner, CargoPhysicalReceipt.Absorb(Session, Component, 1, 1,
                V(owner, "b"), V(owner, "a"), null, O("a", 4)));
            Applied(owner, CargoPhysicalReceipt.Split(Session, Component, 2, 2,
                V(owner, "a"), O("a", 2), O("x", 2)));
            var transfer = Applied(owner, CargoPhysicalReceipt.Absorb(Session, Component, 3, 3,
                V(owner, "a"), V(owner, "c"), O("a", 1), O("c", 3), true));
            var feasibleSubgroup = new List<int>(); var feasibleTarget = new List<int>();
            for (int bInX = 0; bInX <= 2; bInX++)
                for (int bMoved = 0; bMoved <= 1; bMoved++)
                {
                    int bInRestBefore = 2 - bInX, bInResidual = bInRestBefore - bMoved;
                    if (bInResidual < 0 || bInResidual > 1 || bMoved > bInRestBefore) continue;
                    feasibleSubgroup.Add(bInX + bInResidual); feasibleTarget.Add(bMoved);
                }
            Assert.That(feasibleSubgroup.Distinct(), Is.EquivalentTo(new[] { 1, 2 }));
            Bounds(View(owner, selected, false, V(owner, "x").VersionId, V(owner, "a").VersionId),
                feasibleSubgroup.Min(), feasibleSubgroup.Max());
            Bounds(View(owner, selected, false, V(owner, "c").VersionId), feasibleTarget.Min(), feasibleTarget.Max());
            Bounds(View(owner, cOrigin, false, V(owner, "a").VersionId, V(owner, "x").VersionId), 0, 0);
            Bounds(View(owner, transfer.CohortId.Value, true, V(owner, "c").VersionId), 1, 1);
        }

        [Test]
        public void Move_ProducesSoleCurrentVersionAndExactDuplicateDoesNotReplayOldOutput()
        {
            var owner = Create(O("a", 3)); var original = V(owner, "a");
            var receipt = CargoPhysicalReceipt.Move(Session, Component, 10, 20, original, O("a", 3, "inventory"), true);
            var first = Applied(owner, receipt); var after = Snap(owner);
            Assert.That(first.Outputs.Single().VersionId, Is.Not.EqualTo(original.VersionId));
            Assert.That(after.FrontierEntities.Select(x => x.Observation.EntityKey), Is.EqualTo(new[] { "a" }));
            var equivalent = CargoPhysicalReceipt.Move(Session, Component, 10, 20, original, O("a", 3, "inventory"), true);
            var duplicate = owner.Apply(equivalent, Work);
            Assert.That(duplicate.Status, Is.EqualTo(CargoPhysicalStatus.ExactDuplicate));
            Assert.That(duplicate.Outputs, Is.Empty);
            Assert.That(duplicate.CohortId, Is.EqualTo(first.CohortId));
            Assert.That(owner.IsCurrent(first.Stamp), Is.True);
            Assert.That(Physical(Snap(owner)), Is.EqualTo(Physical(after)));
            Bounds(View(owner, first.CohortId.Value, true, V(owner, "a").VersionId), 3, 3);
        }

        [Test]
        public void StaleInput_InvalidatesCurrentCoverageWithoutErasingTrustedHistory()
        {
            var owner = Create(O("a", 3)); var old = V(owner, "a");
            var receipt = CargoPhysicalReceipt.Move(Session, Component, 1, 1, old, O("a", 3, "inventory"));
            Applied(owner, receipt); var before = Snap(owner);
            var oldView = View(owner, 1, false, V(owner, "a").VersionId);
            var failed = owner.Apply(CargoPhysicalReceipt.Move(Session, Component, 2, 2, old, O("a", 3, "hands")), Work);
            RejectedAtomically(owner, before, failed, CargoPhysicalStatus.StaleVersion);
            Assert.That(owner.IsCurrent(oldView.Stamp), Is.False);
            var duplicate = owner.Apply(receipt, Work);
            Assert.That(duplicate.Status, Is.EqualTo(CargoPhysicalStatus.ExactDuplicate));
            Assert.That(duplicate.Stamp.Complete, Is.False, "A retained duplicate cannot resynchronize lost coverage.");
            long revision = duplicate.Stamp.CoverageRevision;
            var refused = owner.Apply(CargoPhysicalReceipt.Move(Session, Component, 3, 3, V(owner, "a"), O("a", 3, "hands")), Work);
            Assert.That(refused.Status, Is.EqualTo(CargoPhysicalStatus.CoverageInvalidated));
            Assert.That(refused.Stamp.CoverageRevision, Is.EqualTo(revision));
            Assert.That(Snap(owner).CoverageReason, Is.EqualTo(CargoPhysicalStatus.StaleVersion));
        }

        [TestCase("same-id", CargoPhysicalStatus.ConflictingReceipt)]
        [TestCase("same-sequence", CargoPhysicalStatus.ConflictingReceipt)]
        [TestCase("older-unknown", CargoPhysicalStatus.ReceiptHistoryUnavailable)]
        [TestCase("older-id", CargoPhysicalStatus.ReceiptHistoryUnavailable)]
        [TestCase("older-sequence", CargoPhysicalStatus.ReceiptHistoryUnavailable)]
        public void ReceiptIdentity_IsNeverEvictedOrReinterpreted(string variant, CargoPhysicalStatus expected)
        {
            var owner = Create(O("a", 2)); var original = V(owner, "a");
            Applied(owner, CargoPhysicalReceipt.Move(Session, Component, 10, 10, original, O("a", 2, "inventory")));
            var before = Snap(owner); var current = V(owner, "a");
            long id = variant == "same-id" ? 10 : variant == "older-unknown" || variant == "older-id" ? 9 : 11;
            long sequence = variant == "same-sequence" ? 10 : variant == "older-unknown" || variant == "older-sequence" ? 9 : 11;
            var result = owner.Apply(CargoPhysicalReceipt.Move(Session, Component, id, sequence, current, O("a", 2, "hands")), Work);
            RejectedAtomically(owner, before, result, expected);
        }

        [Test]
        public void DestroyedKey_CannotBeReadmittedAsFreshOrigin()
        {
            var owner = Create(O("a", 2));
            Applied(owner, CargoPhysicalReceipt.Sink(Session, Component, 1, 1, V(owner, "a"), null, 2, "consumed"));
            var before = Snap(owner);
            Assert.That(before.FrontierEntities, Is.Empty);
            Bounds(View(owner, 1, false, before.Nodes.Single(n => n.Frontier).Id), 2, 2);
            RejectedAtomically(owner, before, owner.Apply(CargoPhysicalReceipt.Birth(Session, Component, 2, 2, O("a", 2)), Work),
                CargoPhysicalStatus.DuplicateEntity);
        }

        [Test]
        public void CrossComponentParticipant_RequiresFutureAtomicCoordinatorAndCannotImportHistory()
        {
            var owner = Create(O("a", 2));
            var creation = CargoPhysicalComponent.TryCreate(Session, Guid.NewGuid(), new[] { O("foreign", 3) }, Work, out var other);
            Assert.That(creation.Status, Is.EqualTo(CargoPhysicalStatus.Created));
            var before = Snap(owner); var foreignBefore = Snap(other);
            var receipt = CargoPhysicalReceipt.Absorb(Session, Component, 1, 1,
                V(other, "foreign"), V(owner, "a"), null, O("a", 5));
            RejectedAtomically(owner, before, owner.Apply(receipt, Work), CargoPhysicalStatus.CrossComponentUnsupported);
            Assert.That(other.IsCurrent(foreignBefore.Stamp), Is.True);
            Assert.That(Physical(Snap(other)), Is.EqualTo(Physical(foreignBefore)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ForeignReceiptHeader_FailsClosed(bool session)
        {
            var owner = Create(O("a", 2)); var before = Snap(owner);
            var receipt = CargoPhysicalReceipt.Move(session ? Guid.NewGuid() : Session, session ? Component : Guid.NewGuid(),
                1, 1, V(owner, "a"), O("a", 2, "inventory"));
            RejectedAtomically(owner, before, owner.Apply(receipt, Work),
                session ? CargoPhysicalStatus.SessionMismatch : CargoPhysicalStatus.ComponentMismatch);
        }

        [TestCase("wrong-count")]
        [TestCase("wrong-context")]
        [TestCase("self-absorb")]
        [TestCase("no-op-move")]
        [TestCase("sink-delta")]
        [TestCase("null-receipt")]
        public void InvalidObservedPrimitive_NeverPublishesPartialOutput(string variant)
        {
            var owner = Create(O("a", 4), O("b", 2)); var before = Snap(owner);
            CargoPhysicalReceipt receipt;
            switch (variant)
            {
                case "wrong-count": receipt = CargoPhysicalReceipt.Split(Session, Component, 1, 1, V(owner, "a"), O("a", 2), O("new", 3)); break;
                case "wrong-context": receipt = CargoPhysicalReceipt.Absorb(Session, Component, 1, 1, V(owner, "a"), V(owner, "b"), O("a", 2, "elsewhere"), O("b", 4)); break;
                case "self-absorb": receipt = CargoPhysicalReceipt.Absorb(Session, Component, 1, 1, V(owner, "a"), V(owner, "a"), null, O("a", 8)); break;
                case "no-op-move": receipt = CargoPhysicalReceipt.Move(Session, Component, 1, 1, V(owner, "a"), O("a", 4)); break;
                case "sink-delta": receipt = CargoPhysicalReceipt.Sink(Session, Component, 1, 1, V(owner, "a"), O("a", 1), 2, "consumed"); break;
                default: receipt = null; break;
            }
            RejectedAtomically(owner, before, owner.Apply(receipt, Work), CargoPhysicalStatus.InvalidReceipt);
        }

        [Test]
        public void NodeCap_IsAtomicAndPreservesEveryAcceptedReceiptWithoutEviction()
        {
            var owner = Create(O("a", 1));
            for (int i = 1; i < CargoPhysicalComponent.MaximumNodes; i++)
                Applied(owner, CargoPhysicalReceipt.Move(Session, Component, i, i, V(owner, "a"), O("a", 1, "context-" + i)));
            var before = Snap(owner);
            Assert.That(before.Nodes.Count, Is.EqualTo(128));
            Assert.That(before.AcceptedReceiptCount, Is.EqualTo(127));
            Assert.That(before.Receipts.Select(r => r.OperationId), Is.EqualTo(Enumerable.Range(1, 127).Select(i => (long)i)));
            var result = owner.Apply(CargoPhysicalReceipt.Move(Session, Component, 128, 128, V(owner, "a"), O("a", 1, "last")), Work);
            RejectedAtomically(owner, before, result, CargoPhysicalStatus.NodeLimit);
            Assert.That(owner.Apply(before.Receipts.First(), Work).Status, Is.EqualTo(CargoPhysicalStatus.ExactDuplicate));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FrontierCap_CoversInternalMeasuredCutAndCommitsNoPartialReceipt(bool absorb)
        {
            var owner = Create(Enumerable.Range(0, 32).Select(i => O("a" + i, 2)).ToArray()); var before = Snap(owner);
            var receipt = absorb
                ? CargoPhysicalReceipt.Absorb(Session, Component, 1, 1, V(owner, "a0"), V(owner, "a1"), O("a0", 1), O("a1", 3), true)
                : CargoPhysicalReceipt.Split(Session, Component, 1, 1, V(owner, "a0"), O("a0", 1), O("child", 1), true);
            RejectedAtomically(owner, before, owner.Apply(receipt, Work), CargoPhysicalStatus.FrontierLimit);
        }

        [Test]
        public void WorkCutoffs_AbortEveryStageOfObservedAbsorbAtomically()
        {
            var reference = Create(O("source", 3), O("target", 2));
            var full = Applied(reference, CargoPhysicalReceipt.Absorb(Session, Component, 1, 1,
                V(reference, "source"), V(reference, "target"), O("source", 1), O("target", 4), true));
            // Every integer cutoff exercises staged copy, separation, cut, merge, validation and
            // publication. Quantity is small, but the budget never iterates over cargo units.
            for (long allowance = 0; allowance <= full.WorkSpent; allowance++)
            {
                var owner = Create(O("source", 3), O("target", 2)); var before = Snap(owner);
                var result = owner.Apply(CargoPhysicalReceipt.Absorb(Session, Component, 1, 1,
                    V(owner, "source"), V(owner, "target"), O("source", 1), O("target", 4), true), allowance);
                if (allowance == full.WorkSpent)
                {
                    Assert.That(result.Status, Is.EqualTo(CargoPhysicalStatus.Applied));
                    Assert.That(Physical(Snap(owner)), Is.EqualTo(Physical(Snap(reference))));
                }
                else
                {
                    RejectedAtomically(owner, before, result, CargoPhysicalStatus.WorkLimit);
                    Assert.That(result.WorkSpent, Is.EqualTo(allowance));
                }
            }
        }

        [Test]
        public void SnapshotAndViewCutoffs_NeverMutateCoverageOrExposePartialArrays()
        {
            var owner = Create(O("a", 4), O("b", 2));
            var moved = Applied(owner, CargoPhysicalReceipt.Absorb(Session, Component, 1, 1,
                V(owner, "a"), V(owner, "b"), O("a", 2), O("b", 4), true));
            var stamp = Snap(owner).Stamp; int target = V(owner, "b").VersionId;
            var fullSnapshot = owner.CaptureSnapshot(Work);
            for (long allowance = 0; allowance <= fullSnapshot.WorkSpent; allowance++)
            {
                var result = owner.CaptureSnapshot(allowance);
                Assert.That(result.Status, Is.EqualTo(allowance < fullSnapshot.WorkSpent ? CargoPhysicalStatus.WorkLimit : CargoPhysicalStatus.SnapshotCaptured));
                Assert.That(result.Snapshot == null, Is.EqualTo(allowance < fullSnapshot.WorkSpent));
                Assert.That(owner.IsCurrent(stamp), Is.True);
            }
            foreach (bool cohort in new[] { false, true })
            {
                int selection = cohort ? moved.CohortId.Value : 1;
                var full = cohort ? owner.CompileCohort(stamp, selection, new[] { target }, Work) : owner.CompileOrigin(stamp, selection, new[] { target }, Work);
                Assert.That(full.Status, Is.EqualTo(CargoPhysicalStatus.ViewCompiled));
                for (long allowance = 0; allowance <= full.WorkSpent; allowance++)
                {
                    var result = cohort ? owner.CompileCohort(stamp, selection, new[] { target }, allowance) : owner.CompileOrigin(stamp, selection, new[] { target }, allowance);
                    Assert.That(result.Status, Is.EqualTo(allowance < full.WorkSpent ? CargoPhysicalStatus.WorkLimit : CargoPhysicalStatus.ViewCompiled));
                    Assert.That(result.View == null, Is.EqualTo(allowance < full.WorkSpent));
                    Assert.That(owner.IsCurrent(stamp), Is.True);
                }
            }
            Assert.That(Physical(Snap(owner)), Is.EqualTo(Physical(fullSnapshot.Snapshot)));
        }

        [Test]
        public void UnknownSelectionAndStaleSnapshot_AreUnknownNotEmptyAnswers()
        {
            var owner = Create(O("a", 4)); var old = Snap(owner);
            Applied(owner, CargoPhysicalReceipt.Move(Session, Component, 1, 1, V(owner, "a"), O("a", 4, "inventory")));
            var current = Snap(owner); int target = V(owner, "a").VersionId;
            Assert.That(owner.CompileOrigin(old.Stamp, 1, new[] { target }, Work).Status, Is.EqualTo(CargoPhysicalStatus.StaleSnapshot));
            Assert.That(owner.CompileOrigin(default, 1, new[] { target }, Work).Status, Is.EqualTo(CargoPhysicalStatus.StaleSnapshot));
            Assert.That(owner.CompileOrigin(current.Stamp, 999, new[] { target }, Work).Status, Is.EqualTo(CargoPhysicalStatus.UnknownSelection));
            Assert.That(owner.CompileCohort(current.Stamp, 1, new[] { target }, Work).Status, Is.EqualTo(CargoPhysicalStatus.UnknownSelection));
            foreach (var targets in new[] { new[] { 1 }, new[] { 0 }, new[] { int.MaxValue }, new[] { target, target }, null })
                Assert.That(owner.CompileOrigin(current.Stamp, 1, targets, Work).Status, Is.EqualTo(CargoPhysicalStatus.UnknownSelection));
            Assert.That(owner.CaptureSnapshot(-1).Snapshot, Is.Null);
            Assert.That(owner.CompileOrigin(current.Stamp, 1, new[] { target }, -1).View, Is.Null);
            Assert.That(owner.IsCurrent(current.Stamp), Is.True);
            Bounds(View(owner, 1, false), 0, 0); // Deliberately empty, explicitly requested objective.
        }

        [Test]
        public void LongQuantities_UseSameWorkAndCheckedTotalAdmission()
        {
            var large = Create(O("a", long.MaxValue)); var small = Create(O("a", 10));
            var largeMove = Applied(large, CargoPhysicalReceipt.Move(Session, Component, 1, 1, V(large, "a"), O("a", long.MaxValue, "inventory"), true));
            var smallMove = Applied(small, CargoPhysicalReceipt.Move(Session, Component, 1, 1, V(small, "a"), O("a", 10, "inventory"), true));
            Assert.That(largeMove.WorkSpent, Is.EqualTo(smallMove.WorkSpent));
            Bounds(View(large, 1, false, V(large, "a").VersionId), long.MaxValue, long.MaxValue);
            Applied(large, CargoPhysicalReceipt.Split(Session, Component, 2, 2, V(large, "a"),
                O("a", long.MaxValue - 1, "inventory"), O("child", 1, "hands")));
            Bounds(View(large, largeMove.CohortId.Value, true, V(large, "child").VersionId), 1, 1);
            var before = Snap(large);
            RejectedAtomically(large, before, large.Apply(CargoPhysicalReceipt.Birth(Session, Component, 3, 3, O("extra", 1)), Work), CargoPhysicalStatus.ArithmeticOverflow);
            var creation = CargoPhysicalComponent.TryCreate(Session, Component, new[] { O("x", long.MaxValue), O("y", 1) }, Work, out var absent);
            Assert.That(creation.Status, Is.EqualTo(CargoPhysicalStatus.ArithmeticOverflow));
            Assert.That(absent, Is.Null);
        }

        [Test]
        public void PublicSnapshotsAndInputs_AreImmutableAndNotCallerOwned()
        {
            var census = new[] { O("a", 4) }; var owner = Create(census); census[0] = O("changed", 99);
            var snapshot = Snap(owner);
            Assert.That(snapshot.FrontierEntities.Single().Observation.EntityKey, Is.EqualTo("a"));
            Assert.Throws<NotSupportedException>(() => ((IList<CargoPhysicalNode>)snapshot.Nodes)[0] = default);
            Assert.Throws<NotSupportedException>(() => ((IList<CargoFlowEdge>)snapshot.Edges).Add(default));
            var moved = Applied(owner, CargoPhysicalReceipt.Move(Session, Component, 1, 1, V(owner, "a"), O("a", 4, "inventory"), true));
            var current = Snap(owner);
            Assert.Throws<NotSupportedException>(() => ((IList<CargoPhysicalReceipt>)current.Receipts)[0] = null);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)current.Cohorts.Single().CompleteCut)[0] = 123);
            int[] targets = { V(owner, "a").VersionId };
            var compiled = owner.CompileCohort(current.Stamp, moved.CohortId.Value, targets, Work);
            targets[0] = 123;
            Assert.Throws<NotSupportedException>(() => ((IList<CargoFlowNode>)compiled.View.Nodes)[0] = default);
            Bounds(compiled.View, 4, 4);
            Assert.That(snapshot.Nodes.Single().Frontier, Is.True, "An old snapshot keeps its historical frontier while its stamp becomes stale.");
            Assert.That(owner.IsCurrent(snapshot.Stamp), Is.False);
        }

        [Test]
        public void InvalidInitialAdmission_DoesNotCreateAnOwner()
        {
            var cases = new[] { Array.Empty<CargoPhysicalObservation>(), new[] { O("a", 0) }, new[] { O("a", -1) },
                new[] { O(" ", 1) }, new[] { O(new string('x', 129), 1) }, new[] { O("a", 1, "") },
                new[] { O("a", 1), O("a", 2) }, Enumerable.Range(0, 33).Select(i => O("a" + i, 1)).ToArray(), null };
            foreach (var census in cases)
            {
                var result = CargoPhysicalComponent.TryCreate(Session, Component, census, Work, out var owner);
                Assert.That(result.Status, Is.Not.EqualTo(CargoPhysicalStatus.Created)); Assert.That(owner, Is.Null);
            }
            Assert.That(CargoPhysicalComponent.TryCreate(Guid.Empty, Component, new[] { O("a", 1) }, Work, out _).Status, Is.EqualTo(CargoPhysicalStatus.InvalidReceipt));
            Assert.That(CargoPhysicalComponent.TryCreate(Session, Component, new[] { O("a", 1) }, 0, out _).Status, Is.EqualTo(CargoPhysicalStatus.WorkLimit));
        }
    }
}
