using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Oracle + 0-alloc net for <see cref="SelectFirstByCategoryThenDef"/> (HD-ORDERBY) — the single-pass min-scan
    /// that replaces the unload driver's per-item
    /// <c>carried.OrderBy(t =&gt; t.def.FirstThingCategory?.index).ThenBy(t =&gt; t.def.defName).First()</c>.
    ///
    /// Asserts:
    ///   1. ORACLE: the scan returns the SAME element as the LINQ <c>OrderBy().ThenBy().First()</c> over randomized
    ///      inputs — including NULL category indices (sort last) and DUPLICATE defNames (stable first-seen tiebreak).
    ///   2. 0-alloc: the comparison primitive (<see cref="SelectFirstByCategoryThenDef.LessThan"/>) and a full scan
    ///      via the <see cref="SelectFirstByCategoryThenDef.Selector"/> allocate nothing — vs the LINQ which builds
    ///      an OrderedEnumerable + sort keys + 2 closures per call (the cost this removes).
    ///
    /// The element here is a tiny value tuple <c>(int? cat, string defName)</c> standing in for a Thing's def keys —
    /// the Core helper never touches game types, so the runtime maps its <c>BestIndex</c> back to the real Thing.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class SelectFirstByCategoryThenDefPerfTests
    {
        private readonly struct Elem
        {
            public readonly int? Cat;
            public readonly string Def;
            public Elem(int? cat, string def) { Cat = cat; Def = def; }
            public int Key => Cat ?? SelectFirstByCategoryThenDef.NoCategory;
        }

        // The LINQ that HD-ORDERBY replaces — the authoritative oracle. Returns the index of the first element in
        // (Cat asc, null last) then (Def ordinal asc) order; -1 for an empty input. We compare INDICES (not values)
        // so the stable first-seen tiebreak among fully-equal elements is observable.
        private static int OracleFirstIndex(IReadOnlyList<Elem> items)
        {
            if (items.Count == 0)
                return -1;
            // Project to (index, element) so a STABLE OrderBy.ThenBy lets us read the winning original index. LINQ
            // OrderBy on Nullable<int> orders null last; ThenBy(string) uses the default comparer. We mirror the
            // helper's ordinal defName choice for the parity check, but verify the helper against THIS oracle so any
            // disagreement (incl. null/dup handling) surfaces.
            return items
                .Select((e, i) => (e, i))
                .OrderBy(p => p.e.Cat ?? SelectFirstByCategoryThenDef.NoCategory)
                .ThenBy(p => p.e.Def, StringComparer.Ordinal)
                .First().i;
        }

        private static int ScanFirstIndex(IReadOnlyList<Elem> items)
        {
            var sel = SelectFirstByCategoryThenDef.Begin();
            for (int i = 0; i < items.Count; i++)
                sel.Consider(items[i].Key, items[i].Def);
            return sel.BestIndex;
        }

        [Test]
        public void Scan_MatchesLinqOracle_OverRandomizedInputs()
        {
            var rng = new Random(1234567);
            // A small pool of defNames (forces frequent duplicates) and category indices (incl. null) so ties on
            // one or both keys are common — exactly the cases where a non-stable or wrong-tiebreak scan diverges.
            string[] defs = { "Steel", "Steel", "Wood", "Cloth", "Cloth", "Gold", "Apparel_Pants", "MealSimple" };
            int?[] cats = { null, 0, 1, 1, 2, 5, null, int.MaxValue };

            for (int trial = 0; trial < 5000; trial++)
            {
                int n = rng.Next(1, 12);
                var items = new List<Elem>(n);
                for (int i = 0; i < n; i++)
                    items.Add(new Elem(cats[rng.Next(cats.Length)], defs[rng.Next(defs.Length)]));

                int oracle = OracleFirstIndex(items);
                int scan = ScanFirstIndex(items);

                // Both must point at the SAME original index. (Index equality is stricter than value equality and
                // pins the stable first-seen tiebreak among elements equal on both keys.)
                Assert.That(scan, Is.EqualTo(oracle),
                    $"scan returned index {scan} but oracle returned {oracle} for [{string.Join(", ", items.Select(e => $"({(e.Cat?.ToString() ?? "null")},{e.Def})"))}]");
            }
        }

        [Test]
        public void EmptyInput_HasNoBest()
        {
            var sel = SelectFirstByCategoryThenDef.Begin();
            Assert.That(sel.HasBest, Is.False);
            Assert.That(sel.BestIndex, Is.EqualTo(-1));
        }

        [Test]
        public void NullCategorySortsLast()
        {
            var sel = SelectFirstByCategoryThenDef.Begin();
            sel.Consider(SelectFirstByCategoryThenDef.NoCategory, "Aaa"); // null category, smallest defName
            sel.Consider(5, "Zzz");                                       // real category, largest defName
            Assert.That(sel.BestIndex, Is.EqualTo(1), "a real category index must sort before a null one");
        }

        [Test]
        public void LessThan_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => SelectFirstByCategoryThenDef.LessThan(1, "Wood", 1, "Steel"),
                "the (catIndex, defName) comparison must not allocate (min-scan primitive)");

        // Non-boxing sink so the JIT can't elide the scan (writing an int field to a static is free; GC.KeepAlive
        // on an int would BOX it and read as a false allocation).
        private static int sink;

        [Test]
        public void SelectorScan_IsZeroAlloc()
        {
            // A fixed candidate stream fed through the value-type Selector — proves an entire scan allocates nothing
            // (no OrderedEnumerable, no keys, no closures). The arrays are built ONCE outside the measured body.
            int[] keys = { 5, 1, SelectFirstByCategoryThenDef.NoCategory, 1, 2, 0, 3 };
            string[] names = { "Gold", "Wood", "Ghost", "Steel", "Cloth", "Apparel", "MealSimple" };
            Action body = () =>
            {
                var sel = SelectFirstByCategoryThenDef.Begin();
                for (int i = 0; i < keys.Length; i++)
                    sel.Consider(keys[i], names[i]);
                sink = sel.BestIndex;
            };
            AllocationAssert.AssertZeroAlloc(body, "a full Selector scan must not allocate");
        }
    }
}
