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
    /// Loot that doesn't fit the carry/CE limits stays on the ground as ordinary haulables (the bulk-haul
    /// sweep picks it up later) — nothing is ever lost by stripping.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry),
        typeof(Thing), typeof(int), typeof(bool))]
    public static class Patch_TryStartCarry_AutoStrip
    {
        static void Prefix(Pawn_CarryTracker __instance, Thing item)
        {
            try
            {
                if (item is Corpse corpse)
                    CorpseStripper.MaybeStripForHaul(__instance.pawn, corpse);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Hauler's Dream] Auto-strip on haul failed (corpse hauled unstripped): " + e,
                    0x535452 ^ e.GetType().FullName.GetHashCode()); // key per exception type — a second failure mode still reports
            }
        }
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
    /// (its own toils fail on CanBeStrippedByColony).
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
            try
            {
                __instance.AddFinishAction(cond =>
                {
                    try
                    {
                        if (cond == JobCondition.Succeeded)
                            CorpseStripper.QueueReStripIfNeeded(__instance.pawn, __instance.job?.targetA.Thing);
                    }
                    catch (Exception e)
                    {
                        Log.WarningOnce("[Hauler's Dream] re-strip follow-up failed: " + e, 0x525354);
                    }
                });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Hauler's Dream] re-strip follow-up failed: " + e, 0x525354);
            }
            return toils; // toils themselves are untouched — vanilla's strip runs exactly as shipped
        }
    }

    public static class CorpseStripper
    {
        /// <summary>After stripping a LIVING pawn, append a vanilla Strip job at the end of the stripper's
        /// queue so re-equipped clothing (a prisoner dressing from the leftovers) gets stripped again.</summary>
        internal static void QueueReStripIfNeeded(Pawn stripper, Thing target)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.haulStrip)
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
            var queue = stripper.jobs.jobQueue;
            if (queue != null)
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i]?.job?.def == JobDefOf.Strip && queue[i].job.targetA.Thing == victim)
                        return; // a re-strip on this target is already queued
            stripper.jobs.jobQueue.EnqueueLast(JobMaker.MakeJob(JobDefOf.Strip, victim), JobTag.Misc);
        }

        /// <summary>Strip <paramref name="corpse"/> if this pickup qualifies under the settings. Loot is
        /// scooped into <paramref name="hauler"/>'s inventory (tagged) where it fits; the rest stays on
        /// the ground as normal haulables. Safe to call speculatively — it gates itself.</summary>
        internal static void MaybeStripForHaul(Pawn hauler, Corpse corpse)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || s.autoStripMode == AutoStripMode.Off)
                return;
            if (hauler == null || corpse == null || !corpse.Spawned || hauler.Map == null || corpse.Map != hauler.Map)
                return;
            // On a map the mod is configured to leave alone, don't strip: the unload side honors the same
            // setting, so the scooped loot would just strand in the hauler's inventory until it gets home.
            if (corpse.Map != null && !s.enableOnNonHomeMaps && !corpse.Map.IsPlayerHome)
                return;
            // Quest lodgers excluded like the bulk-haul sweep: their pockets leave with the quest,
            // so scooped loot could walk off-map before the unload pass runs.
            if (hauler.Faction != Faction.OfPlayerSilentFail || hauler.IsQuestLodger())
                return;
            if (hauler.RaceProps == null
                || (!hauler.RaceProps.Humanlike && !(hauler.RaceProps.IsMechanoid && s.allowMechanoids)))
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

        // Which hauls trigger a strip. AllHauls: any of the three corpse-moving jobs. DisposalOnly: only
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
            return false; // caravan packing, transport pods, anything else: never strip
        }

        private static void StripAndScoop(Pawn hauler, Corpse corpse, HaulersDreamSettings s)
        {
            var inner = corpse.InnerPawn;
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
                    int eqCount = eq.stackCount; // snapshot before the drop — the out param may be a merged stack
                    if (inner.equipment.TryDropEquipment(eq, out var droppedEq, pos, forbid: false)
                        && droppedEq != null)
                    {
                        // Min: a partial absorb returns the smaller remainder — the ctor would clamp
                        // anyway, but with a per-drop Log.Warning (modded stackable equipment only).
                        loot.Add(new ThingCount(droppedEq, Math.Min(eqCount, droppedEq.stackCount)));
                        strippedAnything = true;
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
                    int apCount = ap.stackCount; // 1 for vanilla apparel; snapshot anyway (modded stackables)
                    if (!inner.apparel.TryDrop(ap, out var droppedAp, pos, forbid: false) || droppedAp == null)
                        continue;
                    strippedAnything = true;
                    // Min: a partial absorb returns the smaller remainder — the ctor would clamp anyway,
                    // but with a per-drop Log.Warning (modded stackable apparel only; vanilla is 1).
                    int apCredit = Math.Min(apCount, droppedAp.stackCount);
                    switch (action)
                    {
                        case TaintedApparelPolicy.Destroy:
                            // The mod's one deliberate destruction — explicit player opt-in, tainted only.
                            // Never quest-tagged or relic pieces, though (a failed quest / an irreplaceable
                            // relic is too steep a price for a policy default): treat those as Take.
                            if (!droppedAp.questTags.NullOrEmpty() || droppedAp.IsRelic())
                            {
                                loot.Add(new ThingCount(droppedAp, apCredit));
                                break;
                            }
                            // A MERGED drop (only possible with modded stackable apparel) contains
                            // pre-existing ground pieces — destroying it would exceed the opt-in's scope.
                            // Vanilla apparel is stackLimit 1, so this guard never fires there.
                            if (droppedAp != ap)
                            {
                                loot.Add(new ThingCount(droppedAp, apCredit));
                                break;
                            }
                            if (!droppedAp.Destroyed)
                                droppedAp.Destroy(DestroyMode.Vanish);
                            break;
                        case TaintedApparelPolicy.DropAndForbid:
                            // Same merged-drop insurance as Destroy: forbidding a pre-existing ground
                            // stack the piece merged into would forbid the player's own goods — Take.
                            if (droppedAp != ap)
                            {
                                loot.Add(new ThingCount(droppedAp, apCredit));
                                break;
                            }
                            droppedAp.SetForbidden(true, warnOnFail: false);
                            break;
                        default:
                            loot.Add(new ThingCount(droppedAp, apCredit));
                            break;
                    }
                }
            }

            if (!strippedAnything)
                return;

            // The vanilla strip consequences, faithfully: the pending Strip designation is now moot, the
            // hauler logs a stripped body, and the dead pawn's faction reacts exactly as to a manual strip.
            map.designationManager.DesignationOn(corpse, DesignationDefOf.Strip)?.Delete();
            hauler.records?.Increment(RecordDefOf.BodiesStripped);
            if (inner.Faction != null)
                inner.Faction.Notify_MemberStripped(inner, Faction.OfPlayer);

            ScoopLoot(hauler, loot, s);
            HDLog.Dbg($"{hauler} auto-stripped {corpse} on haul: {loot.Count} loot entries.");
        }

        // Load the stripped loot into the hauler's inventory (tagged — the unload pass, shared
        // inventories, and CE HoldTracker all pick it up from there). Whatever doesn't fit the
        // carry/CE limits simply stays on the ground as an ordinary haulable.
        private static void ScoopLoot(Pawn hauler, List<ThingCount> loot, HaulersDreamSettings s)
        {
            var inv = hauler.inventory?.GetDirectlyHeldThings();
            var comp = hauler.GetComp<CompHauledToInventory>();
            if (inv == null || comp == null)
                return;
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
                // SplitOff with count >= stackCount despawns the thing itself (full-stack pickup path).
                var split = t.SplitOff(Math.Min(take, t.stackCount));
                if (inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    comp.RegisterHauledItem(split);
                    comp.NotifyYieldPicked();
                    if (!split.Spawned)
                        split.Position = hauler.Position;
                }
                else if (split != null && !split.Destroyed && !split.Spawned)
                {
                    // Add failed (effectively impossible) — put it back rather than ever losing it.
                    GenPlace.TryPlaceThing(split, hauler.Position, hauler.Map, ThingPlaceMode.Near);
                }
            }
        }
    }
}
