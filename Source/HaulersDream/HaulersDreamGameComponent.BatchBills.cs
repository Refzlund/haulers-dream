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

        // --- per-bill "overshoot by Y" for a "Do until you have X" (TargetCount) batch (issue #3) ---
        // Parallel to batchBills, keyed IDENTICALLY by bill.GetUniqueLoadID(). Value = Y, the EXTRA amount to keep
        // crafting past the target X once a batch has started (so the pawn finishes to X+Y "while it's already there").
        // Independent of batchBills: a missing key just means overshoot 0 (the original stop-at-X behaviour), so this
        // is fully backward-compatible with saves that predate the feature — they simply have no overshoot entries.
        private Dictionary<string, int> batchOvershoots = new Dictionary<string, int>();

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

        /// <summary>The bill's "overshoot by Y" amount for a Do-until-X (TargetCount) batch, or 0 if none set.
        /// Independent of <see cref="IsBatchBill"/> — a value here only takes effect while the bill is actually
        /// batching in TargetCount mode (the driver/planner read it only there).</summary>
        public int BatchOvershootOf(Bill bill)
        {
            var k = BatchKey(bill);
            return (k != null && batchOvershoots.TryGetValue(k, out int y)) ? y : 0;
        }

        /// <summary>Set the bill's "overshoot by Y" amount (clamped to [0, BatchSizeMax]). Y == 0 removes the key
        /// (the default, stop-at-X behaviour) so the dict stays sparse and a never-overshot bill scribes nothing.</summary>
        public void SetBatchOvershoot(Bill bill, int y)
        {
            var k = BatchKey(bill);
            if (k == null)
                return;
            y = Mathf.Clamp(y, 0, BatchSizeMax);
            if (y > 0)
                batchOvershoots[k] = y;
            else
                batchOvershoots.Remove(k);
        }

        // The per-bill batch config scribing (additive to base.ExposeData via ExposeData() -> ExposeBatchBills()).
        private void ExposeBatchBills()
        {
            Scribe_Collections.Look(ref batchBills, "haulersDreamBatchBills", LookMode.Value, LookMode.Value);
            // Separate, independently-scribed collection (distinct label) so old saves without it load fine and the
            // overshoot feature never interferes with the existing batchBills data.
            Scribe_Collections.Look(ref batchOvershoots, "haulersDreamBatchOvershoots", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && batchBills == null)
                batchBills = new Dictionary<string, int>();
            if (Scribe.mode == LoadSaveMode.LoadingVars && batchOvershoots == null)
                batchOvershoots = new Dictionary<string, int>();
        }
    }
}
