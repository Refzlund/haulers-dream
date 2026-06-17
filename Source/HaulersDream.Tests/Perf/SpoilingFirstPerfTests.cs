using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the spoiling-first comparator. This is fed to <c>Array.Sort</c> per
    /// bill-ingredient search, so the comparison itself MUST be allocation-free — an enum argument
    /// passed by-value does not box, and the methods return only primitives/enums.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class SpoilingFirstPerfTests
    {
        private const bool IsCorpse = false;
        private const bool IsRottable = true;
        private const bool ButcherFirst = true;
        private const bool CookFirst = true;

        private const IngredientSpoilKind AKind = IngredientSpoilKind.Food;
        private const int ATicks = 60000;
        private const int AIndex = 3;
        private const IngredientSpoilKind BKind = IngredientSpoilKind.None;
        private const int BTicks = SpoilingFirstSelection.NeverRots;
        private const int BIndex = 7;

        [Test]
        public void Categorize_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => SpoilingFirstSelection.Categorize(IsCorpse, IsRottable, ButcherFirst, CookFirst),
                "candidate classification must not allocate");

        [Test]
        public void CompareSpoilRank_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => SpoilingFirstSelection.CompareSpoilRank(AKind, ATicks, BKind, BTicks),
                "spoil-rank comparison must not allocate (Array.Sort comparator)");

        [Test]
        public void Compare_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => SpoilingFirstSelection.Compare(AKind, ATicks, AIndex, BKind, BTicks, BIndex),
                "full comparison must not allocate (Array.Sort comparator)");
    }
}
