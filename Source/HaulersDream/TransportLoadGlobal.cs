using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Shared cross-cutting state for the transport bulk-load path: the per-thread "HD is depositing" flags that
    /// gate the manifest-decrement intercept (so vanilla single-item loads and OTHER mods' loads keep vanilla
    /// accounting), the precise best-match finder feeding <see cref="TransferableMatchPolicy"/>, and the
    /// null-checked reflection fallback for writing a fork-<c>Transferable</c> that lacks the public
    /// <c>ForceTo</c> (RW 1.6's <c>Transferable.ForceTo(int)</c> IS public — the reflection path is a safety net
    /// only, never the primary write).
    /// </summary>
    public static class Global
    {
        /// <summary>Set ONLY inside HD's transporter deposit toil (try/finally), per-thread. While true, the
        /// <c>CompTransporter.SubtractFromToLoadList</c> intercept does the precise <c>ForceTo</c> decrement; while
        /// false (every other caller — vanilla single-item load, another mod), the original fuzzy vanilla math runs.</summary>
        [System.ThreadStatic] public static bool IsExecutingManagedUnload;

        /// <summary>The portal-side counterpart (Stage 3) — set only inside the portal deposit toil. Declared now so
        /// the shared matcher/intercept shape is symmetric; unused until the portal driver lands.</summary>
        [System.ThreadStatic] public static bool IsExecutingManagedPortalUnload;

        // Reflection fallback for a fork Transferable whose ForceTo is non-public (RW 1.6's is public, so these are
        // resolved best-effort and only used when a write target lacks the public method). Resolved lazily, once.
        private static bool reflectionInit;
        private static FieldInfo countToTransferField; // TransferableOneWay.countToTransfer (private int)
        private static FieldInfo editBufferField;       // Transferable.editBuffer (private string), if present

        private static void EnsureReflection()
        {
            if (reflectionInit)
                return;
            reflectionInit = true;
            countToTransferField = AccessTools.Field(typeof(TransferableOneWay), "countToTransfer");
            editBufferField = AccessTools.Field(typeof(Transferable), "editBuffer");
        }

        /// <summary>
        /// Precisely set a manifest entry's remaining count. Primary path: the PUBLIC <c>Transferable.ForceTo</c>
        /// (it sets <c>CountToTransfer</c> AND keeps <c>EditBuffer</c> consistent). Fallback (a fork Transferable
        /// missing the public method): write the private fields by reflection. Never silently no-ops — if neither
        /// path is available the caller's vanilla-original return is what runs.
        /// </summary>
        public static bool ForceTo(TransferableOneWay tr, int value)
        {
            if (tr == null)
                return false;
            // RW 1.6: ForceTo is public on Transferable — call it directly (handles EditBuffer too).
            tr.ForceTo(value);
            return true;
        }

        /// <summary>Reflection fallback write (only reached if a fork lacks public ForceTo — RW 1.6 doesn't need it).</summary>
        public static bool ForceToViaReflection(TransferableOneWay tr, int value)
        {
            EnsureReflection();
            if (tr == null || countToTransferField == null)
                return false;
            countToTransferField.SetValue(tr, value);
            if (editBufferField != null)
                editBufferField.SetValue(tr, value.ToString());
            return true;
        }

        /// <summary>
        /// The best <c>leftToLoad</c> entry to decrement for a deposit of <paramref name="depositedCount"/> units of
        /// <paramref name="deposited"/>'s def — Verse-side wrapper over <see cref="TransferableMatchPolicy"/>. Filters
        /// the manifest to same-def entries with a positive remaining, ranks them (exact → largest-absorbing →
        /// smallest-partial), and returns the chosen entry (or null when nothing matches).
        /// </summary>
        public static TransferableOneWay FindBestMatchFor(Thing deposited, List<TransferableOneWay> leftToLoad)
            => FindBestMatchFor(deposited?.def, depositedCount: deposited?.stackCount ?? 0, leftToLoad);

        /// <summary>Thing-less overload (the portal/precise path captures def+count before the transfer).</summary>
        public static TransferableOneWay FindBestMatchFor(ThingDef def, int depositedCount, List<TransferableOneWay> leftToLoad)
        {
            if (def == null || leftToLoad == null || leftToLoad.Count == 0)
                return null;
            var candidates = new List<TransferableMatchPolicy.Candidate>();
            for (int i = 0; i < leftToLoad.Count; i++)
            {
                var tr = leftToLoad[i];
                if (tr == null || !tr.HasAnyThing || tr.ThingDef != def || tr.CountToTransfer <= 0)
                    continue;
                candidates.Add(new TransferableMatchPolicy.Candidate(i, tr.CountToTransfer));
            }
            int idx = TransferableMatchPolicy.ChooseBestMatchIndex(candidates, depositedCount);
            return idx >= 0 && idx < leftToLoad.Count ? leftToLoad[idx] : null;
        }
    }
}
