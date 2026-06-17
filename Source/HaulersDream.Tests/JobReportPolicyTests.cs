using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins each <see cref="JobReportKind"/> × leg to the exact translation key the Verse report patch and
    /// the Languages XML must agree on (the contract). Mirrors WYU's <c>BaseDetour.GetJobReport</c> mapping
    /// (detour type → <c>*_LoadReport</c>/<c>*_UnloadReport</c> key; <c>BaseDetour.cs:107-120</c>).
    /// </summary>
    [TestFixture]
    public class JobReportPolicyTests
    {
        [Test]
        public void Normal_NoKey_NoRewrite()
        {
            // Inactive/Normal -> WYU's `_ => text` default: no key, no rewrite, on either leg.
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.Normal, isLoad: true), Is.Null);
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.Normal, isLoad: false), Is.Null);
            Assert.That(JobReportPolicy.RewritesReport(JobReportKind.Normal), Is.False);
            Assert.That(JobReportPolicy.UsesDestination(JobReportKind.Normal), Is.False);
        }

        [Test]
        public void EnRoute_PinsKeys()
        {
            // WYU Opportunity_LoadReport / Opportunity_UnloadReport ("X (on the way to Y)").
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.EnRoute, isLoad: true),
                Is.EqualTo("HaulersDream.JobReport.EnRoute.Load"));
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.EnRoute, isLoad: false),
                Is.EqualTo("HaulersDream.JobReport.EnRoute.Unload"));
            Assert.That(JobReportPolicy.RewritesReport(JobReportKind.EnRoute), Is.True);
            Assert.That(JobReportPolicy.UsesDestination(JobReportKind.EnRoute), Is.True);
        }

        [Test]
        public void CloserTo_PinsKeys()
        {
            // WYU HaulBeforeCarry_LoadReport / HaulBeforeCarry_UnloadReport ("X (closer to Y)").
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.CloserTo, isLoad: true),
                Is.EqualTo("HaulersDream.JobReport.CloserTo.Load"));
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.CloserTo, isLoad: false),
                Is.EqualTo("HaulersDream.JobReport.CloserTo.Unload"));
            Assert.That(JobReportPolicy.RewritesReport(JobReportKind.CloserTo), Is.True);
            Assert.That(JobReportPolicy.UsesDestination(JobReportKind.CloserTo), Is.True);
        }

        [Test]
        public void Efficient_PinsKeys_NoDestination()
        {
            // WYU PickUpAndHaulPlus_LoadReport / PickUpAndHaulPlus_UnloadReport ("Efficiently hauling…").
            // Only the ORIGINAL arg -> no DESTINATION.
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.Efficient, isLoad: true),
                Is.EqualTo("HaulersDream.JobReport.Efficient.Load"));
            Assert.That(JobReportPolicy.ReportKeyFor(JobReportKind.Efficient, isLoad: false),
                Is.EqualTo("HaulersDream.JobReport.Efficient.Unload"));
            Assert.That(JobReportPolicy.RewritesReport(JobReportKind.Efficient), Is.True);
            Assert.That(JobReportPolicy.UsesDestination(JobReportKind.Efficient), Is.False);
        }

        [Test]
        public void LoadAndUnload_KeysDiffer_PerKind()
        {
            // Every rewriting kind has DISTINCT keys for the two legs (WYU's _Load vs _Unload suffix).
            foreach (var kind in new[] { JobReportKind.EnRoute, JobReportKind.CloserTo, JobReportKind.Efficient })
            {
                var load = JobReportPolicy.ReportKeyFor(kind, isLoad: true);
                var unload = JobReportPolicy.ReportKeyFor(kind, isLoad: false);
                Assert.That(load, Is.Not.Null.And.Not.Empty, $"{kind} load key");
                Assert.That(unload, Is.Not.Null.And.Not.Empty, $"{kind} unload key");
                Assert.That(load, Is.Not.EqualTo(unload), $"{kind} load vs unload keys must differ");
            }
        }

        [Test]
        public void AllRewritingKeys_AreUnique()
        {
            // No two (kind, leg) pairs collide on a key — each maps to its own XML entry.
            var keys = new[]
            {
                JobReportPolicy.ReportKeyFor(JobReportKind.EnRoute, true),
                JobReportPolicy.ReportKeyFor(JobReportKind.EnRoute, false),
                JobReportPolicy.ReportKeyFor(JobReportKind.CloserTo, true),
                JobReportPolicy.ReportKeyFor(JobReportKind.CloserTo, false),
                JobReportPolicy.ReportKeyFor(JobReportKind.Efficient, true),
                JobReportPolicy.ReportKeyFor(JobReportKind.Efficient, false),
            };
            Assert.That(keys, Is.Unique);
        }

        [Test]
        public void RewritesReport_MatchesKeyNullity()
        {
            // Contract self-consistency: RewritesReport(kind) <=> ReportKeyFor(kind, *) != null.
            foreach (JobReportKind kind in System.Enum.GetValues(typeof(JobReportKind)))
            {
                bool rewrites = JobReportPolicy.RewritesReport(kind);
                Assert.That(JobReportPolicy.ReportKeyFor(kind, isLoad: true) != null, Is.EqualTo(rewrites),
                    $"{kind} load: RewritesReport must match key nullity");
                Assert.That(JobReportPolicy.ReportKeyFor(kind, isLoad: false) != null, Is.EqualTo(rewrites),
                    $"{kind} unload: RewritesReport must match key nullity");
            }
        }
    }
}
