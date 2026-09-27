using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    // Only split provenance and the synchronous observation barrier are shared. Each transfer
    // family keeps its own custody, quantity, tag and recovery policy.
    internal class StorageSplitScope : IDisposable
    {
        [ThreadStatic] internal static StorageSplitScope Current;
        private readonly StorageSplitScope previous;
        private readonly Thing source;
        private readonly int requested;
        private readonly IDisposable barrier;
        private bool entered, disposed;
        internal readonly List<Thing> Fragments = new List<Thing>();

        internal StorageSplitScope(Thing source, int requested)
        {
            this.source = source; this.requested = requested;
            previous = Current; Current = this;
            barrier = StorageCommitments.BeginResourceTransfer();
        }
        internal bool Contains(Thing item) => item != null && Fragments.Contains(item);
        internal bool AdmitSplit(Thing item, int count)
        {
            if (item == null || count <= 0 || count > item.stackCount) return false;
            if (ReferenceEquals(item, source) && !entered)
            {
                if (count > requested) return false;
                entered = true;
                // Whole-source native removal can throw before returning this same identity.
                // A partial original remainder is never added as an owned descendant.
                if (count == item.stackCount) Record(item);
                return true;
            }
            return Contains(item);
        }
        internal void Record(Thing item)
        { if (item != null && !Fragments.Contains(item)) Fragments.Add(item); }
        internal virtual void SplitReturned(Thing item, int before, int count, Thing result) => Record(result);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Current = previous;
            barrier.Dispose();
        }

        internal sealed class SplitCall
        {
            internal readonly StorageSplitScope Scope;
            internal readonly Thing Item;
            internal readonly int Before, Count;
            internal SplitCall(StorageSplitScope scope, Thing item, int count)
            { Scope = scope; Item = item; Before = item.stackCount; Count = count; }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class Patch_StorageSplitReceipt
    {
        static void Prefix(Thing __instance, int count, out StorageSplitScope.SplitCall __state)
        {
            var scope = StorageSplitScope.Current;
            __state = scope != null && scope.AdmitSplit(__instance, count)
                ? new StorageSplitScope.SplitCall(scope, __instance, count) : null;
        }
        static void Postfix(Thing __result, StorageSplitScope.SplitCall __state)
        { __state?.Scope.SplitReturned(__state.Item, __state.Before, __state.Count, __result); }
    }
}
