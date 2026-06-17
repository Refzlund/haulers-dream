using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for the bleeding intake gate — it is evaluated on the hot scoop/sweep/bulk-haul
    /// intake paths, so it must stay branch-only (no allocation).
    /// </summary>
    [TestFixture, Category("Perf")]
    public class FitToHaulPolicyPerfTests
    {
        private const bool GateEnabled = true;
        private const float BleedRate = 0.0011f;   // above threshold -> the "not fit" branch
        private const float Threshold = 0.001f;

        [Test]
        public void FitToStartHaul_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => FitToHaulPolicy.FitToStartHaul(GateEnabled, BleedRate, Threshold),
                "bleeding intake gate must stay branch-only (no allocation)");
    }
}
