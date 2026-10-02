using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    // Only the current explicitly confirmed native drop owns these references. There is no
    // global cargo history or dependency on an explicit-haul order/recovery container.
    internal sealed class InventoryDropRecovery : IDisposable
    {
        [ThreadStatic] internal static InventoryDropRecovery current;
        private readonly InventoryDropRecovery previous;
        private readonly Thing source;
        private readonly ThingOwner owner;
        private readonly int requested, initial;
        private readonly CompHauledToInventory comp;
        private readonly CompHauledToInventory.ExplicitDropTag tag;
        private readonly List<Thing> pieces = new List<Thing>();
        private bool rootSplitEntered;
        internal bool Recovered { get; private set; }

        internal InventoryDropRecovery(Pawn pawn, Thing source, int requested)
        {
            this.source = source;
            this.requested = requested;
            initial = source.stackCount;
            owner = pawn.inventory.innerContainer;
            comp = pawn.GetComp<CompHauledToInventory>();
            tag = comp?.CaptureExplicitDropTag(source) ?? default;
            // GenSpawn removes a whole original from its owner before SpawnSetup; a
            // native removal callback can throw before the original is spawned.
            pieces.Add(source);
            previous = current;
            current = this;
        }

        internal bool Admit(Thing item, int count)
        {
            if (item == null || count <= 0 || count > item.stackCount) return false;
            if (ReferenceEquals(item, source))
            {
                if (requested < initial)
                {
                    if (rootSplitEntered || count != requested) return false;
                    rootSplitEntered = true;
                    return true;
                }
                // A full oversized stack is split by native GenPlace into stack-limit parcels.
                return count == item.def.stackLimit && item.stackCount > count;
            }
            return pieces.Contains(item);
        }

        internal void Record(Thing piece)
        {
            if (piece != null && !ReferenceEquals(piece, source) && !pieces.Contains(piece))
                pieces.Add(piece);
        }

        private bool Owned(Thing item) => item != null && !item.Destroyed && !item.Spawned
            && ReferenceEquals(item.holdingOwner, owner) && owner.Contains(item);

        internal long HeldUnits
        {
            get
            {
                long count = 0;
                foreach (var piece in pieces) if (Owned(piece)) count += piece.stackCount;
                return count;
            }
        }

        internal void RetainDetached(ref Exception primary)
        {
            foreach (var piece in pieces)
            {
                if (piece.Destroyed || piece.Spawned || piece.holdingOwner != null) continue;
                try
                {
                    // Do not retry placement or merge through the callback that just failed.
                    if (!owner.TryAdd(piece, false) && !Owned(piece))
                        throw new InvalidOperationException("Could not retain the exact inventory-drop fragment.");
                }
                catch (Exception recovery)
                {
                    if (primary == null) primary = recovery;
                    else primary.Data["HaulersDream.InventoryDrop.Recovery." + piece.thingIDNumber] = recovery.ToString();
                }
                finally
                {
                    // Native TryAdd establishes ownership before NotifyAdded can throw.
                    // Keep, lastYieldTick and CE Hold are deliberately not renewed.
                    if (Owned(piece))
                    {
                        Recovered = true;
                        comp?.RestoreExplicitDropFragment(piece, tag);
                    }
                }
            }
        }

        public void Dispose() { current = previous; }
    }

    // Base SplitOff returns before ThingWithComps.PostSplitOff callbacks. A callback can
    // throw before native TryDrop receives that result, so retain its exact identity here.
    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class Patch_InventoryDropSplitRecovery
    {
        static void Prefix(Thing __instance, int count, out InventoryDropRecovery __state)
        {
            var scope = InventoryDropRecovery.current;
            __state = scope != null && scope.Admit(__instance, count) ? scope : null;
        }
        static void Postfix(Thing __result, InventoryDropRecovery __state) { __state?.Record(__result); }
    }
}
