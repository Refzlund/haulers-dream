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

        // The single item taken into HANDS this visit (UNtagged). Captured so the finalize can ship it directly
        // via a HaulToStorage job. In-flight only — not scribed (a save mid-visit re-derives it: a handheld item
        // already in the carry tracker is picked up by the next unload trigger / vanilla haul anyway).
        [System.NonSerialized] private Thing handTail;

        // Reused per-pull scratch for the carrier-stack view fed to the pure planner, replacing a fresh
        // List<CarrierStack> per select cycle (each select toil re-snapshots the carrier's inventory). [ThreadStatic]
        // + lazy-init matches the repo's hook-reachable scratch convention; Cleared at the point of use, never trusted
        // empty. SAFETY: the select initAction builds + consumes this within one JumpToToil cycle (sequential within
        // a tick, no re-entrant job re-enters this scratch) before the next reuse.
        [System.ThreadStatic] private static List<BulkUnloadCarrierPolicy.CarrierStack> scratchStacks;

        private Pawn Carrier => job.GetTarget(CarrierInd).Thing as Pawn;

        private static HaulersDreamSettings Settings => HaulersDreamMod.Settings;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pullLoops, "hdUcibPullLoops", 0);
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
            if (carrier?.inventory != null)
                carrier.inventory.UnloadEverything = true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(CarrierInd);
            this.FailOnForbidden(CarrierInd);
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

                // Build the pure planner's view of the carrier's stacks (index, per-unit mass, count), then ask
                // for the next pull. Free carry mass is the hauler's live headroom (negative when overloaded -> 0
                // backpack room -> the ladder routes to hands).
                var stacks = scratchStacks ?? (scratchStacks = new List<BulkUnloadCarrierPolicy.CarrierStack>());
                stacks.Clear();
                for (int i = 0; i < carrierInner.Count; i++)
                {
                    var t = carrierInner[i];
                    if (t == null || t.Destroyed) continue;
                    stacks.Add(new BulkUnloadCarrierPolicy.CarrierStack(
                        i, t.GetStatValue(StatDefOf.Mass), t.stackCount));
                }
                float freeSpace = MassUtility.FreeSpace(pawn);
                var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, freeSpace);
                if (plan.ChosenIndex < 0 || plan.ChosenIndex >= carrierInner.Count || plan.Count <= 0)
                { JumpToToil(finalize); return; }

                var thing = carrierInner[plan.ChosenIndex];
                if (thing == null || thing.Destroyed) { JumpToToil(selectNext); return; }

                // Combat Extended adds a BULK dimension the pure (vanilla-mass) planner can't see — so the BACKPACK
                // pull defers to CE's own weight+bulk fit, EXACTLY as the LOAD sibling does (JobDriver_LoadPackAnimal
                // sweepTake / PackAnimalLoad: count = Min(count, CECompat.MaxFitCount(pawn, t))). MaxFitCount returns
                // int.MaxValue without CE, so this Min is a no-op then — CE-absent behaviour is byte-identical.
                // The to-hands rung is NEVER CE-clamped: the carry tracker is exempt from the soft ceiling (that's
                // why the JobDef has no checkEncumbrance), and the LOAD side likewise only clamps inventory pulls.
                int count = plan.Count;
                bool toHands = plan.ToHands;
                if (!toHands)
                {
                    int ceFit = CECompat.MaxFitCount(pawn, thing);
                    if (ceFit <= 0)
                    {
                        // CE weight/bulk is exhausted even though vanilla mass thought there was backpack room (CE's
                        // bulk dimension the planner can't see). Treat the backpack as full and re-plan with zero free
                        // space so the Core ladder routes this stack to HANDS — mirroring the LOAD side's sweepDecide,
                        // which flips roomLeft=false when CECompat.AvailableBulk(pawn) <= 0 and falls through to the
                        // overflow path. (CE-absent: ceFit is int.MaxValue, never <= 0, so this branch never runs.)
                        plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, 0f);
                        if (plan.ChosenIndex < 0 || plan.ChosenIndex >= carrierInner.Count || plan.Count <= 0)
                        { JumpToToil(finalize); return; }
                        thing = carrierInner[plan.ChosenIndex];
                        if (thing == null || thing.Destroyed) { JumpToToil(selectNext); return; }
                        count = plan.Count;
                        toHands = plan.ToHands;
                    }
                    else
                    {
                        count = System.Math.Min(count, ceFit);
                    }
                }
                // Stash the selection + the plan's destination on the job for the transfer toil.
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

                int count = job.count > 0 ? job.count : thing.stackCount;
                count = System.Math.Min(count, thing.stackCount);
                if (count <= 0) { JumpToToil(finalize); return; }

                if (pendingToHands)
                {
                    // Overflow / last stack -> the carry tracker, UNtagged. It ships directly via the finalize's
                    // HaulToStorage job (tagging it would double-ship it through HD's inventory unload pass too).
                    int moved = carrierInner.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, count, out Thing movedThing);
                    if (moved > 0 && movedThing != null)
                        handTail = movedThing;
                    // One item to hands is enough for this visit (the carry tracker holds one stack); finalize.
                    AfterTransferRefresh(carrier);
                    JumpToToil(finalize);
                    return;
                }

                // Backpack: into the hauler's inventory, tagged in HD's comp so the normal unload ships it.
                // canMergeWithExistingStacks:false (matching the LOAD sibling's inv.TryAdd) — a task item must NOT
                // merge into the pawn's personal kit. If it merged, movedBpThing would be the MERGED personal-kit
                // stack, RegisterHauledItem would tag it, and the comp's same-def self-heal would re-tag every
                // matching personal stack -> non-keep-stock the pawn legitimately carries gets shipped to storage.
                var pawnInner = pawn.inventory?.innerContainer;
                if (pawnInner == null) { JumpToToil(finalize); return; }
                int movedBp = carrierInner.TryTransferToContainer(thing, pawnInner, count, out Thing movedBpThing, canMergeWithExistingStacks: false);
                if (movedBp > 0 && movedBpThing != null)
                {
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    comp?.RegisterHauledItem(movedBpThing);
                }
                else
                {
                    // Nothing moved (a non-mergeable passenger, or another mod holding the stack) -> end the visit
                    // rather than spin (the backstop loop count also bounds this). The carrier keeps the stack;
                    // vanilla / a later trigger retries.
                    JumpToToil(finalize);
                    return;
                }
                AfterTransferRefresh(carrier);
                JumpToToil(selectNext); // more stacks -> keep pulling in this same visit
            };
            transfer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transfer;

            // ============ FINALIZE: ship the backpack stock + the hand-tail to storage ============
            finalize.initAction = delegate
            {
                // Hand-tail (UNtagged, in the carry tracker) ships DIRECTLY: enqueue a HaulToStorage job FIRST so
                // it goes out ahead of the inventory unload. forced:true so it isn't gated away.
                if (handTail != null && !handTail.Destroyed
                    && pawn.carryTracker?.innerContainer?.Contains(handTail) == true)
                {
                    var haulJob = HaulAIUtility.HaulToStorageJob(pawn, handTail, forced: true);
                    if (haulJob != null && pawn.jobs != null)
                    {
                        haulJob.playerForced = job.playerForced;
                        pawn.jobs.jobQueue.EnqueueFirst(haulJob, JobTag.Misc);
                    }
                }
                // Backpack stock is tagged -> HD's normal storage unload pass ships it (forced recovery).
                PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true, behindQueuedWork: true);
                EndJobWith(JobCondition.Succeeded);
            };
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalize;
        }

        // Carried between the select and transfer toils (which stack goes to hands vs backpack). In-flight only.
        [System.NonSerialized] private bool pendingToHands;

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
