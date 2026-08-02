using System;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// AUTO STRIP ON HAUL — when a pawn picks up a corpse to haul it (to a stockpile, a grave, or as a
    /// bill ingredient for cremation/butchering), it first strips the body and SCOOPS the loot into its
    /// inventory, then carries the corpse in its hands. One trip moves the body AND brings the gear home —
    /// the same shape as the harvest scoop, and the tagged loot flows through the existing unload pass,
    /// shared inventories, and CE integration for free. No more manual strip orders after every battle.
    ///
    /// HOOK: a prefix on <see cref="Pawn_CarryTracker.TryStartCarry(Thing, int, bool)"/> — the single
    /// choke point where a hauler physically takes the corpse into its hands (vanilla's StartCarryThing
    /// toil calls it after the walk, so the pawn is standing at the body). One patch covers stockpile
    /// hauls (HaulToCell), grave/casket interment (HaulToContainer) and corpse bills (DoBill) alike.
    ///
    /// Vanilla-faithful stripping, built from the decompiled originals (NOT ported from any mod):
    /// per-piece drops via the same <c>TryDropEquipment</c>/<c>apparel.TryDrop</c>/<c>TryDrop</c> calls
    /// <c>Pawn.Strip</c> makes, the Strip designation is cleared, <c>BodiesStripped</c> is recorded, and
    /// the dead pawn's faction gets the same <c>Notify_MemberStripped</c> call a manual strip makes
    /// (decompile-verified to no-op for the DEAD — corpse stripping carries no relations hit in vanilla;
    /// the call is kept for exact behavioral parity should that ever change).
    ///
    /// Tainted apparel follows the player's per-category policy (see <see cref="TaintedApparelPolicy"/>);
    /// LeaveOnCorpse pieces are simply not stripped, so they travel with the body (a cremated corpse takes
    /// them along — clean disposal). The DESTROY policy is the one deliberate exception to this mod's
    /// never-delete rule: an explicit opt-in, applied only to tainted apparel of the configured category.
    ///
    /// Loot that doesn't fit the carry/CE limits, or that no storage would accept, stays on the ground as
    /// ordinary haulables (the bulk-haul sweep picks it up later) — nothing is ever lost by stripping.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry),
        typeof(Thing), typeof(int), typeof(bool))]
    public static class Patch_TryStartCarry_AutoStrip
    {
        static void Prefix(Pawn_CarryTracker __instance, Thing item)
        {
            // An auto-strip failure is a real bug: it must stay a visible red error, never a silent downgrade. The
            // Finalizer below preserves that (logs with HD + the hauler, then RETHROWS — see HDGuard). This matters
            // most for MODDED corpses, whose unusual equipment/apparel can throw inside MaybeStripForHaul and
            // would otherwise abort the triggering haul job with an anonymous stack (fix/mix #3b, verified H3).
            if (item is Corpse corpse)
                CorpseStripper.MaybeStripForHaul(__instance.pawn, corpse);
        }

        // Seam guard: log + rethrow so an auto-strip throw names the hauler instead of an opaque TryStartCarry stack.
        static System.Exception Finalizer(System.Exception __exception, Pawn_CarryTracker __instance)
            => HDGuard.SeamThrew(__exception, "Pawn_CarryTracker.TryStartCarry (HD auto-strip-on-haul)", __instance?.pawn,
                "the haul that triggered the auto-strip failed.");
    }

    /// <summary>
    /// HAUL AFTER STRIPPING (living targets) — the strip ORDER side of the family. The removed gear is
    /// already scooped into the stripper's inventory by the yield hook (JobDriver_Strip is a recognized
    /// producer in <see cref="YieldRouter"/>: drops land at the target's cell, the self-pickup job is
    /// queued at the FRONT, so the stripper sweeps the pile the moment the strip ends — fewer trips, the
    /// tagged loot rides the normal unload pass). This patch adds the one thing the scoop can't: a
    /// RE-STRIP safety net. A living target (a prisoner) re-equips clothing left lying nearby, so when a
    /// strip of a living pawn completes, a fresh vanilla Strip job is appended at the END of the
    /// stripper's job queue — by the time it comes up (after the scoop and any unload trip), it catches
    /// anything the target put back on, and ends instantly doing nothing when the target stayed bare
    /// (its own toils fail on CanBeStrippedByColony). BEST-EFFORT, not a guarantee: the queued job is
    /// deliberately unreserved, so if the target is reserved when it dequeues (a warden feeding the
    /// prisoner) it ends silently and is not retried — a fresh strip order recovers.
    ///
    /// WHY a FINISH ACTION and not an appended toil: vanilla JobDriver_Strip registers a driver-global
    /// FailOn(!CanBeStrippedByColony(TargetThingA)), and global fail conditions are evaluated BEFORE a
    /// toil's initAction runs — after a successful strip the target is bare, so the condition fires
    /// first and an appended toil could never run (worse, it would flip every strip job from Succeeded
    /// to Incompletable, suppressing vanilla's "Stripped" completion tale). A finish action runs on job
    /// end regardless of toil flow; gating on JobCondition.Succeeded keeps the re-strip to genuinely
    /// completed strips. The postfix is deliberately EAGER (no yield): the finish action must register
    /// when MakeNewToils is called, not lazily on first enumeration of the toils.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_Strip), "MakeNewToils")]
    public static class Patch_JobDriver_Strip_HaulAfter
    {
        static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_Strip __instance)
        {
            // No try/catch: a follow-up failure is a real bug, surfaced as a red error rather than swallowed.
            __instance.AddFinishAction(cond =>
            {
                // Tainted pieces this strip dropped follow the player's per-category policy,
                // like the auto-strip. Applied HERE (job end, any condition — drops recorded
                // before an interruption are already on the ground) and not in the GenPlace
                // hook: vanilla's TryDrop chain un-forbids its result AFTER placement, so a
                // forbid set inside the hook would be silently reverted. Corpse strips only —
                // see the method doc for why the target matters.
                CorpseStripper.ApplyTaintedPolicyToPending(__instance.pawn,
                    corpseStrip: __instance.job?.targetA.Thing is Corpse);
                if (cond == JobCondition.Succeeded)
                    CorpseStripper.QueueReStripIfNeeded(__instance.pawn, __instance.job?.targetA.Thing);
            });
            return toils; // toils themselves are untouched — vanilla's strip runs exactly as shipped
        }
    }

    /// <summary>
    /// LEAVE-ON-CORPSE FOR MANUAL STRIP ORDERS (#211): vanilla's <c>Pawn.Strip</c> (called from
    /// <c>JobDriver_Strip</c> → <c>Corpse.Strip</c> → <c>InnerPawn.Strip</c>) calls
    /// <c>apparel.DropAll(pos, forbid, dropLocked)</c> which strips EVERYTHING — including tainted pieces the
    /// player's per-category policy says to leave on the body. The auto-strip path
    /// (<see cref="CorpseStripper.StripAndScoop"/>) checks the policy per-piece BEFORE dropping and correctly
    /// skips LeaveOnCorpse, but a player-ordered Strip goes through vanilla's code, which has no per-piece
    /// filter. The drops land on the ground and HD's <see cref="CorpseStripper.ApplyTaintedPolicyToPending"/>
    /// runs after the fact — it can forbid them in place but can't put them back on the body, so the user sees
    /// "drop and forbid" behavior instead of the expected "stays on the corpse."
    ///
    /// This prefix injects a <c>selector</c> into <c>DropAll</c> that excludes LeaveOnCorpse pieces when the
    /// pawn is dead, so they are never dropped and stay on the body. Narrow by design: fires only when
    /// <c>pawn.Dead</c> (inside a corpse) and HD's settings have at least one LeaveOnCorpse policy; for living
    /// pawns or when no leave policy is set, the original selector (typically null = strip all) is unchanged.
    ///
    /// <para>HALF OF A PAIR. Refusing to drop a piece is only correct if the game also stops CLAIMING that piece
    /// is strippable — otherwise a strip order is accepted, deletes its own designation and removes nothing, over
    /// and over. <see cref="Patch_CanBeStrippedByColony_LeaveOnCorpse"/> is the other half; both consult the same
    /// <see cref="CorpseStripper.StaysOnCorpse"/> rule behind the same
    /// <see cref="StripPolicy.LeavesAnyTainted"/> pre-gate, so they cannot come apart.</para>
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.DropAll))]
    public static class Patch_DropAll_LeaveOnCorpse
    {
        static void Prefix(Pawn_ApparelTracker __instance, ref Predicate<Apparel> selector)
        {
            var pawn = __instance.pawn;
            if (pawn == null || !pawn.Dead)
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || !StripPolicy.LeavesAnyTainted(s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy))
                return;

            var original = selector;
            selector = ap => !CorpseStripper.StaysOnCorpse(ap, s) && (original == null || original(ap));
        }
    }

    /// <summary>
    /// KEEP VANILLA'S VIEW OF "IS THERE ANYTHING TO STRIP?" HONEST (the manual-Strip no-op bug). A POSTFIX on
    /// <c>StrippableUtility.CanBeStrippedByColony(Thing)</c>, the one gate every strip entry point funnels
    /// through, narrowing it to false for a CORPSE whose every remaining piece
    /// <see cref="Patch_DropAll_LeaveOnCorpse"/> would refuse to drop.
    ///
    /// <para>WHY: vanilla's <c>Pawn.AnythingToStrip()</c> counts unlocked worn apparel and cannot see HD's
    /// selector (decompile-verified). So with a tainted policy set to "leave on corpse", a body wearing only
    /// such pieces still reported strippable — the player could designate it, the pawn walked over, and
    /// <c>JobDriver_Strip</c>'s last toil ran its three steps IN THIS ORDER (decompile-verified): delete the
    /// Strip designation, call <c>Strip()</c>, increment <c>BodiesStripped</c>. The designation went away, the
    /// selector dropped nothing, and the record counted a strip that never happened. Re-designating did the same
    /// thing forever: permanent, silent and repeatable. It only bit a player who changed a tainted policy to
    /// "leave on corpse", which is why it read as "sometimes".</para>
    ///
    /// <para>ONE postfix fixes every symptom because all four consumers read this gate (each verified against the
    /// decompiled 1.6 source): <c>Designator_Strip.CanDesignateThing</c> (so the order can no longer be placed),
    /// <c>WorkGiver_Strip.HasJobOnThing</c> (so an already-placed designation hands out no job),
    /// <c>JobDriver_Strip</c>'s driver-global <c>FailOn</c> (so an in-flight job fails BEFORE the toil that
    /// deletes the designation — global fail conditions are evaluated before a toil's initAction), and HD's own
    /// <see cref="CorpseStripper.MaybeStripForHaul"/> pre-check (which would have found nothing to strip anyway).</para>
    ///
    /// <para>NARROW BY CONSTRUCTION, in the order the checks run: a false result is left alone; a non-Corpse is
    /// left alone (HD's DropAll prefix is dead-gated on <c>pawn.Dead</c>, so living prisoners and downed raiders
    /// keep vanilla's answer exactly); and the whole thing short-circuits unless a tainted policy is set to a
    /// keep-out-of-storage value — the SAME <see cref="StripPolicy.LeavesAnyTainted"/> pre-gate the DropAll
    /// prefix uses, so the two are active together or not at all. At the Take/Take defaults it is byte-identical
    /// to vanilla.</para>
    /// </summary>
    [HarmonyPatch(typeof(StrippableUtility), nameof(StrippableUtility.CanBeStrippedByColony))]
    public static class Patch_CanBeStrippedByColony_LeaveOnCorpse
    {
        static void Postfix(Thing th, ref bool __result)
        {
            if (!__result || !(th is Corpse corpse))
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || !StripPolicy.LeavesAnyTainted(s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy))
                return;
            if (!CorpseStripper.AnythingHdWouldStrip(corpse, s))
                __result = false;
        }
    }

    /// <summary>
    /// STRIP BEFORE CREMATION, vanilla (single-rep) seam (#222). A POSTFIX on the private
    /// <c>Toils_Recipe.CalculateIngredients(Job, Pawn)</c>, which vanilla runs to resolve a bill's ingredient
    /// Things immediately BEFORE it consumes/destroys them (its own body already runs the symmetric
    /// autoStripCorpses strip at this exact point). Each resolved ingredient is offered to
    /// <see cref="CorpseStripper.MaybeStripForCremation"/>, which strips a cremation-bound corpse onto the bill
    /// tile so its gear is salvaged instead of burned; that method does all the gating (opt-in, non-autoStrip
    /// recipes only), so a butcher bill (autoStripCorpses on) is never double-stripped. The HD batch-craft
    /// driver runs its OWN replica of this seam and calls MaybeStripForCremation directly (see
    /// JobDriver_BatchCraft); a batch job never invokes vanilla CalculateIngredients, so the two paths are
    /// disjoint and no corpse is offered twice.
    ///
    /// Gated by <see cref="Prepare"/> so a RimWorld rename of the private method disables the whole patch (the
    /// feature quietly no-ops) instead of crashing at startup, the same soft-guard idiom as
    /// <c>Patch_ITab_Pawn_Gear_KeepButton</c>.
    /// </summary>
    [HarmonyPatch(typeof(Toils_Recipe), "CalculateIngredients")]
    public static class Patch_Toils_Recipe_CalculateIngredients_CremationStrip
    {
        static bool Prepare() => AccessTools.Method(typeof(Toils_Recipe), "CalculateIngredients") != null;

        static void Postfix(List<Thing> __result, Job job, Pawn actor)
        {
            if (__result == null || job == null)
                return;
            // job.RecipeDef == job.bill?.recipe (null when there is no bill); MaybeStripForCremation guards it.
            var recipe = job.RecipeDef;
            for (int i = 0; i < __result.Count; i++)
                CorpseStripper.MaybeStripForCremation(actor, __result[i], recipe);
        }
    }

    public static class CorpseStripper
    {
        /// <summary>
        /// SHARED INTAKE GUARD (issue #187a): true when <paramref name="t"/> is a LOOSE tainted-apparel piece the
        /// player's keep-policy says HD must NOT haul to storage — the resolved <see cref="StripPolicy.ApparelAction"/>
        /// is LeaveOnCorpse or DropAndForbid. The auto-strip loop already keeps such pieces off the haul at strip
        /// time, but once a piece is OFF the body and loose on the ground (a manual Strip order, a bench/rot drop
        /// when the corpse is destroyed) it becomes an unforbidden haulable that HD's grab paths would re-pocket
        /// with no policy awareness — the reported bug. Every HD intake gate (en-route pickup, work-spot sweep,
        /// bulk-haul pool + its cheap potential-work probe) calls this so a keep-on-corpse rag is skipped wherever
        /// it lands, trigger-agnostically.
        ///
        /// "Tainted" is the game's own definition, IDENTICAL to <see cref="StripAndScoop"/>'s per-piece test:
        /// WornByCorpse AND the apparel kind cares (careIfWornByCorpse). WornByCorpse is STICKY (it persists after
        /// the piece leaves the body — see <see cref="ApplyTaintedPolicyToPending"/>), so a loose stripped piece
        /// still reads tainted here. Inert (false) for non-apparel, untainted apparel, and the Take/Destroy
        /// resolutions, so ordinary apparel hauling is byte-identical for the Take/Smelt defaults.
        /// </summary>
        /// <param name="t">The candidate haulable an intake gate is about to pocket.</param>
        /// <param name="s">Live settings (the two tainted policies); a null settings reads as "leave nothing".</param>
        internal static bool ShouldLeaveTaintedApparel(Thing t, HaulersDreamSettings s)
        {
            // Cheapest reject first: with neither category set to a keep-out-of-storage policy no piece is ever
            // left, so the default config never pays the type-check or the per-piece reads below.
            if (s == null || !StripPolicy.LeavesAnyTainted(s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy))
                return false;
            if (!(t is Apparel ap) || ap.def.apparel == null)
                return false;
            if (!ap.WornByCorpse || !ap.def.apparel.careIfWornByCorpse)
                return false;
            return StripPolicy.LeaveWhereItIs(tainted: true, ap.Smeltable,
                s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy);
        }

        /// <summary>
        /// Would HD refuse to take <paramref name="ap"/> OFF the body it is worn on? The ONE per-piece rule behind
        /// both halves of the leave-on-corpse feature: <see cref="Patch_DropAll_LeaveOnCorpse"/>'s injected drop
        /// filter rejects a piece iff this is true, and <see cref="AnythingHdWouldStrip"/> discounts it for the
        /// same reason. Sharing one call is what keeps vanilla's "anything to strip?" answer and HD's actual drop
        /// behaviour from drifting apart.
        ///
        /// <para>"Tainted" is the game's own definition, identical to <see cref="StripCorpseDroppingLoot"/>'s
        /// per-piece test: <c>WornByCorpse</c> AND the apparel kind cares (<c>careIfWornByCorpse</c>). Untainted
        /// apparel is never left. The caller is expected to have run the cheap
        /// <see cref="StripPolicy.LeavesAnyTainted"/> pre-gate; this method does the per-piece work
        /// unconditionally.</para>
        /// </summary>
        /// <param name="ap">One worn piece; a null <c>def.apparel</c> reads as untainted (never left).</param>
        /// <param name="s">Live settings — the two tainted-apparel policies.</param>
        internal static bool StaysOnCorpse(Apparel ap, HaulersDreamSettings s)
        {
            if (ap == null || s == null)
                return false;
            bool tainted = ap.WornByCorpse && ap.def.apparel != null && ap.def.apparel.careIfWornByCorpse;
            // Thing.Smeltable, not def.smeltable: the instance check also excludes relics and non-smeltable
            // stuff, matching what a smelter would actually accept (same call StripCorpseDroppingLoot makes).
            return StripPolicy.StaysOnCorpse(tainted, ap.Smeltable,
                s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy);
        }

        /// <summary>
        /// Is there anything on <paramref name="corpse"/> that a strip — by hand or automatic — would actually
        /// remove, once HD's leave-on-corpse rule is applied? A faithful mirror of vanilla
        /// <c>Pawn.AnythingToStrip()</c> (decompile-verified), with the one apparel clause narrowed by
        /// <see cref="StaysOnCorpse"/>: equipment counts, a non-empty inventory counts, and worn apparel counts
        /// only when the strip would take it off.
        ///
        /// <para>The locked-apparel split is vanilla's, not ours: <c>Pawn.Strip</c> calls
        /// <c>apparel.DropAll(pos, forbid: false, dropLocked: pawn.Destroyed)</c>, so a bonded/biocoded/royal-locked
        /// piece comes off only once the inner pawn is destroyed — which is exactly why vanilla's own probe reads
        /// <c>AnyApparel</c> in that case and <c>AnyApparelUnlocked</c> otherwise. Mirroring it here keeps the
        /// answer honest for both.</para>
        ///
        /// <para>Only meaningful for the caller that already knows a tainted policy leaves something on the body
        /// (see <see cref="Patch_CanBeStrippedByColony_LeaveOnCorpse"/>); with the Take/Take defaults it returns
        /// exactly what vanilla's probe returned, since no piece is ever refused.</para>
        /// </summary>
        /// <param name="corpse">The body to test; its <c>InnerPawn</c> supplies the trackers.</param>
        /// <param name="s">Live settings — the two tainted-apparel policies.</param>
        /// <returns>False only when the body is, for HD's purposes, already stripped bare.</returns>
        internal static bool AnythingHdWouldStrip(Corpse corpse, HaulersDreamSettings s)
        {
            var inner = corpse?.InnerPawn;
            if (inner == null)
                return false;
            // Vanilla's own first clause. Redundant for the postfix caller (CanBeStrippedByColony already ran
            // AnythingToStrip, which checks it), kept so this reads as a complete mirror on its own.
            if (!inner.kindDef.canStrip)
                return false;
            if (inner.equipment != null && inner.equipment.HasAnything())
                return true;
            if (inner.inventory != null && inner.inventory.innerContainer.Count > 0)
                return true;
            var apparel = inner.apparel;
            if (apparel == null)
                return false;
            bool dropLocked = inner.Destroyed;
            var worn = apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                var ap = worn[i];
                if (!dropLocked && apparel.IsLocked(ap))
                    continue; // stays on the body whatever the policy says — vanilla wouldn't drop it either
                if (!StaysOnCorpse(ap, s))
                    return true;
            }
            return false;
        }

        /// <summary>After stripping a LIVING pawn, append a vanilla Strip job at the end of the stripper's
        /// queue so re-equipped clothing (a prisoner dressing from the leftovers) gets stripped again.</summary>
        internal static void QueueReStripIfNeeded(Pawn stripper, Thing target)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.IsTypeEnabled(HaulSourceType.Strip))
                return;
            // Living pawns only: a corpse can't re-dress, and the gear is already scooped/queued.
            if (!(target is Pawn victim) || victim.Dead || !victim.Spawned)
                return;
            // Player pawns only — a mod-driven non-player pawn completing a Strip job shouldn't get
            // follow-up work queued by us.
            if (stripper?.jobs == null || stripper.Map != victim.Map
                || stripper.Faction != Faction.OfPlayerSilentFail)
                return;
            // Deliberately NOT pre-reserved: reserving the victim now would block wardens (feeding,
            // tending) until the re-strip comes up. Reservations are made when the job actually starts,
            // like any normal queued job; if the target is bare or gone by then, it ends instantly.
            // KNOWN BEST-EFFORT GAP: if someone else holds the victim's reservation at that moment, the
            // queued job ends silently (fromQueue ends are quiet) and nothing retries — accepted, since
            // any retry scheme needs persistent watcher state; a fresh strip order recovers.
            var queue = stripper.jobs.jobQueue;
            if (queue == null)
                return;
            for (int i = 0; i < queue.Count; i++)
                if (queue[i]?.job?.def == JobDefOf.Strip && queue[i].job.targetA.Thing == victim)
                    return; // a re-strip on this target is already queued
            queue.EnqueueLast(JobMaker.MakeJob(JobDefOf.Strip, victim), JobTag.Misc);
        }

        /// <summary>
        /// Apply the tainted-apparel policies to the pieces a manual strip ORDER on a CORPSE just
        /// dropped (recorded in the stripper's pending-scoop list by the yield hook). The auto-strip
        /// applies the policies inline; the order path drops via vanilla Strip, so they run here — at
        /// job end, after the drops have left vanilla's TryDrop chain and before the queued
        /// self-pickup scoops. Gated to corpse targets because WornByCorpse is STICKY: a living
        /// prisoner re-dressed from corpse leftovers wears "tainted" pieces, and applying Destroy or
        /// LeaveOnCorpse to a LIVING target's strip would destroy gear outside the policies'
        /// documented corpse scope (or churn with the re-strip net) — living strips scoop everything
        /// as before. A mixed pending list (harvest yields and the like) is untouched by the apparel
        /// filter. LeaveOnCorpse can't put a piece already off the body back ON it, so it degrades to
        /// DropAndForbid: the rag is forbidden in place (not scooped, and no vanilla hauler takes it either)
        /// — honoring the keep-out-of-storage intent instead of leaving it a free ground haulable (#187a).
        /// With <see cref="Patch_DropAll_LeaveOnCorpse"/> now preventing LeaveOnCorpse pieces from being
        /// stripped in the first place, this degradation is a SAFETY NET for edge cases (a mod that calls
        /// DropAll directly, or a future RimWorld code path that bypasses the prefix); the normal manual-strip
        /// path never reaches it for LeaveOnCorpse pieces.
        /// </summary>
        internal static void ApplyTaintedPolicyToPending(Pawn stripper, bool corpseStrip)
        {
            if (!corpseStrip)
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || stripper == null)
                return;
            var pending = stripper.GetComp<CompHauledToInventory>()?.pendingSelfPickups;
            if (pending == null || pending.Count == 0)
                return;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (!(pending[i] is Apparel ap) || ap.Destroyed || !ap.Spawned
                    || !ap.WornByCorpse || ap.def.apparel == null || !ap.def.apparel.careIfWornByCorpse)
                    continue;
                var action = StripPolicy.ApparelAction(tainted: true, ap.Smeltable,
                    s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy);
                switch (action)
                {
                    case TaintedApparelPolicy.LeaveOnCorpse:
                        // Already off the body — LeaveOnCorpse can't put it back on, so degrade to DropAndForbid:
                        // forbid it in place so NO pawn (HD or vanilla) hauls the rag home. (The intake gates'
                        // ShouldLeaveTaintedApparel also skips it, but the forbid is what stops vanilla haulers.)
                        ap.SetForbidden(true, warnOnFail: false);
                        pending.RemoveAt(i); // don't scoop it
                        SelfPickupClaims.Release(ap, stripper);
                        break;
                    case TaintedApparelPolicy.DropAndForbid:
                        ap.SetForbidden(true, warnOnFail: false);
                        pending.RemoveAt(i);
                        SelfPickupClaims.Release(ap, stripper);
                        break;
                    case TaintedApparelPolicy.Destroy:
                        // Same guards as the auto-strip: never quest/relic; never a merged stack
                        // (modded stackables — vanilla apparel is stackLimit 1). Guarded cases scoop
                        // normally (Take).
                        if (!ap.questTags.NullOrEmpty() || ap.IsRelic() || ap.stackCount != 1)
                            break;
                        pending.RemoveAt(i);
                        SelfPickupClaims.Release(ap, stripper);
                        ap.Destroy(DestroyMode.Vanish);
                        break;
                }
            }
        }

        /// <summary>Strip <paramref name="corpse"/> if this pickup qualifies under the settings. Loot is
        /// scooped into <paramref name="hauler"/>'s inventory (tagged) where it fits and has somewhere to
        /// go; the rest stays on the ground as normal haulables. Safe to call speculatively — it gates
        /// itself.</summary>
        internal static void MaybeStripForHaul(Pawn hauler, Corpse corpse)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || s.autoStripMode == AutoStripMode.Off)
                return;
            if (hauler == null || corpse == null || !corpse.Spawned || hauler.Map == null || corpse.Map != hauler.Map)
                return;
            // On a map the mod is configured to leave alone, don't strip: the unload side honors the same
            // setting, so the scooped loot would just strand in the hauler's inventory until it gets home.
            if (!MapGate.HdActiveOnMap(corpse.Map))
                return;
            // Quest lodgers excluded like the bulk-haul sweep: their pockets leave with the quest,
            // so scooped loot could walk off-map before the unload pass runs.
            if (hauler.Faction != Faction.OfPlayerSilentFail || hauler.IsQuestLodger())
                return;
            // Per-pawn auto-haul opt-out: a pawn with "Auto-haul yields" OFF must not auto-strip + pocket loot —
            // that is AUTONOMOUS auto-hauling (a corpse hauled to a stockpile/grave/bill stripped along the way),
            // exactly what the toggle is supposed to stop (the toggle's own contract). Only this autonomous
            // on-haul strip is gated here; an EXPLICIT player Strip order runs through JobDriver_Strip, whose
            // dropped gear is scooped by YieldRouter — and that path now OVERRIDES the toggle (see
            // OptOutOverridePolicy), so a toggled-off pawn still scoops+hauls gear for an order it was given by
            // hand. The same toggled-off pawn also still empties what it already carries (the unload paths never
            // read this flag). Mirrors YieldRouter.IsCandidate's opt-out gate (without the explicit-order override).
            var comp = hauler.GetComp<CompHauledToInventory>();
            if (comp == null || !comp.autoHaulYields)
                return;
            // RACE gate: delegate to the canonical predicate instead of a hand-rolled copy (which had already
            // drifted — it omitted the animal-allowance branch). IsRaceEligible is the documented superset that
            // honors humanlike / mechanoid (allowMechanoids) / animal (allowAnimals), is the SAME Core
            // EligibilityPolicy the runtime scoop gate uses, and null-guards RaceProps itself (returns false on a
            // null-RaceProps pawn), so no extra null check is needed here. INTENDED behavior change: when
            // allowAnimals is ON, a Haul-trained colony animal now auto-strips on a qualifying corpse haul, matching
            // every other YieldRouter intake path.
            if (!YieldRouter.IsRaceEligible(hauler))
                return;
            var job = hauler.CurJob;
            if (job == null || !QualifyingHaul(job, s.autoStripMode))
                return;
            // Your own dead are not loot: player-faction corpses (colonists, colony animals) are left
            // dressed unless the player opted in.
            var inner = corpse.InnerPawn;
            if (inner == null)
                return;
            if (!s.stripColonistCorpses && inner.Faction == Faction.OfPlayerSilentFail)
                return;
            // The same gate vanilla's strip job uses (strippable + actually has anything + kind allows it).
            if (!StrippableUtility.CanBeStrippedByColony(corpse))
                return;

            StripAndScoop(hauler, corpse, s);
        }

        /// <summary>
        /// STRIP BEFORE CREMATION (#222): when <paramref name="ingredient"/> is a corpse a bill is about to
        /// consume with a recipe that would DESTROY its gear (cremation, a modded incinerator, any corpse
        /// recipe with autoStripCorpses OFF), strip the body onto its own cell (the bill tile) FIRST so its
        /// weapons, apparel and carried items become loose, un-forbidden haulables that the normal haul pass
        /// salvages to storage instead of burning with the corpse. A no-op unless the player opted in
        /// (stripBeforeCremation) and <see cref="CremationStripPolicy.ShouldStrip"/> passes: it SKIPS an
        /// autoStripCorpses recipe (vanilla's consume seam already strips that, so no double strip), a bare
        /// corpse (nothing to salvage, e.g. a haul-strip already ran), and the player's own dead unless
        /// stripColonistCorpses is also on. Tainted apparel still follows the per-category policy (a
        /// LeaveOnCorpse rag stays on the body and is cremated with it, for clean disposal).
        ///
        /// Called from BOTH cremation consume seams so single-rep and HD-batched cremation behave alike: a
        /// Harmony postfix on vanilla <c>Toils_Recipe.CalculateIngredients</c>
        /// (<see cref="Patch_Toils_Recipe_CalculateIngredients_CremationStrip"/>) for normal bills, and
        /// directly from <c>JobDriver_BatchCraft.CalculateIngredientsFromPlacedThings</c> for batched bills.
        /// Safe to call once per resolved ingredient: it gates itself and no-ops on any non-corpse ingredient.
        /// </summary>
        /// <param name="worker">The pawn running the bill, credited with the strip.</param>
        /// <param name="ingredient">One resolved bill ingredient; only a <see cref="Corpse"/> is acted on.</param>
        /// <param name="recipe">The bill's recipe, whose autoStripCorpses decides whether vanilla already stripped.</param>
        internal static void MaybeStripForCremation(Pawn worker, Thing ingredient, RecipeDef recipe)
        {
            if (!(ingredient is Corpse corpse))
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || recipe == null || worker == null)
                return;
            if (!corpse.Spawned || corpse.Map == null)
                return;
            var inner = corpse.InnerPawn;
            if (inner == null)
                return;
            // The same "your own dead" test the on-haul strip uses (OfPlayerSilentFail never logs when there
            // is no player faction). The pure policy composes the opt-ins and skip conditions.
            //
            // DELIBERATE: this reads corpse.AnythingToStrip() DIRECTLY, so it gets VANILLA's answer — the
            // Patch_CanBeStrippedByColony_LeaveOnCorpse postfix sits on a different method and does not reach it.
            // That is what this seam wants. The question here is "is this body worth stripping before the bill
            // burns it?", and a body wearing only leave-on-corpse rags still answers yes: the strip runs, the
            // per-piece rule keeps those rags on the body, and they are cremated with it — which is exactly the
            // clean disposal the policy promises. Narrowing this to HD's own view would skip the strip entirely
            // and burn the WEAPONS too. Don't "fix" it to match the postfix.
            bool isPlayerFactionCorpse = inner.Faction == Faction.OfPlayerSilentFail;
            if (!CremationStripPolicy.ShouldStrip(s.stripBeforeCremation, recipe.autoStripCorpses,
                    corpse.AnythingToStrip(), isPlayerFactionCorpse, s.stripColonistCorpses))
                return;
            // Drop the gear on the corpse's own cell (the cremation tile). No scoop: this is a disposal seam,
            // the worker is not hauling the corpse home, so the dropped gear rides the normal haul pass instead.
            StripCorpseDroppingLoot(worker, corpse, s);
        }

        // Which hauls trigger a strip. AllHauls: any of the corpse-moving jobs. DisposalOnly: only
        // where the gear would otherwise be LOST — interment (a casket container) or a corpse bill
        // (cremation/butchering); a plain stockpile haul leaves the body dressed.
        private static bool QualifyingHaul(Job job, AutoStripMode mode)
        {
            if (job.def == JobDefOf.DoBill)
                return true; // cremation / butchering fetch — qualifies under both modes
            if (job.def == JobDefOf.HaulToContainer)
            {
                if (mode == AutoStripMode.AllHauls)
                    return true;
                return job.targetB.Thing is Building_Casket; // grave / sarcophagus interment
            }
            if (job.def == JobDefOf.HaulToCell)
                return mode == AutoStripMode.AllHauls; // stockpile haul — only under "every haul"
            // HD's bulk pickup: semantically a STORAGE haul (the unload pass delivers to best storage), so it
            // strips exactly like HaulToCell — under "every haul" only. A corpse now reaches this driver four
            // ways: "Pick up X" on a body, "Haul everything nearby" anchored on one, and — since the corpse sweep
            // opt-in — the automatic corpse-haul scan and "Prioritize hauling" on a body, both through the
            // WorkGiver_HaulCorpses postfix. (Before that, corpses reached no bulk path at all: vanilla's general
            // haul giver nulls them and the sweep pool skipped them.)
            //
            // The eventual destination is unknown at pickup, so under DisposalOnly a picked corpse that later
            // unloads into a grave would arrive DRESSED. That is accepted for the two MANUAL entries — the gear
            // is buried with the body, recoverable by exhuming, and nothing is destroyed — because the player
            // pointed at that body and asked for the trip.
            //
            // It is NOT accepted for the automatic scan, which is why CorpseSweepPolicy stands the automatic
            // corpse sweep down entirely in this mode (both as an anchor and as a swept neighbour). Otherwise a
            // DisposalOnly player who changed no setting would start seeing bodies interred with their gear on,
            // where before every automatic grave haul was a vanilla HaulToContainer stripped at the TryStartCarry
            // seam. A body too heavy for the inventory ceiling still falls back to that hand-haul and strips as
            // before, and the default "every corpse haul" mode is unaffected (it strips at pickup either way).
            // "Keep X in inventory" is NOT a haul and never strips.
            if (job.def == HaulersDreamDefOf.HaulersDream_BulkHaul)
                return mode == AutoStripMode.AllHauls;
            return false; // caravan packing, transport pods, anything else: never strip
        }

        /// <summary>
        /// SHARED CORPSE-STRIP CORE: drop <paramref name="corpse"/>'s gear onto its OWN cell (the strip tile)
        /// and return the pieces that became loose loot, applying every per-piece rule the auto-strip uses:
        /// equipment + inventory are always loot; apparel follows the tainted-apparel policy (LeaveOnCorpse
        /// stays on the body, DropAndForbid is forbidden in place, Destroy is destroyed), never touching a
        /// quest, relic, locked, biocoded, or merged-stack piece. Drops are UN-forbidden so the normal haul
        /// pass salvages them. On a body that had something to strip it also clears the pending Strip
        /// designation, records <c>BodiesStripped</c> on <paramref name="actor"/>, and notifies the dead pawn's
        /// faction exactly as a manual strip would. Shared by the on-haul scoop (<see cref="StripAndScoop"/>,
        /// which then pockets the returned loot) and the cremation strip (<see cref="MaybeStripForCremation"/>,
        /// which drops onto the cremation tile and does NOT scoop).
        /// </summary>
        /// <param name="actor">The pawn credited with the strip (the hauler, or the crafter at the cremation bench).</param>
        /// <param name="corpse">The body to strip; the caller ensures it is Spawned.</param>
        /// <param name="s">Live settings (the two tainted-apparel policies).</param>
        /// <returns>The loose loot the corpse contributed, each entry carrying the EXACT count the body added (a
        /// merged drop is excluded); or <c>null</c> when the body had nothing to strip, in which case no
        /// designation/record/notify side effect ran either.</returns>
        internal static List<ThingCount> StripCorpseDroppingLoot(Pawn actor, Corpse corpse, HaulersDreamSettings s)
        {
            var inner = corpse.InnerPawn;
            if (inner == null)
                return null;
            var map = corpse.Map;
            var pos = corpse.PositionHeld;
            // Each entry remembers what the CORPSE contributed: the drop result can be a pre-existing
            // ground stack the piece MERGED into (GenPlace rates merge-capable cells "Perfect"), and the
            // scoop must never take more than the stripped piece — see ScoopLoot.
            var loot = new List<ThingCount>();
            bool strippedAnything = false;

            // EQUIPMENT (weapons) — always loot. Per-piece TryDropEquipment, the same call Pawn.Strip makes.
            if (inner.equipment != null)
            {
                var eqList = inner.equipment.AllEquipmentListForReading;
                for (int i = eqList.Count - 1; i >= 0; i--)
                {
                    var eq = eqList[i];
                    if (inner.equipment.TryDropEquipment(eq, out var droppedEq, pos, forbid: false)
                        && droppedEq != null)
                    {
                        strippedAnything = true;
                        // Scoop only when the drop result IS the piece (then its stackCount is exact —
                        // a partially-absorbed remainder included). A MERGED result (modded stackable
                        // equipment only; vanilla weapons are stackLimit 1) is a pre-existing ground
                        // stack with no per-landing split available — crediting it could over-take
                        // someone else's stack, so it stays grounded for the normal haul sweep.
                        if (droppedEq == eq)
                            loot.Add(new ThingCount(droppedEq, droppedEq.stackCount));
                    }
                }
            }

            // INVENTORY (drugs, silver, pack-animal cargo) — always loot. The placedAction callback
            // fires once per LANDING with the exact landed count: a stackable drop can split across
            // several pre-existing ground stacks (GenPlace's partial-absorb cascade), and crediting the
            // whole drop to the last landing would let the scoop over-take from that stack.
            var invOwner = inner.inventory?.innerContainer;
            if (invOwner != null)
            {
                for (int i = invOwner.Count - 1; i >= 0; i--)
                {
                    if (invOwner.TryDrop(invOwner[i], pos, map, ThingPlaceMode.Near, out _,
                            (placed, count) => loot.Add(new ThingCount(placed, count))))
                        strippedAnything = true;
                }
            }

            // APPAREL — untainted is loot; tainted follows the per-category policy. "Tainted" matches the
            // game's own definition: worn by the corpse AND the apparel kind cares (careIfWornByCorpse).
            if (inner.apparel != null)
            {
                var worn = inner.apparel.WornApparel;
                for (int i = worn.Count - 1; i >= 0; i--)
                {
                    var ap = worn[i];
                    // LOCKED apparel (bonded/biocoded/royal-locked) stays on the body, exactly like vanilla:
                    // Pawn.Strip's DropAll only drops locked pieces when the inner pawn is Destroyed.
                    if (!inner.Destroyed && inner.apparel.IsLocked(ap))
                        continue;
                    bool tainted = ap.WornByCorpse && ap.def.apparel != null && ap.def.apparel.careIfWornByCorpse;
                    // Thing.Smeltable, not def.smeltable: the instance check also excludes relics and
                    // non-smeltable stuff, matching what a smelter would actually accept.
                    var action = StripPolicy.ApparelAction(tainted, ap.Smeltable,
                        s.taintedSmeltablePolicy, s.taintedNonSmeltablePolicy);
                    if (action == TaintedApparelPolicy.LeaveOnCorpse)
                        continue; // stays on the body, goes wherever the body goes
                    if (!inner.apparel.TryDrop(ap, out var droppedAp, pos, forbid: false) || droppedAp == null)
                        continue;
                    strippedAnything = true;
                    // A MERGED drop result (modded stackable apparel only — vanilla apparel is
                    // stackLimit 1) is a pre-existing ground stack containing the player's own pieces:
                    // never scoop, destroy, or forbid it. The contribution stays grounded as an
                    // ordinary haulable, and the tainted policy degrades to leave-on-ground.
                    if (droppedAp != ap)
                        continue;
                    // Identity holds, so stackCount is the piece's own count — exact even for a
                    // partially-absorbed remainder.
                    switch (action)
                    {
                        case TaintedApparelPolicy.Destroy:
                            // The mod's one deliberate destruction — explicit player opt-in, tainted only.
                            // Never quest-tagged or relic pieces, though (a failed quest / an irreplaceable
                            // relic is too steep a price for a policy default): treat those as Take.
                            if (!droppedAp.questTags.NullOrEmpty() || droppedAp.IsRelic())
                            {
                                loot.Add(new ThingCount(droppedAp, droppedAp.stackCount));
                                break;
                            }
                            if (!droppedAp.Destroyed)
                                droppedAp.Destroy(DestroyMode.Vanish);
                            break;
                        case TaintedApparelPolicy.DropAndForbid:
                            droppedAp.SetForbidden(true, warnOnFail: false);
                            break;
                        default:
                            loot.Add(new ThingCount(droppedAp, droppedAp.stackCount));
                            break;
                    }
                }
            }

            if (!strippedAnything)
                return null; // nothing was on the body: no loot, and no strip consequences to record

            // The vanilla strip consequences, faithfully: the pending Strip designation is now moot, the
            // actor logs a stripped body, and the dead pawn's faction reacts exactly as to a manual strip.
            map.designationManager.DesignationOn(corpse, DesignationDefOf.Strip)?.Delete();
            actor.records?.Increment(RecordDefOf.BodiesStripped);
            if (inner.Faction != null)
                inner.Faction.Notify_MemberStripped(inner, Faction.OfPlayer);

            return loot;
        }

        /// <summary>AUTO-STRIP ON HAUL: drop the corpse's gear on its cell, then SCOOP the loose loot into the
        /// hauler's inventory (tagged for the unload pass) where it fits and has somewhere to go. The strip
        /// core is shared with the cremation seam; only this haul path scoops.</summary>
        private static void StripAndScoop(Pawn hauler, Corpse corpse, HaulersDreamSettings s)
        {
            var loot = StripCorpseDroppingLoot(hauler, corpse, s);
            if (loot == null)
                return; // the body had nothing to strip

            // Scoop only into inventories the AUTOMATIC unload will serve: a pawn that fails
            // YieldRouter.IsEligible (a hauling-incapable cook fetching a butcher-bill corpse, with
            // allowIncapable off) would carry the tagged loot forever; every automatic unload trigger
            // skips ineligible pawns, and the only recovery is the manual gizmo. The strip itself
            // still happened: the gear stays on the ground as ordinary haulables for real haulers.
            if (YieldRouter.IsEligible(hauler))
            {
                int scooped = ScoopLoot(hauler, loot, s);
                HDLog.Dbg($"{hauler} auto-stripped {corpse} on haul: scooped {scooped} of {loot.Count} loot entries.");
            }
        }

        /// <summary>
        /// Load the stripped loot into the hauler's inventory (tagged — the unload pass, shared inventories, and
        /// CE HoldTracker all pick it up from there). A piece is scooped only when it FITS the carry/CE limits
        /// AND some storage would accept it; anything else simply stays on the ground as an ordinary haulable —
        /// spawned and UN-forbidden at the corpse's own cell, visible to <c>listerHaulables</c>, and collected
        /// automatically the moment a stockpile accepts it. Per-PIECE, not all-or-nothing: a raider yields a
        /// rifle, a vest, a tainted duster and drugs, and one unwanted rag must not strand the rifle.
        ///
        /// Deliberately NO #121 pickup pause here (see PickupPause): this scoop fires inside vanilla's
        /// Pawn_CarryTracker.TryStartCarry seam as a side effect of a committed corpse carry (no HD toil
        /// exists to pace); the BulkHaul corpse path already pays the pause in its own take loop.
        /// </summary>
        /// <param name="hauler">The pawn that stripped the body; the loot goes into its inventory.</param>
        /// <param name="loot">The strip's per-piece drops, each entry capped at what the CORPSE contributed
        /// (a merged drop may sit in a bigger pre-existing ground stack).</param>
        /// <param name="s">Live settings — the smart-overload ceiling that sizes each take.</param>
        /// <returns>How many of <paramref name="loot"/>'s entries were actually pocketed, so the caller can log
        /// "scooped N of M" without the reader subtracting. Zero when the pawn has no inventory or comp.</returns>
        private static int ScoopLoot(Pawn hauler, List<ThingCount> loot, HaulersDreamSettings s)
        {
            var inv = hauler.inventory?.GetDirectlyHeldThings();
            var comp = hauler.GetComp<CompHauledToInventory>();
            if (inv == null || comp == null)
                return 0;
            int scooped = 0;
            int noDestination = 0;
            for (int i = 0; i < loot.Count; i++)
            {
                var t = loot[i].Thing;
                if (t == null || t.Destroyed || !t.Spawned)
                    continue;
                // A drop that landed in (or merged into) valid storage is already home — scooping it
                // would pull stored goods back out (the same guard the yield hook applies).
                if (t.IsInValidStorage())
                    continue;
                int take = OverloadGate.CountToPickUp(hauler, t, s);
                // Never more than the corpse contributed: the drop may have MERGED into a pre-existing
                // ground stack (someone else's haul target, possibly reserved) — taking the whole merged
                // stack would yank player property that was never on the body.
                take = Math.Min(take, loot[i].Count);
                if (take <= 0)
                    continue;
                // #234 — never pocket a piece with nowhere to go. This was the one non-player-ordered intake in the
                // mod that skipped the destination probe all its siblings apply, so a tainted duster no stockpile
                // accepts rode along to the hauler's next job (a butcher-bill fetch — QualifyingHaul admits DoBill)
                // and got dumped at the bench. Reuse YieldRouter's gate instead of writing a second probe: it
                // satisfies two load-bearing constraints by construction.
                //   1. It takes the THING, not the def. Tainted apparel is rejected instance-by-instance
                //      (SpecialThingFilterWorker_DeadmansApparel.Matches reads (t as Apparel)?.WornByCorpse), so a
                //      def-level probe or per-def cache would answer "yes, apparel is allowed" and fix nothing.
                //   2. It hard-codes needAccurateResult:false, which consumes no Rand — no multiplayer-determinism
                //      hazard, hence none of the Rand.PushState/PopState wrapping that InventorySurplus
                //      .HasUnloadDestination has to carry for its per-frame alert callers.
                // No StorageBuildingFilter context is pushed, so the documented allow-all default (Unload) applies.
                // That is deliberate: this asks only whether a home EXISTS, not which one to pick, and an intake
                // probe NARROWER than the unload probe would under-admit — refusing a piece whose only home is a
                // building the player denied for opportunistic routing, even though the unload would deliver it
                // there happily. Neither this seam nor the bulk take toil is reachable from inside an
                // Opportunistic/BeforeCarry scope anyway (StorageRouting.cs:238-240).
                // ORDER: after IsInValidStorage above — mandatory, since a piece already in accepting storage is
                // HOME and probing it would ask for something BETTER, a different and wrong question. After the
                // capacity gate — pure arithmetic before a slot-group walk, the cost ordering documented at
                // BulkHaul.cs:949-950; the outcome is identical either way.
                if (!YieldRouter.HasScoopDestination(hauler, t))
                {
                    noDestination++;
                    continue;
                }
                // SplitOff with count >= stackCount despawns the thing itself (full-stack pickup path).
                var split = t.SplitOff(Math.Min(take, t.stackCount));
                if (inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    comp.RegisterHauledItem(split);
                    comp.NotifyYieldPicked();
                    if (!split.Spawned)
                        split.Position = hauler.Position;
                    scooped++;
                }
                else if (split != null && !split.Destroyed && !split.Spawned)
                {
                    // Add failed (effectively impossible) — put it back rather than ever losing it.
                    GenPlace.TryPlaceThing(split, hauler.Position, hauler.Map, ThingPlaceMode.Near);
                }
            }
            // Conditional: HDLog.Dbg ALWAYS writes the disk trail (HaulersDreamMod.cs:427-432), so an
            // unconditional line would add noise to every bug report for the overwhelmingly common
            // nothing-left-behind case. Recorded when it does happen, so a report shows the decision.
            // "nowhere better to put them", not "no storage accepts them": a HasScoopDestination miss is
            // TryFindBestBetterStorageFor failing for ANY reason — a full, reserved or unreachable cell
            // (IsGoodStoreCell -> CanReserveNew / CanReach) as much as a filter rejection. The count is
            // ENTRIES, not pieces: StripCorpseDroppingLoot's placedAction fires once per landing, so one
            // stackable drop that split across two ground stacks contributes two entries pointing at the
            // same merged Thing, and both are counted when it has nowhere to go.
            if (noDestination > 0)
                HDLog.Dbg($"{hauler} auto-strip: left {noDestination} stripped loot entr(ies) at the body — nowhere better to put them.");
            return scooped;
        }
    }
}
