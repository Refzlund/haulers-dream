using HarmonyLib;
using RimWorld;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// DOBILL LOAD-CRASH RESILIENCE — Common Sense "clean before crafting" + save/load.
    ///
    /// <para>Common Sense (avilmask.CommonSense) replaces vanilla <see cref="JobDriver_DoBill"/>.<c>MakeNewToils</c>
    /// and, with its "clean the area before crafting" feature (<c>adv_cleaning</c>, ON by default), REUSES
    /// <see cref="TargetIndex.A"/> for its filth queue: it lists room filth into the A target queue, then
    /// <c>ExtractNextTargetFromQueue(A)</c> sets <c>job.targetA</c> to a <see cref="Verse.Filth"/> so the pawn walks
    /// to and cleans it before returning to the bench. During that window the job's target A is a Filth, not the
    /// bench. Common Sense tolerates this in its own <c>FailOn</c>, and at runtime it works.</para>
    ///
    /// <para>The bug surfaces on SAVE/LOAD. If the game is saved while a pawn is mid-clean (target A == Filth), then
    /// on load <c>JobDriver.ExposeData</c> re-runs <c>SetupToils</c> → Common Sense's <c>MakeNewToils</c> iterator,
    /// whose very first statements read <see cref="JobDriver_DoBill.BillGiver"/>
    /// (<c>bool placeInBillGiver = BillGiver is Building_WorkTableAutonomous</c>) BEFORE any filth-tolerant logic.
    /// Vanilla's <c>BillGiver</c> getter does <c>targetA.Thing as IBillGiver</c> and THROWS
    /// <c>InvalidOperationException("DoBill on non-Billgiver.")</c> when target A is the Filth — aborting
    /// <c>SetupToils</c>. Vanilla then starts an error-recover Wait job mid-load, which itself NREs, and the pawn is
    /// left permanently jobless. (Reported with Hauler's Dream + Common Sense + Medieval Overhaul cooking; the crash
    /// trace is pure Common Sense + vanilla — Hauler's Dream is not in it — but HD ships the resilience because it
    /// already integrates the Common Sense DoBill flow, see <see cref="CommonSenseCompat"/>.)</para>
    ///
    /// <para>FIX (defensive — mirrors <see cref="Patch_JobGiver_Work_WorkGiverResilient"/>): a prefix on the
    /// <c>BillGiver</c> getter. When target A already IS a bill giver (every normal case) this is a pure no-op and
    /// vanilla runs unchanged. ONLY when target A is NOT a bill giver do we recover the real giver from the bill
    /// itself — <c>job.bill.billStack.billGiver</c>, which is EXACTLY the Thing vanilla <see cref="WorkGiver_DoBill"/>
    /// used as target A when it built the job — and return that instead of throwing. This is NOT error suppression:
    /// it returns the correct, documented value of "BillGiver" (the bill's giver) that the throw was masking, so
    /// <c>SetupToils</c> completes and the pawn resumes its bill normally across the save/load. <c>job.targetA</c> is
    /// left untouched (still the Filth), so Common Sense's cleaning toils resume exactly as they would have without
    /// the save. If the bill has no resolvable giver (genuinely unrecoverable) we let vanilla throw, so a real fault
    /// still surfaces.</para>
    ///
    /// <para>Ungated (not behind a Common Sense check) on purpose: it is a pure safety net that diverges from vanilla
    /// ONLY on the throw path, so it can never regress a normal DoBill, and it also rescues an existing corrupt save
    /// even if Common Sense has since been removed (the bad target A is baked into the save either way).</para>
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_DoBill), nameof(JobDriver_DoBill.BillGiver), MethodType.Getter)]
    public static class Patch_JobDriver_DoBill_BillGiver_LoadCrash
    {
        static bool Prefix(JobDriver_DoBill __instance, ref IBillGiver __result)
        {
            var job = __instance?.job;
            if (job == null)
                return true; // nothing to recover from -> let vanilla run (it surfaces any real null fault itself)

            // Normal case: target A is the bill giver. Pure no-op -> vanilla getter runs, behaviour byte-identical.
            if (job.GetTarget(TargetIndex.A).Thing is IBillGiver)
                return true;

            // Target A is transiently NOT a bill giver (Common Sense parks floor Filth there while cleaning before
            // the craft; a save taken mid-clean re-runs SetupToils on load, which reads BillGiver before that state
            // is tolerated). Recover the real giver from the bill itself -- this is precisely the Thing vanilla
            // WorkGiver_DoBill used as target A when it created the job -- so BillGiver returns its correct value
            // instead of throwing "DoBill on non-Billgiver" and bricking the load.
            if (job.bill?.billStack?.billGiver is IBillGiver giver)
            {
                __result = giver;
                HDLog.WarnOnce(
                    "recovered JobDriver_DoBill.BillGiver from the bill because the job's target A was not a bill "
                    + "giver. This is typically Common Sense's 'clean before crafting' parking floor filth in target "
                    + "A, plus a save taken mid-clean: on load SetupToils reads BillGiver before that state is "
                    + "tolerated and vanilla would throw 'DoBill on non-Billgiver', leaving the pawn jobless. "
                    + "Hauler's Dream returned the bill's own giver so the bill resumes normally. This is a "
                    + "compatibility safety net, not a Hauler's Dream bug.",
                    "HD.doBillBillGiverRecover".GetHashCode());
                return false; // skip the vanilla throw; __result now holds the correct giver
            }

            // No resolvable giver on the bill -> genuinely unrecoverable. Let vanilla throw so the fault stays visible.
            return true;
        }
    }
}
