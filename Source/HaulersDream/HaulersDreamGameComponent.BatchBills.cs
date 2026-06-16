using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        // --- per-bill BATCH config (the Batch-Y bill mode) ---
        // Key = bill.GetUniqueLoadID() (stable across save/load; bill loadIDs are monotonic and never reused
        // within a game, so a stale entry left by a deleted bill is INERT — it can never mis-apply to a future
        // bill — which is why no active pruning is needed). Presence in the map = batching ON; value = batch
        // size (>= 1). Scribed WITH THE GAME (bills are game-scoped, not global like the def-keyed settings),
        // via a plain Scribe_Collections — no core-serialization Harmony patching.
        private Dictionary<string, int> batchBills = new Dictionary<string, int>();
        public const int BatchSizeMax = 1000;

        private static string BatchKey(Bill bill) => bill?.recipe == null ? null : bill.GetUniqueLoadID();

        /// <summary>Is this bill set to batch?</summary>
        public bool IsBatchBill(Bill bill)
        {
            var k = BatchKey(bill);
            return k != null && batchBills.ContainsKey(k);
        }

        /// <summary>The bill's batch size, or 0 if it isn't batching.</summary>
        public int BatchSizeOf(Bill bill)
        {
            var k = BatchKey(bill);
            return (k != null && batchBills.TryGetValue(k, out int n)) ? n : 0;
        }

        /// <summary>Turn batching on (size clamped to [1, BatchSizeMax]) or off for a bill.</summary>
        public void SetBatch(Bill bill, bool on, int size)
        {
            var k = BatchKey(bill);
            if (k == null)
                return;
            if (on)
                batchBills[k] = Mathf.Clamp(size, 1, BatchSizeMax);
            else
                batchBills.Remove(k);
        }

        // The per-bill batch config scribing (additive to base.ExposeData via ExposeData() -> ExposeBatchBills()).
        private void ExposeBatchBills()
        {
            Scribe_Collections.Look(ref batchBills, "haulersDreamBatchBills", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && batchBills == null)
                batchBills = new Dictionary<string, int>();
        }
    }
}
