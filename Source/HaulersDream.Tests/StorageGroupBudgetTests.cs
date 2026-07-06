using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class StorageGroupBudgetTests
    {
        // Def keys stand in for ThingDef singletons (reference-compared in the real budget). Distinct string
        // objects give distinct keys; reusing the variable reuses the key.
        private const string Meat = "meat";
        private const string Rice = "rice";

        // ── single def: available = partial + emptyCells * perCell, consume decrements ──────────────────

        [Test]
        public void SingleDef_AvailableIsPartialPlusEmptyCells()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 10, perCellCapacity: 75);
            // 10 top-up room in existing meat cells + two empty cells at 75 each.
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(10 + 2 * 75));
        }

        [Test]
        public void Consume_SpendsPartialRoomFirst()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 30, perCellCapacity: 75);
            b.Consume(Meat, 20);                       // fits entirely in partial room
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(10 + 2 * 75)); // 10 partial left, cells untouched
        }

        [Test]
        public void Consume_OpensWholeEmptyCells_AndKeepsTheTailAsSameDefTopUp()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            b.Consume(Meat, 40);                       // opens one empty cell, 35 of it left unfilled
            // 35 tail (now meat top-up room) + one still-empty cell at 75.
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(35 + 1 * 75));
            b.Consume(Meat, 35);                       // top up the opened cell, no new cell used
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(0 + 1 * 75));
        }

        // ── THE FIX (#138): two defs bound for the same group share the empty-cell pool ─────────────────

        [Test]
        public void CrossDef_EmptyCellsAreSharedNotDoubleCounted()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            b.PriceDef(Rice, partialSpace: 0, perCellCapacity: 75);
            // Before the fix each def independently saw the full 150; they must see the SAME shared pool.
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(150));
            Assert.That(b.AvailableFor(Rice), Is.EqualTo(150));

            b.Consume(Meat, 150);                      // meat fills both empty cells
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(0));
            Assert.That(b.AvailableFor(Rice), Is.EqualTo(0)); // rice now correctly gets nothing
        }

        [Test]
        public void CrossDef_PartialConsumeLeavesOneCellForTheOtherDef()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            b.PriceDef(Rice, partialSpace: 0, perCellCapacity: 75);
            b.Consume(Meat, 40);                       // opens one of the two empty cells
            // Meat can still top up its opened cell AND take the remaining empty one; rice only the empty one.
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(35 + 75));
            Assert.That(b.AvailableFor(Rice), Is.EqualTo(75));
        }

        [Test]
        public void Reporter_SmallStockpileHoldsTwoStacks_ThirdDefStaysHome()
        {
            // The reported case: a small 2-cell high-priority stockpile; the pawn sweeps meat, meat, rice.
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            int meatA = b.AvailableFor(Meat);
            b.Consume(Meat, meatA >= 75 ? 75 : meatA); // first meat stack
            int meatB = b.AvailableFor(Meat);
            b.Consume(Meat, meatB >= 75 ? 75 : meatB); // second meat stack fills the stockpile
            b.PriceDef(Rice, partialSpace: 0, perCellCapacity: 75);
            Assert.That(b.AvailableFor(Rice), Is.EqualTo(0)); // no room left, rice is not over-hauled
        }

        // ── unbounded destination: no clamp, no tracking ────────────────────────────────────────────────

        [Test]
        public void Unbounded_AlwaysReportsMaxValueAndIgnoresConsume()
        {
            var b = new StorageGroupBudget(int.MaxValue);
            Assert.That(b.Unbounded, Is.True);
            b.PriceDef(Meat, partialSpace: 5, perCellCapacity: 75); // ignored
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(int.MaxValue));
            b.Consume(Meat, 100);
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void HugeGroup_ReportsMaxValueWithoutOverflowingToNegative()
        {
            var b = new StorageGroupBudget(emptyCells: 100_000_000);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 100); // 1e10 units total
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(int.MaxValue));
        }

        // ── fail-open + defensive clamps ───────────────────────────────────────────────────────────────

        [Test]
        public void UnpricedDef_FailsOpenToMaxValueAndConsumeIsNoOp()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(int.MaxValue)); // never priced
            b.Consume(Meat, 10);                                        // no-op, empty cells untouched
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(150));
        }

        [Test]
        public void Consume_NeverDrivesEmptyCellsNegative()
        {
            var b = new StorageGroupBudget(emptyCells: 1);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            b.Consume(Meat, 200);                       // asks for more than the one cell can hold
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(0)); // clamped: exactly the one cell spent, no negatives
        }

        [Test]
        public void Consume_NonPositiveCountIsNoOp()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            b.Consume(Meat, 0);
            b.Consume(Meat, -5);
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(150));
        }

        [Test]
        public void PriceDef_IsIdempotent_FirstScanWins()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: 0, perCellCapacity: 75);
            b.PriceDef(Meat, partialSpace: 999, perCellCapacity: 999); // ignored
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(150));
            Assert.That(b.IsPriced(Meat), Is.True);
            Assert.That(b.IsPriced(Rice), Is.False);
        }

        [Test]
        public void PriceDef_ClampsPerCellToAtLeastOneAndPartialToNonNegative()
        {
            var b = new StorageGroupBudget(emptyCells: 2);
            b.PriceDef(Meat, partialSpace: -5, perCellCapacity: 0);
            // partial clamped to 0, perCell clamped to 1 → two empty cells at 1 unit each.
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(2));
        }

        [Test]
        public void PartialOnly_NoEmptyCells_CannotExceedPartialRoom()
        {
            var b = new StorageGroupBudget(emptyCells: 0);
            b.PriceDef(Meat, partialSpace: 30, perCellCapacity: 75);
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(30));
            b.Consume(Meat, 25);
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(5));
            b.Consume(Meat, 100);                        // over-ask with no empty cells
            Assert.That(b.AvailableFor(Meat), Is.EqualTo(0));
        }
    }
}
