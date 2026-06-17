using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// The pure decision heart of <c>CompHauledToInventory.GetHashSet</c>'s self-heal — the single most
    /// load-bearing path in the mod, and (until this extraction) entirely Verse-bound and untested.
    ///
    /// A single scoop can land across MULTIPLE inventory stacks (a yield exceeding the stack limit, or not
    /// merging into the first stack), but the registration only ever tags ONE of them; stacks also merge and
    /// split over time, and a MERGE can DESTROY a def's last tag (the absorbed Thing is <c>Destroy()</c>ed).
    /// The self-heal treats EVERY current inventory stack whose def we've already scooped (the live tags) — OR
    /// whose last tag a merge just destroyed (the "carry-over" defs) — as HD-owned surplus and (re)tags it.
    /// Without this, the untagged stacks are invisible to both inventory-sharing and the unload pass: stranded
    /// in the pawn's pockets forever — a silent black hole.
    ///
    /// Two correctness boundaries it must NEVER cross, both encoded here so they are unit-testable:
    /// <list type="bullet">
    /// <item>It is bounded to ALREADY-SCOOPED defs (the union), so a pawn's non-scooped kit is never claimed.</item>
    /// <item>It must never tag a genuine Simple Sidearms remembered sidearm (a separate Thing of a scooped
    ///   weapon's def): SS would re-fetch it, producing the "unloads its own sidearm" bug. The
    ///   <see cref="Stack.IsRememberedSidearm"/> flag (resolved Verse-side per candidate) excludes it.</item>
    /// </list>
    ///
    /// This class holds NO Verse types: defs are opaque <c>object</c> tokens (the live <c>ThingDef</c>
    /// references at runtime; reference identity is all the decision needs), stacks are a flat value-type
    /// list, and the output is a list of indices. The Verse mapper (<c>GetHashSet</c>) supplies the inputs
    /// and applies the Thing-level side effects (set-add, tag-age stamp, CE hold-notify) for the returned
    /// indices, plus the mechanical tag-age bookkeeping (which stays Verse-side — it is a derived sync of a
    /// <c>Dictionary&lt;Thing,int&gt;</c> against the final set, not a decision).
    /// </summary>
    public static class TagHealPolicy
    {
        /// <summary>
        /// One current inventory stack, reduced to exactly the three facts the tag decision needs.
        /// </summary>
        public readonly struct Stack
        {
            /// <summary>The stack's def, as an opaque token (the live <c>ThingDef</c> at runtime). Null = an
            /// empty/invalid slot, which is never tagged.</summary>
            public readonly object Def;

            /// <summary>True if this exact stack is ALREADY in the tracked set — it must not be re-tagged
            /// (no duplicate stamp / CE re-notify; the runtime's set-add would no-op anyway).</summary>
            public readonly bool AlreadyTagged;

            /// <summary>True if this stack is a GENUINE Simple Sidearms remembered sidearm (precise (def,stuff)
            /// match) — it must be excluded even when its def is in the scooped union. Resolved Verse-side and
            /// only needs to be accurate for union-member, not-already-tagged stacks (the only ones it can
            /// affect); the runtime computes it lazily for exactly those to preserve its reflection
            /// short-circuit.</summary>
            public readonly bool IsRememberedSidearm;

            public Stack(object def, bool alreadyTagged, bool isRememberedSidearm)
            {
                Def = def;
                AlreadyTagged = alreadyTagged;
                IsRememberedSidearm = isRememberedSidearm;
            }
        }

        /// <summary>
        /// Whether the self-heal must run this call (vs. returning the already-healed set untouched). The heal
        /// is idempotent WITHIN one tick — the read-only share/probe callers that drive it cannot change the
        /// inventory mid-tick — so once healed at tick <paramref name="now"/>, repeat same-tick calls
        /// short-circuit. A path that MUTATES the set resets <paramref name="lastHealTick"/> to -1, forcing a
        /// re-heal so a same-tick scoop is always observed. A tickless call (<paramref name="now"/> == -1, e.g.
        /// a unit-test/edit-mode call with no TickManager) ALWAYS re-heals and never updates the stamp, so it
        /// can never poison the cache into matching a future "now == -1".
        /// </summary>
        public static bool ShouldReheal(int lastHealTick, int now)
            => now == -1 || lastHealTick != now;

        /// <summary>
        /// Build the set of defs the self-heal will claim: the defs of the still-live tags UNION the carry-over
        /// defs (a def whose last tag a merge just destroyed, so the same-def stack that ABSORBED it can be
        /// re-tagged). Nulls are dropped. Clears <paramref name="outUnion"/> first. The two inputs accept any
        /// reference-typed def token via covariance (the runtime passes <c>HashSet&lt;ThingDef&gt;</c>).
        /// </summary>
        public static void BuildScoopedUnion(
            IReadOnlyCollection<object> liveTaggedDefs,
            IReadOnlyCollection<object> carryOverDefs,
            HashSet<object> outUnion)
        {
            outUnion.Clear();
            if (liveTaggedDefs != null)
                foreach (var d in liveTaggedDefs)
                    if (d != null)
                        outUnion.Add(d);
            if (carryOverDefs != null)
                foreach (var d in carryOverDefs)
                    if (d != null)
                        outUnion.Add(d);
        }

        /// <summary>
        /// The core selection: indices of <paramref name="stacks"/> that must be NEWLY tagged — a stack whose
        /// def is in <paramref name="scoopedUnion"/>, that is not already tagged, and that is not a remembered
        /// sidearm. Appends to <paramref name="outIndices"/> (cleared first). Allocation-free given reused
        /// output. An empty union (nothing scooped and no carry-over) selects nothing.
        ///
        /// This is where the headline cases live and are testable: a scoop spanning several same-def stacks
        /// (all the untagged ones are selected), a merge that destroyed a def's last tag (its def is in the
        /// union via carry-over, so the absorbing stack is selected — the silent-black-hole fix), a remembered
        /// sidearm of a scooped weapon's def (excluded), and a benign def overlap such as harvested-vs-personal
        /// medicine (both selected — harmless: the surplus is merely unloaded to storage, where it stays usable).
        /// </summary>
        public static void SelectStacksToTag(
            HashSet<object> scoopedUnion,
            IReadOnlyList<Stack> stacks,
            List<int> outIndices)
        {
            outIndices.Clear();
            if (scoopedUnion == null || scoopedUnion.Count == 0 || stacks == null)
                return;
            for (int i = 0; i < stacks.Count; i++)
            {
                var s = stacks[i];
                if (s.Def != null && !s.AlreadyTagged && !s.IsRememberedSidearm && scoopedUnion.Contains(s.Def))
                    outIndices.Add(i);
            }
        }
    }
}
