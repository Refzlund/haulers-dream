using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// A tiny pure per-tick READ memo: a <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>
    /// keyed by an integer (e.g. a pawn's <c>thingIDNumber</c>) plus a tick stamp. The first
    /// <see cref="TryGet"/> on a new tick auto-invalidates (clears) the whole map, so every entry is at most
    /// one tick old. It holds NO RimWorld/Verse types, so the cache DECISION — when it invalidates, when it
    /// hits vs misses — is unit-testable headlessly, while the value computation (the <c>MassUtility</c> walk)
    /// stays Verse-side in the caller. The mutable instance is owned by a <c>[ThreadStatic]</c> field in the
    /// runtime wrapper, matching this assembly's per-(pawn,tick) memo convention; this struct only holds the
    /// dictionary reference + the stamp.
    ///
    /// Correctness contract: this is a READ cache. Within one tick the cached value for a key is assumed
    /// stable; a caller that MUTATES the underlying state mid-tick and must observe the change immediately
    /// must NOT route through this memo (the change is otherwise seen only on the next tick, when the stamp
    /// advances and the map clears). This matches vanilla stat-cache semantics.
    /// </summary>
    /// <typeparam name="TValue">The memoized value (a small value type / immutable struct, e.g. a (cap,mass) pair).</typeparam>
    public struct TickKeyedMemo<TValue>
    {
        private Dictionary<int, TValue> map;
        private int stampTick;
        private bool initialized;

        /// <summary>
        /// True for the FIRST access of a fresh tick — i.e. the map would be cleared on the next
        /// <see cref="TryGet"/>/<see cref="Store"/> at <paramref name="currentTick"/>. Pure predicate exposed
        /// for testing the invalidation decision independent of the dictionary.
        /// </summary>
        public bool WouldInvalidate(int currentTick) => !initialized || currentTick != stampTick;

        /// <summary>
        /// Look up <paramref name="key"/> at <paramref name="currentTick"/>. If the tick advanced since the last
        /// access (or this is the first ever access), the whole memo is cleared first (so the result is always a
        /// MISS on a new tick). Returns true and the stored value on a same-tick hit; false otherwise.
        /// </summary>
        public bool TryGet(int currentTick, int key, out TValue value)
        {
            EnsureTick(currentTick);
            return map.TryGetValue(key, out value);
        }

        /// <summary>Store <paramref name="value"/> for <paramref name="key"/> at <paramref name="currentTick"/>
        /// (clearing first if the tick advanced), so the next same-tick <see cref="TryGet"/> hits.</summary>
        public void Store(int currentTick, int key, TValue value)
        {
            EnsureTick(currentTick);
            map[key] = value;
        }

        /// <summary>Drop all entries and reset the stamp — for cross-session hygiene on game load. The next
        /// access re-stamps to the current tick.</summary>
        public void Clear()
        {
            map?.Clear();
            stampTick = 0;
            initialized = false;
        }

        /// <summary>Current number of cached entries (for tests/diagnostics).</summary>
        public int Count => map?.Count ?? 0;

        private void EnsureTick(int currentTick)
        {
            if (map == null)
                map = new Dictionary<int, TValue>();
            if (!initialized || currentTick != stampTick)
            {
                map.Clear();
                stampTick = currentTick;
                initialized = true;
            }
        }
    }
}
