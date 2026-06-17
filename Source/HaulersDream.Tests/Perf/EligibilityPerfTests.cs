using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for the per-pawn scoop-eligibility gate — this must stay branch-only.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class EligibilityPerfTests
    {
        private const bool IsMechanoid = false;
        private const bool IsHumanlike = true;
        private const bool IsDrafted = false;
        private const bool IncapableOfHauling = false;
        private const bool AllowMechanoids = true;
        private const bool PauseWhileDrafted = true;
        private const bool AllowIncapable = false;
        private const bool AllowAnimals = false;

        [Test]
        public void IsEligible_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => EligibilityPolicy.IsEligible(
                    IsMechanoid, IsHumanlike, IsDrafted, IncapableOfHauling,
                    AllowMechanoids, PauseWhileDrafted, AllowIncapable, AllowAnimals),
                "eligibility gate must stay branch-only (no allocation)");
    }
}
