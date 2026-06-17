using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Shared cross-cutting state for the transport bulk-load path: the per-thread "HD is depositing" flags that
    /// gate the manifest-decrement intercept (so vanilla single-item loads and OTHER mods' loads keep vanilla
    /// accounting), the precise best-match finder that mirrors vanilla's entry selection, and the null-checked
    /// reflection fallback for writing a fork-<c>Transferable</c> that lacks the public <c>ForceTo</c> (RW 1.6's
    /// <c>Transferable.ForceTo(int)</c> IS public — the reflection path is a safety net only, never the primary
    /// write).
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
        /// The <c>leftToLoad</c> entry vanilla would decrement for a deposit of the WHOLE <paramref name="deposited"/>
        /// stack. Pure delegation to <see cref="TransferableUtility.TransferableMatchingDesperate"/> — see the
        /// <see cref="FindBestMatchFor(Thing, int, List{TransferableOneWay})"/> overload for why.
        /// </summary>
        public static TransferableOneWay FindBestMatchFor(Thing deposited, List<TransferableOneWay> leftToLoad)
            => FindBestMatchFor(deposited, depositedCount: deposited?.stackCount ?? 0, leftToLoad);

        /// <summary>
        /// The <c>leftToLoad</c> entry to decrement for a deposit of <paramref name="depositedCount"/> units of
        /// <paramref name="deposited"/> — selected by the EXACT same matcher vanilla's
        /// <c>CompTransporter.SubtractFromToLoadList</c> / <c>MapPortal.SubtractFromToLoadList</c> use,
        /// <see cref="TransferableUtility.TransferableMatchingDesperate"/> in <c>PodsOrCaravanPacking</c> mode (the
        /// 3-tier ladder: identity → <c>TransferAsOne</c> variant → def-only fallback). Mirroring vanilla here is what
        /// keeps the decrement in lock-step with the deposit CLAMP (<c>PortalRemainingFor</c> /
        /// <c>MemberRemainingFor</c>), which call the SAME 3-tier matcher: an off-quality fungible deposit HD's
        /// def-keyed scoop side delivered (e.g. a masterwork longsword against a by-count "normal longsword" entry)
        /// matches the def entry via Tier-3 in BOTH the clamp and here, so the entry is actually decremented (no
        /// stranded-positive desync) — exactly as vanilla would accept it. A mixed-stuff/quality manifest still
        /// decrements the RIGHT entry: Tier-2 distinguishes a variant the manifest holds explicitly (e.g. deposit
        /// normal vs. a separate good-armor entry) from the others before Tier-3 is ever consulted.
        ///
        /// <paramref name="depositedCount"/> is accepted for signature symmetry with the caller (which clamps the move
        /// to the matched entry's remaining); it is intentionally NOT used to choose AMONG entries — vanilla's matcher
        /// returns a single entry per deposited variant (the load dialog groups every <c>TransferAsOne</c> instance
        /// into ONE entry via <c>TransferableMatching</c>, and each <c>SubtractFromToLoadList</c> runs against a single
        /// member's <c>leftToLoad</c>), so there is no multi-candidate count-ranking to do, and re-introducing one
        /// would diverge from vanilla.
        /// </summary>
        public static TransferableOneWay FindBestMatchFor(Thing deposited, int depositedCount, List<TransferableOneWay> leftToLoad)
        {
            if (deposited?.def == null || leftToLoad == null || leftToLoad.Count == 0)
                return null;
            // EXACT vanilla entry selection — the same call SubtractFromToLoadList and the deposit clamp make.
            return TransferableUtility.TransferableMatchingDesperate(deposited, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
        }
    }
}
