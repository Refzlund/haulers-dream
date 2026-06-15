using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// <see cref="IManagedLoadable"/> over a transporter/shuttle GROUP (a <see cref="CompTransporter"/> and every
    /// other transporter sharing its <c>groupID</c>). The bulk-load ledger + planner + deposit loop see one uniform
    /// manifest/mass view; this adapter folds the group together. Shuttles are <c>CompTransporter + CompShuttle</c> —
    /// the same code path (the autoload patch handles the shuttle-specific regen).
    ///
    /// Created via <see cref="TryCreate"/> (null/!Spawned/Map==null guarded; the Map is cached because the parent
    /// may despawn mid-trip). CRITICAL: <c>CompTransporter.TransportersInGroup</c> returns the SHARED static
    /// <c>tmpTransportersInGroup</c> buffer (reused across every call), so this copies the group OUT once at creation
    /// and never holds the live buffer across toils.
    /// </summary>
    public class LoadTransportersAdapter : IManagedLoadable
    {
        private readonly CompTransporter primary;
        private readonly Map map;
        private readonly List<CompTransporter> group; // copied OUT of the shared static buffer

        private LoadTransportersAdapter(CompTransporter primary, Map map, List<CompTransporter> group)
        {
            this.primary = primary;
            this.map = map;
            this.group = group;
        }

        /// <summary>Build an adapter for a transporter group, or null when the comp is unusable (no parent / not
        /// spawned / no map). Copies the group OUT of the shared static buffer.</summary>
        public static LoadTransportersAdapter TryCreate(CompTransporter transporter)
        {
            if (transporter?.parent == null || !transporter.parent.Spawned)
                return null;
            var map = transporter.Map;
            if (map == null)
                return null;
            // Copy the group out of the SHARED static tmpTransportersInGroup buffer immediately.
            var live = transporter.TransportersInGroup(map);
            var copy = new List<CompTransporter>(live != null ? live.Count : 1);
            if (live != null)
                for (int i = 0; i < live.Count; i++)
                    if (live[i] != null)
                        copy.Add(live[i]);
            if (copy.Count == 0)
                copy.Add(transporter);
            return new LoadTransportersAdapter(transporter, map, copy);
        }

        public int GetUniqueLoadID() => primary.groupID;

        public Map GetMap() => map;

        public Thing GetParentThing() => primary.parent;

        public CompTransporter Primary => primary;

        public IReadOnlyList<CompTransporter> Group => group;

        public List<TransferableOneWay> GetTransferables()
        {
            // Union of every group member's leftToLoad with CountToTransfer > 0 (copied OUT — these are live refs
            // the caller reads, but the LIST is ours, never the shared buffer).
            var result = new List<TransferableOneWay>();
            for (int i = 0; i < group.Count; i++)
            {
                var ltl = group[i]?.leftToLoad;
                if (ltl == null)
                    continue;
                for (int j = 0; j < ltl.Count; j++)
                {
                    var tr = ltl[j];
                    if (tr != null && tr.HasAnyThing && tr.CountToTransfer > 0)
                        result.Add(tr);
                }
            }
            return result;
        }

        public bool AnythingToLoad()
        {
            // Allocation-free emptiness pre-gate: short-circuit on the first positive entry across the group's
            // leftToLoad lists (no List<TransferableOneWay> materialised, unlike GetTransferables).
            for (int i = 0; i < group.Count; i++)
            {
                var ltl = group[i]?.leftToLoad;
                if (ltl == null)
                    continue;
                for (int j = 0; j < ltl.Count; j++)
                {
                    var tr = ltl[j];
                    if (tr != null && tr.HasAnyThing && tr.CountToTransfer > 0)
                        return true;
                }
            }
            return false;
        }

        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            // Fast-path the primary when it still wants this def; else scan the group for a member whose manifest
            // still needs the def (so the deposit's auto-decrement lands on a transporter that wanted it).
            if (depositTarget?.def == null)
                return primary.innerContainer;
            if (WantsDef(primary, depositTarget.def))
                return primary.innerContainer;
            for (int i = 0; i < group.Count; i++)
            {
                var t = group[i];
                if (t != null && t != primary && WantsDef(t, depositTarget.def))
                    return t.innerContainer;
            }
            // Nothing in the group still wants it — default to the primary (the precise decrement intercept will
            // find the best match across the group's leftToLoad anyway).
            return primary.innerContainer;
        }

        /// <summary>The group member whose <c>leftToLoad</c> still wants <paramref name="def"/> (for the deposit
        /// loop's "re-find the active member when the current one fills"), or null when none does.</summary>
        public CompTransporter ActiveMemberFor(ThingDef def)
        {
            if (def == null)
                return null;
            if (WantsDef(primary, def))
                return primary;
            for (int i = 0; i < group.Count; i++)
                if (group[i] != null && group[i] != primary && WantsDef(group[i], def))
                    return group[i];
            return null;
        }

        /// <summary>The group member whose <c>leftToLoad</c> has an entry vanilla's
        /// <see cref="TransferableUtility.TransferableMatchingDesperate"/> (in <c>PodsOrCaravanPacking</c> mode) would
        /// decrement for <paramref name="item"/> — the SAME 3-tier ladder (identity → <c>TransferAsOne</c> variant →
        /// def-only fallback) the auto-fired <c>SubtractFromToLoadList</c> uses — or null when none does. Using the
        /// vanilla matcher (not a strict Tier-2 <c>TransferAsOne</c>) keeps routing in lock-step with the deposit clamp
        /// (<see cref="MemberRemainingFor"/>, also 3-tier) and the decrement intercept: an off-quality fungible item
        /// HD's def-keyed scoop delivered routes to (and decrements) the def entry via Tier-3 exactly as vanilla would,
        /// instead of being refused. A mixed-variant manifest still routes to the pod that explicitly holds THIS
        /// variant first (Tier-2 precedes Tier-3 inside the matcher). The clamp is computed against the SAME match, so
        /// they line up exactly.</summary>
        public CompTransporter ActiveMemberFor(Thing item)
        {
            if (item?.def == null)
                return null;
            if (WantsThing(primary, item))
                return primary;
            for (int i = 0; i < group.Count; i++)
                if (group[i] != null && group[i] != primary && WantsThing(group[i], item))
                    return group[i];
            return null;
        }

        /// <summary>How many units MATCHING <paramref name="item"/>'s exact transferable identity (def + stuff +
        /// quality, via the SAME vanilla matcher the auto-fired <c>SubtractFromToLoadList</c> uses to find the entry it
        /// decrements — <see cref="TransferableUtility.TransferableMatchingDesperate"/> in
        /// <c>PodsOrCaravanPacking</c> mode) a SINGLE member still wants. The deposit MUST clamp to this — depositing
        /// more than the matching entry holds would over-load that pod AND, with the precise intercept, decrement only
        /// what that one entry held (so the rest of the deposit silently goes un-accounted). Mirrors the vehicle path's
        /// <c>VehicleFrameworkCompat.RemainingDemandForThing</c> so transporter/portal/vehicle clamp identically.
        /// Returns 0 when there is no matching entry (another pawn filled this exact variant).</summary>
        public static int MemberRemainingFor(CompTransporter member, Thing item)
        {
            var ltl = member?.leftToLoad;
            if (ltl == null || item?.def == null)
                return 0;
            var match = TransferableUtility.TransferableMatchingDesperate(item, ltl, TransferAsOneMode.PodsOrCaravanPacking);
            int remaining = match?.CountToTransfer ?? 0;
            return remaining > 0 ? remaining : 0;
        }

        private static bool WantsDef(CompTransporter t, ThingDef def)
        {
            var ltl = t?.leftToLoad;
            if (ltl == null)
                return false;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.CountToTransfer > 0 && tr.ThingDef == def)
                    return true;
            }
            return false;
        }

        /// <summary>True if <paramref name="t"/>'s <c>leftToLoad</c> has the positive-remaining entry vanilla's
        /// <see cref="TransferableUtility.TransferableMatchingDesperate"/> (in <c>PodsOrCaravanPacking</c> mode) would
        /// decrement for <paramref name="item"/> — the SAME 3-tier ladder (identity → <c>TransferAsOne</c> variant →
        /// def-only fallback) <c>SubtractFromToLoadList</c> uses. The def-only Tier-3 fallback is what lets an
        /// off-quality fungible item route to the def entry exactly as vanilla accepts it (the strict-Tier-2
        /// counterpart of <see cref="WantsDef"/> would wrongly refuse it). Mirrors <see cref="MemberRemainingFor"/>'s
        /// matcher so the gate and the clamp agree.</summary>
        private static bool WantsThing(CompTransporter t, Thing item)
        {
            var ltl = t?.leftToLoad;
            if (ltl == null || item == null)
                return false;
            var match = TransferableUtility.TransferableMatchingDesperate(item, ltl, TransferAsOneMode.PodsOrCaravanPacking);
            return match != null && match.CountToTransfer > 0;
        }

        public float GetMassCapacity()
        {
            float sum = 0f;
            for (int i = 0; i < group.Count; i++)
                if (group[i] != null)
                    sum += group[i].MassCapacity;
            return sum;
        }

        public float GetMassUsage()
        {
            float sum = 0f;
            for (int i = 0; i < group.Count; i++)
                if (group[i] != null)
                    sum += group[i].MassUsage;
            return sum;
        }

        public bool HasMassCap => true;

        public bool HandlesAbstractDemands => false;

        public LoadableKind Kind => LoadableKind.Transporter;
    }
}
