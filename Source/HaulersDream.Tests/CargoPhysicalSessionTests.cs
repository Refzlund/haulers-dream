using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CargoPhysicalSessionTests
    {
        private const long Work = 1000000;
        private static readonly Guid SessionId = Guid.Parse("90383863-d42e-403b-b364-881a8302e513");
        private static CargoPhysicalObservation O(string key, long quantity, string context = "floor") => new CargoPhysicalObservation(key, quantity, context);
        private static CargoPhysicalSession Create(CargoSessionLimits limits = null)
        {
            var result = CargoPhysicalSession.TryCreate(SessionId, limits ?? new CargoSessionLimits(), out var session);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.Created)); return session;
        }
        private static CargoSessionResult Admit(CargoPhysicalSession session, string key, long quantity, long id, int tick = 7)
        {
            var result = session.AdmitObserved(O(key, quantity), id, tick, Work);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.Admitted), result.Detail); return result;
        }
        private static CargoEntityHandle V(CargoPhysicalSession session, string key)
        {
            var result = session.FindCurrent(key);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.Current), result.Detail); return result.Outputs.Single();
        }
        private static CargoSessionSnapshot Snap(CargoPhysicalSession session, CargoComponentHandle component)
        {
            var result = session.CaptureSnapshot(component, Work);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.SnapshotCaptured), result.Detail); return result.Snapshot;
        }
        private static CargoSessionSnapshot Snap(CargoPhysicalSession session, string key) => Snap(session, V(session, key).Component);
        private static CargoSessionResult Apply(CargoPhysicalSession session, CargoSessionReceipt receipt)
        {
            var result = session.ApplyObserved(receipt, Work);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.Applied), result.Detail);
            Assert.That(session.IsCurrent(result.Stamp), Is.True); return result;
        }
        private static CargoSessionReceipt Absorb(CargoPhysicalSession session, long id, string source, string target,
            long remainder, long after, bool cohort = false)
            => CargoSessionReceipt.Absorb(id, 7, CargoSessionParticipant.Tracked(V(session, source)), CargoSessionParticipant.Tracked(V(session, target)),
                remainder == 0 ? (CargoPhysicalObservation?)null : O(source, remainder), O(target, after), cohort);
        private static CargoFrontierHandle[] Objective(CargoSessionSnapshot s, params string[] keys)
            => s.Frontier.Where(f => keys.Contains(s.Physical.Nodes[f.NodeId - 1].EntityKey)).ToArray();
        private static CargoPhysicalFlowView OriginView(CargoPhysicalSession session, CargoOriginHandle origin, CargoSessionSnapshot snapshot, params string[] keys)
        {
            var result = session.CompileOrigin(snapshot.Stamp, origin, Objective(snapshot, keys), Work);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.ViewCompiled), result.Detail); return result.View;
        }
        private static CargoPhysicalFlowView CohortView(CargoPhysicalSession session, CargoCohortHandle cohort, CargoSessionSnapshot snapshot, params string[] keys)
        {
            var result = session.CompileCohort(snapshot.Stamp, cohort, Objective(snapshot, keys), Work);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.ViewCompiled), result.Detail); return result.View;
        }
        private static void Bounds(CargoPhysicalFlowView view, long minimum, long maximum)
        {
            var result = CargoFlowBounds.Measure(view.Nodes.ToArray(), view.Edges.ToArray(), Work);
            Assert.That(result.Status, Is.EqualTo(CargoFlowStatus.Complete), result.Detail);
            Assert.That(new[] { result.Minimum, result.Maximum }, Is.EqualTo(new long?[] { minimum, maximum }));
        }
        private static string Physical(CargoSessionSnapshot snapshot) => string.Join("|", snapshot.Physical.Nodes.Select(n =>
            $"{n.Id}/{n.Kind}/{n.EntityKey}/{n.ContextKey}/{n.Quantity}/{n.BirthQuantity}/{n.OriginId}/{n.CreatedSequence}/{n.Suboperation}/{n.Frontier}"))
            + ";" + string.Join("|", snapshot.Physical.Edges.Select(e => $"{e.From}/{e.To}/{e.Quantity}"))
            + ";" + string.Join("|", snapshot.Physical.Cohorts.Select(c => $"{c.Id}/{c.SelectedNodeId}/{c.NextNodeId}/{c.Quantity}:" + string.Join(",", c.CompleteCut)))
            + ";" + string.Join("|", snapshot.Physical.Receipts.Select(r => $"{r.OperationId}/{r.Sequence}/{r.Kind}/{r.Component}/{r.Source.VersionId}/{r.Target.VersionId}"));

        [TestCase(false)]
        [TestCase(true)]
        public void CrossComponentPartialAbsorb_PreservesOriginsBothEarlierCohortsAndSourceOnlyResidual(bool reverse)
        {
            var s = Create(); var a = Admit(s, "a", 4, 1).Origin.Value; var b = Admit(s, "b", 3, 2).Origin.Value;
            var ca = Apply(s, CargoSessionReceipt.Split(3, 7, V(s, "a"), O("a", 2), O("ap", 2), true)).Cohort.Value;
            Apply(s, CargoSessionReceipt.Split(4, 7, V(s, "b"), O("b", 2), O("bp", 1)));
            Apply(s, Absorb(s, 5, "bp", "b", 0, 3));
            var cb = Apply(s, CargoSessionReceipt.Move(6, 7, V(s, "b"), O("b", 3, "inventory"), true)).Cohort.Value;
            Apply(s, CargoSessionReceipt.Move(7, 7, V(s, "b"), O("b", 3), false));
            var oldA = Snap(s, "a"); var oldB = Snap(s, "b");
            if (reverse) Apply(s, Absorb(s, 8, "b", "ap", 1, 4, true));
            else Apply(s, Absorb(s, 8, "ap", "b", 1, 4, true));
            var after = Snap(s, "b");
            Assert.That(after.Physical.TotalObservedBirthQuantity, Is.EqualTo(7));
            Assert.That(after.Origins.Count, Is.EqualTo(2)); Assert.That(after.Cohorts.Count, Is.EqualTo(3));
            Assert.That(s.IsCurrent(oldA.Stamp), Is.False); Assert.That(s.IsCurrent(oldB.Stamp), Is.False);
            Assert.That(Physical(Snap(s, oldA.Stamp.Component)), Is.EqualTo(Physical(oldA)));
            Assert.That(Physical(Snap(s, oldB.Stamp.Component)), Is.EqualTo(Physical(oldB)));
            Bounds(OriginView(s, a, after, "a"), 2, 2);
            Bounds(OriginView(s, b, after, "a"), 0, 0);
            Bounds(CohortView(s, ca, after, "ap", "b"), 2, 2);
            Bounds(CohortView(s, cb, after, "ap", "b"), 3, 3);
            Bounds(OriginView(s, reverse ? b : a, after, reverse ? "b" : "ap"), 1, 1);
            Assert.That(after.Physical.Nodes.Select(n => n.CreatedSequence), Is.Ordered);
            Assert.That(after.Physical.Receipts.All(r => r.Component == after.Stamp.Physical.Component), Is.True);
            CheckAllTinyObjectives(s, after);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HistoricalCut_IncludesEarlierComplementButKeepsLaterAdmissionAsLaterInjection(bool admitBefore)
        {
            var s = Create(); Admit(s, "a", 2, 1);
            long id = 2; if (admitBefore) Admit(s, "b", 2, id++);
            var cohort = Apply(s, CargoSessionReceipt.Split(id++, 7, V(s, "a"), O("a", 1), O("ap", 1), true)).Cohort.Value;
            if (!admitBefore) Admit(s, "b", 2, id++);
            Apply(s, CargoSessionReceipt.Split(id++, 7, V(s, "b"), O("b", 1), O("bp", 1), true));
            Apply(s, Absorb(s, id, "ap", "b", 0, 2));
            var snapshot = Snap(s, "b"); var row = snapshot.Cohorts.Single(c => c.Handle.Equals(cohort));
            var cut = snapshot.Physical.Cohorts.Single(c => c.Id == row.LocalId);
            var bBirth = snapshot.Physical.Nodes.Single(n => n.EntityKey == "b" && n.BirthQuantity > 0);
            Assert.That(cut.CompleteCut.Contains(bBirth.Id), Is.EqualTo(admitBefore));
            Assert.That(bBirth.Id >= cut.NextNodeId, Is.EqualTo(!admitBefore));
            Bounds(CohortView(s, cohort, snapshot, "b"), 1, 1);
            Bounds(CohortView(s, cohort, snapshot, "bp"), 0, 0);
            CheckAllTinyObjectives(s, snapshot);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AbsorbInternalCut_RetainsExactFullOrPartialSeparationBeforeTargetMerge(bool partial)
        {
            var s = Create(); Admit(s, "a", 2, 1);
            Apply(s, CargoSessionReceipt.Birth(2, 7, V(s, "a"), O("at", 1)));
            var cohort = Apply(s, Absorb(s, 3, "a", "at", partial ? 1 : 0, partial ? 2 : 3, true)).Cohort.Value;
            var old = Snap(s, "at"); var oldCut = old.Physical.Cohorts.Single();
            Assert.That(old.Physical.Nodes[oldCut.NextNodeId - 2].Suboperation, Is.EqualTo(partial ? 2 : 1));
            Admit(s, "b", 1, 4); Apply(s, Absorb(s, 5, "b", "at", 0, partial ? 3 : 4));
            var snapshot = Snap(s, "at"); var cut = snapshot.Physical.Cohorts.Single();
            Assert.That(snapshot.Physical.Nodes[cut.SelectedNodeId - 1].Kind, Is.EqualTo(CargoPhysicalNodeKind.MeasuredTransfer));
            Assert.That(snapshot.Physical.Nodes[cut.NextNodeId - 2].Suboperation, Is.EqualTo(partial ? 2 : 1));
            var finalMerge = snapshot.Physical.Nodes.Single(n => n.CreatedSequence == cut.Sequence && n.Suboperation == 3);
            Assert.That(finalMerge.Id, Is.GreaterThanOrEqualTo(cut.NextNodeId));
            Bounds(CohortView(s, cohort, snapshot, "at"), partial ? 1 : 2, partial ? 1 : 2);
            CheckAllTinyObjectives(s, snapshot);
        }

        [Test]
        public void SinksAndUnrelatedAllSinkBranchRemainConservedAndUnrelatedStampStaysCurrent()
        {
            var s = Create(); var originA = Admit(s, "a", 3, 1).Origin.Value; Admit(s, "b", 3, 2); var c = Admit(s, "c", 2, 3);
            Apply(s, CargoSessionReceipt.Sink(4, 7, V(s, "c"), null, 2, "consumed"));
            var sinkOnly = Snap(s, c.Stamp.Component);
            Apply(s, CargoSessionReceipt.Sink(5, 7, V(s, "a"), O("a", 2), 1, "consumed"));
            Apply(s, CargoSessionReceipt.Sink(6, 7, V(s, "b"), O("b", 2), 1, "consumed"));
            Apply(s, Absorb(s, 7, "a", "b", 0, 4));
            var snapshot = Snap(s, "b");
            Assert.That(snapshot.Physical.TotalObservedBirthQuantity, Is.EqualTo(6));
            Assert.That(snapshot.Physical.Nodes.Count(n => n.Kind == CargoPhysicalNodeKind.AccountedSink), Is.EqualTo(2));
            Assert.That(s.IsCurrent(sinkOnly.Stamp), Is.True);
            var all = s.CompileOrigin(snapshot.Stamp, originA, snapshot.Frontier.ToArray(), Work);
            Assert.That(all.Status, Is.EqualTo(CargoSessionStatus.ViewCompiled)); Bounds(all.View, 3, 3);
            Assert.That(s.FindCurrent("c").Status, Is.EqualTo(CargoSessionStatus.RetiredEntity));
            CheckAllTinyObjectives(s, snapshot);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UntrackedPartner_AdmitsActualBeforeQuantityAndTransfersInOnePublication(bool untrackedSource)
        {
            var s = Create(); var tracked = Admit(s, "tracked", 3, 1); var before = Snap(s, "tracked");
            var receipt = CargoSessionReceipt.Absorb(2, 7,
                untrackedSource ? CargoSessionParticipant.Untracked(O("new", 2)) : CargoSessionParticipant.Tracked(V(s, "tracked")),
                untrackedSource ? CargoSessionParticipant.Tracked(V(s, "tracked")) : CargoSessionParticipant.Untracked(O("new", 2)),
                untrackedSource ? O("new", 1) : O("tracked", 1), untrackedSource ? O("tracked", 4) : O("new", 4), true);
            var result = Apply(s, receipt); var after = Snap(s, "tracked");
            Assert.That(after.Physical.TotalObservedBirthQuantity, Is.EqualTo(5));
            Assert.That(after.Physical.Nodes.Single(n => n.EntityKey == "new" && n.BirthQuantity > 0).BirthQuantity, Is.EqualTo(2));
            Assert.That(s.ObservationSequence, Is.EqualTo(3)); Assert.That(s.RetainedBranchCount, Is.EqualTo(3));
            Assert.That(s.RetainedOperationCount, Is.EqualTo(2)); Assert.That(s.IsCurrent(before.Stamp), Is.False);
            Assert.That(result.Origin, Is.Not.Null); Bounds(OriginView(s, result.Origin.Value, after, "tracked", "new"), 2, 2);
            Assert.That(s.ApplyObserved(receipt, Work).Status, Is.EqualTo(CargoSessionStatus.ExactDuplicate));
            CheckAllTinyObjectives(s, after);
        }

        [Test]
        public void BothInactiveAbsorb_DoesNotTrackTheColonyOrAdvanceCanonicalHistory()
        {
            var s = Create();
            var result = s.ApplyObserved(CargoSessionReceipt.Absorb(1, 7, CargoSessionParticipant.Untracked(O("a", 1)),
                CargoSessionParticipant.Untracked(O("b", 1)), null, O("b", 2)), Work);
            Assert.That(result.Status, Is.EqualTo(CargoSessionStatus.Inactive));
            Assert.That(new[] { s.RetainedBranchCount, s.RetainedEntityCount, s.RetainedOperationCount }, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(s.ObservationSequence, Is.Zero);
        }

        [TestCase("live")]
        [TestCase("merged")]
        [TestCase("sink")]
        [TestCase("split")]
        [TestCase("untracked-claim")]
        public void GlobalIdentityRejectsReuseIncludingRetiredKeysAndForgedUntrackedClaims(string mode)
        {
            var s = Create(); Admit(s, "a", 2, 1); Admit(s, "b", 1, 2); long id = 3;
            if (mode == "merged") Apply(s, Absorb(s, id++, "a", "b", 0, 3));
            if (mode == "sink") Apply(s, CargoSessionReceipt.Sink(id++, 7, V(s, "a"), null, 2, "consumed"));
            CargoSessionResult result;
            if (mode == "split") result = s.ApplyObserved(CargoSessionReceipt.Split(id, 7, V(s, "a"), O("a", 1), O("b", 1)), Work);
            else if (mode == "untracked-claim") result = s.ApplyObserved(CargoSessionReceipt.Absorb(id, 7,
                CargoSessionParticipant.Tracked(V(s, "a")), CargoSessionParticipant.Untracked(O("b", 1)), null, O("b", 3)), Work);
            else result = s.AdmitObserved(O("a", 2), id, 7, Work);
            Assert.That(result.Status, Is.AnyOf(CargoSessionStatus.InvalidReceipt, CargoSessionStatus.StaleHandle));
            Assert.That(result.Outputs, Is.Empty); Assert.That(s.RetainedBranchCount, Is.EqualTo(mode == "merged" ? 3 : 2));
            Assert.That(s.FindCurrent("a").Status, Is.EqualTo(CargoSessionStatus.Quarantined));
        }

        [Test]
        public void CanonicalReplayAfterUnion_KeepsHistoricalSelectionButNeverReturnsObsoleteOutputs()
        {
            var s = Create(); var admitted = Admit(s, "a", 2, 1); Admit(s, "b", 1, 2);
            var receipt = CargoSessionReceipt.Split(3, 7, V(s, "a"), O("a", 1), O("ap", 1), true);
            var first = Apply(s, receipt); Assert.That(s.ApplyObserved(receipt, Work).Outputs, Is.Empty);
            Apply(s, Absorb(s, 4, "ap", "b", 0, 2));
            var before = Snap(s, "b"); long sequence = s.ObservationSequence;
            var duplicate = s.ApplyObserved(receipt, Work);
            Assert.That(duplicate.Status, Is.EqualTo(CargoSessionStatus.ExactDuplicate)); Assert.That(duplicate.Outputs, Is.Empty);
            Assert.That(duplicate.Cohort, Is.EqualTo(first.Cohort)); Assert.That(s.IsCurrent(duplicate.Stamp), Is.True);
            Assert.That(s.ObservationSequence, Is.EqualTo(sequence)); Assert.That(Physical(Snap(s, "b")), Is.EqualTo(Physical(before)));
            Assert.That(s.AdmitObserved(O("a", 2), 1, 7, Work).Status, Is.EqualTo(CargoSessionStatus.ExactDuplicate));
            var contradictory = s.ApplyObserved(CargoSessionReceipt.Move(3, 7, V(s, "b"), O("b", 2, "inventory")), Work);
            Assert.That(contradictory.Status, Is.EqualTo(CargoSessionStatus.ConflictingReceipt));
            Assert.That(s.IsCurrent(before.Stamp), Is.False); Assert.That(Physical(Snap(s, before.Stamp.Component)), Is.EqualTo(Physical(before)));
        }

        [TestCase("invalid-tick", false)]
        [TestCase("invalid-output", false)]
        [TestCase("zero-allowance", false)]
        [TestCase("invalid-tick", true)]
        [TestCase("invalid-output", true)]
        [TestCase("zero-allowance", true)]
        public void EarlyCanonicalFailureInvalidatesBothEvidenceScopesAndPreservesUnrelatedHistory(string mode, bool admission)
        {
            var s = Create(); Admit(s, "a", 2, 1); Admit(s, "b", 2, 2); Admit(s, "c", 1, 3);
            var a = Snap(s, "a"); var b = Snap(s, "b"); var c = Snap(s, "c");
            int tick = mode == "invalid-tick" ? -1 : 7;
            int quantity = mode == "invalid-output" ? 0 : 2;
            long allowance = mode == "zero-allowance" ? 0 : Work;
            var failed = admission ? s.AdmitObserved(O("b", quantity), 1, tick, allowance)
                : s.ApplyObserved(CargoSessionReceipt.Move(1, tick, V(s, "b"), O("b", quantity, "inventory")), allowance);
            Assert.That(failed.Status, Is.EqualTo(mode == "zero-allowance" ? CargoSessionStatus.WorkLimit : CargoSessionStatus.InvalidReceipt));
            Assert.That(failed.Outputs, Is.Empty); Assert.That(s.IsCurrent(a.Stamp), Is.False); Assert.That(s.IsCurrent(b.Stamp), Is.False);
            Assert.That(s.IsCurrent(c.Stamp), Is.True); Assert.That(s.FindCurrent("a").Status, Is.EqualTo(CargoSessionStatus.Quarantined));
            Assert.That(s.FindCurrent("b").Status, Is.EqualTo(CargoSessionStatus.Quarantined));
            foreach (var snapshot in new[] { a, b, c }) Assert.That(Physical(Snap(s, snapshot.Stamp.Component)), Is.EqualTo(Physical(snapshot)));
            var replay = s.AdmitObserved(O("a", 2), 1, 7, Work);
            Assert.That(replay.Status, Is.EqualTo(CargoSessionStatus.ExactDuplicate)); Assert.That(replay.Outputs, Is.Empty);
            Assert.That(s.IsCurrent(replay.Stamp), Is.False);
            Assert.That(s.RetainedBranchCount, Is.EqualTo(3)); Assert.That(s.RetainedOperationCount, Is.EqualTo(3));
            Assert.That(s.ObservationSequence, Is.EqualTo(3));
        }

        [TestCase("invalid-tick")]
        [TestCase("invalid-output")]
        [TestCase("zero-allowance")]
        public void EarlyFailureOfAdmissionReplayCannotRevalidateItsOriginalOwner(string mode)
        {
            var s = Create(); Admit(s, "a", 2, 1); Admit(s, "b", 1, 2);
            var a = Snap(s, "a"); var b = Snap(s, "b");
            var failed = s.AdmitObserved(O("a", mode == "invalid-output" ? 0 : 2), 1, mode == "invalid-tick" ? -1 : 7,
                mode == "zero-allowance" ? 0 : Work);
            Assert.That(failed.Status, Is.EqualTo(mode == "zero-allowance" ? CargoSessionStatus.WorkLimit : CargoSessionStatus.InvalidReceipt));
            Assert.That(s.IsCurrent(a.Stamp), Is.False); Assert.That(s.IsCurrent(b.Stamp), Is.True);
            Assert.That(Physical(Snap(s, a.Stamp.Component)), Is.EqualTo(Physical(a)));
            var replay = s.AdmitObserved(O("a", 2), 1, 7, Work);
            Assert.That(replay.Status, Is.EqualTo(CargoSessionStatus.ExactDuplicate)); Assert.That(s.IsCurrent(replay.Stamp), Is.False);
            Assert.That(s.RetainedOperationCount, Is.EqualTo(2)); Assert.That(s.ObservationSequence, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedUnion_RemapsAllStableHandlesAndCanonicalAliasesWithoutNewOrigins()
        {
            var s = Create(); var a = Admit(s, "a", 2, 1); Admit(s, "b", 1, 2);
            var pickup = CargoSessionReceipt.Split(3, 7, V(s, "a"), O("a", 1), O("ap", 1), true);
            var cohort = Apply(s, pickup).Cohort.Value;
            var firstJoin = Absorb(s, 4, "ap", "b", 0, 2); Apply(s, firstJoin);
            var old = Snap(s, "b"); Admit(s, "c", 1, 5); Apply(s, Absorb(s, 6, "b", "c", 1, 2, true));
            var snapshot = Snap(s, "c");
            Assert.That(snapshot.Origins.Count, Is.EqualTo(3)); Assert.That(snapshot.Physical.TotalObservedBirthQuantity, Is.EqualTo(4));
            Assert.That(s.RetainedBranchCount, Is.EqualTo(5)); Assert.That(s.IsCurrent(old.Stamp), Is.False);
            Bounds(OriginView(s, a.Origin.Value, snapshot, "a"), 1, 1);
            Bounds(CohortView(s, cohort, snapshot, "b", "c"), 1, 1);
            foreach (var receipt in new[] { pickup, firstJoin })
            {
                var duplicate = s.ApplyObserved(receipt, Work);
                Assert.That(duplicate.Status, Is.EqualTo(CargoSessionStatus.ExactDuplicate)); Assert.That(duplicate.Outputs, Is.Empty);
                Assert.That(duplicate.Stamp.Physical.Component, Is.EqualTo(snapshot.Stamp.Physical.Component));
                Assert.That(s.IsCurrent(duplicate.Stamp), Is.True);
            }
            CheckAllTinyObjectives(s, snapshot);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedUntrackedJoinNeverPublishesItsStagedAdmission(bool wrongDelta)
        {
            var s = Create(); Admit(s, "a", 2, 1); var before = Snap(s, "a");
            var receipt = CargoSessionReceipt.Absorb(2, 7, CargoSessionParticipant.Untracked(O("new", 1)),
                CargoSessionParticipant.Tracked(V(s, "a")), null, O("a", wrongDelta ? 4 : 3), true);
            var result = s.ApplyObserved(receipt, wrongDelta ? Work : 0);
            Assert.That(result.Status, Is.EqualTo(wrongDelta ? CargoSessionStatus.PhysicalFailure : CargoSessionStatus.WorkLimit));
            Assert.That(result.Outputs, Is.Empty); Assert.That(result.Origin, Is.Null); Assert.That(result.Cohort, Is.Null);
            Assert.That(s.RetainedBranchCount, Is.EqualTo(1)); Assert.That(s.RetainedOperationCount, Is.EqualTo(1));
            Assert.That(s.FindCurrent("new").Status, Is.EqualTo(CargoSessionStatus.Quarantined));
            Assert.That(s.IsCurrent(before.Stamp), Is.False); Assert.That(Physical(Snap(s, before.Stamp.Component)), Is.EqualTo(Physical(before)));
        }

        [Test]
        public void OldCurrentHandlesAndForeignFacadeHandlesCannotAuthorizeMutationOrQueries()
        {
            var s = Create(); var a = Admit(s, "a", 1, 1); Admit(s, "b", 1, 2);
            var old = V(s, "a"); var oldSnapshot = Snap(s, "a"); Apply(s, Absorb(s, 3, "a", "b", 0, 2));
            var current = Snap(s, "b");
            Assert.That(s.CompileOrigin(current.Stamp, a.Origin.Value, oldSnapshot.Frontier.ToArray(), Work).Status, Is.EqualTo(CargoSessionStatus.StaleHandle));
            var other = Create(); var otherA = Admit(other, "a", 1, 1);
            Assert.That(other.IsCurrent(oldSnapshot.Stamp), Is.False);
            Assert.That(s.CompileOrigin(current.Stamp, otherA.Origin.Value, current.Frontier.ToArray(), Work).Status, Is.EqualTo(CargoSessionStatus.ForeignHandle));
            Assert.That(s.ApplyObserved(CargoSessionReceipt.Move(4, 7, old, O("a", 1, "inventory")), Work).Status, Is.EqualTo(CargoSessionStatus.StaleHandle));
            Assert.That(s.IsCurrent(current.Stamp), Is.False);
            Assert.That(other.ApplyObserved(CargoSessionReceipt.Move(2, 7, old, O("a", 1, "inventory")), Work).Status, Is.EqualTo(CargoSessionStatus.ForeignHandle));
        }

        [Test]
        public void EveryInsufficientJoinAllowance_LeavesBothTrustedGraphsIntactAndNeitherCurrent()
        {
            CargoPhysicalSession Fixture()
            { var x = Create(new CargoSessionLimits(3, 2, 3)); Admit(x, "a", 1, 1); Admit(x, "b", 1, 2); return x; }
            var positive = Fixture(); long required = Apply(positive, Absorb(positive, 3, "a", "b", 0, 2)).WorkSpent;
            Assert.That(required, Is.GreaterThan(0));
            for (long allowance = 0; allowance < required; allowance++)
            {
                var s = Fixture(); var a = Snap(s, "a"); var b = Snap(s, "b"); var receipt = Absorb(s, 3, "a", "b", 0, 2);
                var rejected = s.ApplyObserved(receipt, allowance);
                Assert.That(rejected.Status, Is.EqualTo(CargoSessionStatus.WorkLimit), "allowance " + allowance);
                Assert.That(rejected.WorkSpent, Is.EqualTo(allowance)); Assert.That(rejected.Outputs, Is.Empty);
                Assert.That(s.IsCurrent(a.Stamp) || s.IsCurrent(b.Stamp), Is.False);
                Assert.That(Physical(Snap(s, a.Stamp.Component)), Is.EqualTo(Physical(a)));
                Assert.That(Physical(Snap(s, b.Stamp.Component)), Is.EqualTo(Physical(b)));
                Assert.That(s.RetainedBranchCount, Is.EqualTo(2)); Assert.That(s.RetainedOperationCount, Is.EqualTo(2));
                Assert.That(s.ObservationSequence, Is.EqualTo(2));
            }
        }

        [TestCase("branch", CargoSessionStatus.BranchLimit)]
        [TestCase("operation", CargoSessionStatus.OperationLimit)]
        [TestCase("entity", CargoSessionStatus.EntityLimit)]
        [TestCase("overflow", CargoSessionStatus.ArithmeticOverflow)]
        public void ObservedCapAndOverflowFailuresAreAtomic(string kind, CargoSessionStatus status)
        {
            var limits = kind == "branch" ? new CargoSessionLimits(2, 3, 5) : kind == "operation" ? new CargoSessionLimits(4, 3, 2)
                : kind == "entity" ? new CargoSessionLimits(4, 1, 5) : new CargoSessionLimits(4, 3, 5);
            var s = Create(limits); Admit(s, "a", kind == "overflow" ? long.MaxValue : 1, 1);
            if (kind != "entity") Admit(s, "b", 1, 2);
            var a = Snap(s, "a"); CargoSessionSnapshot b = kind == "entity" ? null : Snap(s, "b");
            var receipt = kind == "entity" ? CargoSessionReceipt.Absorb(2, 7, CargoSessionParticipant.Untracked(O("b", 1)),
                CargoSessionParticipant.Tracked(V(s, "a")), null, O("a", 2)) : Absorb(s, 3, "b", "a", 0, kind == "overflow" ? long.MaxValue : 2);
            var result = s.ApplyObserved(receipt, Work);
            Assert.That(result.Status, Is.EqualTo(status), result.Detail); Assert.That(result.Outputs, Is.Empty);
            Assert.That(s.IsCurrent(a.Stamp), Is.False); Assert.That(Physical(Snap(s, a.Stamp.Component)), Is.EqualTo(Physical(a)));
            if (b != null) { Assert.That(s.IsCurrent(b.Stamp), Is.False); Assert.That(Physical(Snap(s, b.Stamp.Component)), Is.EqualTo(Physical(b))); }
            Assert.That(s.RetainedOperationCount, Is.EqualTo(kind == "entity" ? 1 : 2));
        }

        [Test]
        public void InheritedNodeAndFrontierCapsDoNotRebaseOrDropEitherHistory()
        {
            var s = Create(); Admit(s, "a", 1, 1); Admit(s, "b", 1, 2); long id = 3;
            for (int i = 0; i < 63; i++) Apply(s, CargoSessionReceipt.Move(id++, 7, V(s, "a"), O("a", 1, i % 2 == 0 ? "inventory" : "floor")));
            for (int i = 0; i < 63; i++) Apply(s, CargoSessionReceipt.Move(id++, 7, V(s, "b"), O("b", 1, i % 2 == 0 ? "inventory" : "floor")));
            var a = Snap(s, "a"); var b = Snap(s, "b");
            var failed = s.ApplyObserved(CargoSessionReceipt.Absorb(id, 7, CargoSessionParticipant.Tracked(V(s, "a")),
                CargoSessionParticipant.Tracked(V(s, "b")), null, O("b", 2, "inventory")), Work);
            Assert.That(failed.PhysicalStatus, Is.EqualTo(CargoPhysicalStatus.NodeLimit));
            Assert.That(Physical(Snap(s, a.Stamp.Component)), Is.EqualTo(Physical(a))); Assert.That(Physical(Snap(s, b.Stamp.Component)), Is.EqualTo(Physical(b)));

            var frontier = Create(); Admit(frontier, "a", 1, 1); Admit(frontier, "b", 1, 2); id = 3;
            for (int i = 0; i < 16; i++) Apply(frontier, CargoSessionReceipt.Birth(id++, 7, V(frontier, "a"), O("a" + i, 1)));
            for (int i = 0; i < 16; i++) Apply(frontier, CargoSessionReceipt.Birth(id++, 7, V(frontier, "b"), O("b" + i, 1)));
            failed = frontier.ApplyObserved(Absorb(frontier, id, "a", "b", 0, 2), Work);
            Assert.That(failed.PhysicalStatus, Is.EqualTo(CargoPhysicalStatus.FrontierLimit));
        }

        [Test]
        public void ExactNodeCapAcceptsCompleteHistoryAndOlderOverfullCutsAreRejected()
        {
            var exact = Create(); Admit(exact, "a", 1, 1); Admit(exact, "b", 1, 2); long id = 3;
            for (int i = 0; i < 62; i++) Apply(exact, CargoSessionReceipt.Move(id++, 7, V(exact, "a"), O("a", 1, i % 2 == 0 ? "inventory" : "floor")));
            for (int i = 0; i < 62; i++) Apply(exact, CargoSessionReceipt.Move(id++, 7, V(exact, "b"), O("b", 1, i % 2 == 0 ? "inventory" : "floor")));
            Apply(exact, Absorb(exact, id, "a", "b", 0, 2));
            Assert.That(Snap(exact, "b").Physical.Nodes.Count, Is.EqualTo(CargoPhysicalComponent.MaximumNodes));

            var cut = Create(); Admit(cut, "a", 1, 1); Admit(cut, "b", 1, 2); id = 3;
            for (int i = 0; i < 16; i++) Apply(cut, CargoSessionReceipt.Birth(id++, 7, V(cut, "a"), O("a" + i, 1)));
            for (int i = 0; i < 16; i++) Apply(cut, CargoSessionReceipt.Birth(id++, 7, V(cut, "b"), O("b" + i, 1)));
            Apply(cut, CargoSessionReceipt.Move(id++, 7, V(cut, "a"), O("a", 1, "inventory"), true));
            Apply(cut, CargoSessionReceipt.Sink(id++, 7, V(cut, "b14"), null, 1, "consumed"));
            Apply(cut, CargoSessionReceipt.Sink(id++, 7, V(cut, "b15"), null, 1, "consumed"));
            var failed = cut.ApplyObserved(CargoSessionReceipt.Absorb(id, 7, CargoSessionParticipant.Tracked(V(cut, "a")),
                CargoSessionParticipant.Tracked(V(cut, "b")), null, O("b", 2)), Work);
            Assert.That(failed.PhysicalStatus, Is.EqualTo(CargoPhysicalStatus.FrontierLimit));
            Assert.That(failed.Detail, Does.Contain("historical complete cut"));
        }

        [Test]
        public void RetirementAndExplicitGapInvalidateCurrentnessAndOldAliasesStayAuditOnly()
        {
            var s = Create(); var a = Admit(s, "a", 1, 1); Admit(s, "b", 1, 2); Apply(s, Absorb(s, 3, "a", "b", 0, 2));
            var snapshot = Snap(s, "b");
            var invalidated = s.InvalidateObservedCoverage(new[] { V(s, "b") }, CargoSessionGapReason.MissingObservation, 4);
            Assert.That(invalidated.Status, Is.EqualTo(CargoSessionStatus.CoverageInvalidated)); Assert.That(s.IsCurrent(snapshot.Stamp), Is.False);
            Assert.That(s.FirstGap.Reason, Is.EqualTo(CargoSessionGapReason.MissingObservation));
            s.RetireSession(CargoSessionRetirementReason.LoadBoundary);
            Assert.That(s.FindCurrent("b").Status, Is.EqualTo(CargoSessionStatus.SessionRetired));
            Assert.That(s.AdmitObserved(O("a", 1), 5, 8, Work).Status, Is.EqualTo(CargoSessionStatus.SessionRetired));
            Assert.That(Snap(s, a.Stamp.Component).Stamp.Complete, Is.False);
            var replacement = Create(); Admit(replacement, "a", 1, 1); Assert.That(replacement.IsCurrent(a.Stamp), Is.False);
        }

        [Test]
        public void ConcurrentReadersSeeWholeSnapshotsAndOldStampsCannotSurviveAJoin()
        {
            var s = Create(); var a = Admit(s, "a", 2, 1); Admit(s, "b", 1, 2);
            var receipt = Absorb(s, 3, "a", "b", 1, 2); var errors = new List<string>();
            var reader = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    var lookup = s.FindCurrent("b"); var snapshot = Snap(s, lookup.Outputs.Single().Component);
                    long mass = snapshot.Physical.Nodes.Where(n => n.Frontier).Sum(n => n.Quantity);
                    if (mass != snapshot.Physical.TotalObservedBirthQuantity || mass != 1 && mass != 3) lock (errors) errors.Add("Partial frontier census.");
                }
            });
            Apply(s, receipt); reader.GetAwaiter().GetResult(); Assert.That(errors, Is.Empty); Assert.That(s.IsCurrent(a.Stamp), Is.False);
        }

        [Test]
        public void SameTickOrderingIsDeterministicAndWorkDependsOnShapeRatherThanQuantity()
        {
            long? work = null; string order = null;
            foreach (long scale in new[] { 1L, 1000000L, 1000000000000L })
            {
                var s = Create(); var a = Admit(s, "a", 4 * scale, 1).Origin.Value; Admit(s, "b", 3 * scale, 2);
                Apply(s, CargoSessionReceipt.Split(3, 7, V(s, "a"), O("a", 2 * scale), O("ap", 2 * scale), true));
                var joined = Apply(s, Absorb(s, 4, "ap", "b", scale, 4 * scale, true)); var snapshot = Snap(s, "b");
                Bounds(OriginView(s, a, snapshot, "ap"), scale, scale);
                string chronology = string.Join("|", snapshot.Physical.Nodes.Select(n => $"{n.Id}/{n.CreatedSequence}/{n.Suboperation}/{n.Kind}/{n.OriginId}"));
                if (work.HasValue) { Assert.That(joined.WorkSpent, Is.EqualTo(work.Value)); Assert.That(chronology, Is.EqualTo(order)); }
                work = joined.WorkSpent; order = chronology;
            }
        }

        // Tiny integer transport enumeration through the actual compiled session view. Each edge
        // receives a possible selected quantity subject to this node's conservation; no min-cut
        // algorithm or selected-origin allocation supplied to the session is used by this oracle.
        private static void CheckAllTinyObjectives(CargoPhysicalSession session, CargoSessionSnapshot snapshot)
        {
            Assert.That(snapshot.Frontier.Count, Is.LessThanOrEqualTo(6));
            for (int mask = 0; mask < 1 << snapshot.Frontier.Count; mask++)
            {
                var objective = snapshot.Frontier.Where((f, i) => (mask & 1 << i) != 0).ToArray();
                foreach (var origin in snapshot.Origins)
                {
                    var view = session.CompileOrigin(snapshot.Stamp, origin.Handle, objective, Work);
                    Assert.That(view.Status, Is.EqualTo(CargoSessionStatus.ViewCompiled), view.Detail); CompareEnumeration(view.View);
                }
                foreach (var cohort in snapshot.Cohorts)
                {
                    var view = session.CompileCohort(snapshot.Stamp, cohort.Handle, objective, Work);
                    Assert.That(view.Status, Is.EqualTo(CargoSessionStatus.ViewCompiled), view.Detail); CompareEnumeration(view.View);
                }
            }
        }
        private static void CompareEnumeration(CargoPhysicalFlowView view)
        {
            var incoming = new long[view.Nodes.Count]; var outgoing = new List<CargoFlowEdge>[view.Nodes.Count];
            for (int i = 0; i < outgoing.Length; i++) outgoing[i] = new List<CargoFlowEdge>();
            foreach (var edge in view.Edges) outgoing[edge.From].Add(edge);
            long minimum = long.MaxValue, maximum = long.MinValue;
            void Visit(int node, long objective)
            {
                if (node == view.Nodes.Count) { minimum = Math.Min(minimum, objective); maximum = Math.Max(maximum, objective); return; }
                var n = view.Nodes[node]; long selected = incoming[node] + n.SelectedBirthQuantity;
                if (selected > n.Quantity) return;
                if (n.Frontier) { Visit(node + 1, objective + (n.SelectedFrontier ? selected : 0)); return; }
                void Distribute(int edgeIndex, long remaining)
                {
                    if (edgeIndex == outgoing[node].Count) { if (remaining == 0) Visit(node + 1, objective); return; }
                    var edge = outgoing[node][edgeIndex];
                    for (long quantity = 0; quantity <= Math.Min(edge.Quantity, remaining); quantity++)
                    { incoming[edge.To] += quantity; Distribute(edgeIndex + 1, remaining - quantity); incoming[edge.To] -= quantity; }
                }
                Distribute(0, selected);
            }
            Visit(0, 0); Assert.That(minimum, Is.LessThanOrEqualTo(maximum), "No physical selected/complement allocation.");
            Bounds(view, minimum, maximum);
        }
    }
}
