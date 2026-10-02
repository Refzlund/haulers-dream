using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Emergency custody only. Its contents are not newly available fuel, and the
    // existing pending record still governs credit/payment/recovery decisions.
    internal sealed class RefuelRecoveryHolder : IThingHolder, IExposable
    {
        private readonly CompHauledToInventory parent;
        private ThingOwner<Thing> contents;

        public RefuelRecoveryHolder(CompHauledToInventory parent)
        {
            this.parent = parent;
            contents = new ThingOwner<Thing>(this);
        }

        public IThingHolder ParentHolder => parent;
        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> children)
            => ThingOwnerUtility.AppendThingHoldersFromThings(children, contents);
        internal bool HasLiveContents
        {
            get
            {
                for (int i = 0; i < contents.Count; i++)
                    if (!contents[i].Destroyed) return true;
                return false;
            }
        }

        internal bool Owns(Thing item) => item != null && !item.Spawned
            && RefuelAttempt.IsMember(contents, item);

        internal float TotalStat(StatDef stat)
        {
            float total = 0f;
            for (int i = 0; i < contents.Count; i++)
            {
                var item = contents[i];
                if (item.Destroyed) continue;
                // A contradictory owner or unavailable quantitative API must not
                // turn live retained material into free carrying capacity.
                if (stat == null || !Owns(item) || item.stackCount <= 0) return float.PositiveInfinity;
                float unit = item.GetStatValue(stat);
                if (float.IsNaN(unit) || float.IsInfinity(unit) || unit < 0f) return float.PositiveInfinity;
                total += item.stackCount * unit;
            }
            return total;
        }

        internal bool Retain(Thing item)
        {
            if (item == null || item.Destroyed || item.Spawned) return false;
            if (Owns(item)) return true;
            if (item.holdingOwner != null) return false;
            try { contents.TryAdd(item, canMergeWithExistingStacks: false); }
            finally { PawnMassCache.Clear(); }
            return Owns(item);
        }

        internal bool ReturnToInventory(Thing item, ThingOwner destination)
        {
            if (item == null || item.Destroyed || !Owns(item) || destination == null) return false;
            Exception primary = null;
            try
            {
                contents.Remove(item);
                destination.TryAdd(item, canMergeWithExistingStacks: false);
            }
            catch (Exception error) { primary = error; }
            finally { PawnMassCache.Clear(); }
            try
            {
                // Before- or after-insertion exceptions must not manufacture ownership.
                // Retain only the same genuinely detached live object; leave real native
                // inventory/foreign custody intact and let the caller report the outcome.
                if (!item.Destroyed && !item.Spawned && item.holdingOwner == null && !Retain(item))
                    throw new InvalidOperationException("Could not retain the exact fuel recovery parcel.");
            }
            catch (Exception recovery)
            {
                if (primary == null) primary = recovery;
                else primary.Data["HaulersDream.RefuelRecovery.Custody"] = recovery.ToString();
            }
            if (primary != null) ExceptionDispatchInfo.Capture(primary).Throw();
            return RefuelAttempt.IsMember(destination, item);
        }

        public void ExposeData()
        {
            // The native owner deep-saves each Thing once; pending CostItem/Items remain
            // references to that same body. The constructor restores its holder chain.
            Scribe_Deep.Look(ref contents, "contents", this);
            if (contents == null) contents = new ThingOwner<Thing>(this);
        }
    }
}
