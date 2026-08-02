using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// SESSION QUARANTINE for a work giver that repeatedly throws out of the unguarded tail of vanilla's work
    /// scan (issue #235).
    ///
    /// <para><b>Why containment alone is not enough.</b> Vanilla <c>JobGiver_Work.TryIssueJobPackage</c> wraps
    /// only the per-giver SCAN in a try/catch; the tail call that converts the winning target into a job
    /// (<c>scannerWhoProvidedTarget.JobOnCell/JobOnThing</c>) is unguarded — decompile-verified, it sits after
    /// the try/catch/finally. A mod postfixing a WorkGiver's <c>JobOnThing</c> and throwing there takes down the
    /// whole think node, and RimWorld's priority sorter then skips that node every scan: the colonist does no
    /// work at all while still eating and sleeping. HD's Finalizer can catch that, but catching alone changes
    /// almost nothing — before, the throw made RimWorld skip the work node and the pawn wandered; after, the
    /// finalizer returns "no job" and the pawn wanders. The colony only recovers if the poisoned giver leaves
    /// the rotation, which is what this class does: once
    /// <see cref="WorkGiverQuarantinePolicy.FaultsBeforeQuarantine"/> faults are attributed to it,
    /// <see cref="Patch_JobGiver_Work_WorkGiverResilient"/>'s prefix makes <c>PawnCanUseWorkGiver</c> return false
    /// for it, so vanilla <c>continue</c>s the whole loop iteration and that giver can never become
    /// <c>scannerWhoProvidedTarget</c> again — every OTHER kind of work resumes.</para>
    ///
    /// <para><b>This class only GATHERS FACTS; the decision lives in
    /// <see cref="WorkGiverQuarantinePolicy"/>.</b> Switching a work giver off is the most destructive thing HD
    /// does to a save — get it wrong and HD reproduces #235's symptom with itself as the cause — so the rule set
    /// sits in the Verse-free Core with a named test per refusal, rather than in a chain of early returns inside
    /// a Harmony finalizer.</para>
    ///
    /// <para><b>Attribution, and why naming a giver is not the same as implicating a mod.</b> The preferred
    /// answer comes from the exception's own frames. That route CANNOT see a Harmony-patched giver, though: the
    /// patched method runs as a <c>DynamicMethod</c>, whose <c>DeclaringType</c> is always null — which is
    /// precisely the #235 shape (Smarter Construction postfixes
    /// <c>RimWorld.WorkGiver_ConstructFinishFrames.JobOnThing</c>). So the fallback — the last giver vanilla
    /// cleared during THIS call — is in practice the common route, and it can name a giver that merely happened
    /// to pass the gate before an unrelated throw. Hence <see cref="ScanContextGiver"/>'s strict per-call
    /// lifetime below. And because naming the giver says nothing about WHOSE fault it is, both routes then have
    /// to clear the same evidence bar in <see cref="WorkGiverQuarantinePolicy"/>: a mod patches this giver's job
    /// entry points (<see cref="ModPatchOwnersOf"/>), or a mod-owned frame is in the trace.</para>
    ///
    /// <para><b>HD's own work givers are never quarantined</b>, mirroring
    /// <see cref="Patch_JobGiver_Work_WorkGiverResilient"/>: an HD bug must stay loud and be fixed, not hidden
    /// behind a switched-off feature.</para>
    ///
    /// <para><b>Session-scoped and never persisted.</b> Nothing here is scribed and nothing clears it on map or
    /// game change: a giver that is broken is broken for as long as the offending mod is loaded, and a restart
    /// (the natural point at which the player updates or removes that mod) starts clean. The player is told via
    /// <see cref="Alert_WorkGiverQuarantined"/> — a work type silently never happening again is exactly the
    /// invisible degradation that would produce the next false bug report against HD.</para>
    ///
    /// <para><b>Threading.</b> Written from a Harmony finalizer, which HD elsewhere documents as possibly running
    /// off the main thread (see <c>HDLog</c>'s per-method occurrence counter, a <c>ConcurrentDictionary</c> for
    /// exactly this reason), and read from the work scan and the alert. Both maps are therefore
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/>, the scan context is <c>[ThreadStatic]</c>, and the hot
    /// read is gated on a <c>volatile bool</c> rather than on <c>Count</c>/<c>IsEmpty</c> (both of which take
    /// every internal lock in .NET Framework's implementation — unacceptable in a per-giver, per-pawn, per-scan
    /// loop).</para>
    /// </summary>
    public static class WorkGiverBlocklist
    {
        // Fault tally per work-giver TYPE (not per instance: WorkGivers are singletons per def, but keying on
        // the type also folds a mod that rebuilds them).
        private static readonly ConcurrentDictionary<Type, int> faultCounts = new ConcurrentDictionary<Type, int>();

        // The quarantine set. The VALUE is the player-facing description ("<giver> - <mod>"), captured at
        // quarantine time while the faulting exception's frames are still available, so the alert never has to
        // re-derive attribution from a stack it no longer has.
        private static readonly ConcurrentDictionary<Type, string> quarantined = new ConcurrentDictionary<Type, string>();

        // Fast path for IsQuarantined: false until the first quarantine, so the per-giver work-scan check is one
        // volatile read in every normal game. Written after the map insert, so a reader that sees it true always
        // finds the entry (and a reader that races just misses one scan — harmless).
        private static volatile bool anyQuarantined;

        // The scan context: the last giver vanilla cleared for this pawn DURING THE CURRENT
        // TryIssueJobPackage call. See ScanContextGiver for why the lifetime is exactly one call.
        [ThreadStatic]
        private static Type scanContextGiver;

        /// <summary>True when at least one work giver is switched off — the alert's activation test, and the
        /// cheap gate on the hot per-giver check.</summary>
        public static bool AnyQuarantined => anyQuarantined;

        /// <summary>Player-facing descriptions of every switched-off work giver, for the alert. Empty when none.</summary>
        public static IEnumerable<string> Quarantined => quarantined.Values;

        /// <summary>
        /// The last work giver vanilla cleared for this pawn during the CURRENT <c>TryIssueJobPackage</c> call,
        /// or null when this call has not cleared one yet.
        ///
        /// <para><b>The per-call lifetime is a correctness requirement, not tidiness (issue #235 QA).</b> A
        /// value that outlives its scan is worse than no value: a mod that postfixes <c>TryIssueJobPackage</c>
        /// and throws does so AFTER the loop finished, and a throw from before the loop even starts
        /// (<c>InLabor()</c>, <c>WorkGiversInOrderNormal</c>) never enters it — in both cases a stale field would
        /// confidently name a giver that had nothing to do with the fault, possibly one from a DIFFERENT pawn's
        /// scan. Reading null in those cases is the correct answer. The seam Finalizer therefore clears this on
        /// EVERY invocation, success path included, in a <c>finally</c>.</para>
        /// </summary>
        public static Type ScanContextGiver => scanContextGiver;

        /// <summary>
        /// Record that this work giver passed vanilla's own <c>PawnCanUseWorkGiver</c> gate, so it is a candidate
        /// to be the scanner that provides the target for the unguarded tail call.
        /// </summary>
        /// <param name="giver">The giver vanilla just cleared for this pawn; null records nothing.</param>
        public static void NoteGiverPassedGate(WorkGiver giver)
        {
            if (giver != null)
                scanContextGiver = giver.GetType();
        }

        /// <summary>
        /// Drop the scan context. The seam Finalizer calls this unconditionally, so the value can only ever
        /// describe the call it was recorded in (see <see cref="ScanContextGiver"/>). One <c>[ThreadStatic]</c>
        /// write per work scan, and it cannot throw.
        /// </summary>
        public static void ClearScanContext() => scanContextGiver = null;

        /// <summary>
        /// True if this work giver is switched off for the session. Called for EVERY giver, for every pawn, on
        /// every work scan — the volatile pre-check keeps the normal case to a single field read.
        /// </summary>
        /// <param name="giver">The giver vanilla is about to consider.</param>
        public static bool IsQuarantined(WorkGiver giver)
        {
            if (!anyQuarantined || giver == null)
                return false;
            return quarantined.ContainsKey(giver.GetType());
        }

        /// <summary>
        /// Count one fault observed at the work-selection seam and, if <see cref="WorkGiverQuarantinePolicy"/>
        /// allows it, switch the responsible work giver off for the session.
        ///
        /// <para>TOTAL by contract: this is called from a Harmony finalizer BEFORE that finalizer decides what to
        /// return, and Harmony's generated handler is a bare <c>catch { pop }</c> that leaves the in-flight
        /// exception untouched — a fault here would therefore destroy the containment it exists to support,
        /// invisibly. Everything is wrapped, with the last-resort disk trail as the only report channel (the same
        /// logger-never-throws boundary <see cref="HDDebugLog"/> documents).</para>
        /// </summary>
        /// <param name="ex">The exception that escaped the work scan.</param>
        /// <param name="scanGiver">The scan context captured by the caller BEFORE it cleared it — the last giver
        /// vanilla cleared during this very call, or null. Passed in rather than read here so the read cannot
        /// accidentally outlive the <c>finally</c> that clears it.</param>
        /// <param name="pawn">The pawn whose scan it was; may be null, used only for the report.</param>
        public static void NoteSeamFault(Exception ex, Type scanGiver, Pawn pawn)
        {
            try
            {
                var giver = HDFault.FirstFrameTypeAssignableTo(ex, typeof(WorkGiver));
                var attribution = giver != null ? GiverAttribution.FrameWalk
                    : scanGiver != null ? GiverAttribution.ScanContext
                    : GiverAttribution.None;
                giver = giver ?? scanGiver;

                // The innermost frame belonging to a MOD other than HD (and not Harmony's shared plumbing).
                // Doubles as one of the policy's two mod-evidence facts and as the culprit named in the report,
                // so it is resolved once.
                string origin = HDFault.DescribeOrigin(ex);
                // The other, and the one that makes the REPORTED bug quarantinable at all: Smarter Construction
                // patches a VANILLA giver type, so nothing about the giver's own assembly implicates a mod — the
                // patch does.
                string patchOwners = giver == null ? null : ModPatchOwnersOf(giver);
                int count = giver == null ? 0 : faultCounts.AddOrUpdate(giver, 1, (_, c) => c + 1);

                var verdict = WorkGiverQuarantinePolicy.Decide(
                    attribution,
                    giverIsHaulersDream: giver != null && HDFault.IsHaulersDream(giver.Assembly),
                    giverIsPatchedByAMod: patchOwners != null,
                    originIsModOwned: origin != null,
                    faultCount: count);
                if (verdict != QuarantineVerdict.Quarantine)
                    return;

                string owner = OwnerText(giver, origin, patchOwners);
                if (!quarantined.TryAdd(giver, giver.FullName + " - " + owner))
                    return;
                anyQuarantined = true;

                // SCOPE OF A QUARANTINE, decompile-verified — and the trap is that PawnCanUseWorkGiver DOES sit
                // on a priority path, just not the one a right-click uses:
                //   - JobGiver_Work:89  (the automatic scan loop)          -> blocked, which is the whole point.
                //   - JobGiver_Work:308 (GiverTryGiveJobPrioritized, reached only from the emergency/priorityWork
                //     SUSTAIN branch at :65, which then calls priorityWork.Clear() at :76) -> also blocked, so a
                //     prioritised pawn does not keep going at that work by itself.
                //   - The player's right-click "Prioritize ..." does NOT pass through either. It is built and
                //     started by FloatMenuOptionProvider_WorkGivers: :123 calls HasJobOnThing/JobOnThing(...,
                //     forced: true) directly and :178 starts it via TryTakeOrderedJobPrioritizedWork. It never
                //     consults PawnCanUseWorkGiver, and could not - that method is private (:268).
                // So a manual order still works, one job at a time. Say so: it is a real escape hatch, and in the
                // reported case it also dodges the fault outright (Smarter Construction's postfix is !forced-gated).
                HDLog.Err("the work giver '" + giver.FullName + "' threw " + count + " times while RimWorld turned "
                    + "its chosen target into a job for " + (pawn?.LabelShort ?? "a pawn")
                    + ". This is NOT a Hauler's Dream work giver; the most likely source is " + owner
                    + ". Vanilla does not guard that call, so the throw aborts the pawn's ENTIRE work selection "
                    + "every scan - colonists then do no work at all while still eating and sleeping, which looks "
                    + "like laziness rather than an error. Hauler's Dream has switched THAT work giver off for the "
                    + "rest of this session so every other kind of work keeps running. That work will no longer "
                    + "happen automatically until the mod responsible is updated or removed; a player right-click "
                    + "'Prioritize' still issues a single job (RimWorld builds that order without this check), but "
                    + "the pawn will not carry on with it. Restarting the game clears the list. Please report this "
                    + "to the mod responsible, with your log attached.\n" + HDFault.Render(ex));
            }
            catch (Exception noteFailed)
            {
                // Total-function boundary (see the summary): the disk trail is the one channel that cannot itself
                // be the thing that broke, and losing this bookkeeping must never cost the caller its decision.
                try
                {
                    HDDebugLog.Enqueue(DateTime.Now.ToString("MM-dd HH:mm:ss")
                        + " ERR [seam] work-giver quarantine bookkeeping threw " + noteFailed.GetType().Name);
                }
                catch
                {
                    // Nothing further can be reported; the containment decision above still proceeds.
                }
            }
        }

        /// <summary>
        /// Who to tell the player to report to, best evidence first: the mod whose code is actually in the stack,
        /// then the mods that patch this giver's job entry points, then the mod that ships the giver itself.
        ///
        /// <para>The last resort is "could not be identified" — NEVER a bare assembly name. A vanilla giver's
        /// assembly is <c>Assembly-CSharp</c>, and printing that reads as "report this bug to Assembly-CSharp",
        /// which is both meaningless to a player and the same false blame issue #236 removed. That branch is a
        /// structural backstop rather than a live path today: the policy only ever quarantines when at least one
        /// of the two mod-evidence facts holds, so one of the first two branches always answers.</para>
        /// </summary>
        /// <param name="giver">The quarantined giver type; never null here.</param>
        /// <param name="origin">The innermost mod-owned frame, or null.</param>
        /// <param name="patchOwners">The non-HD Harmony owners patching this giver, or null.</param>
        private static string OwnerText(Type giver, string origin, string patchOwners)
        {
            if (origin != null)
                return origin;
            if (patchOwners != null)
                return "whichever mod owns the Harmony patch(es) on it: " + patchOwners;
            return HDFault.OwningModName(giver.Assembly) ?? "a mod that could not be identified from this error";
        }

        // Whether a mod patches a giver's job entry points is stable for the session (mods patch at startup) and
        // the key space is bounded by the number of work-giver types, so it is computed once per type.
        private static readonly ConcurrentDictionary<Type, string> giverPatchOwners = new ConcurrentDictionary<Type, string>();

        // The three vanilla entry points a mod hooks to influence what job a giver produces. JobOnThing is the
        // one the reported #235 case (Smarter Construction) postfixes.
        private static readonly string[] GiverJobEntryPoints = { "JobOnThing", "JobOnCell", "HasJobOnThing" };

        /// <summary>
        /// The non-Hauler's-Dream Harmony owners patching this giver's job entry points, comma-separated, or null
        /// when no mod patches them.
        ///
        /// <para>This is the fact that makes the REPORTED bug quarantinable. Smarter Construction postfixes
        /// <c>RimWorld.WorkGiver_ConstructFinishFrames.JobOnThing</c> — a vanilla type — so asking "does this
        /// giver belong to a mod?" answers no and refuses the very case the feature exists for; asking "does a
        /// mod's code run inside this giver's job call?" answers yes. It is equally the fact that keeps HD from
        /// switching off an unpatched vanilla giver that merely choked on some mod's DATA.</para>
        ///
        /// <para>Resolution walks base types (<c>AccessTools.Method</c> does), so a mod patching the shared
        /// <c>WorkGiver_Scanner</c> declaration also counts — correctly: its code runs in this giver's call too.</para>
        /// </summary>
        /// <param name="giverType">The concrete work-giver type.</param>
        private static string ModPatchOwnersOf(Type giverType) => giverPatchOwners.GetOrAdd(giverType, ComputeModPatchOwners);

        private static string ComputeModPatchOwners(Type giverType)
        {
            try
            {
                List<string> owners = null;
                for (int i = 0; i < GiverJobEntryPoints.Length; i++)
                {
                    var method = AccessTools.Method(giverType, GiverJobEntryPoints[i]);
                    if (method == null)
                        continue;
                    var patched = Harmony.GetPatchInfo(method)?.Owners;
                    if (patched == null)
                        continue;
                    foreach (var owner in patched)
                    {
                        if (owner == null || owner == HaulersDreamMod.HarmonyId)
                            continue;
                        owners = owners ?? new List<string>();
                        if (!owners.Contains(owner))
                            owners.Add(owner);
                    }
                }
                return owners == null ? null : string.Join(", ", owners.ToArray());
            }
            catch
            {
                // Total-function boundary (see NoteSeamFault): an unanswerable probe degrades to "no mod patches
                // this giver", which can only ever make HD MORE reluctant to switch a work giver off.
                return null;
            }
        }
    }
}
