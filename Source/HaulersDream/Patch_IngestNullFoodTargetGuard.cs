using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// VANILLA WORKAROUND (#207): <c>JobDriver_Ingest.MakeNewToils</c> registers a global <c>FailOn</c>:
    /// <code>this.FailOn(() =&gt; !IngestibleSource.Destroyed &amp;&amp; !IngestibleSource.IngestibleNow);</code>
    /// where <c>IngestibleSource =&gt; job.targetA.Thing</c>. <c>LocalTargetInfo.Thing</c> returns the raw
    /// <c>thingInt</c> reference — so when the food Thing reference is null (a Thing lost to a save/load
    /// cycle, a mod interaction, or game-state corruption in a heavily-modded save — observed with 230 mods
    /// including a broken <c>MoreFactionInteraction</c> whose static ctor throws ~960×/session), the lambda
    /// dereferences null and throws <see cref="NullReferenceException"/> at <c>.Destroyed</c>.
    ///
    /// <c>CheckCurrentToilEndOrFail</c> catches the NRE, logs a red "Exception in CheckCurrentToilEndOrFail"
    /// (81 occurrences in the report's log), and starts a 150-tick error-recovery Wait. The pawn re-thinks,
    /// gets another Ingest job whose target may ALSO be null, and the cycle repeats — the pawn appears frozen
    /// and goes hungry.
    ///
    /// This prefix detects the null-food-target case BEFORE the buggy lambda can throw and ends the job
    /// <c>Incompletable</c> — the same condition the vanilla lambda would have returned had it been written
    /// null-safe. The pawn cleanly re-evaluates its food options without the red error or the 150-tick
    /// penalty, and the log is not spammed.
    ///
    /// Narrow by design: the check fires only for <see cref="JobDriver_Ingest"/> whose <c>targetA.Thing</c> is
    /// null (the food Thing reference is gone). A valid Ingest job — including a nutrient-paste-dispenser whose
    /// targetA is the building — always has a non-null Thing, so the original method runs unchanged.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver), "CheckCurrentToilEndOrFail")]
    public static class Patch_IngestNullFoodTargetGuard
    {
        [HarmonyPriority(Priority.High)]
        static bool Prefix(JobDriver __instance, ref bool __result)
        {
            if (__instance is JobDriver_Ingest ingest && ingest.job != null
                && ingest.job.targetA.Thing == null)
            {
                // Log once per pawn so the underlying save/mod corruption stays diagnosable
                // (replaces 81 red errors with a single yellow warning per pawn).
                Log.WarningOnce("[Hauler's Dream] Ended a JobDriver_Ingest with a null food target for "
                    + ingest.pawn?.LabelShort ?? "a pawn" + " — likely a save/load or mod-corruption artifact (#207).",
                    ingest.pawn?.thingIDNumber ?? 0x207);
                ingest.EndJobWith(JobCondition.Incompletable);
                __result = true;
                return false;
            }
            return true;
        }
    }
}
