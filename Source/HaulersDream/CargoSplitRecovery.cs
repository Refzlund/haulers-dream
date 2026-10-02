using System;
using Verse;

namespace HaulersDream
{
    // The base split receipt precedes ThingWithComps.PostSplitOff. Each caller still
    // owns its transfer policy; this scope never merges or adopts same-def stock.
    internal sealed class CargoSplitRecovery : StorageSplitScope
    {
        private readonly ThingOwner returnOwner, inventory, hands;
        private readonly CompHauledToInventory comp;
        internal bool RecoveredDetached { get; private set; }
        internal bool HasInventoryCargo { get; private set; }

        internal CargoSplitRecovery(Pawn pawn, Thing source, int requested, ThingOwner returnOwner = null)
            : base(source, requested)
        {
            this.returnOwner = returnOwner;
            inventory = pawn.inventory?.innerContainer;
            hands = pawn.carryTracker?.innerContainer;
            comp = pawn.GetComp<CompHauledToInventory>();
        }

        private static bool Detached(Thing item) => item != null && !item.Destroyed && !item.Spawned
            && item.stackCount > 0 && item.holdingOwner == null;
        private static bool Owns(ThingOwner owner, Thing item) => owner != null && item != null
            && !item.Destroyed && !item.Spawned && item.stackCount > 0
            && ReferenceEquals(item.holdingOwner, owner) && owner.Contains(item);

        private static void Preserve(ref Exception primary, Exception secondary, string operation)
        {
            if (primary == null) primary = secondary;
            else primary.Data["HaulersDream.CargoSplitRecovery." + operation] = secondary.ToString();
        }

        private static void TryRetain(ThingOwner owner, Thing item, ref Exception failure, string operation)
        {
            if (owner == null || !Detached(item)) return;
            try { owner.TryAdd(item, canMergeWithExistingStacks: false); }
            catch (Exception error) { Preserve(ref failure, error, operation); }
            // NotifyAdded can throw after native ownership is established. The next
            // route must inspect custody, rather than assume a missing return means failure.
        }

        internal void Finish(ref Exception failure)
        {
            try
            {
                for (int i = 0; i < Fragments.Count; i++)
                {
                    Thing item = Fragments[i];
                    if (Detached(item))
                    {
                        RecoveredDetached = true;
                        TryRetain(returnOwner, item, ref failure, "source." + item.thingIDNumber);
                        if (!ReferenceEquals(inventory, returnOwner))
                            TryRetain(inventory, item, ref failure, "inventory." + item.thingIDNumber);
                        if (hands != null && hands.Count == 0 && !ReferenceEquals(hands, returnOwner)
                            && !ReferenceEquals(hands, inventory))
                            TryRetain(hands, item, ref failure, "hands." + item.thingIDNumber);
                        if (Detached(item)) Preserve(ref failure,
                            new InvalidOperationException("Could not retain the exact cargo split fragment."),
                            "unowned." + item.thingIDNumber);
                    }
                    // Repair only actual receiving inventory, not carrier stock or hands.
                    if (Owns(inventory, item))
                    {
                        HasInventoryCargo = true;
                        if (comp != null && !comp.PeekHashSet().Contains(item))
                            try { comp.RegisterHauledItem(item); }
                            catch (Exception error) { Preserve(ref failure, error, "tag." + item.thingIDNumber); }
                    }
                }
            }
            finally
            {
                try { Dispose(); }
                catch (Exception error) { Preserve(ref failure, error, "scope"); }
            }
        }
    }
}
