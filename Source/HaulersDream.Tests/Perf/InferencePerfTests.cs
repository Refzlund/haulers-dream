using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the per-item-place inference predicates (yield routing / true-producer).
    /// </summary>
    [TestFixture, Category("Perf")]
    public class InferencePerfTests
    {
        private const bool Reentrant = false;
        private const bool NearMode = true;
        private const bool IsItem = true;

        private const int PawnX = 12;
        private const int PawnZ = 34;
        private const bool HasJobTarget = true;
        private const int TargetX = 12;
        private const int TargetZ = 34;
        private const int CenterX = 12;
        private const int CenterZ = 34;

        [Test]
        public void IsRoutablePlacement_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => InferencePolicy.IsRoutablePlacement(Reentrant, NearMode, IsItem),
                "per-item-place routing gate must not allocate");

        [Test]
        public void IsTrueProducer_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => InferencePolicy.IsTrueProducer(PawnX, PawnZ, HasJobTarget, TargetX, TargetZ, CenterX, CenterZ),
                "producer inference must not allocate");
    }
}
