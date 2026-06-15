using System;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Allocation / throughput measurement for the perf harness (see tools/perf-plan.md §4).
    ///
    /// <para><see cref="GC.GetAllocatedBytesForCurrentThread"/> is the deterministic, per-thread
    /// jitter proxy (available on net48 since .NET FW 4.6). The body MUST be a pre-built delegate
    /// (no loop-variable capture) and the test MUST run on ONE thread (no Task/async — the counter
    /// is per-thread). Never assert an absolute ns budget (CPU/box-dependent); the throughput helper
    /// is informational / relative-ratio only.</para>
    /// </summary>
    public static class AllocationAssert
    {
        /// <summary>
        /// Bytes allocated per call of <paramref name="body"/>, after JIT warmup + a GC quiesce.
        ///
        /// <para>Returns the <b>minimum</b> per-op bytes across several measurement batches. The true
        /// per-call allocation is a hard FLOOR — a method that allocates N bytes/call can never measure
        /// below N. Transient noise (a tiered-JIT recompilation landing inside one batch, an incidental
        /// on-thread allocation) can only ADD bytes to a given batch, so the minimum across batches is
        /// the robust, deterministic estimate of the real per-call cost. A genuinely 0-alloc body yields
        /// a clean 0 batch (and we early-exit); an allocating body measures its floor every batch.</para>
        /// </summary>
        public static long Allocations(Action body, int warmup = 200, int iters = 1000, int batches = 8)
        {
            for (int i = 0; i < warmup; i++) body();               // force JIT + first-use static ctors
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long best = long.MaxValue;
            for (int b = 0; b < batches; b++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < iters; i++) body();
                long after = GC.GetAllocatedBytesForCurrentThread();
                long perOp = (after - before) / iters;             // bytes/op (integer) for this batch
                if (perOp < best) best = perOp;                    // floor = true cost; noise only adds
                if (best == 0) break;                              // 0-alloc proven — stop early
            }
            return best;
        }

        /// <summary>Assert <paramref name="body"/> allocates 0 bytes/op (the regression net).</summary>
        public static void AssertZeroAlloc(Action body, string because) =>
            Assert.That(Allocations(body), Is.EqualTo(0), because);

        /// <summary>Assert <paramref name="body"/> allocates at most <paramref name="maxBytes"/>/op
        /// (a committed baseline for the intrinsically-allocating leaves).</summary>
        public static void AssertAllocAtMost(Action body, long maxBytes, string because) =>
            Assert.That(Allocations(body), Is.LessThanOrEqualTo(maxBytes), because);

        /// <summary>
        /// Throughput (informational / relative-ratio only — NEVER an absolute ns threshold):
        /// nanoseconds per op of <paramref name="body"/>.
        /// </summary>
        public static double NsPerOp(Action body, int warmup = 1000, int iters = 100_000)
        {
            for (int i = 0; i < warmup; i++) body();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iters; i++) body();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds * 1e6 / iters;
        }
    }
}
