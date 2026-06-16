using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for the inspector job-report key selection. It runs on the per-frame
    /// <c>JobDriver.GetReport</c> path (inspector pane, every tick a haul job is selected), so the key
    /// pick must be allocation-free — it returns interned <c>const</c> string literals, never a concat.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class JobReportPolicyPerfTests
    {
        [Test]
        public void ReportKeyFor_IsZeroAlloc()
        {
            // Cover every kind × leg; selecting an interned const must allocate nothing on any branch.
            AllocationAssert.AssertZeroAlloc(
                () =>
                {
                    JobReportPolicy.ReportKeyFor(JobReportKind.Normal, true);
                    JobReportPolicy.ReportKeyFor(JobReportKind.EnRoute, true);
                    JobReportPolicy.ReportKeyFor(JobReportKind.EnRoute, false);
                    JobReportPolicy.ReportKeyFor(JobReportKind.CloserTo, true);
                    JobReportPolicy.ReportKeyFor(JobReportKind.CloserTo, false);
                    JobReportPolicy.ReportKeyFor(JobReportKind.Efficient, true);
                    JobReportPolicy.ReportKeyFor(JobReportKind.Efficient, false);
                },
                "job-report key selection must return interned consts (no allocation) on every branch");
        }

        [Test]
        public void RewritesReport_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => JobReportPolicy.RewritesReport(JobReportKind.EnRoute),
                "RewritesReport must stay branch-only (no allocation)");

        [Test]
        public void UsesDestination_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => JobReportPolicy.UsesDestination(JobReportKind.CloserTo),
                "UsesDestination must stay branch-only (no allocation)");
    }
}
