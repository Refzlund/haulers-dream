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

        /// <summary>How many units of <paramref name="def"/> a SINGLE member still wants (Σ CountToTransfer over that
        /// member's <c>leftToLoad</c> entries for the def). The deposit MUST clamp to this — depositing more than one
        /// transporter wants into its container under-counts the manifest (its auto-fired SubtractFromToLoadList
        /// only subtracts what that member's entry held).</summary>
        public static int MemberRemainingFor(CompTransporter member, ThingDef def)
        {
            var ltl = member?.leftToLoad;
            if (ltl == null || def == null)
                return 0;
            int sum = 0;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.ThingDef == def && tr.CountToTransfer > 0)
                    sum += tr.CountToTransfer;
            }
            return sum;
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
    }
}
