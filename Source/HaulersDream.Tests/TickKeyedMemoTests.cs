using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Hit / miss / tick-invalidation correctness for the pure <see cref="TickKeyedMemo{TValue}"/> that backs
    /// the runtime <c>PawnMassCache</c>. The cache DECISION (when it invalidates, when it hits vs misses) is the
    /// load-bearing correctness property of HD-MASS — a stale value served across a tick boundary would let a
    /// pawn read last tick's mass — so it is pinned headlessly here, independent of the Verse-side
    /// <c>MassUtility</c> value computation.
    /// </summary>
    [TestFixture]
    public class TickKeyedMemoTests
    {
        [Test]
        public void Miss_OnFirstAccess()
        {
            var memo = new TickKeyedMemo<int>();
            Assert.That(memo.TryGet(100, key: 7, out _), Is.False, "an empty memo must miss");
        }

        [Test]
        public void Hit_SameTickSameKey_ReturnsStoredValue()
        {
            var memo = new TickKeyedMemo<int>();
            memo.Store(100, key: 7, value: 42);
            Assert.That(memo.TryGet(100, key: 7, out int v), Is.True);
            Assert.That(v, Is.EqualTo(42));
        }

        [Test]
        public void Miss_SameTickDifferentKey()
        {
            var memo = new TickKeyedMemo<int>();
            memo.Store(100, key: 7, value: 42);
            Assert.That(memo.TryGet(100, key: 8, out _), Is.False, "a different key in the same tick must miss");
        }

        [Test]
        public void Invalidate_OnTickAdvance_DropsStaleValue()
        {
            var memo = new TickKeyedMemo<int>();
            memo.Store(100, key: 7, value: 42);
            // The next tick MUST NOT serve last tick's value (this is the stale-read guard).
            Assert.That(memo.TryGet(101, key: 7, out _), Is.False, "advancing the tick must invalidate the memo");
        }

        [Test]
        public void Restore_AfterInvalidation_HitsAtNewTick()
        {
            var memo = new TickKeyedMemo<int>();
            memo.Store(100, key: 7, value: 42);
            memo.Store(101, key: 7, value: 99); // re-store after the tick advanced
            Assert.That(memo.TryGet(101, key: 7, out int v), Is.True);
            Assert.That(v, Is.EqualTo(99), "the fresh value, not the stale 42, must be served at the new tick");
        }

        [Test]
        public void WouldInvalidate_TracksTickBoundary()
        {
            var memo = new TickKeyedMemo<int>();
            Assert.That(memo.WouldInvalidate(100), Is.True, "first access ever invalidates");
            memo.Store(100, key: 1, value: 5);
            Assert.That(memo.WouldInvalidate(100), Is.False, "same tick does not invalidate");
            Assert.That(memo.WouldInvalidate(101), Is.True, "a new tick invalidates");
        }

        [Test]
        public void Clear_EmptiesAndResetsStamp()
        {
            var memo = new TickKeyedMemo<int>();
            memo.Store(100, key: 7, value: 42);
            memo.Clear();
            Assert.That(memo.Count, Is.EqualTo(0));
            Assert.That(memo.TryGet(100, key: 7, out _), Is.False, "cleared memo must miss even at the same tick");
        }

        [Test]
        public void TickAdvanceClears_AllKeys_NotJustTheRead()
        {
            var memo = new TickKeyedMemo<int>();
            memo.Store(100, key: 1, value: 10);
            memo.Store(100, key: 2, value: 20);
            Assert.That(memo.Count, Is.EqualTo(2));
            // First access of the new tick clears the WHOLE map (so a different pawn read this tick is also fresh).
            memo.TryGet(101, key: 1, out _);
            Assert.That(memo.Count, Is.EqualTo(0), "the first read of a new tick clears every key, not just the one read");
        }

        [Test]
        public void PawnMass_RoundTrips_AsCachedValue()
        {
            var memo = new TickKeyedMemo<PawnMass>();
            memo.Store(5, key: 13, value: new PawnMass(35f, 18.5f));
            Assert.That(memo.TryGet(5, key: 13, out var pm), Is.True);
            Assert.That(pm.Capacity, Is.EqualTo(35f));
            Assert.That(pm.CurrentMass, Is.EqualTo(18.5f));
        }
    }
}
