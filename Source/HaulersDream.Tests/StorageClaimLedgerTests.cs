using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The storage claim ledger's arithmetic — who has promised what to which shelf, and what that promise
    /// is still worth once the pawn's real cargo is taken into account.
    ///
    /// <para>The two properties everything else rests on, pinned after every operation: a group's total is
    /// exactly one pawn's own claim plus everybody else's, and a row is worth
    /// <c>min(recorded, evidence)</c>. The first is what makes the exclude-self split trustworthy; the
    /// second is the entire release mechanism — it is why a claim cannot outlive its cargo and why the
    /// phantom claim that starves a destination forever is not expressible here.</para>
    /// </summary>
    [TestFixture]
    public class StorageClaimLedgerTests
    {
        /// <summary>A stand-in for a pawn, a slot group or a thing def. Named so a failed assertion says
        /// which one, and reference-compared exactly as the game types are.</summary>
        private sealed class Token
        {
            private readonly string name;

            /// <summary>Name it.</summary>
            /// <param name="name">What this token stands for in the scenario.</param>
            public Token(string name) => this.name = name;

            /// <summary>The name, for assertion messages.</summary>
            /// <returns>The name.</returns>
            public override string ToString() => name;
        }

        private static readonly Token Alice = new Token("Alice");
        private static readonly Token Bob = new Token("Bob");
        private static readonly Token Carl = new Token("Carl");
        private static readonly Token Shelf = new Token("Shelf");
        private static readonly Token Fridge = new Token("Fridge");
        private static readonly Token Steel = new Token("Steel");
        private static readonly Token Wood = new Token("Wood");

        /// <summary>An evidence source built from a table, so a scenario states what each pawn is really
        /// carrying instead of implying it.</summary>
        /// <param name="table">Per (pawn, def) units; anything absent reads as nothing carried.</param>
        /// <returns>The evidence delegate.</returns>
        private static StorageClaimEvidence EvidenceOf(Dictionary<(object, object), int> table)
            => (pawn, def) => table.TryGetValue((pawn, def), out int units) ? units : 0;

        /// <summary>Evidence that always agrees with whatever was recorded — the "nothing has gone wrong
        /// yet" world, where the ledger's own bookkeeping is the only thing under test.</summary>
        private static readonly StorageClaimEvidence Plenty = (pawn, def) => int.MaxValue;

        /// <summary>Evidence that every pawn is carrying nothing at all.</summary>
        private static readonly StorageClaimEvidence Nothing = (pawn, def) => 0;

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  1. The invariant: a group's total splits exactly into "mine" and "everyone else's".
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Assert the split for every pawn mentioned, so an operation that corrupted one row is
        /// caught wherever it happened rather than only where the test happened to look.</summary>
        /// <param name="rows">The ledger.</param>
        /// <param name="evidence">The evidence source in force.</param>
        /// <param name="where">What had just been done, for the failure message.</param>
        private static void AssertSplitsCleanly(StorageClaimRow[] rows, StorageClaimEvidence evidence, string where)
        {
            foreach (var group in new[] { Shelf, Fridge })
            {
                foreach (var def in new[] { Steel, Wood })
                {
                    int total = StorageClaimLedger.ClaimedTotal(rows, group, def, evidence);
                    foreach (var pawn in new[] { Alice, Bob, Carl })
                    {
                        int others = StorageClaimLedger.ClaimedByOthers(rows, group, def, pawn, evidence);
                        int own = StorageClaimLedger.ClaimedByPawn(rows, group, def, pawn, evidence);
                        Assert.That(others + own, Is.EqualTo(total),
                            $"{group}/{def} does not split cleanly for {pawn} after {where}");
                    }
                }
            }
        }

        [Test]
        public void EveryOperationLeavesTheGroupTotalSplittingCleanly()
        {
            var rows = StorageClaimLedger.Empty;
            AssertSplitsCleanly(rows, Plenty, "an empty ledger");

            rows = StorageClaimLedger.Add(rows, Alice, Shelf, Steel, 40);
            AssertSplitsCleanly(rows, Plenty, "Alice claiming 40 steel");

            rows = StorageClaimLedger.Add(rows, Bob, Shelf, Steel, 35);
            AssertSplitsCleanly(rows, Plenty, "Bob claiming 35 steel on the same shelf");

            rows = StorageClaimLedger.Add(rows, Bob, Fridge, Wood, 10);
            AssertSplitsCleanly(rows, Plenty, "Bob also claiming wood elsewhere");

            rows = StorageClaimLedger.Add(rows, Alice, Shelf, Steel, 0);
            AssertSplitsCleanly(rows, Plenty, "Alice standing down");

            rows = StorageClaimLedger.DropPawn(rows, Bob);
            AssertSplitsCleanly(rows, Plenty, "Bob being dropped");
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  2. One destination per (pawn, def) — a re-target replaces, never accumulates.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void RetargetingADefMovesTheClaim_ItDoesNotDuplicateIt()
        {
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 50);
            rows = StorageClaimLedger.Add(rows, Alice, Fridge, Steel, 50);

            Assert.That(rows.Length, Is.EqualTo(1), "the pawn delivers one def to one place at a time");
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, Plenty), Is.EqualTo(0),
                "the abandoned destination must stop being charged the moment the pawn re-targets");
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Fridge, Steel, Plenty), Is.EqualTo(50));
        }

        [Test]
        public void APawnMayClaimSeveralDefsAtOnce()
        {
            // A bulk sweep carries several defs in one trip, so the "one destination" rule is per DEF, not
            // per pawn — getting that wrong would silently drop every claim but the last.
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 20);
            rows = StorageClaimLedger.Add(rows, Alice, Shelf, Wood, 30);

            Assert.That(rows.Length, Is.EqualTo(2));
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, Plenty), Is.EqualTo(20));
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Wood, Plenty), Is.EqualTo(30));
        }

        [Test]
        public void AddIsAllocationFreeWhenNothingChanges()
        {
            // Same array back on a no-op, because the hot path reads this reference on every storage query
            // and a fresh array per write would be pure garbage — and because a reader holding the old
            // reference must stay correct.
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 5);
            Assert.That(StorageClaimLedger.Add(rows, Bob, Shelf, Steel, 0), Is.SameAs(rows));
            Assert.That(StorageClaimLedger.Add(rows, Alice, null, Steel, 5), Is.Not.SameAs(rows),
                "a null group retires the claim, which is a real change");
            Assert.That(StorageClaimLedger.DropPawn(rows, Carl), Is.SameAs(rows));
        }

        [Test]
        public void ANullPawnOrDefIsRefused()
        {
            // An unattributable row could never be released and would hold its destination forever; a
            // def-less row could never be matched by a reader and would leak.
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, null, Shelf, Steel, 10);
            Assert.That(rows.Length, Is.EqualTo(0));
            rows = StorageClaimLedger.Add(rows, Alice, Shelf, null, 10);
            Assert.That(rows.Length, Is.EqualTo(0));
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  3. Evidence: effectiveClaim = min(recorded, evidence). The whole release mechanism.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void AClaimIsWorthTheLesserOfWhatWasPromisedAndWhatIsCarried()
        {
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 40);

            var carryingLess = EvidenceOf(new Dictionary<(object, object), int> { [(Alice, Steel)] = 10 });
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, carryingLess), Is.EqualTo(10),
                "a pawn that deposited most of its load stops withholding what it no longer has");

            var carryingMore = EvidenceOf(new Dictionary<(object, object), int> { [(Alice, Steel)] = 400 });
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, carryingMore), Is.EqualTo(40),
                "and carrying more than it promised never inflates the promise");
        }

        [Test]
        public void ACargolessPawnStopsHoldingTheDestination()
        {
            // The phantom claim — a load that vanished while the accounting kept blocking — made
            // inexpressible. AClaimNeverReleased_StarvesTheDestinationInstead in the concurrency suite is
            // the failure this prevents.
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 75);

            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, Nothing), Is.EqualTo(0));
            Assert.That(StorageClaimLedger.ClaimedByOthers(rows, Shelf, Steel, Bob, Nothing), Is.EqualTo(0));
        }

        [Test]
        public void EvidenceBoundaries()
        {
            var row = new StorageClaimRow(Alice, Shelf, Steel, 20);

            Assert.That(StorageClaimLedger.EffectiveClaim(row, Nothing), Is.EqualTo(0), "evidence 0");
            Assert.That(StorageClaimLedger.EffectiveClaim(row, Plenty), Is.EqualTo(20), "evidence int.MaxValue");
            Assert.That(StorageClaimLedger.EffectiveClaim(row, (p, d) => -5), Is.EqualTo(0),
                "negative evidence reads as nothing carried, never as a negative claim");
            Assert.That(StorageClaimLedger.EffectiveClaim(new StorageClaimRow(Alice, Shelf, Steel, -3), Plenty),
                Is.EqualTo(0), "a negative recorded figure never becomes a negative charge");
            Assert.That(StorageClaimLedger.EffectiveClaim(row, null), Is.EqualTo(20),
                "no way to measure means charge what was recorded — never silently zero every claim");
        }

        [Test]
        public void SumsSaturateInsteadOfWrapping()
        {
            // Two pathological rows must report "as much as an int can say", not a negative number that
            // would read as a destination with room to spare.
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, int.MaxValue);
            rows = StorageClaimLedger.Add(rows, Bob, Shelf, Steel, int.MaxValue);

            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, Plenty), Is.EqualTo(int.MaxValue));
            Assert.That(StorageClaimLedger.ClaimedByOthers(rows, Shelf, Steel, Carl, Plenty),
                Is.EqualTo(int.MaxValue));
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  4. Reconcile: the periodic self-heal.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void ReconcileDropsRowsWithNothingBehindThem()
        {
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 10);
            rows = StorageClaimLedger.Add(rows, Bob, Shelf, Steel, 10);

            var onlyAliceCarries = EvidenceOf(new Dictionary<(object, object), int> { [(Alice, Steel)] = 10 });
            rows = StorageClaimLedger.Reconcile(rows, onlyAliceCarries, null);

            Assert.That(rows.Length, Is.EqualTo(1));
            Assert.That(rows[0].Pawn, Is.SameAs(Alice));
        }

        [Test]
        public void ReconcileDropsRowsWhoseDestinationIsGone()
        {
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 10);
            rows = StorageClaimLedger.Add(rows, Bob, Fridge, Steel, 10);

            rows = StorageClaimLedger.Reconcile(rows, Plenty, group => !ReferenceEquals(group, Shelf));

            Assert.That(rows.Length, Is.EqualTo(1), "the deconstructed shelf's claim goes with it");
            Assert.That(rows[0].Group, Is.SameAs(Fridge));
        }

        [Test]
        public void ReconcileWithNoWayToMeasureKeepsEverything()
        {
            // A reconcile that cannot measure must not delete the ledger — that would hand every
            // destination back to every hauler at once, which is the original bug on a timer.
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 10);
            Assert.That(StorageClaimLedger.Reconcile(rows, null, null), Is.SameAs(rows));
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  5. The hot-path gate.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void AnyRowsIsAPlainLengthTest()
        {
            Assert.That(StorageClaimLedger.AnyRows(null), Is.False);
            Assert.That(StorageClaimLedger.AnyRows(StorageClaimLedger.Empty), Is.False);

            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, Alice, Shelf, Steel, 1);
            Assert.That(StorageClaimLedger.AnyRows(rows), Is.True);
            // Deliberately still true for a row whose pawn is carrying nothing: the gate must stay a length
            // read, and a stale row costs one wasted evidence check on the rare path, never a wrong answer.
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, Shelf, Steel, Nothing), Is.EqualTo(0));
        }

        [Test]
        public void EveryReadTreatsANullLedgerAsEmpty()
        {
            Assert.That(StorageClaimLedger.ClaimedTotal(null, Shelf, Steel, Plenty), Is.EqualTo(0));
            Assert.That(StorageClaimLedger.ClaimedByOthers(null, Shelf, Steel, Alice, Plenty), Is.EqualTo(0));
            Assert.That(StorageClaimLedger.ClaimedByPawn(null, Shelf, Steel, Alice, Plenty), Is.EqualTo(0));
            Assert.That(StorageClaimLedger.DropPawn(null, Alice), Is.SameAs(StorageClaimLedger.Empty));
            Assert.That(StorageClaimLedger.Reconcile(null, Plenty, null), Is.SameAs(StorageClaimLedger.Empty));
        }
    }
}
