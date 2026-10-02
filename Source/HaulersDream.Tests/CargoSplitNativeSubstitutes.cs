// Test-only substitutes for the selected native split/owner contracts. The real recovery
// and split-receipt source is linked into this assembly; this does not execute Harmony.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
}

namespace Verse
{
    internal class Thing
    {
        private static int nextId;
        internal readonly int thingIDNumber = ++nextId;
        internal int stackCount;
        internal bool Spawned, Destroyed;
        internal ThingOwner holdingOwner;
        internal Thing LastSplit;
        public virtual Thing SplitOff(int count)
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            object[] entry = { this, count, null };
            typeof(HaulersDream.Patch_StorageSplitReceipt).GetMethod("Prefix", flags).Invoke(null, entry);
            Thing piece;
            if (count >= stackCount)
            {
                Spawned = false;
                holdingOwner?.Remove(this);
                piece = this;
            }
            else
            {
                piece = new Thing { stackCount = count };
                stackCount -= count;
            }
            LastSplit = piece;
            typeof(HaulersDream.Patch_StorageSplitReceipt).GetMethod("Postfix", flags)
                .Invoke(null, new[] { (object)piece, entry[2] });
            return piece;
        }
    }

    internal sealed class ThrowingSplitThing : Thing
    {
        internal Exception Failure;
        public override Thing SplitOff(int count)
        {
            Thing piece = base.SplitOff(count);
            // ThingWithComps runs PostSplitOff after base returns, before its own return.
            if (Failure != null) throw Failure;
            return piece;
        }
    }

    internal sealed class ThingOwner
    {
        private readonly List<Thing> items = new List<Thing>();
        internal Action<Thing> BeforeAdd, AfterAdd;
        internal readonly List<bool> MergeArguments = new List<bool>();
        internal int Count => items.Count;
        internal bool Contains(Thing item) => items.Contains(item);
        internal bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            MergeArguments.Add(canMergeWithExistingStacks);
            BeforeAdd?.Invoke(item);
            if (item.Destroyed || item.Spawned || item.holdingOwner != null) return false;
            items.Add(item); item.holdingOwner = this;
            AfterAdd?.Invoke(item);
            return true;
        }
        internal void Remove(Thing item)
        { if (items.Remove(item)) item.holdingOwner = null; }
        internal int TryTransfer(Thing item, ThingOwner destination, int count, out Thing transferred)
        {
            transferred = null;
            Thing piece = item.SplitOff(count);
            if (Contains(piece)) Remove(piece);
            if (!destination.TryAdd(piece, false)) return 0;
            transferred = piece;
            return piece.stackCount;
        }
    }

    internal sealed class Pawn
    {
        internal sealed class Inventory { internal readonly ThingOwner innerContainer = new ThingOwner(); }
        internal readonly Inventory inventory = new Inventory(), carryTracker = new Inventory();
        internal readonly HaulersDream.CompHauledToInventory Comp = new HaulersDream.CompHauledToInventory();
        internal T GetComp<T>() where T : class => Comp as T;
    }
}

namespace HaulersDream
{
    internal sealed class CompHauledToInventory
    {
        private readonly HashSet<Verse.Thing> tagged = new HashSet<Verse.Thing>();
        internal Action<Verse.Thing> AfterTag;
        internal int RegisterCalls;
        internal HashSet<Verse.Thing> PeekHashSet() => tagged;
        internal void RegisterHauledItem(Verse.Thing item)
        { RegisterCalls++; tagged.Add(item); AfterTag?.Invoke(item); }
    }
    internal static class StorageCommitments
    {
        internal static int BarrierDepth;
        internal static Exception DisposeFailure;
        internal static IDisposable BeginResourceTransfer() { BarrierDepth++; return new Barrier(); }
        private sealed class Barrier : IDisposable
        {
            public void Dispose()
            { BarrierDepth--; if (DisposeFailure != null) throw DisposeFailure; }
        }
    }
}
