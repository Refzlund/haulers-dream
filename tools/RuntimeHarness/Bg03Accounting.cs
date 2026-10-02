using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Forty fixture units only. This is an observer's identity ledger, never a
    // recipe selector, allocation helper, or writer to a live Thing/ThingOwner.
    internal sealed class Bg03Accounting
    {
        private sealed class Unit
        {
            internal int id;
            internal ThingDef def;
            internal bool initiallyHeld, everHeld, consumed;
        }
        private readonly Dictionary<int, List<int>> byThing = new Dictionary<int, List<int>>();
        private readonly List<Unit> units = new List<Unit>();
        private readonly Dictionary<ThingDef, int> acquired = new Dictionary<ThingDef, int>();
        private readonly Dictionary<ThingDef, int> reentered = new Dictionary<ThingDef, int>();
        private readonly Action<string> fault;
        internal List<Bg03Origin> Origins { get; } = new List<Bg03Origin>();
        internal bool Healthy { get; private set; } = true;
        internal Bg03Accounting(Action<string> fault) { this.fault = fault; }
        internal void Seed(Thing thing, bool held)
        {
            if (thing == null || thing.stackCount <= 0 || byThing.ContainsKey(thing.thingIDNumber)) throw new InvalidOperationException("Duplicate/invalid initial unit source.");
            var ids = new List<int>();
            for (int i = 0; i < thing.stackCount; i++)
            {
                int id = units.Count; units.Add(new Unit { id = id, def = thing.def, initiallyHeld = held, everHeld = held }); ids.Add(id);
            }
            byThing.Add(thing.thingIDNumber, ids);
            Origins.Add(new Bg03Origin { thingId = thing.thingIDNumber, def = thing.def.defName, count = thing.stackCount, initiallyHeld = held, units = ids.ToArray() });
        }
        private void Fail(string reason) { Healthy = false; fault(reason); }
        internal int[] Units(int id, int count)
        {
            if (!byThing.TryGetValue(id, out var ids) || count < 0 || count > ids.Count)
            { Fail("Unknown/count-mismatched Thing in unit observation: " + id + "/" + count); return Array.Empty<int>(); }
            return ids.Take(count).ToArray();
        }
        internal int[] SelectionUnits(int id, int count) => count >= 0 && byThing.TryGetValue(id, out var ids) && count <= ids.Count
            ? ids.Take(count).ToArray() : Array.Empty<int>();
        internal int[] Split(Bg03ThingState before, Thing source, Thing result)
        {
            if (result == null || source == null || before == null || !byThing.TryGetValue(before.id, out var ids) || ids.Count != before.count)
            { Fail("Unreconciled SplitOff input/result."); return Array.Empty<int>(); }
            if (ReferenceEquals(source, result))
            {
                if (result.stackCount != before.count || result.Destroyed) Fail("Whole-stack SplitOff changed count or destroyed source.");
                return ids.ToArray();
            }
            int count = result.stackCount;
            if (result.Destroyed || result.def != source.def || source.stackCount + count != before.count || count <= 0 || count > ids.Count
                || (byThing.TryGetValue(result.thingIDNumber, out var existing) && existing.Count != 0))
            { Fail("SplitOff ancestry/count equation failed."); return Array.Empty<int>(); }
            var moved = ids.Skip(ids.Count - count).ToList(); ids.RemoveRange(ids.Count - count, count);
            byThing[result.thingIDNumber] = moved; return moved.ToArray();
        }
        internal int[] Merge(Bg03ThingState targetBefore, Bg03ThingState sourceBefore, Thing target, Thing source)
        {
            if (targetBefore == null || sourceBefore == null || target == null || source == null
                || !byThing.TryGetValue(targetBefore.id, out var targetUnits) || !byThing.TryGetValue(sourceBefore.id, out var sourceUnits)
                || targetUnits.Count != targetBefore.count || sourceUnits.Count != sourceBefore.count || target.def != source.def)
            { Fail("Unreconciled TryAbsorbStack inputs."); return Array.Empty<int>(); }
            int growth = (target.Destroyed ? 0 : target.stackCount) - targetBefore.count;
            int removed = sourceBefore.count - (source.Destroyed ? 0 : source.stackCount);
            if (growth < 0 || growth != removed || growth > sourceUnits.Count)
            { Fail("TryAbsorbStack target growth and source removal disagree."); return Array.Empty<int>(); }
            var moved = sourceUnits.Take(growth).ToList(); sourceUnits.RemoveRange(0, growth); targetUnits.AddRange(moved); return moved.ToArray();
        }
        internal HashSet<int> Held(Bg03State snapshot)
        {
            var result = new HashSet<int>();
            foreach (var thing in snapshot.things.Where(x => x.custody == "inventory" || x.custody == "hands"))
                foreach (int id in Units(thing.id, thing.count)) if (!result.Add(id)) Fail("Same unit simultaneously appears in two held Things.");
            return result;
        }
        internal void Transfer(HashSet<int> before, HashSet<int> after, out int[] newUnits, out int[] repeats, out int[] left)
        {
            var fresh = new List<int>(); var again = new List<int>();
            foreach (int id in after.Except(before).OrderBy(x => x))
            {
                var unit = units[id];
                if (unit.consumed) Fail("Consumed unit reappeared in custody.");
                if (!unit.everHeld) { unit.everHeld = true; fresh.Add(id); acquired[unit.def] = Acquired(unit.def) + 1; }
                else { again.Add(id); reentered[unit.def] = Reentered(unit.def) + 1; }
            }
            newUnits = fresh.ToArray(); repeats = again.ToArray(); left = before.Except(after).OrderBy(x => x).ToArray();
        }
        internal int[] Consume(Bg03ThingState before, Thing actual)
        {
            if (before == null || actual == null || !actual.Destroyed || !byThing.TryGetValue(before.id, out var ids)
                || ids.Count != before.count || ids.Any(i => units[i].consumed))
            { Fail("Consumption cannot be reconciled to unique previously live fixture units."); return Array.Empty<int>(); }
            var used = ids.ToArray(); foreach (int id in used) units[id].consumed = true; ids.Clear(); return used;
        }
        internal bool ValidateSettled(Bg03State snapshot)
        {
            var seen = new HashSet<int>(); bool okay = true;
            foreach (var thing in snapshot.things)
            {
                if (!byThing.TryGetValue(thing.id, out var ids) || ids.Count != thing.count || thing.destroyed)
                { okay = false; continue; }
                foreach (int id in ids)
                    if (!seen.Add(id) || units[id].consumed || units[id].def.defName != thing.def) okay = false;
            }
            if (seen.Count + units.Count(x => x.consumed) != units.Count) okay = false;
            if (!okay) Fail("Settled identity/count conservation failed at " + snapshot.reason + "/tick=" + snapshot.tick);
            return okay;
        }
        internal bool CompleteSelection(Bg03JobState job, IReadOnlyDictionary<ThingDef, int> expected)
        {
            if (job == null || !job.queueLengthsMatch || job.selection.Count == 0) return false;
            var seen = new HashSet<int>();
            foreach (var row in job.selection)
            {
                if (row.thing == null || !row.selected.HasValue || row.selected <= 0 || row.selected > row.thing.count || row.units == null || row.units.Length != row.selected.Value) return false;
                // Selection DTOs are captured before toils run and reviewed after
                // consumption; today's consumed flag cannot rewrite that history.
                foreach (int id in row.units) if (!seen.Add(id) || units[id].def.defName != row.thing.def) return false;
            }
            return seen.Count == units.Count && expected.All(pair => seen.Count(i => units[i].def == pair.Key) == pair.Value);
        }
        internal int Acquired(ThingDef def) => acquired.TryGetValue(def, out int n) ? n : 0;
        internal int Reentered(ThingDef def) => reentered.TryGetValue(def, out int n) ? n : 0;
        internal int Consumed(ThingDef def) => units.Count(x => x.def == def && x.consumed);
        internal int InitialHeld(ThingDef def) => units.Count(x => x.def == def && x.initiallyHeld);
        internal int InitialFloor(ThingDef def) => units.Count(x => x.def == def && !x.initiallyHeld);
    }
}
