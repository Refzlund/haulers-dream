using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Pack-animal BULK UNLOAD — the net-new INVERSE of <see cref="JobDriver_LoadPackAnimal"/>. Vanilla's
    /// WorkGiver_UnloadCarriers pulls ONE stack into the hauler's hands per job (one walk per stack); this driver
    /// makes a hauler walk to a flagged carrier ONCE and pull MANY stacks out of it in that single visit:
    ///   • BACKPACK-FIRST — each stack that fits under the hauler's free carry mass is transferred into its
    ///     INVENTORY and tagged in <see cref="CompHauledToInventory"/>, so HD's normal unload pass ships it to
    ///     storage (exactly like a scooped yield).
    ///   • LAST/OVERFLOW-TO-HANDS — once the backpack is full, the last stack (or, near-full, one more) goes into
    ///     the CARRY TRACKER, UNtagged, and ships directly via a HaulToStorage job appended in the finalize.
    ///
    /// The per-pull ladder is the pure <see cref="BulkUnloadCarrierPolicy.PlanNextPull"/>. The carrier reservation
    /// is NON-exclusive by default (<c>reserveCarrierOnUnload=false</c>) so roping / caravan formation can still
    /// interrupt; a periodic FailOn scanning <see cref="Verse.AI.ReservationManager.ReservationsReadOnly"/> for
    /// ANOTHER claimant yields cleanly when a different pawn lays claim. NO try/catch on the transfer path; the
    /// JobDef carries NO <checkEncumbrance> (the fallback one-to-hands deliberately exceeds the soft ceiling).
    /// </summary>
    public class JobDriver_UnloadCarrierInBulk : JobDriver
    {
        private const TargetIndex CarrierInd = TargetIndex.A; // the flagged pack animal being emptied
        private const TargetIndex ItemInd = TargetIndex.C;     // scratch: the carrier stack currently selected

        private int pullLoops;
        private const int MaxPullLoops = 256; // backstop: bounds the select->transfer cycle
        private const int AiUpdateInterval = 60; // how often the other-claimant FailOn re-scans (ticks)

        // Actual no-merge visit cargo survives a save between transfer and cleanup. All-ending recovery
        // uses these identities, never a newly unrelated object found in the pawn's hands.
        private Thing handTail;
        private int handTailCount;
        private List<Thing> visitCargo = new List<Thing>();

        private Pawn Carrier => job.GetTarget(CarrierInd).Thing as Pawn;

        private static HaulersDreamSettings Settings => HaulersDreamMod.Settings;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pullLoops, "hdUcibPullLoops", 0);
            Scribe_References.Look(ref handTail, "hdUcibHandTail");
            Scribe_Values.Look(ref handTailCount, "hdUcibHandTailCount");
            Scribe_Collections.Look(ref visitCargo, "hdUcibVisitCargo", LookMode.Reference);
            Scribe_Values.Look(ref pendingToHands, "hdUcibPendingToHands");
            if (Scribe.mode == LoadSaveMode.LoadingVars) visitCargo ??= new List<Thing>();
        }

        public override string GetReport()
        {
            var carrier = Carrier;
            return carrier != null
                ? "HaulersDream.UnloadCarrier.Report".Translate(carrier.LabelShort)
                : "HaulersDream.UnloadCarrier.Report".Translate("");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Non-exclusive by default: do NOT reserve the carrier, so roping / caravan formation / another mod
            // can still interrupt the unload (the per-AiUpdateFrequency FailOn catches a competing claimant). Only
            // when reserveCarrierOnUnload is on do we take the reservation (then it's an exclusive, uninterruptible
            // unload). errorOnFailed is honored so a queued order that can't reserve fails loudly, not silently.
            var s = Settings;
            if (s == null || !s.reserveCarrierOnUnload)
                return true;
            return pawn.Reserve(Carrier, job, 1, -1, null, errorOnFailed);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            // Flag the carrier for unloading (vanilla's unload gate keys off this). MP: this is a write to a SCRIBED
            // field (synced world state), so it lives HERE — in Notify_Starting, which StartJob calls once on the real
            // running driver in-tick — rather than in the float-menu callback. The ordered-job command is the only
            // thing MP syncs for the click; everything in the started job (including this flag write) then runs
            // deterministically in-tick on every client. Pure relocation: no MP API needed, single-player unchanged.
            var carrier = Carrier;
            if (carrier?.inventory == null)
                return;
            // PERMISSION, whatever queued this job. The flag write is a WIDER hole than the menu it normally comes
            // from: UnloadEverything is SCRIBED, and raising it also opens vanilla's own faction-blind
            // WorkGiver_UnloadCarriers on that pawn for EVERY hauler on the map, indefinitely, until its pack is
            // empty. So the write is gated at the write, not only at the offer. The shared seam keeps this answer
            // identical to the one the menu and the work-giver takeover already gave, and reads only synced world
            // state — no Rand, no client-local input — so the MP reasoning above is untouched.
            if (!BulkUnloadGate.PlayerMayUnload(pawn, carrier))
                return;
            carrier.inventory.UnloadEverything = true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            // A pre-feature save can prove ownership only if its scratch target is the actual hand stack.
            // Do not adopt arbitrary hands when the old unscribed reference cannot be reconstructed.
            if (handTail == null && job.targetC.Thing != null
                && pawn.carryTracker?.innerContainer.Contains(job.targetC.Thing) == true)
            { handTail = job.targetC.Thing; handTailCount = handTail.stackCount; }
            AddFinishAction(_ => BulkUnloadRecovery.Queue(pawn, visitCargo, handTail, handTailCount, job.playerForced));
            this.FailOnDespawnedOrNull(CarrierInd);
            this.FailOnForbidden(CarrierInd);
            // PERMISSION as an END CONDITION, not just a gate on the flag write above: withholding the flag stops
            // vanilla's haulers, but the transfer loop below would still empty a carrier this job should never
            // have targeted. Global fail conditions are evaluated before any toil's initAction, so a job that
            // reaches here on a guest — a foreign caller, or one queued in a save made before this rule existed —
            // ends Incompletable without moving a single stack.
            //
            // → NOTE: decided ONCE per driver setup rather than per tick, and that is exactly enough. SetupToils
            //   runs on job start AND again at PostLoadInit (decompiled JobDriver.ExposeData), so a resumed save
            //   re-asks; and the answer is a permission — who the carrier IS — which no single visit changes.
            //   Re-asking every tick would also re-walk the quest manager's extra-faction parts twice per tick
            //   (IsQuestLodger tests both the home and the mini faction) for the whole length of the visit.
            bool mayUnload = BulkUnloadGate.PlayerMayUnload(pawn, Carrier);
            this.FailOn(() => !mayUnload);
            // Non-exclusive reserve: another pawn claiming this carrier (roping, caravan-form gather, a second
            // hauler) must pre-empt us. CanReserve can't be used — it returns false on the pawn's OWN reservation
            // when reserveCarrierOnUnload is on — so scan the live reservation list for a DIFFERENT live claimant.
            // Gated to a periodic interval (the scan is O(reservations); a competing claim need not be caught the
            // very tick it lands — within ~1s is fine for a clean yield).
            this.FailOn(() => pawn.IsHashIntervalTick(AiUpdateInterval) && AnotherPawnClaims(Carrier));

            yield return Toils_Goto.GotoThing(CarrierInd, PathEndMode.Touch);

            // The select/transfer loop: pick the next stack to pull (backpack-first -> last/overflow to hands),
            // pause for the visual delay, transfer it, and jump back — emptying the carrier in this one visit.
            Toil selectNext = ToilMaker.MakeToil("HD_Ucib_SelectNext");
            Toil finalize = ToilMaker.MakeToil("HD_Ucib_Finalize");

            selectNext.initAction = delegate
            {
                if (++pullLoops > MaxPullLoops) { JumpToToil(finalize); return; }
                var carrier = Carrier;
                if (carrier == null || !carrier.Spawned || carrier.Dead || carrier.inventory == null)
                { JumpToToil(finalize); return; }

                var carrierInner = carrier.inventory.innerContainer;
                if (carrierInner == null || carrierInner.Count == 0) { JumpToToil(finalize); return; }

                if (!BulkUnloadPull.TrySelect(pawn, carrierInner, respectForbidden: false, job.playerForced,
                    out Thing thing, out int count, out bool toHands))
                { JumpToToil(finalize); return; }
                job.SetTarget(ItemInd, thing);
                job.count = count;
                pendingToHands = toHands;
            };
            selectNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return selectNext;

            // A short, settings-driven pause so the unload reads as a deliberate per-stack action (mirrors the
            // visual cadence of vanilla's per-stack unload). 0 = instant. Resolved once at job start (the JobDriver
            // reads defaultCompleteMode/defaultDuration when the toil STARTS, before initAction runs, so the delay
            // cannot be set per-iteration in initAction — it is fixed for the whole job, which is fine: a setting
            // change mid-job is not expected and would only affect the next job).
            // Deliberately NOT the #121 pickupDelayTicks pause (see PickupPause): this is a carrier-to-backpack
            // transfer with its own shipped pacing (visualUnloadDelay, vanilla-unload cadence), not a ground
            // pickup; re-basing it on the 120-tick pickup default would change released unload behavior.
            int delay = Settings?.visualUnloadDelay ?? 0;
            Toil wait = delay > 0 ? Toils_General.Wait(delay) : Toils_General.Label();
            yield return wait;

            Toil transfer = ToilMaker.MakeToil("HD_Ucib_Transfer");
            transfer.initAction = delegate
            {
                var carrier = Carrier;
                var thing = job.GetTarget(ItemInd).Thing;
                if (carrier == null || carrier.inventory == null || thing == null || thing.Destroyed)
                { JumpToToil(selectNext); return; }
                var carrierInner = carrier.inventory.innerContainer;
                if (carrierInner == null || !carrierInner.Contains(thing)) { JumpToToil(selectNext); return; }

                // Recheck permission after the visual delay; the shared transfer rechecks actual custody,
                // remaining quantity and both vanilla/CE capacity before moving anything.
                if (!BulkUnloadGate.ShouldHandle(pawn, carrier) || !BulkUnloadGate.PlayerMayUnload(pawn, carrier))
                { EndJobWith(JobCondition.Incompletable); return; }
                int moved = BulkUnloadPull.Transfer(pawn, carrierInner, thing, job.count, pendingToHands,
                    respectForbidden: false, job.playerForced, out Thing movedThing, out bool toHands);
                if (moved > 0) { visitCargo.Add(movedThing); AfterTransferRefresh(carrier); }
                if (toHands && moved > 0) { handTail = movedThing; handTailCount = moved; }
                if (moved <= 0 || toHands) { JumpToToil(finalize); return; }
                JumpToToil(selectNext);
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;

            // ============ FINALIZE: ship the backpack stock + the hand-tail to storage ============
            finalize.initAction = delegate
            {
                EndJobWith(JobCondition.Succeeded);
            };
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalize;
        }

        private bool pendingToHands;

        /// <summary>True if a pawn OTHER than this hauler holds a live reservation on the carrier — the
        /// non-exclusive-reserve pre-empt check (roping, caravan-form gather, a second hauler). Scans
        /// <see cref="Verse.AI.ReservationManager.ReservationsReadOnly"/> directly because <c>pawn.CanReserve</c>
        /// returns false on the pawn's OWN reservation when reserveCarrierOnUnload is on.</summary>
        private bool AnotherPawnClaims(Pawn carrier)
        {
            if (carrier == null || pawn?.Map?.reservationManager == null)
                return false;
            var reservations = pawn.Map.reservationManager.ReservationsReadOnly;
            if (reservations == null)
                return false;
            for (int i = 0; i < reservations.Count; i++)
            {
                var r = reservations[i];
                if (r == null) continue;
                var claimant = r.Claimant;
                if (claimant == null || claimant == pawn || claimant.Dead) continue;
                if (r.Target.Thing == carrier)
                    return true;
            }
            return false;
        }

        /// <summary>Bookkeeping after a transfer: when the carrier's saddlebags are now empty, refresh its
        /// graphics (vanilla does this in JobDriver_UnloadInventory so the loaded look clears).</summary>
        private static void AfterTransferRefresh(Pawn carrier)
        {
            if (carrier != null && carrier.RaceProps.packAnimal
                && carrier.inventory?.innerContainer?.Count == 0)
                carrier.Drawer.renderer.SetAllGraphicsDirty();
        }
    }
}
