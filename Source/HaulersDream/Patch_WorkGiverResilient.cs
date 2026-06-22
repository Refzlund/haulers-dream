using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// WORK-SCAN RESILIENCE (issue #7).
    ///
    /// <para>Vanilla <see cref="JobGiver_Work"/>.<c>PawnCanUseWorkGiver</c> decides whether a pawn may use a given
    /// <see cref="WorkGiver"/> this scan (its <c>ShouldSkip</c> / capacity / faction checks). It is called in a tight
    /// loop over EVERY work giver inside <c>TryIssueJobPackage</c>, and vanilla wraps NONE of those calls in a
    /// try/catch. So if a single work giver throws here, the exception propagates out of the whole loop and aborts the
    /// pawn's ENTIRE work selection — and if the fault is persistent it repeats every scan, which permanently stalls
    /// all of that pawn's dumb labor (hauling, cleaning, hauling corpses, etc.). A real report (issue #7) hit exactly
    /// this: a foreign hauling work giver (Haul Explicitly, reached via Vehicle Map Framework's transpiler on this
    /// method) threw a <c>NullReferenceException</c> in its <c>ShouldSkip</c>, bricking the colony's work.</para>
    ///
    /// <para>This Finalizer makes ONE broken work giver degrade to "skipped this scan" instead of bricking ALL work:</para>
    /// <list type="bullet">
    ///   <item>If the throwing work giver belongs to HAULER'S DREAM, RE-THROW it unchanged. HD's own faults must stay
    ///   loud and visible (the project's no-swallow rule) — they are real bugs to fix, not to hide.</item>
    ///   <item>If it belongs to ANY OTHER mod (or vanilla), log it ONCE per work-giver type — so it stays visible and
    ///   attributable, never silently swallowed — and return <c>__result = false</c> ("this pawn can't use this work
    ///   giver this scan"). The work loop then simply advances to the next giver, so the rest of the pawn's work still
    ///   runs.</item>
    /// </list>
    ///
    /// <para>This mirrors the mod's existing resilient-degrade stance (<see cref="HaulersDreamMod"/>'s
    /// <c>ApplyPatchesResilient</c>): contain a foreign failure at a controlled boundary, log it loudly, and keep the
    /// save playable — rather than let a third-party bug brick the colony. It only ever changes behaviour on the
    /// exception path; when nothing throws it is a pure no-op, so a normal work scan is byte-identical.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver", new[] { typeof(Pawn), typeof(WorkGiver) })]
    public static class Patch_JobGiver_Work_WorkGiverResilient
    {
        static Exception Finalizer(Exception __exception, ref bool __result, Pawn pawn, WorkGiver giver)
        {
            if (__exception == null)
                return null; // the common path — no fault, nothing to contain

            // HD's OWN work giver threw -> keep it loud (no-swallow). Returning the exception tells Harmony to
            // re-raise it, so RimWorld still reports the fault (and HD's HDGuard finalizer on TryIssueJobPackage
            // still adds its breadcrumb). A real HD bug must never be hidden by this safety net.
            var giverType = giver?.GetType();
            if (giverType != null && giverType.Assembly == typeof(Patch_JobGiver_Work_WorkGiverResilient).Assembly)
                return __exception;

            // A FOREIGN (or vanilla) work giver threw while RimWorld checked whether this pawn can use it. Contain it
            // at this per-giver boundary: vanilla has no guard, so letting it escape would abort the pawn's ENTIRE
            // work selection every scan. Log once per work-giver type (visible + attributable), then treat it as
            // "can't use this giver this scan" so only that one giver is skipped and all other work keeps running.
            string giverName = giverType?.FullName ?? "an unknown WorkGiver";
            HDLog.ErrOnce(
                "the work giver '" + giverName + "' threw while RimWorld evaluated whether "
                + (pawn?.LabelShort ?? "a pawn") + " can use it. This is NOT a Hauler's Dream work giver — the fault is "
                + "in that work giver (see the stack trace below). Vanilla has no guard here, so this throw would "
                + "otherwise abort the pawn's ENTIRE work selection every scan (all hauling/cleaning/etc. would stall). "
                + "Hauler's Dream is skipping just that one work giver for this pawn this scan so the rest of its work "
                + "keeps running; please report this to the owning mod.\n" + __exception,
                ("HD.wgResilient." + giverName).GetHashCode());
            __result = false; // "pawn can't use this work giver" -> the work loop advances to the next giver
            return null;      // handled: suppress only this foreign giver's throw so the work scan isn't bricked
        }
    }
}
