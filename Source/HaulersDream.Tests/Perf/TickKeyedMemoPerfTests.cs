using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for the cache DECISION that backs HD-MASS's <c>PawnMassCache</c>. The whole point of the
    /// memo is to turn N per-cell mass walks/tick into one read, so the same-tick HIT path (the common case once
    /// the value is computed) must itself allocate nothing — otherwise the memo trades a CPU walk for GC jitter,
    /// which on RimWorld's hot path is a net loss. A <see cref="TickKeyedMemo{TValue}"/> hit is a struct-keyed
    /// <c>Dictionary&lt;int, PawnMass&gt;.TryGetValue</c> with no boxing (int key, struct value), so it is 0 B/op.
    /// (The Verse-side <c>MassUtility</c> walk on a cold miss can't be benchmarked headlessly — see §4 of the
    /// perf plan; this fixture covers the pure cache leaf the runtime calls per cell.)
    /// </summary>
    [TestFixture, Category("Perf")]
    public class TickKeyedMemoPerfTests
    {
        // A memo pre-populated for the read path, hoisted out of the measured delegate so the body captures no
        // freshly-built state. Mutable struct -> field, not local, so TryGet sees the stored entry.
        private TickKeyedMemo<PawnMass> hitMemo;

        [SetUp]
        public void Seed()
        {
            hitMemo = new TickKeyedMemo<PawnMass>();
            hitMemo.Store(currentTick: 1000, key: 42, value: new PawnMass(35f, 18f));
        }

        [Test]
        public void SameTickHit_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => hitMemo.TryGet(1000, 42, out _),
                "a same-tick memo hit (the per-cell MoveSpeed read) must not allocate");

        [Test]
        public void WouldInvalidate_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => hitMemo.WouldInvalidate(1000),
                "the invalidation-decision predicate must not allocate");

        [Test]
        public void SameKeyReStore_IsZeroAlloc() =>
            // Storing over an existing key in the same tick replaces the value in place (no growth, no boxing).
            AllocationAssert.AssertZeroAlloc(
                () => hitMemo.Store(1000, 42, new PawnMass(35f, 18f)),
                "re-storing an existing same-tick key must not allocate");
    }
}
