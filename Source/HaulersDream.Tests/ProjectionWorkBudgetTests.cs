using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class ProjectionWorkBudgetTests
    {
        private static ProjectionWork Limit(long grid) => new ProjectionWork(32, 200, grid, grid, 200, 800, 4096, 4096, 4096, 200, 200, 4096, 0, 4096);

        [Test]
        public void NativeFourPassReservationDefersBeforeAnyPartIsCharged()
        {
            var budget = new ProjectionWorkBudget(Limit(49));
            Assert.That(budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.GridEntries, 10)), Is.True);
            Assert.That(budget.TryCharge(ProjectionWork.NativeCellPredicate(10, true)), Is.False);
            Assert.That(budget.Used[ProjectionWorkKind.NativeCalls], Is.Zero);
            Assert.That(budget.Used[ProjectionWorkKind.Reachability], Is.Zero);
            Assert.That(budget.Used[ProjectionWorkKind.GridEntries], Is.EqualTo(10));
        }

        [Test]
        public void NativeBudgetIncludesRepeatedLimitCallsAndSeparateReservationReachability()
        {
            var budget = new ProjectionWorkBudget(Limit(50));
            Assert.That(budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.GridEntries, 10)), Is.True);
            Assert.That(budget.TryCharge(ProjectionWork.NativeCellPredicate(10, true)), Is.True);
            Assert.That(budget.Used[ProjectionWorkKind.NativeGridVisits], Is.EqualTo(40));
            Assert.That(budget.Used[ProjectionWorkKind.CellLimitCalls], Is.EqualTo(11));
            Assert.That(budget.Used[ProjectionWorkKind.Reservations], Is.EqualTo(1));
            Assert.That(budget.Used[ProjectionWorkKind.Reachability], Is.EqualTo(1));
        }

        [Test]
        public void OversizedCellDoesNotConsumeAllowanceNeededByLaterSmallCell()
        {
            var budget = new ProjectionWorkBudget(Limit(128));
            Assert.That(budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.GridEntries, 129)), Is.False);
            Assert.That(budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.GridEntries, 2)), Is.True);
            Assert.That(budget.Used[ProjectionWorkKind.GridEntries], Is.EqualTo(2));
        }

        [Test]
        public void GuardRetainsCompletedMemberAndRejectsEqualCountMutation()
        {
            var a = new List<int> { 1, 2 }; var b = new List<int> { 3 };
            var aGuard = new ProjectionListGuard<int>(a); var bGuard = new ProjectionListGuard<int>(b);
            foreach (var ignored in a) { }
            a[0] = 4;
            Assert.That(bGuard.Matches(b), Is.True);
            Assert.That(aGuard.Matches(a), Is.False);
        }

        [Test]
        public void GuardRejectsReplacementListEvenWhenAllValuesMatch()
        {
            var source = new List<int> { 1, 2 };
            var guard = new ProjectionListGuard<int>(source);
            Assert.That(guard.Matches(new List<int>(source)), Is.False);
            Assert.That(guard.Matches(source), Is.True);
            Assert.That(guard.Matches(source), Is.True, "Validation must not consume the retained guard.");
        }

        [Test]
        public void NativeVisitReservationUsesWideArithmetic()
        {
            var cost = ProjectionWork.NativeCellPredicate(int.MaxValue, false);
            Assert.That(cost[ProjectionWorkKind.NativeGridVisits], Is.EqualTo(4L * int.MaxValue));
            Assert.That(cost[ProjectionWorkKind.CellLimitCalls], Is.EqualTo((long)int.MaxValue + 1));
            Assert.That(cost[ProjectionWorkKind.Reachability], Is.Zero);
        }

        [TestCase(6, false)]
        [TestCase(11, false)]
        [TestCase(12, true)]
        public void ProviderScanReservesPreflightAndExecutionBeforeEitherPass(long availableVisits, bool accepted)
        {
            var limit = ProjectionWork.Cost(ProjectionWorkKind.ProviderVisits, availableVisits)
                .Plus(ProjectionWork.Cost(ProjectionWorkKind.Compatibility, 6));
            var budget = new ProjectionWorkBudget(limit);
            Assert.That(budget.TryCharge(ProjectionWork.ProviderMemberScan(6)), Is.EqualTo(accepted));
            Assert.That(budget.Used[ProjectionWorkKind.ProviderVisits], Is.EqualTo(accepted ? 12 : 0));
            Assert.That(budget.Used[ProjectionWorkKind.Compatibility], Is.EqualTo(accepted ? 6 : 0));
        }

        [Test]
        public void ProviderScanDoesNotWrapLargeMemberCountIntoAnAffordableCost()
        {
            var cost = ProjectionWork.ProviderMemberScan(int.MaxValue);
            var budget = new ProjectionWorkBudget(ProjectionWork.Cost(ProjectionWorkKind.ProviderVisits, int.MaxValue)
                .Plus(ProjectionWork.Cost(ProjectionWorkKind.Compatibility, int.MaxValue)));
            Assert.That(cost[ProjectionWorkKind.ProviderVisits], Is.EqualTo(2L * int.MaxValue));
            Assert.That(budget.TryCharge(cost), Is.False);
            Assert.That(budget.Used[ProjectionWorkKind.ProviderVisits], Is.Zero);
        }

        [Test]
        public void CallerCannotRaiseBudgetByMutatingConstructorArray()
        {
            var values = new long[(int)ProjectionWorkKind.Count];
            var limit = new ProjectionWork(values);
            values[(int)ProjectionWorkKind.Coordinates] = long.MaxValue;
            Assert.That(new ProjectionWorkBudget(limit).TryCharge(ProjectionWork.Cost(ProjectionWorkKind.Coordinates, 1)), Is.False);
        }
    }
}
