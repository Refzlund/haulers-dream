using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Oracle tests for the self-heal decision behind <c>CompHauledToInventory.GetHashSet</c> — the single
    /// most load-bearing path in Hauler's Dream, previously untested because it was Verse-bound. Def tokens
    /// are plain strings (a stand-in for the runtime <c>ThingDef</c> reference; equality semantics match — the
    /// same token instance for the same def). Each test pins one of the historically bug-prone scenarios.
    /// </summary>
    [TestFixture]
    public class TagHealPolicyTests
    {
        // Def tokens.
        const string Berries = "Berries";
        const string Steel = "Steel";
        const string Healroot = "MedicineHerbal"; // harvested AND carried as personal kit (the def overlap)
        const string Revolver = "Revolver";

        static TagHealPolicy.Stack S(object def, bool tagged = false, bool sidearm = false)
            => new TagHealPolicy.Stack(def, tagged, sidearm);

        static HashSet<object> Union(params object[] defs)
        {
            var u = new HashSet<object>();
            foreach (var d in defs)
                u.Add(d);
            return u;
        }

        static List<int> Select(HashSet<object> union, params TagHealPolicy.Stack[] stacks)
        {
            var outIdx = new List<int>();
            TagHealPolicy.SelectStacksToTag(union, stacks, outIdx);
            return outIdx;
        }

        // ----- SelectStacksToTag: the headline scenarios -----------------------------------------------

        [Test]
        public void ScoopSpansMultipleStacks_AllUntaggedSameDefStacksSelected()
        {
            // One scoop landed across 3 berry stacks but only stack 0 got tagged at registration. The heal
            // must select the other two (1, 2) so they aren't stranded untagged. (Berries is in the union
            // because at least one live tag carries that def.)
            var idx = Select(Union(Berries),
                S(Berries, tagged: true),   // 0 already tagged -> skip
                S(Berries),                 // 1 -> select
                S(Berries));                // 2 -> select
            Assert.That(idx, Is.EquivalentTo(new[] { 1, 2 }));
        }

        [Test]
        public void MergeDestroyedLastTag_CarryOverDef_AbsorbingStackSelected()
        {
            // The silent-black-hole fix: a merge destroyed the LAST steel tag, so there are NO live steel
            // tags — but the absorbing untagged steel stack must still be re-tagged. The destroyed def enters
            // the union via carry-over. (Build the union the way the runtime does: no live defs, steel carried over.)
            var union = new HashSet<object>();
            TagHealPolicy.BuildScoopedUnion(
                liveTaggedDefs: new List<object>(),      // every steel tag was just destroyed
                carryOverDefs: new List<object> { Steel },
                outUnion: union);
            var idx = Select(union, S(Steel)); // the stack that absorbed the merge
            Assert.That(idx, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void RememberedSidearm_NeverSelected_EvenWhenDefIsScooped()
        {
            // The pawn scooped a Revolver off the ground (Revolver is in the union), but a SEPARATE Revolver
            // stack is its genuine remembered sidearm. The sidearm must not be tagged (SS would re-fetch it);
            // the loose swept one still tags.
            var idx = Select(Union(Revolver),
                S(Revolver),                    // 0 loose swept -> select
                S(Revolver, sidearm: true));    // 1 remembered sidearm -> exclude
            Assert.That(idx, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void MedicineDefOverlap_BothSelected_HarmlessByDesign()
        {
            // A pawn harvested healroot-derived medicine AND carries personal medicine of the same def. The
            // heal tags BOTH (it is def-keyed) — accepted as harmless: the surplus is unloaded to storage,
            // where it stays usable. This pins the documented behavior so a future change is a conscious one.
            var idx = Select(Union(Healroot),
                S(Healroot),    // harvested surplus
                S(Healroot));   // personal kit (also tagged — by design)
            Assert.That(idx, Is.EquivalentTo(new[] { 0, 1 }));
        }

        // ----- SelectStacksToTag: boundaries --------------------------------------------------------------

        [Test]
        public void NonScoopedDef_NeverSelected()
        {
            // A pawn's personal Steel (never scooped — not in the union) is never claimed, even alongside a
            // scooped def.
            var idx = Select(Union(Berries),
                S(Steel),     // not in union -> skip
                S(Berries));  // in union -> select
            Assert.That(idx, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void EmptyUnion_SelectsNothing()
        {
            Assert.That(Select(Union(), S(Berries), S(Steel)), Is.Empty);
        }

        [Test]
        public void NullDefSlot_Skipped()
        {
            var idx = Select(Union(Berries), S(null), S(Berries));
            Assert.That(idx, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void AllAlreadyTagged_SelectsNothing()
        {
            Assert.That(Select(Union(Berries), S(Berries, tagged: true), S(Berries, tagged: true)), Is.Empty);
        }

        [Test]
        public void NullStacks_NoThrow_EmptyResult()
        {
            var outIdx = new List<int>();
            TagHealPolicy.SelectStacksToTag(Union(Berries), null, outIdx);
            Assert.That(outIdx, Is.Empty);
        }

        [Test]
        public void OutIndices_ClearedEachCall()
        {
            var outIdx = new List<int> { 99 }; // stale content
            TagHealPolicy.SelectStacksToTag(Union(Berries), new[] { S(Berries) }, outIdx);
            Assert.That(outIdx, Is.EqualTo(new[] { 0 })); // 99 was cleared, not appended-to
        }

        // ----- BuildScoopedUnion --------------------------------------------------------------------------

        [Test]
        public void BuildScoopedUnion_IsLiveUnionCarryOver_NullsDropped()
        {
            var union = new HashSet<object>();
            TagHealPolicy.BuildScoopedUnion(
                liveTaggedDefs: new List<object> { Berries, null, Steel },
                carryOverDefs: new List<object> { Healroot, null },
                outUnion: union);
            Assert.That(union, Is.EquivalentTo(new object[] { Berries, Steel, Healroot }));
        }

        [Test]
        public void BuildScoopedUnion_ClearsPriorContent()
        {
            var union = new HashSet<object> { Revolver }; // stale
            TagHealPolicy.BuildScoopedUnion(new List<object> { Berries }, new List<object>(), union);
            Assert.That(union, Is.EquivalentTo(new object[] { Berries })); // Revolver gone
        }

        [Test]
        public void BuildScoopedUnion_NullInputs_NoThrow()
        {
            var union = new HashSet<object> { Steel };
            TagHealPolicy.BuildScoopedUnion(null, null, union);
            Assert.That(union, Is.Empty);
        }

        // ----- ShouldReheal: invalidate-on-mutation + tickless --------------------------------------------

        [Test]
        public void ShouldReheal_FalseOnlyWhenAlreadyHealedThisTick()
        {
            Assert.That(TagHealPolicy.ShouldReheal(lastHealTick: 100, now: 100), Is.False); // healed this tick -> skip
        }

        [Test]
        public void ShouldReheal_TrueAfterMutationInvalidatedStamp()
        {
            // A scoop/deregister resets lastHealTick to -1 -> the next same-tick call must re-heal.
            Assert.That(TagHealPolicy.ShouldReheal(lastHealTick: -1, now: 100), Is.True);
        }

        [Test]
        public void ShouldReheal_TrueWhenTickAdvanced()
        {
            Assert.That(TagHealPolicy.ShouldReheal(lastHealTick: 100, now: 101), Is.True);
        }

        [Test]
        public void ShouldReheal_TicklessAlwaysReheals()
        {
            // now == -1 (no TickManager, e.g. a unit-test / edit-mode call) must always re-heal, even if a
            // prior tickless call had left the stamp at -1 -> never short-circuit on a "-1 == -1" match.
            Assert.That(TagHealPolicy.ShouldReheal(lastHealTick: -1, now: -1), Is.True);
            Assert.That(TagHealPolicy.ShouldReheal(lastHealTick: 100, now: -1), Is.True);
        }
    }
}
