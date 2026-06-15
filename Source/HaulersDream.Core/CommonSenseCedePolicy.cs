namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decisions for ceding the DoBill ingredient-gather flow to Common Sense.
    /// No Verse/RimWorld refs (auto-compiled by the SDK default glob).
    /// </summary>
    public static class CommonSenseCedePolicy
    {
        /// <summary>
        /// Should HD cede the DoBill gather flow to Common Sense (i.e. NOT convert automatic bills to
        /// BillPrepGather / BatchCraft)?
        /// <list type="bullet">
        /// <item>CS absent  => false (FAIL-OPEN: HD operates exactly as vanilla-HD).</item>
        /// <item>CS present but its toggle fields are UNREADABLE (fork/rename) => true (present-as-owning
        /// fallback: CS installs the MakeNewToils Prefix every session and defaults both toggles true, so the
        /// safe direction when we can't prove the user disabled both is to cede). This is the ONE HD bridge
        /// that is deliberately fail-CLOSED on the unreadable path.</item>
        /// <item>CS present + readable => adv_cleaning || adv_haul_all_ings (mirrors CS's own Prefix: it owns
        /// the driver exactly when either is on). Both off => false (DO NOT over-cede: CS runs vanilla, so HD
        /// must keep operating).</item>
        /// </list>
        /// </summary>
        public static bool ShouldCedeDoBillFlow(bool csPresent, bool fieldsReadable, bool advCleaning, bool advHaulAll)
        {
            if (!csPresent) return false;
            if (!fieldsReadable) return true;
            return advCleaning || advHaulAll;
        }

        /// <summary>
        /// Belt-and-suspenders (#2): should the automatic unload pass DEFER because the pawn's current/queued
        /// vanilla DoBill needs the tagged carried stock? Identity predicate — the impure bill-matching work
        /// (InventoryShare.IsUsableForBill over CurJob + jobQueue) lives in PawnUnloadChecker, this pins the
        /// named contract unit-visibly (mirrors UnloadPolicy.HasPendingRealWork's thin-pure shape).
        /// </summary>
        public static bool ShouldDeferUnloadForActiveBill(bool curOrQueuedJobIsDoBillMatchingTagged)
            => curOrQueuedJobIsDoBillMatchingTagged;
    }
}
