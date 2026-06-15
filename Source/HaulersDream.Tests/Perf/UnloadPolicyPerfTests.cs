using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Allocation regression net for the per-tick "has pending real work" queue scan (HD-UNLWORK).
    ///
    /// The runtime used to materialise a <c>List&lt;string&gt;</c> of queued job defNames and call
    /// <see cref="UnloadPolicy.HasPendingRealWork(IEnumerable{string}, string[])"/> (whose <c>params string[]</c>
    /// ALSO allocates) on a per-tick path (the 250t idle backstop + the 60t vein-extend), per player pawn/tracker.
    /// The fix iterates the queue in place and calls the allocation-free
    /// <see cref="UnloadPolicy.IsPendingRealWork{T}(T, T, T)"/> per queued job, comparing the <c>JobDef</c> identity
    /// by reference (here modelled with sentinel <see cref="object"/>s, exactly the reference-equality the runtime
    /// uses on Verse <c>JobDef</c> singletons).
    ///
    /// This fixture asserts:
    ///   1. The reference-compare scan (the new runtime shape) allocates 0 bytes/op (the win).
    ///   2. The old <c>List&lt;string&gt;</c> + <c>params string[]</c> path allocates &gt; 0 bytes/op (the contrast).
    ///   3. ORACLE: the new per-job scan agrees with the old string-based <c>HasPendingRealWork</c> across scenarios.
    ///
    /// Measurement: <see cref="GC.GetAllocatedBytesForCurrentThread"/> (net48 / .NET FW 4.6+) — the deterministic
    /// per-thread jitter proxy. Bodies are pre-built delegates on a single thread; never an absolute ns budget.
    /// </summary>
    [TestFixture]
    [Category("Perf")]
    public class UnloadPolicyPerfTests
    {
        private static long BytesPerOp(Action body) => AllocationAssert.Allocations(body);

        // Non-boxing sink for the bool result (GC.KeepAlive(bool) would BOX the bool ~24 B/op, a TEST artifact —
        // not a real allocation in the runtime path). Folding into an int field keeps the measurement honest.
        private static int sink;

        // Two stable housekeeping "JobDef" identities (reference equality stand-ins for the Verse JobDef singletons
        // HaulersDream_SelfPickup / HaulersDream_UnloadInventory). Each carries a defName so the old string path can
        // be exercised in parallel for the oracle.
        private static readonly object SelfPickup = "HaulersDream_SelfPickup";
        private static readonly object Unload = "HaulersDream_UnloadInventory";

        // A realistic queued-job set: a real harvest job ahead of the two housekeeping jobs (the common "mid-run"
        // case the unload deferral protects).
        private static readonly object[] QueueWithRealWork =
            { "HarvestDesignated", SelfPickup, Unload };

        // Only housekeeping queued (the case where an automatic unload may proceed).
        private static readonly object[] QueueOnlyHousekeeping = { SelfPickup, Unload };

        /// <summary>The runtime's new shape: an indexed scan over the queue calling IsPendingRealWork per job.</summary>
        private static bool ScanRefCompare(object[] queue)
        {
            for (int i = 0; i < queue.Length; i++)
                if (UnloadPolicy.IsPendingRealWork(queue[i], SelfPickup, Unload))
                    return true;
            return false;
        }

        // --- (1) the win: the reference-compare scan is allocation-free ---

        [Test]
        public void IsPendingRealWorkScan_IsZeroAlloc_WithRealWork()
        {
            Action body = () => { sink += ScanRefCompare(QueueWithRealWork) ? 1 : 0; };
            long bytes = BytesPerOp(body);
            Assert.That(bytes, Is.EqualTo(0L),
                $"the IsPendingRealWork queue scan must be 0-alloc on the per-tick path; measured {bytes} B/op");
        }

        [Test]
        public void IsPendingRealWorkScan_IsZeroAlloc_OnlyHousekeeping()
        {
            // Full iteration (no real work found → false) must also not allocate.
            Action body = () => { sink += ScanRefCompare(QueueOnlyHousekeeping) ? 1 : 0; };
            long bytes = BytesPerOp(body);
            Assert.That(bytes, Is.EqualTo(0L),
                $"the IsPendingRealWork scan must be 0-alloc even on the all-housekeeping false path; measured {bytes} B/op");
        }

        // --- (2) the contrast: the old List<string> + params string[] path allocates ---

        [Test]
        public void OldListStringPath_AllocatesMoreThanZero()
        {
            // The exact pattern HD-UNLWORK removes: build a List<string> of queued defNames, then call the
            // params-overload (which also allocates its string[] when the call site passes two names).
            string self = (string)SelfPickup, unl = (string)Unload;
            Action body = () =>
            {
                var defNames = new List<string>();
                for (int i = 0; i < QueueWithRealWork.Length; i++)
                    defNames.Add((string)QueueWithRealWork[i]);
                sink += UnloadPolicy.HasPendingRealWork(defNames, self, unl) ? 1 : 0;
            };
            long bytes = BytesPerOp(body);
            Assert.That(bytes, Is.GreaterThan(0L),
                "the List<string> + params overload path must allocate (the list + params array) — this is the cost " +
                $"the reference-compare scan removes; measured {bytes} B/op");
        }

        // --- (3) the oracle: the new per-job scan agrees with the old string-based HasPendingRealWork ---

        private static IEnumerable<TestCaseData> OracleCases()
        {
            yield return new TestCaseData((object)new object[] { "HarvestDesignated", SelfPickup, Unload })
                .SetName("real work ahead of housekeeping → true");
            yield return new TestCaseData((object)new object[] { SelfPickup, Unload })
                .SetName("only housekeeping → false");
            yield return new TestCaseData((object)new object[] { SelfPickup })
                .SetName("single self-pickup → false");
            yield return new TestCaseData((object)new object[] { SelfPickup, "Mine" })
                .SetName("housekeeping then real work → true");
            yield return new TestCaseData((object)new object[0])
                .SetName("empty queue → false");
            yield return new TestCaseData((object)new object[] { null, SelfPickup })
                .SetName("null job then housekeeping → false (null = not real work)");
            yield return new TestCaseData((object)new object[] { null, "Mine" })
                .SetName("null job then real work → true");
        }

        [TestCaseSource(nameof(OracleCases))]
        public void IsPendingRealWorkScan_MatchesStringOverload(object[] queue)
        {
            bool fast = ScanRefCompare(queue);

            // Old oracle: materialise the defName list (skipping nulls, mirroring the old runtime which only added
            // qj.job.def != null) and run the string overload.
            var names = new List<string>();
            for (int i = 0; i < queue.Length; i++)
                if (queue[i] != null)
                    names.Add((string)queue[i]);
            bool oracle = UnloadPolicy.HasPendingRealWork(names, (string)SelfPickup, (string)Unload);

            Assert.That(fast, Is.EqualTo(oracle),
                "the reference-compare IsPendingRealWork scan must agree with the string-based HasPendingRealWork");
        }
    }
}
