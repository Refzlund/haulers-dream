using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
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
    /// <para><b>This class only GATHERS FACTS; both decisions live in the Verse-free Core.</b> Switching a work
    /// giver off is the most destructive thing HD does to a save — get it wrong and HD reproduces #235's symptom
    /// with itself as the cause — so the rule sets sit beside the headless tests rather than in a chain of early
    /// returns inside a Harmony finalizer. There are TWO rules, with different bars, and keeping them apart is
    /// the point:
    /// <list type="bullet">
    /// <item><see cref="WorkGiverQuarantinePolicy"/> — may this work type be switched off? Satisfied by
    /// INVOLVEMENT: a mod is hooked into this giver's job call, or a mod-owned frame is in the trace.</item>
    /// <item><see cref="WorkGiverNamingPolicy"/> — may a mod be NAMED to the player for it? Satisfied only by a
    /// mod-owned frame, because hook ownership shows who CAN run inside a call and never who threw.</item>
    /// </list>
    /// So HD may switch a hooked work giver off while telling the player it cannot say who is at fault. That
    /// asymmetry is deliberate. The shipped version instead named "the mod responsible" by listing the giver's
    /// patch owners, dropping HD's own Harmony id from that list, and printing whoever remained — a process of
    /// elimination with the one suspect it could not judge removed by hand. On the method behind #235 that would
    /// have printed a third party's name even if one of HD's own two postfixes on it had thrown.</para>
    ///
    /// <para><b>Attribution, and why naming a giver is not the same as implicating a mod.</b> The preferred
    /// answer comes from the exception's own frames. That route CANNOT see a Harmony-patched giver, though: the
    /// patched method runs as a <c>DynamicMethod</c>, whose <c>DeclaringType</c> is always null — which is
    /// precisely the #235 shape, where the mod involved postfixed a VANILLA giver type
    /// (<c>RimWorld.WorkGiver_ConstructFinishFrames.JobOnThing</c>). So the fallback — the last giver vanilla
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

        // The quarantine set. The VALUE is what the player will be told, captured at quarantine time while the
        // faulting exception's frames are still available — by the time the alert draws, Harmony has re-thrown
        // the exception and reset its trace, so the evidence no longer exists to re-derive.
        private static readonly ConcurrentDictionary<Type, QuarantinedWork> quarantined =
            new ConcurrentDictionary<Type, QuarantinedWork>();

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

        /// <summary>Every switched-off work giver with the attribution captured at its fault, for the alert to
        /// word. Empty when none.</summary>
        public static IEnumerable<QuarantinedWork> Quarantined => quarantined.Values;

        /// <summary>How many work givers are switched off — the alert's label. Deliberately NOT used by
        /// <see cref="IsQuarantined"/>: <c>ConcurrentDictionary.Count</c> takes every internal lock, which is
        /// fine for a handful of entries while the alert is on screen and unacceptable in the per-giver,
        /// per-pawn, per-scan check.</summary>
        public static int QuarantinedCount => quarantined.Count;

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

                // The innermost frame belonging to a MOD other than HD (and not Harmony's shared plumbing). It
                // is the ONLY fact allowed to become a name, and one of the policy's two involvement facts, so
                // it is resolved once and used for both.
                string origin = HDFault.DescribeOrigin(ex);
                // The other involvement fact, and the one that makes the REPORTED bug quarantinable at all: the
                // mod there patched a VANILLA giver type, so nothing about the giver's own assembly implicates a
                // mod — the patch does. It carries the DECISION and never a name; see WorkGiverNamingPolicy.
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

                // Who may be named is a SECOND question with a stricter bar, asked only once the first one has
                // said "switch it off". The two involvement facts are passed and refused by name in the Core, so
                // "a patch owner may not become a name" is a property with a test rather than an omission here.
                var naming = WorkGiverNamingPolicy.Decide(
                    exceptionCarriesModOwnedFrame: origin != null,
                    aModPatchesTheGiver: patchOwners != null,
                    theGiverTypeBelongsToAMod: HDFault.OwningModName(giver.Assembly) != null);

                var work = new QuarantinedWork(DescribeWork(giver), naming,
                    naming == QuarantineNaming.NameTheMod ? origin : null);
                if (!quarantined.TryAdd(giver, work))
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
                // So a manual order still works, one job at a time. Say so: it is a real escape hatch.
                HDLog.Err("the work giver '" + giver.FullName + "' threw " + count + " times while RimWorld turned "
                    + "its chosen target into a job for " + (pawn?.LabelShort ?? "a pawn") + ". "
                    + SourceSentence(naming, origin)
                    + " Vanilla does not guard that call, so the throw aborts the pawn's ENTIRE work selection "
                    + "every scan - colonists then do no work at all while still eating and sleeping, which looks "
                    + "like laziness rather than an error. Hauler's Dream has switched THAT work giver off for the "
                    + "rest of this session so every other kind of work keeps running (the giver is not one of "
                    + "Hauler's Dream's own - it never switches those off). That work will no longer happen "
                    + "automatically until the cause is fixed or removed; a player right-click 'Prioritize' still "
                    + "issues a single job (RimWorld builds that order without this check), but the pawn will not "
                    + "carry on with it. Restarting the game clears the list.\n"
                    + HookFacts(giver) + "\n" + HDFault.Render(ex));
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
        /// What the log says about WHERE the fault came from — the log's copy of the naming rule, and the
        /// sentence that replaced "the most likely source is &lt;whoever patches this&gt;".
        ///
        /// <para>The unknown case is deliberately long. "Could not be identified" invites the reader to fill the
        /// gap in with whichever mod name is lying around (which is how #235 was attributed in the first place),
        /// so the two mechanisms that erase the evidence are stated outright, and the sentence closes on what
        /// actually helps: the full error is below, and attaching it to a report is how the source gets found.</para>
        /// </summary>
        /// <param name="naming">The verdict from <see cref="WorkGiverNamingPolicy"/>.</param>
        /// <param name="origin">The named mod and code location; non-null under
        /// <see cref="QuarantineNaming.NameTheMod"/>.</param>
        private static string SourceSentence(QuarantineNaming naming, string origin)
        {
            if (naming == QuarantineNaming.NameTheMod && !origin.NullOrEmpty())
                return "The exception's own frames put " + origin + " on the path to the throw, so that is the "
                    + "most likely source - likely, not proven: a stack records what a method called, never who "
                    + "called it.";
            return "No frame in this exception resolves to a mod's own code, so the source could not be "
                + "identified from the error itself. That is not a finding about anyone in particular: a "
                + "Harmony-patched method runs as a DynamicMethod whose frame names nothing, and any "
                + "Exception-returning finalizer deeper in the call chain has already reset the trace before it "
                + "reaches here. The full error is below - attaching it to a bug report is what allows the source "
                + "to be worked out.";
        }

        /// <summary>
        /// The work giver as a player can recognise it: <c>"&lt;def label&gt; (&lt;Type.FullName&gt;)"</c>, or
        /// the bare type name when no <c>WorkGiverDef</c> declares this type. Def labels are already localized by
        /// RimWorld and by mods, so this costs no translation keys; the type name stays for bug reports.
        /// </summary>
        /// <param name="giverType">The quarantined giver type; never null here.</param>
        private static string DescribeWork(Type giverType)
        {
            try
            {
                var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    var def = defs[i];
                    if (def != null && def.giverClass == giverType && !def.label.NullOrEmpty())
                        return def.LabelCap.ToString() + " (" + giverType.FullName + ")";
                }
            }
            catch
            {
                // Total-function boundary (see NoteSeamFault): an unreadable def database costs the label, not
                // the report — the type name below identifies the work giver either way.
            }
            return giverType.FullName;
        }

        // Whether a mod patches a giver's job entry points is stable for the session (mods patch at startup) and
        // the key space is bounded by the number of work-giver types, so it is computed once per type.
        private static readonly ConcurrentDictionary<Type, string> giverPatchOwners = new ConcurrentDictionary<Type, string>();

        // The three vanilla entry points a mod hooks to influence what job a giver produces. JobOnThing is the
        // one the mod involved in the reported #235 case postfixes.
        private static readonly string[] GiverJobEntryPoints = { "JobOnThing", "JobOnCell", "HasJobOnThing" };

        /// <summary>
        /// The non-Hauler's-Dream Harmony owners patching this giver's job entry points, comma-separated, or null
        /// when no mod patches them.
        ///
        /// <para>This is the fact that makes the REPORTED bug quarantinable. The mod involved there postfixed
        /// <c>RimWorld.WorkGiver_ConstructFinishFrames.JobOnThing</c> — a vanilla type — so asking "does this
        /// giver belong to a mod?" answers no and refuses the very case the feature exists for; asking "does a
        /// mod's code run inside this giver's job call?" answers yes. It is equally the fact that keeps HD from
        /// switching off an unpatched vanilla giver that merely choked on some mod's DATA.</para>
        ///
        /// <para><b>Excluding HD here is right for the DECISION and wrong for a NAME</b>, which is why this feeds
        /// only <see cref="WorkGiverQuarantinePolicy"/>. The policy question is "is a mod OTHER THAN US mixed into
        /// this giver?", and removing ourselves is what that question means. Presenting the remainder as the mod
        /// responsible was the #235 attribution bug; the player-facing name now comes from frame evidence alone,
        /// and <see cref="HookFacts"/> lists hooks WITH HD in them, as involvement rather than blame.</para>
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

        /// <summary>
        /// The hook facts for the maintainer's log: who is patched into this giver's job entry points and who
        /// transpiles the seam method, resolved from <c>Patch.PatchMethod</c> to real mod display names —
        /// <b>Hauler's Dream included, listed on the same terms as anyone else.</b>
        ///
        /// <para>These are facts worth having and they are NOT attribution: they establish whose code CAN run
        /// inside a call, never whose code threw, and the sentence says so. Reading such a list as a suspect
        /// list is exactly what produced #235's misattribution — the log may state a fact as a fact, but it must
        /// not let the reader mistake it for a verdict, and hiding HD's own name from the list is what turned it
        /// into one.</para>
        ///
        /// <para>Built only when a giver is actually quarantined (once per giver per session), so the reflection
        /// cost never touches the fault path.</para>
        /// </summary>
        /// <param name="giverType">The quarantined giver type; never null here.</param>
        /// <returns>A short paragraph, never null — the lists read "none" rather than going missing, so an empty
        /// answer is distinguishable from a probe that did not run.</returns>
        private static string HookFacts(Type giverType)
        {
            try
            {
                var giverHooks = new List<string>();
                for (int i = 0; i < GiverJobEntryPoints.Length; i++)
                    AddPatchOwners(AccessTools.Method(giverType, GiverJobEntryPoints[i]), false, giverHooks);

                var seamHooks = new List<string>();
                AddPatchOwners(SeamMethod, true, seamHooks);

                return "Harmony hooks on this work giver's job entry points (JobOnThing / JobOnCell / "
                    + "HasJobOnThing): " + Join(giverHooks) + ". Transpilers on "
                    + "JobGiver_Work.TryIssueJobPackage: " + Join(seamHooks) + ". Being hooked into a method is "
                    + "INVOLVEMENT, not blame - it shows whose code can run inside that call, never whose code "
                    + "threw, and Hauler's Dream is listed above on the same terms as everyone else.";
            }
            catch
            {
                // Total-function boundary (see NoteSeamFault): say the probe failed rather than let its silence
                // read as "nobody is hooked into this".
                return "The Harmony hooks on this work giver could not be read.";
            }
        }

        // The seam this class exists for. Resolved lazily and once: the only caller of NoteSeamFault is the
        // Finalizer on this very method (HarmonyPatches.Patch_JobGiver_Work_OpportunisticUnload), and the
        // transpilers on it are a hook fact the giver's own entry points cannot show - in #235 the method that
        // threw carried a foreign transpiler, two HD postfixes and an HD finalizer at once.
        private static MethodBase seamMethod;
        private static MethodBase SeamMethod
            => seamMethod = seamMethod ?? AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage");

        /// <summary>
        /// Append every Harmony patch owner of <paramref name="method"/> to <paramref name="into"/> as
        /// <c>"&lt;Mod Name&gt; (&lt;harmony id&gt;)"</c>, deduped, resolving the mod from the patch method's own
        /// assembly rather than from the id string (an id is whatever its author typed; the assembly is the mod).
        /// </summary>
        /// <param name="method">The patched method; null adds nothing.</param>
        /// <param name="transpilersOnly">True to list only transpilers — for the seam method, where a transpiler
        /// is the hook that leaves no frame of its own behind and so is the one worth naming.</param>
        /// <param name="into">The accumulating list; owners already present are not repeated.</param>
        private static void AddPatchOwners(MethodBase method, bool transpilersOnly, List<string> into)
        {
            if (method == null)
                return;
            var info = Harmony.GetPatchInfo(method);
            if (info == null)
                return;
            AddPatches(info.Transpilers, into);
            if (transpilersOnly)
                return;
            AddPatches(info.Prefixes, into);
            AddPatches(info.Postfixes, into);
            AddPatches(info.Finalizers, into);
        }

        // One patch list. The owner id is kept alongside the resolved name because a bug report is matched
        // against it, and it is all there is when the patch method's assembly belongs to no running mod.
        private static void AddPatches(IList<Patch> patches, List<string> into)
        {
            if (patches == null)
                return;
            for (int i = 0; i < patches.Count; i++)
            {
                var patch = patches[i];
                if (patch == null)
                    continue;
                string modName = HDFault.OwningModName(patch.PatchMethod?.DeclaringType?.Assembly);
                string entry = modName == null ? patch.owner : modName + " (" + patch.owner + ")";
                if (!entry.NullOrEmpty() && !into.Contains(entry))
                    into.Add(entry);
            }
        }

        // "none" rather than an empty string: a missing list reads as a missing probe, and this whole report
        // exists because an absence was once read as a finding.
        private static string Join(List<string> entries)
            => entries.Count == 0 ? "none" : string.Join(", ", entries.ToArray());
    }
}
