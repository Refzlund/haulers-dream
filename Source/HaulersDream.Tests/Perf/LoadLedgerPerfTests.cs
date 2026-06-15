using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Allocation regression net for the claim-ledger's per-pawn-scan emptiness probe (HD-LEDGER).
    ///
    /// The flagship 0-alloc target: the new <see cref="LoadLedger{TDef,TPawn}.HasWork"/> must compute the same
    /// predicate as <c>AvailableToClaim(...).Values.Any(v &gt; 0)</c> WITHOUT building the throwaway result
    /// <c>Dictionary</c>. This fixture asserts:
    ///   1. <c>HasWork</c> allocates 0 bytes/op (the win).
    ///   2. The old <c>AvailableToClaim(...).Any()</c> path allocates &gt; 0 bytes/op (the contrast it replaces).
    ///   3. ORACLE: <c>HasWork == AvailableToClaim(...).Values.Any(v &gt; 0)</c> across many scenarios (correctness
    ///      — the optimisation may not change behaviour).
    ///
    /// Measurement: <see cref="GC.GetAllocatedBytesForCurrentThread"/> (available since .NET FW 4.6; we target
    /// net48) is the deterministic per-thread jitter proxy. The body must be a pre-built delegate run on a single
    /// thread (no Task/async — the counter is per-thread). Never assert an absolute ns budget here.
    /// </summary>
    [TestFixture]
    [Category("Perf")]
    public class LoadLedgerPerfTests
    {
        // --- allocation measurement (self-contained; no dependency on a shared harness helper) ---

        /// <summary>
        /// Mean bytes allocated per <paramref name="body"/> invocation: JIT-warm + GC-quiesce, then diff the
        /// per-thread allocation counter across <paramref name="iters"/> calls. The integer division floors, so a
        /// genuine sub-1-byte/op average reads as 0 — fine for a "must be 0" guard; the contrast path allocates
        /// tens of bytes/op so it reads &gt; 0 robustly.
        /// </summary>
        private static long BytesPerOp(Action body, int warmup = 200, int iters = 2000)
        {
            for (int i = 0; i < warmup; i++) body();           // force JIT + first-use static ctors
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iters; i++) body();
            long after = GC.GetAllocatedBytesForCurrentThread();
            return (after - before) / iters;
        }

        // --- fixtures: realistic ledger state (a partly-claimed multi-def manifest) ---

        // A manifest with leftover work for asker 99 (steel 50 needed, only 10 claimed by others → 40 available).
        private static (Dictionary<string, int> needed,
                        Dictionary<string, int> claimed,
                        Dictionary<int, Dictionary<string, int>> pawnClaims) WithLeftoverWork()
        {
            var needed = new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30, ["cloth"] = 20 };
            var claimed = new Dictionary<string, int> { ["steel"] = 10, ["wood"] = 30, ["cloth"] = 20 };
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>
            {
                [1] = new Dictionary<string, int> { ["steel"] = 10, ["wood"] = 30 },
                [2] = new Dictionary<string, int> { ["cloth"] = 20 },
            };
            return (needed, claimed, pawnClaims);
        }

        // A fully-claimed manifest: nothing left for a fresh asker (every def's claimed == needed).
        private static (Dictionary<string, int> needed,
                        Dictionary<string, int> claimed,
                        Dictionary<int, Dictionary<string, int>> pawnClaims) FullyClaimed()
        {
            var needed = new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30 };
            var claimed = new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30 };
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>
            {
                [1] = new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30 },
            };
            return (needed, claimed, pawnClaims);
        }

        // --- (1) the win: HasWork is allocation-free ---

        [Test]
        public void HasWork_IsZeroAlloc_WithLeftoverWork()
        {
            var (needed, claimed, pawnClaims) = WithLeftoverWork();
            // asker 99 is fresh (no own claim) and there IS leftover steel → HasWork returns true on the first
            // positive def. Even the true-result path must not allocate.
            Action body = () => LoadLedger<string, int>.HasWork(needed, claimed, pawnClaims, 99);
            long bytes = BytesPerOp(body);
            Assert.That(bytes, Is.EqualTo(0L),
                $"LoadLedger.HasWork must be 0-alloc on the per-pawn-scan path; measured {bytes} B/op");
        }

        [Test]
        public void HasWork_IsZeroAlloc_WhenFullyClaimed()
        {
            var (needed, claimed, pawnClaims) = FullyClaimed();
            // No leftover for a fresh asker → HasWork iterates every def and returns false. The full-iteration
            // (false) path must also not allocate.
            Action body = () => LoadLedger<string, int>.HasWork(needed, claimed, pawnClaims, 99);
            long bytes = BytesPerOp(body);
            Assert.That(bytes, Is.EqualTo(0L),
                $"LoadLedger.HasWork must be 0-alloc even on the all-defs-iterated false path; measured {bytes} B/op");
        }

        // --- (2) the contrast: the replaced AvailableToClaim(...).Any() path allocates ---

        [Test]
        public void AvailableToClaimAny_AllocatesMoreThanZero()
        {
            var (needed, claimed, pawnClaims) = WithLeftoverWork();
            // This is the pattern HD-LEDGER eliminates: build (and immediately discard) the result Dictionary just
            // to test emptiness. Proves the contrast — the dict materialisation is real allocation.
            Action body = () =>
            {
                var avail = LoadLedger<string, int>.AvailableToClaim(needed, claimed, pawnClaims, 99);
                bool any = avail.Values.Any(v => v > 0);
                GC.KeepAlive(any);
            };
            long bytes = BytesPerOp(body);
            Assert.That(bytes, Is.GreaterThan(0L),
                "AvailableToClaim(...).Any() must allocate (the throwaway dict) — this is the cost HasWork removes; " +
                $"measured {bytes} B/op");
        }

        // --- (3) the oracle: HasWork agrees with AvailableToClaim(...).Values.Any(v > 0) everywhere ---

        // Each case: (needed, claimed, pawnClaims, asker). Spans: fresh asker with leftover, fully claimed,
        // asker re-planning its own claim (own units must NOT count against it), over-claim, empty/null inputs,
        // def present only in claimed (not needed), and a mix.
        private static IEnumerable<TestCaseData> OracleCases()
        {
            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<string, int> { ["steel"] = 10 },
                new Dictionary<int, Dictionary<string, int>> { [1] = new Dictionary<string, int> { ["steel"] = 10 } },
                99).SetName("fresh asker, leftover steel → true");

            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<int, Dictionary<string, int>> { [1] = new Dictionary<string, int> { ["steel"] = 50 } },
                99).SetName("fully claimed, fresh asker → false");

            // The asker re-planning: its own 50 must be excluded so it still sees the work as available to itself.
            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<int, Dictionary<string, int>> { [1] = new Dictionary<string, int> { ["steel"] = 50 } },
                1).SetName("asker re-plans own full claim → true (own excluded)");

            // Over-claim by others (claimed > needed) → nothing for a fresh asker.
            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<string, int> { ["steel"] = 80 },
                new Dictionary<int, Dictionary<string, int>> { [1] = new Dictionary<string, int> { ["steel"] = 80 } },
                99).SetName("others over-claimed → false");

            // Multi-def: only one def has leftover.
            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30, ["cloth"] = 20 },
                new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30, ["cloth"] = 5 },
                new Dictionary<int, Dictionary<string, int>>
                {
                    [1] = new Dictionary<string, int> { ["steel"] = 50, ["wood"] = 30, ["cloth"] = 5 },
                },
                99).SetName("multi-def, only cloth leftover → true");

            // Def present in claimed but NOT in needed — must be ignored (only needed defs count).
            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50 },
                new Dictionary<string, int> { ["steel"] = 50, ["ghost"] = 99 },
                new Dictionary<int, Dictionary<string, int>>
                {
                    [1] = new Dictionary<string, int> { ["steel"] = 50, ["ghost"] = 99 },
                },
                99).SetName("extra claimed def absent from needed → false");

            // Empty needed → no work.
            yield return new TestCaseData(
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<int, Dictionary<string, int>>(),
                99).SetName("empty needed → false");

            // Null claimed + null pawnClaims, with needed → everything available.
            yield return new TestCaseData(
                new Dictionary<string, int> { ["steel"] = 50 },
                (Dictionary<string, int>)null,
                (Dictionary<int, Dictionary<string, int>>)null,
                99).SetName("null claimed/pawnClaims, needed present → true");

            // Null needed → false (no work possible).
            yield return new TestCaseData(
                (Dictionary<string, int>)null,
                new Dictionary<string, int> { ["steel"] = 10 },
                new Dictionary<int, Dictionary<string, int>>(),
                99).SetName("null needed → false");
        }

        [TestCaseSource(nameof(OracleCases))]
        public void HasWork_MatchesAvailableToClaimAny(
            Dictionary<string, int> needed,
            Dictionary<string, int> claimed,
            Dictionary<int, Dictionary<string, int>> pawnClaims,
            int asker)
        {
            bool fast = LoadLedger<string, int>.HasWork(needed, claimed, pawnClaims, asker);
            bool oracle = LoadLedger<string, int>.AvailableToClaim(needed, claimed, pawnClaims, asker)
                                                 .Values.Any(v => v > 0);
            Assert.That(fast, Is.EqualTo(oracle),
                "HasWork must agree with AvailableToClaim(...).Values.Any(v > 0)");
        }
    }
}
