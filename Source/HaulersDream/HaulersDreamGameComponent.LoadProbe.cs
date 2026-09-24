using System.Collections.Generic;
using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        // Render/menu probe: read current manifest and existing claims without creating, refreshing or
        // pruning a saved ledger entry. Only the returned private dictionary is mutable by the planner.
        internal Dictionary<ThingDef, int> LoadAvailableForMenu(IManagedLoadable loadable, Pawn pawn)
        {
            var needed = new Dictionary<ThingDef, int>();
            var transferables = loadable.GetTransferables();
            for (int i = 0; transferables != null && i < transferables.Count; i++)
            {
                var item = transferables[i];
                if (item == null || !item.HasAnyThing || item.CountToTransfer <= 0 || item.ThingDef == null) continue;
                needed[item.ThingDef] = (needed.TryGetValue(item.ThingDef, out int count) ? count : 0) + item.CountToTransfer;
            }
            BucketFor(loadable).TryGetValue(loadable.GetUniqueLoadID(), out var entry);
            return LoadLedger<ThingDef, Pawn>.AvailableToClaim(needed, entry?.totalClaimed, entry?.pawnClaims, pawn);
        }
    }
}
