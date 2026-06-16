using System;
using System.Collections.Generic;

namespace HaulersDream
{
    /// <summary>
    /// A SELF-REGISTERING registry of per-session static cache clears, so a new per-session cache can never be
    /// forgotten in the game-load hygiene sweep. <see cref="HaulersDreamGameComponent.FinalizeInit"/> calls
    /// <see cref="ClearAll"/> once whenever a game finishes initialising (new game and load alike); each per-session
    /// static cache contributes its idempotent <c>Clear()</c> via a static constructor (<c>Register(Clear)</c>),
    /// replacing the former hand-maintained list of <c>X.Clear();</c> calls that silently rotted whenever a cache
    /// was added without a matching line there.
    ///
    /// <para><b>Why a static constructor on each cache is the correct registration point.</b> A static cache only
    /// holds stale cross-session data if it was actually USED this process — and using it (any static member access)
    /// runs its static constructor first, which registers its <c>Clear</c>. A cache that was never touched this
    /// process is empty, so not clearing it is harmless. This registry is itself static, so registrations persist
    /// across same-process quickloads: a cache used before the first load is registered for every subsequent load
    /// without re-running its static ctor.</para>
    ///
    /// <para><b>Thread safety.</b> A static field initializer / static constructor can run on whatever thread first
    /// touches the type — including a worker-thread work scan that reaches a cache before the main thread does — so
    /// registrations may race. A simple lock around the backing list makes <see cref="Register"/> /
    /// <see cref="ClearAll"/> safe. <see cref="ClearAll"/> snapshots under the lock and invokes the actions OUTSIDE
    /// it, so a clear action that itself touches a (lazily-registering) cache cannot deadlock or mutate the list
    /// mid-iteration.</para>
    ///
    /// <para>This is HYGIENE, not the correctness safeguard: each cache's own <c>tick != -1</c> / tick-stamp
    /// populate guard is what actually prevents a cross-session stale read. <see cref="ClearAll"/> only drops the
    /// references promptly and keeps an equal-tick quickload from briefly serving a previous session's value on the
    /// main (FinalizeInit) thread.</para>
    /// </summary>
    internal static class CacheRegistry
    {
        // The lock object and the registered clears. A List (not a HashSet) — the same Action delegate is only ever
        // registered ONCE per cache (from that cache's static ctor, which runs at most once per process), so there
        // are no duplicates to dedupe.
        private static readonly object sync = new object();
        private static readonly List<Action> clears = new List<Action>();

        /// <summary>Register a per-session static cache's idempotent <c>Clear()</c> to be invoked on every game load
        /// (via <see cref="ClearAll"/>). Called from the cache's static constructor, so it runs once per process the
        /// first time that cache's type is touched. Null-safe (a null action is ignored).</summary>
        internal static void Register(Action clear)
        {
            if (clear == null)
                return;
            lock (sync)
                clears.Add(clear);
        }

        /// <summary>Invoke every registered cache clear — the game-load (<see cref="HaulersDreamGameComponent.FinalizeInit"/>)
        /// hygiene sweep. Snapshots the registered clears under the lock, then invokes them OUTSIDE the lock so a
        /// clear that lazily touches another cache (running its static ctor -> <see cref="Register"/>) can't
        /// re-enter the lock or mutate the list mid-iteration. No try/catch: a clear is a trivial reset; a throw
        /// there is a real bug to surface, not to swallow.</summary>
        internal static void ClearAll()
        {
            Action[] snapshot;
            lock (sync)
                snapshot = clears.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i]();
        }
    }
}
