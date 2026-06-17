using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins <see cref="UnloadDestinationOrder.Less"/> (C1b — closest-destination-first unload ordering):
    ///   1. NEARER resolved destination wins (smaller squared distance first).
    ///   2. EQUAL distance falls back to the EXACT category→defName tiebreak
    ///      (<see cref="SelectFirstByCategoryThenDef.LessThan"/>) — so within one destination the order is
    ///      byte-identical to the OFF min-scan.
    ///   3. An INVALID distance (<see cref="UnloadDestinationOrder.NoDestination"/>) sorts LAST; two of them
    ///      compare purely by the tiebreak.
    ///   4. STRICT / deterministic: never reports A&lt;B AND B&lt;A; ties report neither (so a running-best
    ///      min-scan keeps the first-seen element, matching the OFF path's stability).
    /// </summary>
    [TestFixture]
    public class UnloadDestinationOrderTests
    {
        private const int NoDest = UnloadDestinationOrder.NoDestination;
        private const int NoCat = SelectFirstByCategoryThenDef.NoCategory;

        [Test]
        public void NearerDistanceWins_RegardlessOfCategoryOrDef()
        {
            // A is farther but sorts first by category/def; distance must dominate -> B (nearer) wins.
            Assert.That(UnloadDestinationOrder.Less(100, 0, "Aaa", 25, 9, "Zzz"), Is.False,
                "the nearer destination (B) must be picked even though A has a smaller category/def key");
            Assert.That(UnloadDestinationOrder.Less(25, 9, "Zzz", 100, 0, "Aaa"), Is.True,
                "the nearer destination (A) must be picked even though B has a smaller category/def key");
        }

        [Test]
        public void EqualDistance_FallsBackToCategoryThenDef()
        {
            // Same distance => identical to SelectFirstByCategoryThenDef.LessThan on every (cat, def) shape.
            foreach (var (catA, defA, catB, defB) in new[]
                     {
                         (0, "Steel", 1, "Wood"),     // category dominates
                         (1, "Wood", 1, "Steel"),     // same category -> defName ordinal
                         (1, "Steel", 1, "Steel"),    // fully equal
                         (NoCat, "Aaa", 5, "Zzz"),    // null category sorts last
                         (5, "Apparel", 5, "MealSimple"),
                     })
            {
                int d = 49; // arbitrary equal distance
                Assert.That(UnloadDestinationOrder.Less(d, catA, defA, d, catB, defB),
                    Is.EqualTo(SelectFirstByCategoryThenDef.LessThan(catA, defA, catB, defB)),
                    $"equal distance must defer to the category->defName tiebreak for ({catA},{defA}) vs ({catB},{defB})");
            }
        }

        [Test]
        public void NoDestination_SortsLast()
        {
            // A has a real destination, B has none -> A is nearer (wins) regardless of B's smaller category/def.
            Assert.That(UnloadDestinationOrder.Less(0, 9, "Zzz", NoDest, 0, "Aaa"), Is.True,
                "a resolvable destination must be picked before a no-destination candidate");
            Assert.That(UnloadDestinationOrder.Less(NoDest, 0, "Aaa", 0, 9, "Zzz"), Is.False,
                "a no-destination candidate must never be picked before a resolvable one");
        }

        [Test]
        public void BothNoDestination_CompareByCategoryThenDef()
        {
            // Two unreachable-destination stacks: distance is equal (both sentinel) -> stable category/def order,
            // so a destination-less remainder is visited in exactly today's order.
            Assert.That(UnloadDestinationOrder.Less(NoDest, 1, "Steel", NoDest, 1, "Wood"),
                Is.EqualTo(SelectFirstByCategoryThenDef.LessThan(1, "Steel", 1, "Wood")));
            Assert.That(UnloadDestinationOrder.Less(NoDest, 1, "Wood", NoDest, 1, "Steel"),
                Is.EqualTo(SelectFirstByCategoryThenDef.LessThan(1, "Wood", 1, "Steel")));
            Assert.That(UnloadDestinationOrder.Less(NoDest, 2, "X", NoDest, 2, "X"), Is.False,
                "two fully-equal no-destination candidates are not strictly ordered");
        }

        [Test]
        public void ZeroDistance_BeatsAnyPositiveDistance()
        {
            // A stack the pawn is standing on (dist 0) is the canonical "nearest" — always first.
            Assert.That(UnloadDestinationOrder.Less(0, 5, "Z", 1, 0, "A"), Is.True);
            Assert.That(UnloadDestinationOrder.Less(1, 0, "A", 0, 5, "Z"), Is.False);
        }

        [Test]
        public void StrictOrder_NeverBothDirections()
        {
            // Spot-check antisymmetry across distance-driven and tiebreak-driven pairs: Less(A,B) && Less(B,A)
            // must never both hold (a running-best min-scan relies on this to stay deterministic).
            var rows = new[]
            {
                (10, 0, "Steel", 20, 1, "Wood"),   // distance differs
                (10, 0, "Steel", 10, 1, "Wood"),   // equal distance, category differs
                (10, 1, "Wood", 10, 1, "Steel"),   // equal distance, def differs
                (NoDest, 0, "A", 10, 9, "Z"),      // one no-destination
            };
            foreach (var (dA, cA, fA, dB, cB, fB) in rows)
            {
                bool ab = UnloadDestinationOrder.Less(dA, cA, fA, dB, cB, fB);
                bool ba = UnloadDestinationOrder.Less(dB, cB, fB, dA, cA, fA);
                Assert.That(ab && ba, Is.False, $"order must be strict for ({dA},{cA},{fA}) vs ({dB},{cB},{fB})");
            }
        }

        [Test]
        public void FullyEqual_ReportsNeitherDirection()
        {
            // Identical distance + keys -> a tie: neither Less(A,B) nor Less(B,A) (the min-scan then keeps the
            // first-seen, exactly the stable behavior the OFF path guarantees).
            Assert.That(UnloadDestinationOrder.Less(42, 3, "Steel", 42, 3, "Steel"), Is.False);
            Assert.That(UnloadDestinationOrder.Less(42, 3, "Steel", 42, 3, "Steel"), Is.False);
        }
    }
}
