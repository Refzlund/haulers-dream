using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// HAUL AFTER A KILL — the colonist who killed an animal promptly hauls the carcass, covering vanilla's
    /// gap. Two independent toggles, both default ON: haul WILD kills (hunting) and haul TAMED slaughtered
    /// animals (slaughter). Both deliver the SAME honest principle via a finish action that appends a
    /// haul-to-storage job onto the killer's own job queue — but they hook DIFFERENT job drivers, and the
    /// wild path fires ONLY when vanilla's own hunt self-haul did NOT run, so the two never compete.
    ///
    ///   • TAMED (slaughter) — a <c>JobDriver_Slaughter.MakeNewToils</c> finish action, gated on
    ///     <c>Succeeded</c>, that APPENDS a haul-to-storage job on the slaughterer (see
    ///     <see cref="Patch_JobDriver_Slaughter_HaulAfter"/> + <see cref="SlaughterHaul.TryAppendHaul"/>).
    ///     Vanilla's slaughter job does NOT self-haul the corpse, so this is pure added value: the killer is
    ///     standing at its adjacent, downed victim, and a single enqueue at the finish action delivers the
    ///     fresh carcass to storage. Slaughter is same-faction only (<c>WorkGiver_Slaughter</c>), so it can
    ///     only ever kill a player-faction (tamed) animal — classified <see cref="HaulKillSource.Slaughter"/>.
    ///   • WILD (hunt) — a <c>JobDriver_Hunt.MakeNewToils</c> finish action that, ONLY when the hunt ended
    ///     NON-<c>Succeeded</c> (interrupted by a need / draft / mental break / timeout / FailOn AFTER the
    ///     killing blow), resolves the prey's corpse and APPENDS a haul-to-storage job on the hunter (see
    ///     <see cref="Patch_JobDriver_Hunt_HaulAfter"/> + <see cref="SlaughterHaul.TryAppendHuntKillHaul"/>).
    ///     Classified <see cref="HaulKillSource.Hunt"/>.
    ///
    /// WHY the wild path fires only on a NON-clean hunt (the honest scope, and how it avoids a double-haul):
    /// On a CLEAN finish (<c>JobCondition.Succeeded</c>) vanilla's hunt job ITSELF hauls the corpse to storage
    /// as its final toils (<c>StartCollectCorpseToil</c> reserves + hauls). HD must NOT touch that case — so the
    /// finish action skips when <c>cond == Succeeded</c>. It is exactly when the hunt ends NON-Succeeded
    /// (Incompletable: the hunter was pulled off the job by a need, a draft, a mental break, the 5000-tick
    /// timeout, or a FailOn AFTER the kill) that vanilla leaves the carcass behind for the slow generic haul
    /// scan — and that is the gap HD closes by appending a prompt re-haul onto the same hunter. Because the two
    /// branches are mutually exclusive (clean ⇒ vanilla self-haul; not-clean ⇒ HD re-haul), a hunt is never
    /// double-hauled.
    ///
    /// WHAT HD intentionally does NOT add (and why it doesn't need to): a hunted carcass is NEVER auto-forbidden
    /// in the first place — vanilla's <c>Pawn.Kill</c> un-forbids any hunt-designated / hunter-killed carcass
    /// (<c>WasKilledByHunter ⇒ Reserve</c>; otherwise <c>SetForbiddenIfOutsideHomeArea</c> is skipped for a
    /// slaughter-designated body, and a hunt designation is removed only on Destroy). So a bled-out-while-fled
    /// death or a kill with no reachable storage simply sits in the haulables lister and is collected by
    /// vanilla's own corpse hauling whenever accepting storage exists — HD adds nothing there, because there is
    /// nothing to fix. HD's added value is purely the PROMPT re-haul by the interrupted hunter, where vanilla
    /// would otherwise leave the body to the generic queue.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_Slaughter), "MakeNewToils")]
    public static class Patch_JobDriver_Slaughter_HaulAfter
    {
        static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_Slaughter __instance)
        {
            // EAGER (no yield): the finish action must register when MakeNewToils is called, not lazily on
            // first enumeration. No try/catch — a follow-up failure is a real bug surfaced as a red error.
            __instance.AddFinishAction(cond =>
            {
                if (cond == JobCondition.Succeeded)
                    SlaughterHaul.TryAppendHaul(__instance.pawn, __instance.job?.targetA.Thing,
                        HaulKillSource.Slaughter);
            });
            return toils; // toils themselves are untouched — vanilla's slaughter runs exactly as shipped
        }
    }

    /// <summary>The WILD (hunt) side of the family — classified <see cref="HaulKillSource.Hunt"/>.
    ///
    /// HOOK = a <c>JobDriver_Hunt.MakeNewToils</c> postfix that registers a finish action. The finish action
    /// fires for EVERY way the hunt ends, but only does work when the hunt ended NON-<c>Succeeded</c> — i.e.
    /// the hunter was interrupted AFTER the killing blow, exactly when vanilla's own corpse self-haul
    /// (<c>StartCollectCorpseToil</c>, which runs only on a clean finish) did NOT carry the body home. In that
    /// case <see cref="SlaughterHaul.TryAppendHuntKillHaul"/> resolves the prey's corpse and appends a prompt
    /// haul-to-storage job onto the hunter's queue. On a clean (<c>Succeeded</c>) hunt the finish action is a
    /// no-op, so HD never competes with — or duplicates — vanilla's self-haul.</summary>
    [HarmonyPatch(typeof(JobDriver_Hunt), "MakeNewToils")]
    public static class Patch_JobDriver_Hunt_HaulAfter
    {
        static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_Hunt __instance)
        {
            // EAGER (no yield), matching the slaughter patch: register at MakeNewToils call time. No try/catch
            // — a follow-up failure surfaces as a red error (HD never suppresses).
            __instance.AddFinishAction(cond =>
            {
                // Only when the hunt did NOT finish cleanly: a clean hunt self-hauls the corpse (vanilla),
                // so HD must stay out of that path entirely to avoid a double-haul.
                if (cond != JobCondition.Succeeded)
                    SlaughterHaul.TryAppendHuntKillHaul(__instance);
            });
            return toils; // vanilla's hunt runs exactly as shipped
        }
    }

    public static class SlaughterHaul
    {
        /// <summary>The hunt path's finish-action body: an interrupted-after-kill hunt left the carcass behind
        /// for the slow generic haul scan, so append a PROMPT haul-to-storage job onto the HUNTER's queue. Only
        /// called on a NON-clean hunt finish (a clean hunt self-hauls — see the patch), so this never competes
        /// with vanilla's own corpse haul. Resolves the prey's corpse from the hunt job's <c>TargetIndex.A</c>
        /// (decompile-verified <c>JobDriver_Hunt</c>: A is the live prey Pawn for almost the whole job and is
        /// re-pointed to the Corpse via <c>job.SetTarget(A, corpse)</c> ONLY inside <c>StartCollectCorpseToil</c>'s
        /// clean-finish/storage-found branch — the very path that ends <c>Succeeded</c> and self-hauls. On the
        /// NON-<c>Succeeded</c> finishes this method handles, A is therefore still the prey <c>Pawn</c>, and
        /// <see cref="ResolveCorpse"/> reads that Pawn's <c>.Corpse</c>) and routes it through the shared
        /// <see cref="TryAppendHaul"/> body — which gates on the wild toggle, animal-only scope, player faction
        /// / non-lodger, non-home-map policy, hauling eligibility, forbidden state, and storage existence (null
        /// job = no reachable storage ⇒ leave the carcass exactly as vanilla). If the prey is still alive /
        /// downed (no corpse yet) or not spawned, this resolves to no corpse and quietly does nothing. Safe to
        /// call speculatively — it gates itself. No try/catch (HD never suppresses).</summary>
        internal static void TryAppendHuntKillHaul(JobDriver_Hunt driver)
        {
            if (driver == null)
                return;
            var hunter = driver.pawn;
            // Resolve the prey corpse from the hunt job's TargetIndex.A. Decompile-verified: JobDriver_Hunt only
            // re-points A to the Corpse (job.SetTarget(A, corpse)) inside StartCollectCorpseToil's clean-finish
            // branch — which ends Succeeded and is the path we deliberately skip. On the NON-Succeeded finishes we
            // handle here, A is STILL the prey Pawn: if it died (kill landed, then interrupted) ResolveCorpse reads
            // its .Corpse; if it's still alive/downed (interrupted before the kill) .Corpse is null ⇒ ResolveCorpse
            // returns null ⇒ TryAppendHaul no-ops. (A direct Corpse in A is handled too, defensively.)
            var targetA = driver.job?.targetA.Thing;
            TryAppendHaul(hunter, targetA, HaulKillSource.Hunt);
        }

        /// <summary>Append a haul-to-storage job for the fresh carcass at the END of <paramref name="killer"/>'s
        /// job queue, if this finished kill qualifies under the settings AND a reachable storage destination
        /// exists. A null job from <c>HaulToStorageJob</c> (no reachable, accepting destination) ⇒ leave the
        /// corpse exactly as vanilla. Shared by the SLAUGHTER finish action (the killer stands at its adjacent,
        /// downed victim) and the HUNT finish action (the interrupted hunter, via
        /// <see cref="TryAppendHuntKillHaul"/>). Safe to call speculatively — it gates itself. No try/catch
        /// anywhere (HD never suppresses; a real bug surfaces as a red error, propagated to RimWorld's
        /// handler).</summary>
        internal static bool TryAppendHaul(Pawn killer, Thing victimThing, HaulKillSource kind)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return false;
            // Toggle gate (Core): the two toggles are independent — Hunt reads only haulWildKills, Slaughter
            // only haulTamedSlaughter. Checked first so a disabled kind costs nothing.
            if (!HaulAfterKillPolicy.ShouldHaul(kind, s.haulWildKills, s.haulTamedSlaughter))
                return false;
            // victimThing is the hunt/slaughter job's targetA. On the paths that reach here it is the victim
            // Pawn (slaughter; or a hunt interrupted NON-Succeeded, where JobDriver_Hunt has NOT re-pointed A to
            // the Corpse — that SetTarget happens only in the clean-finish branch this code skips). ResolveCorpse
            // reads the dead Pawn's .Corpse, returning null for a still-live/downed victim ⇒ leave as vanilla. The
            // direct-Corpse case is also accepted defensively (e.g. a future caller passing a body directly).
            var corpse = ResolveCorpse(victimThing);
            if (corpse == null || !corpse.Spawned || corpse.Map == null)
                return false;
            // Animal-only scope: excludes slaughtered/hunted humanlikes, mechs, insects.
            var inner = corpse.InnerPawn;
            if (inner?.RaceProps == null || !inner.RaceProps.Animal)
                return false;
            // The killer must have a jobQueue and be on the corpse's map. For both callers the killer is at (or
            // near) the body; a different-map killer (mod-teleported) is left to vanilla.
            if (killer?.jobs?.jobQueue == null || killer.Map != corpse.Map)
                return false;
            // Player pawns only (quest lodgers excluded — their queued work walks off-map with the quest).
            if (killer.Faction != Faction.OfPlayerSilentFail || killer.IsQuestLodger())
                return false;
            // On a map the mod is configured to leave alone, don't haul.
            if (!s.enableOnNonHomeMaps && !corpse.Map.IsPlayerHome)
                return false;
            // Race + DRAFTED (via pauseWhileDrafted) + incapable-of-hauling + mechanoid allowance — drafted /
            // incapable pawns never auto-act.
            if (!YieldRouter.IsEligible(killer))
                return false;
            // Respect a player-forbidden corpse.
            if (corpse.IsForbidden(killer))
                return false;
            // Idempotency: a faster hauler beat us ⇒ no-op.
            if (corpse.IsInValidStorage())
                return false;
            // Build the job: null ⇒ no reachable, accepting storage ⇒ leave the corpse exactly as vanilla.
            // forced: false for automatic behavior.
            var job = HaulAIUtility.HaulToStorageJob(killer, corpse, forced: false);
            if (job == null)
                return false;
            // Dedupe by targetA: if a haul for this corpse is already queued (or this pawn is already hauling
            // it), don't queue another.
            if (killer.CurJob?.def == JobDefOf.HaulToCell && killer.CurJob.targetA.Thing == corpse)
                return false;
            var queue = killer.jobs.jobQueue;
            for (int i = 0; i < queue.Count; i++)
                if (queue[i]?.job?.targetA.Thing == corpse)
                    return false;
            // Enqueue unreserved at the END (mirror the re-strip net): reserve at job start, so it never
            // blocks other haulers. If reserved/unreachable by dequeue, it ends quietly — vanilla's own corpse
            // hauler then recovers it on a later tick (bounded best-effort, never loss, never crash).
            queue.EnqueueLast(job, JobTag.Misc);
            HDLog.Dbg($"{killer} queued haul-after-{kind} for {corpse}.");
            return true;
        }

        /// <summary>A Corpse passed directly (defensive — e.g. a clean-finish hunt where A was re-pointed to the
        /// body, or a future direct-Corpse caller), or the live victim Pawn's <c>.Corpse</c> (slaughter, or an
        /// interrupted hunt whose prey has since died). A still-live, corpse-less victim ⇒ null.</summary>
        private static Corpse ResolveCorpse(Thing victimThing)
        {
            switch (victimThing)
            {
                case Corpse c: return c;
                case Pawn p:   return p.Corpse;
                default:       return null;
            }
        }
    }
}
