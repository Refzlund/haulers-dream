using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class QuestPawnDropPolicyTests
    {
        // Def/stuff tokens: plain strings stand in for the runtime ThingDef references (the policy only
        // needs Equals/GetHashCode on the opaque tokens).
        private const string Meal = "MealSimple";
        private const string Medicine = "MedicineHerbal";
        private const string Blocks = "Blocks";
        private const string Granite = "Granite";
        private const string Marble = "Marble";
        private const string Wood = "Wood";

        private static QuestPawnDropPolicy.InventoryStack Stack(
            int id, string def, int count, string stuff = null, bool tagged = false)
            => new QuestPawnDropPolicy.InventoryStack(id, def, stuff, count, tagged);

        private static QuestPawnDropPolicy.SnapshotEntry Entry(string def, int count, string stuff = null)
            => new QuestPawnDropPolicy.SnapshotEntry(def, stuff, count);

        private static List<QuestPawnDropPolicy.SnapshotEntry> Snapshot(
            params QuestPawnDropPolicy.SnapshotEntry[] entries)
            => new List<QuestPawnDropPolicy.SnapshotEntry>(entries);

        private static List<QuestPawnDropPolicy.SnapshotEntry> BuildSnapshot(
            params QuestPawnDropPolicy.InventoryStack[] stacks)
        {
            var outSnapshot = new List<QuestPawnDropPolicy.SnapshotEntry>();
            QuestPawnDropPolicy.BuildSnapshot(stacks, outSnapshot);
            return outSnapshot;
        }

        private static List<QuestPawnDropPolicy.DropOrder> SelectDrops(
            List<QuestPawnDropPolicy.SnapshotEntry> snapshot,
            params QuestPawnDropPolicy.InventoryStack[] stacks)
        {
            var outDrops = new List<QuestPawnDropPolicy.DropOrder>();
            QuestPawnDropPolicy.SelectDrops(snapshot, stacks, outDrops);
            return outDrops;
        }

        // ---------- BuildSnapshot (gain-time aggregation) ----------

        // An empty inventory records an EMPTY snapshot, which is meaningful: everything the pawn later
        // holds is colony property and drops at revert.
        [Test]
        public void BuildSnapshot_EmptyInventory_EmptySnapshot()
        {
            Assert.That(BuildSnapshot(), Is.Empty);
        }

        // Multiple stacks of one kind fold into a single summed line: 5 + 3 meals arrive as "8 meals",
        // so later stack merges/splits can never change what the pawn is entitled to keep.
        [Test]
        public void BuildSnapshot_AggregatesSameKindAcrossStacks()
        {
            var snapshot = BuildSnapshot(Stack(1, Meal, 5), Stack(2, Meal, 3));

            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0].Def, Is.EqualTo(Meal));
            Assert.That(snapshot[0].Count, Is.EqualTo(8));
        }

        // Stuff is part of the identity: granite blocks and marble blocks are separate lines.
        [Test]
        public void BuildSnapshot_DistinguishesStuff()
        {
            var snapshot = BuildSnapshot(
                Stack(1, Blocks, 10, stuff: Granite),
                Stack(2, Blocks, 5, stuff: Marble));

            Assert.That(snapshot, Has.Count.EqualTo(2));
            Assert.That(snapshot[0].Stuff, Is.EqualTo(Granite));
            Assert.That(snapshot[0].Count, Is.EqualTo(10));
            Assert.That(snapshot[1].Stuff, Is.EqualTo(Marble));
            Assert.That(snapshot[1].Count, Is.EqualTo(5));
        }

        // An HD-tagged stack at gain time (a re-joining guest whose previous revert drop failed) is colony
        // cargo, never arrival kit: counting it would grant allowance that lets same-kind items picked up
        // later walk away with the pawn.
        [Test]
        public void BuildSnapshot_SkipsTaggedStacks()
        {
            var snapshot = BuildSnapshot(
                Stack(1, Meal, 5, tagged: true),
                Stack(2, Meal, 3));

            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0].Count, Is.EqualTo(3));
        }

        [Test]
        public void BuildSnapshot_SkipsNonPositiveCountsAndNullDefs()
        {
            var snapshot = BuildSnapshot(
                Stack(1, Meal, 0),
                Stack(2, null, 5),
                Stack(3, Medicine, -2),
                Stack(4, Wood, 4));

            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0].Def, Is.EqualTo(Wood));
        }

        // Output order is first-seen input order (the caller feeds stacks sorted by thingIDNumber), so the
        // persisted snapshot is byte-identical across MP clients.
        [Test]
        public void BuildSnapshot_PreservesFirstSeenOrder()
        {
            var snapshot = BuildSnapshot(
                Stack(1, Meal, 2),
                Stack(2, Medicine, 1),
                Stack(3, Meal, 2));

            Assert.That(snapshot, Has.Count.EqualTo(2));
            Assert.That(snapshot[0].Def, Is.EqualTo(Meal));
            Assert.That(snapshot[0].Count, Is.EqualTo(4));
            Assert.That(snapshot[1].Def, Is.EqualTo(Medicine));
        }

        // The output list is cleared first, so a reused scratch list can't leak stale lines.
        [Test]
        public void BuildSnapshot_ClearsOutputFirst()
        {
            var outSnapshot = new List<QuestPawnDropPolicy.SnapshotEntry> { Entry(Wood, 99) };
            QuestPawnDropPolicy.BuildSnapshot(
                new[] { Stack(1, Meal, 2) }, outSnapshot);

            Assert.That(outSnapshot, Has.Count.EqualTo(1));
            Assert.That(outSnapshot[0].Def, Is.EqualTo(Meal));
        }

        // ---------- SelectDrops (loss-time diff) ----------

        // "Arrived with nothing" (the empty snapshot): every untagged stack drops in full.
        [Test]
        public void SelectDrops_EmptySnapshot_DropsEverything()
        {
            var drops = SelectDrops(Snapshot(),
                Stack(1, Meal, 5),
                Stack(2, Medicine, 2));

            Assert.That(drops, Has.Count.EqualTo(2));
            Assert.That(drops[0].Id, Is.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(5));
            Assert.That(drops[1].Id, Is.EqualTo(2));
            Assert.That(drops[1].Count, Is.EqualTo(2));
        }

        // The pawn holds exactly what it arrived with: nothing drops.
        [Test]
        public void SelectDrops_ExactMatch_NoDrops()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5), Entry(Medicine, 2)),
                Stack(1, Meal, 5),
                Stack(2, Medicine, 2));

            Assert.That(drops, Is.Empty);
        }

        // Only the units ABOVE the arrival count drop (a partial-stack drop order).
        [Test]
        public void SelectDrops_ExcessAboveAllowance_DropsOnlyExcess()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5)),
                Stack(1, Meal, 8));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Id, Is.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(3));
        }

        // A kind the pawn did not arrive with has zero allowance: the whole stack drops.
        [Test]
        public void SelectDrops_KindAbsentFromSnapshot_FullDrop()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5)),
                Stack(1, Wood, 12));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(12));
        }

        // Allowance is consumed first-stack-first across split stacks: with 10 allowed and stacks [7, 7]
        // the first stack is kept whole and only the second sheds the excess 4.
        [Test]
        public void SelectDrops_AllowanceSpansStacksInOrder()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 10)),
                Stack(1, Meal, 7),
                Stack(2, Meal, 7));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Id, Is.EqualTo(2));
            Assert.That(drops[0].Count, Is.EqualTo(4));
        }

        // HD-tagged cargo ALWAYS drops in full, even when the snapshot would cover its count: HD put it
        // there, so it is colony property regardless of what the pawn originally carried (the arrival
        // originals may have been eaten while the tagged stock remained).
        [Test]
        public void SelectDrops_TaggedStack_DropsInFull_EvenWhenSnapshotCoversIt()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5)),
                Stack(1, Meal, 5, tagged: true));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Id, Is.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(5));
        }

        // A tagged stack never consumes allowance: the untagged sibling stack still gets the full arrival
        // allowance and stays with the pawn.
        [Test]
        public void SelectDrops_TaggedDoesNotConsumeAllowance()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5)),
                Stack(1, Meal, 5, tagged: true),
                Stack(2, Meal, 5));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Id, Is.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(5));
        }

        // Granite arrival kit grants no marble allowance.
        [Test]
        public void SelectDrops_StuffMattersForAllowance()
        {
            var drops = SelectDrops(Snapshot(Entry(Blocks, 10, stuff: Granite)),
                Stack(1, Blocks, 10, stuff: Marble));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(10));
        }

        // Stuffed and unstuffed are distinct identities too (null stuff is its own key).
        [Test]
        public void SelectDrops_NullStuffIsDistinctFromStuffed()
        {
            var drops = SelectDrops(Snapshot(Entry(Blocks, 10)),
                Stack(1, Blocks, 4, stuff: Granite),
                Stack(2, Blocks, 4));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Id, Is.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(4));
        }

        // Arrival kit the pawn no longer holds (eaten/used) simply leaves allowance unused: no orders, and
        // in particular no attempt to "compensate" from other kinds.
        [Test]
        public void SelectDrops_SnapshotKindNoLongerHeld_NoOrders()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5)));

            Assert.That(drops, Is.Empty);
        }

        // Orders come out in stack input order (the runtime feeds thingIDNumber order), so every MP client
        // executes the same drops in the same sequence.
        [Test]
        public void SelectDrops_OrdersFollowStackInputOrder()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 5), Entry(Medicine, 2)),
                Stack(3, Meal, 7),
                Stack(5, Medicine, 2),
                Stack(7, Wood, 4),
                Stack(9, Meal, 2, tagged: true));

            Assert.That(drops, Has.Count.EqualTo(3));
            Assert.That(drops[0].Id, Is.EqualTo(3));
            Assert.That(drops[0].Count, Is.EqualTo(2));
            Assert.That(drops[1].Id, Is.EqualTo(7));
            Assert.That(drops[1].Count, Is.EqualTo(4));
            Assert.That(drops[2].Id, Is.EqualTo(9));
            Assert.That(drops[2].Count, Is.EqualTo(2));
        }

        // Defensive tolerance: non-positive stack counts and null defs are ignored outright.
        [Test]
        public void SelectDrops_NonPositiveOrNullDefStacksIgnored()
        {
            var drops = SelectDrops(Snapshot(),
                Stack(1, Meal, 0),
                Stack(2, null, 5),
                Stack(3, Medicine, -1));

            Assert.That(drops, Is.Empty);
        }

        // Defensive tolerance: a non-positive snapshot line grants no allowance (never a negative one).
        [Test]
        public void SelectDrops_NonPositiveSnapshotCountGrantsNoAllowance()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 0), Entry(Medicine, -3)),
                Stack(1, Meal, 3),
                Stack(2, Medicine, 2));

            Assert.That(drops, Has.Count.EqualTo(2));
            Assert.That(drops[0].Count, Is.EqualTo(3));
            Assert.That(drops[1].Count, Is.EqualTo(2));
        }

        // Defensive tolerance: duplicate snapshot lines for one kind sum (a hand-edited save must not
        // silently lose allowance).
        [Test]
        public void SelectDrops_DuplicateSnapshotLinesSum()
        {
            var drops = SelectDrops(Snapshot(Entry(Meal, 3), Entry(Meal, 4)),
                Stack(1, Meal, 10));

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].Count, Is.EqualTo(3));
        }

        // The output list is cleared first, so a reused scratch list can't leak stale orders.
        [Test]
        public void SelectDrops_ClearsOutputFirst()
        {
            var outDrops = new List<QuestPawnDropPolicy.DropOrder> { new QuestPawnDropPolicy.DropOrder(99, 99) };
            QuestPawnDropPolicy.SelectDrops(
                Snapshot(Entry(Meal, 5)),
                new[] { Stack(1, Meal, 5) },
                outDrops);

            Assert.That(outDrops, Is.Empty);
        }

        // End-to-end oracle: the gain snapshot feeds the loss diff. A guest arrives with 5 meals and 2
        // herbal medicine; while under player control it eats 2 meals, grabs 3 colony meals (merging into
        // one 6-meal stack), and HD sweeps 4 wood into it (tagged). At revert the count diff keeps up to
        // the ARRIVAL COUNT of 5 meals (by design it cannot know which individual meals were eaten, that
        // is the def+count shape), so the 6-meal stack sheds 1, the tagged wood drops in full, and the
        // untouched medicine stays.
        [Test]
        public void RoundTrip_GainSnapshotThenRevertDiff()
        {
            var snapshot = BuildSnapshot(
                Stack(1, Meal, 5),
                Stack(2, Medicine, 2));

            var drops = SelectDrops(snapshot,
                Stack(10, Meal, 6),
                Stack(11, Medicine, 2),
                Stack(12, Wood, 4, tagged: true));

            Assert.That(drops, Has.Count.EqualTo(2));
            Assert.That(drops[0].Id, Is.EqualTo(10));
            Assert.That(drops[0].Count, Is.EqualTo(1));
            Assert.That(drops[1].Id, Is.EqualTo(12));
            Assert.That(drops[1].Count, Is.EqualTo(4));
        }
    }
}
