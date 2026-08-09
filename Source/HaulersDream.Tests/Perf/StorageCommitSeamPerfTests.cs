using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Allocation regression net for the pure leaves of the storage commitment seam (issues
    /// #114/#138/#162/#248) — the design's "pin it with the existing min-of-batches 0-allocation assertion
    /// harness on the pure leaves", which shipped without one.
    ///
    /// <para><b>Why these leaves and not others.</b> The seam is reached from
    /// <c>StoreUtility.IsGoodStoreCell</c>, the hottest method in the haul system: it is asked PER CELL, for
    /// every candidate a work scan considers. The Verse glue memoises the expensive part (the cell walk), but
    /// everything below is re-run on every one of those queries — three ledger sums, the decision rule, and
    /// the cross-def budget arithmetic. A byte allocated here is a byte per cell per candidate per scan, and
    /// this mod has microstutter reports on record. Allocation is deterministic where timing flakes, so this
    /// is the assertion that can actually hold on a shared CI box.</para>
    ///
    /// <para>→ NOTE: <see cref="StorageClaimLedger.Add"/> is deliberately NOT pinned at zero — it allocates a
    /// new array on every write, by design. That copy-on-write is what lets a reader take the array into a
    /// local and be immune to a concurrent replacement without a lock, and writes happen once per job start,
    /// not once per cell. <see cref="Add_Allocates_BecauseTheLedgerIsCopyOnWrite"/> pins that as the harness's
    /// own positive control: an allocation assertion whose passing state is "zero" cannot otherwise tell a
    /// clean leaf from a measurement that stopped working.</para>
    /// </summary>
    [TestFixture, Category("Perf")]
    public class StorageCommitSeamPerfTests
    {
        // --- fixtures ------------------------------------------------------------------------------------
        // Pawn / group / def are opaque `object` to the Core assembly and reference-compared, exactly as the
        // runtime passes them (a Pawn, an ISlotGroup, a ThingDef). Plain objects reproduce that faithfully.

        private static readonly object Steel = new object();
        private static readonly object Wood = new object();
        private static readonly object Cloth = new object();
        private static readonly object Shelf = new object();
        private static readonly object Fridge = new object();
        private static readonly object Asker = new object();
        private static readonly object Bob = new object();
        private static readonly object Cass = new object();

        /// <summary>
        /// The live-cargo source every ledger read clamps against, built ONCE. The runtime holds exactly one
        /// cached delegate instance for the same reason — a closure created per query would be an allocation
        /// per cell, which is the cost this fixture exists to catch. Wood answers below its recorded units so
        /// the clamp's <c>seen &lt; recorded</c> branch is exercised as well as its pass-through.
        /// </summary>
        private static readonly StorageClaimEvidence Evidence = (pawn, def) => ReferenceEquals(def, Wood) ? 5 : 200;

        /// <summary>A realistic ledger: three haulers contending for one shelf's steel (including the asker's
        /// own row, which planning subtracts and delivering does not), a second def on the same shelf for the
        /// cross-def path, and an unrelated group so the group filter has something to reject.</summary>
        private static readonly StorageClaimRow[] Rows =
        {
            new StorageClaimRow(Bob, Shelf, Steel, 75),
            new StorageClaimRow(Cass, Shelf, Steel, 40),
            new StorageClaimRow(Asker, Shelf, Steel, 25),
            new StorageClaimRow(Bob, Shelf, Wood, 60),
            new StorageClaimRow(Cass, Fridge, Cloth, 10)
        };

        /// <summary>One reused budget, mirroring <c>StorageCommitments.crossDefBudget</c> — the runtime keeps
        /// a single instance per thread precisely so the per-cell gate does not allocate one per query.</summary>
        private static readonly StorageGroupBudget Budget = new StorageGroupBudget(0);

        // --- the ledger sums -----------------------------------------------------------------------------

        [Test]
        public void ClaimedByOthers_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageClaimLedger.ClaimedByOthers(Rows, Shelf, Steel, Asker, Evidence),
                "the every-other-hauler sum runs on every per-cell gate query and must stay a plain loop");

        [Test]
        public void ClaimedByPawn_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageClaimLedger.ClaimedByPawn(Rows, Shelf, Steel, Asker, Evidence),
                "the own-claim sum a PLANNING pawn subtracts is read on the same per-cell path");

        [Test]
        public void ClaimedTotal_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageClaimLedger.ClaimedTotal(Rows, Shelf, Wood, Evidence),
                "the cross-def sum is read once per contending def inside a single gate query");

        [Test]
        public void EffectiveClaim_IsZeroAlloc_WhenTheClaimIsClampedByEvidence() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageClaimLedger.EffectiveClaim(Rows[3], Evidence),
                "the min(recorded, evidence) clamp IS the release mechanism — it runs for every row of every sum");

        [Test]
        public void EffectiveClaim_IsZeroAlloc_WhenTheRecordedUnitsStand() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageClaimLedger.EffectiveClaim(Rows[0], Evidence),
                "the pass-through branch (the pawn still carries everything it promised) must allocate nothing either");

        [Test]
        public void AnyRows_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageClaimLedger.AnyRows(Rows),
                "AnyRows is the 99% answer on the storage hot path — with nothing in flight the seam must cost nothing");

        // --- the decision rule ---------------------------------------------------------------------------

        [Test]
        public void Commit_IsZeroAlloc_WhenPlanning() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageCommitPolicy.Commit(new HaulSight(1, 100, 150, 115, 25, 75), delivering: false),
                "the production rule decides every haul count and must stay pure arithmetic");

        [Test]
        public void Commit_IsZeroAlloc_WhenDelivering() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageCommitPolicy.Commit(new HaulSight(1, 100, 150, 115, 25, 75), delivering: true),
                "the delivering branch (the pawn's own claim is not charged against it) must allocate nothing either");

        [Test]
        public void Commit_IsZeroAlloc_ForAnUnmeasuredDestination() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageCommitPolicy.Commit(new HaulSight(1, 100, int.MaxValue, 115, 25, 75), delivering: false),
                "the unmeasured-destination short-circuit is the answer for every group HD cannot price");

        // --- the cross-def budget ------------------------------------------------------------------------

        [Test]
        public void Budget_ResetPriceAndConsume_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () =>
                {
                    // The exact shape of StorageCommitments.RawSpaceFor: re-seed the shared instance, price the
                    // asker's def, charge each other def's live claim against the shared empty-cell pool, then
                    // read what is left. Reset must leave the dictionaries' capacity in place — a Clear that
                    // shed its buckets would re-grow them on every gate query.
                    Budget.Reset(4);
                    Budget.PriceDef(Steel, 30, 75);
                    if (!Budget.IsPriced(Wood))
                    {
                        Budget.PriceDef(Wood, 0, 75);
                        Budget.Consume(Wood, 60);
                    }
                    Budget.AvailableFor(Steel);
                },
                "the whole cross-def budget pass runs inside one per-cell gate query and must not allocate");

        [Test]
        public void Budget_Consume_IsZeroAlloc_WhenItOpensEmptyCells() =>
            AllocationAssert.AssertZeroAlloc(
                () =>
                {
                    Budget.Reset(4);
                    Budget.PriceDef(Steel, 10, 75);
                    // Spends the partial room first, then opens whole cells and books the unfilled tail back as
                    // this def's partial — the branch that writes a dictionary entry rather than only reading one.
                    Budget.Consume(Steel, 100);
                },
                "the cell-opening branch writes back a partial-room entry and must reuse the existing key");

        // --- the harness's own positive control ----------------------------------------------------------

        [Test]
        public void Add_Allocates_BecauseTheLedgerIsCopyOnWrite() =>
            Assert.That(
                AllocationAssert.Allocations(() => StorageClaimLedger.Add(Rows, Bob, Fridge, Cloth, 20)),
                Is.GreaterThan(0L),
                "Add must allocate — it replaces the rows array wholesale, which is what makes a reader that "
                + "took the reference into a local immune to a concurrent write without a lock. This is also "
                + "the control for every zero above: if the measurement ever stops seeing allocation, this "
                + "test fails first rather than the whole fixture passing vacuously.");
    }
}
