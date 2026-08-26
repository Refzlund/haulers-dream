using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Transporter/shuttle BULK UNLOAD, the building-side sibling of <see cref="JobDriver_UnloadCarrierInBulk"/>
    /// and the INVERSE of <see cref="JobDriver_LoadTransportersInBulk"/>. Vanilla empties a landed transporter's
    /// hold either by dumping it on the floor at once (the "Cancel load"/"Unload" gizmo →
    /// <c>CompTransporter.CleanUpLoadingVars</c>) or one thing per second (<c>ShipJob_Unload</c>), either way the
    /// cargo ends up ON THE GROUND and still needs hauling. This driver makes a hauler walk to the transporter ONCE
    /// and pull MANY stacks straight out of its <c>CompTransporter.innerContainer</c>:
    ///   • BACKPACK-FIRST, each stack that fits under the hauler's free carry mass is transferred into its
    ///     INVENTORY and tagged in <see cref="CompHauledToInventory"/>, so HD's normal unload pass ships it to
    ///     storage (exactly like a scooped yield).
    ///   • LAST/OVERFLOW-TO-HANDS, once the backpack is full, the last stack (or, near-full, one more) goes into
    ///     the CARRY TRACKER, UNtagged, and ships directly via a HaulToStorage job appended in the finalize.
    ///
    /// GENERAL, not shuttle-coupled: the target is ANY spawned thing with a <see cref="CompTransporter"/> holding
    /// cargo, an Odyssey player shuttle (<c>Building_PassengerShuttle</c>), a transport pod with leftover load, a
    /// quest shuttle, or a modded shuttle-like thing. Nothing here reads <c>CompShuttle</c>. Pawns in the hold are
    /// NEVER pulled, they leave via their own boarding/exit mechanics (only non-pawn stacks are offered to the
    /// planner). The per-pull ladder is the shared pure <see cref="BulkUnloadCarrierPolicy.PlanNextPull"/> (the
    /// offer gate is <see cref="BulkUnloadTransporterPolicy.MayOffer"/>). The transporter is NOT reserved: like
    /// the load side, several haulers may work one hold in parallel (per-pull Contains/count guards make
    /// concurrent pulls safe), and the conflict seam, not a reservation, decides when to yield. NO try/catch on
    /// the transfer path; the JobDef carries NO <checkEncumbrance> (the fallback one-to-hands deliberately exceeds
    /// the soft ceiling).
    /// </summary>
    public class JobDriver_UnloadTransporterInBulk : JobDriver
    {
        private const TargetIndex TransporterInd = TargetIndex.A; // the transporter/shuttle being emptied
        private const TargetIndex ItemInd = TargetIndex.C;        // scratch: the hold stack currently selected

        private int pullLoops;
        // Units actually transferred this visit (both rungs). Gates the prioritized chain: a visit that moved
        // nothing earns no successor, so a stack that deterministically refuses to transfer (third-party container
        // locks, mid-transfer destruction) cannot loop walk-fail visits forever. In-flight only, a save/load
        // mid-visit just re-arms it.
        [System.NonSerialized] private int unitsMoved;
        private const int MaxPullLoops = 256; // backstop: bounds the select->transfer cycle

        // The single item taken into HANDS this visit (UNtagged). Captured so the finalize can ship it directly
        // via a HaulToStorage job. In-flight only, not scribed (a save mid-visit re-derives it: a handheld item
        // already in the carry tracker is picked up by the next unload trigger / vanilla haul anyway).
        [System.NonSerialized] private Thing handTail;

        // Reused per-pull scratch for the hold-stack view fed to the pure planner, replacing a fresh
        // List<CarrierStack> per select cycle (each select toil re-snapshots the hold). [ThreadStatic] + lazy-init
        // matches the repo's hook-reachable scratch convention; Cleared at the point of use, never trusted empty.
        // SAFETY: the select initAction builds + consumes this within one JumpToToil cycle (sequential within a
        // tick, no re-entrant job re-enters this scratch) before the next reuse.
        [System.ThreadStatic] private static List<BulkUnloadCarrierPolicy.CarrierStack> scratchStacks;

        private CompTransporter Transporter => job.GetTarget(TransporterInd).Thing?.TryGetComp<CompTransporter>();

        private static HaulersDreamSettings Settings => HaulersDreamMod.Settings;

        public override string GetReport()
        {
            var t = job.GetTarget(TransporterInd).Thing;
            return t != null
                ? "HaulersDream.UnloadTransporter.Report".Translate(t.LabelShort)
                : "HaulersDream.UnloadTransporter.Report".Translate("");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // NO reservation on the transporter, mirroring the load side (JobDriver_LoadTransportersInBulk
            // reserves nothing either) so SEVERAL haulers can empty one hold in parallel, the way several couriers
            // load it. That is safe without an exclusive claim: toils execute single-threaded within a tick, and
            // every pull re-checks reality at transfer time, if another unloader already removed the chosen stack
            // the Contains guard skips it; if they removed PART of it, count is clamped to the remainder. Each
            // hauler's mass math uses its own free space. The conflict seam (FailOn below), not a reservation,
            // owns the "someone else may use this hold" question.
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TransporterInd);
            // CONFLICT re-check as an END CONDITION: anything starting to load INTO this hold mid-visit (a load
            // lord, an HD bulk-load courier, a vanilla HaulToTransporter job, a ShipJob_Unload drain, or a freshly
            // recorded load session in its early pre-hauler window; see the shared seam) or a caravan forming
            // around the transporter means we must yield. Global fail conditions run before any toil's initAction,
            // so a state that changed between the offer and the job start (a save resumed mid-walk, a queued
            // order, another player pressing "Set to load") is caught on the FIRST tick: lastConflictScanTick
            // starts at int.MinValue, then the scan re-runs on the periodic cadence (O(pawns); within ~1s is fine
            // for a clean yield mid-visit).
            this.FailOn(ConflictScanDue);

            yield return Toils_Goto.GotoThing(TransporterInd, PathEndMode.Touch);

            // The select/transfer loop: pick the next stack to pull (backpack-first -> last/overflow to hands),
            // pause for the visual delay, transfer it, and jump back, emptying the hold in this one visit.
            Toil selectNext = ToilMaker.MakeToil("HD_Utib_SelectNext");
            Toil finalize = ToilMaker.MakeToil("HD_Utib_Finalize");

            selectNext.initAction = delegate
            {
                if (++pullLoops > MaxPullLoops) { JumpToToil(finalize); return; }
                var comp = Transporter;
                if (comp == null || !comp.parent.Spawned || comp.innerContainer == null
                    || comp.innerContainer.Count == 0)
                { JumpToToil(finalize); return; }

                // Build the pure planner's view of the hold's stacks (index, per-unit mass, count), SKIPPING
                // pawns (they exit on their own; yanking one into a backpack is not a thing) and dead entries.
                // The AUTONOMOUS pass ("Bulk unload all" flag, playerForced == false) additionally skips
                // FORBIDDEN stacks, the player's forbid is respected there. An explicit right-click ORDER
                // (playerForced == true) overrides forbiddance: the player pointed at this hold and said empty it.
                var hold = comp.innerContainer;
                var stacks = scratchStacks ?? (scratchStacks = new List<BulkUnloadCarrierPolicy.CarrierStack>());
                stacks.Clear();
                for (int i = 0; i < hold.Count; i++)
                {
                    var t = hold[i];
                    if (t == null || t.Destroyed || t is Pawn) continue;
                    if (!job.playerForced && t.IsForbidden(pawn)) continue;
                    stacks.Add(new BulkUnloadCarrierPolicy.CarrierStack(i, t.GetStatValue(StatDefOf.Mass), t.stackCount));
                }
                // Free carry mass is the hauler's live headroom (negative when overloaded -> 0 backpack room ->
                // the ladder routes to hands).
                float freeSpace = MassUtility.FreeSpace(pawn);
                var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, freeSpace);
                if (plan.ChosenIndex < 0 || plan.ChosenIndex >= hold.Count || plan.Count <= 0)
                { JumpToToil(finalize); return; }

                var thing = hold[plan.ChosenIndex];
                if (thing == null || thing.Destroyed) { JumpToToil(selectNext); return; }

                int count = plan.Count;
                bool toHands = plan.ToHands;
                // CORPSES ride in the backpack only when the player opted into corpses-in-pockets for bulk
                // hauling (bulkHaulCorpses), through the SAME shared policy the bulk-haul sweep uses — including
                // its disposal-only auto-strip carve-out (an autonomous trip stands down there, an ordered one is
                // explicit player intent). Without the opt-in they go to the HANDS rung, which vanilla hauling
                // fully supports (HaulToStorageJob handles them), so a corpse in the hold still unloads, just
                // carried the way vanilla carries bodies. Corpses are always stackCount 1.
                if (!toHands && thing is Corpse)
                {
                    var s = Settings;
                    bool corpseMayRide = CorpseSweepPolicy.CanSweepAsNeighbor(
                        bulkHaulEnabled: true, // this driver only runs when the transporter-unload feature is on
                        bulkHaulCorpses: s?.bulkHaulCorpses ?? true,
                        autoStripOnDisposalOnly: (s?.autoStripMode ?? AutoStripMode.AllHauls) == AutoStripMode.DisposalOnly,
                        playerOrdered: job.playerForced);
                    if (!corpseMayRide)
                    {
                        count = thing.stackCount;
                        toHands = true;
                    }
                }


                // Combat Extended adds a BULK dimension the pure (vanilla-mass) planner can't see, so the BACKPACK
                // pull defers to CE's own weight+bulk fit, EXACTLY as the carrier-unload sibling does
                // (JobDriver_UnloadCarrierInBulk). MaxFitCount returns int.MaxValue without CE, so this Min is a
                // no-op then, CE-absent behaviour is byte-identical. The to-hands rung is NEVER CE-clamped: the
                // carry tracker is exempt from the soft ceiling (that's why the JobDef has no checkEncumbrance).
                if (!toHands)
                {
                    int ceFit = CECompat.MaxFitCount(pawn, thing);
                    if (ceFit > 0)
                    {
                        count = System.Math.Min(count, ceFit);
                    }
                    else
                    {
                        // CE weight/bulk is exhausted even though vanilla mass thought there was backpack room.
                        // Route STRAIGHT to the HANDS rung via the dedicated fallback, NOT a zero-free-space
                        // re-plan: PullCountWithinFreeSpace admits massless stacks at any free-space value, so a
                        // re-plan could hand back another BACKPACK plan for a zero-vanilla-mass item whose CE
                        // bulk still doesn't fit, bypassing this very clamp. The hands rung is deliberately never
                        // CE-clamped (the carry tracker is exempt from the soft ceiling). CE-absent:
                        // MaxFitCount is int.MaxValue, so this branch never runs.
                        plan = BulkUnloadCarrierPolicy.PlanHandsFallback(stacks);
                        if (plan.ChosenIndex < 0 || plan.ChosenIndex >= hold.Count || plan.Count <= 0)
                        { JumpToToil(finalize); return; }
                        thing = hold[plan.ChosenIndex];
                        if (thing == null || thing.Destroyed) { JumpToToil(selectNext); return; }
                        count = plan.Count;
                        toHands = plan.ToHands;
                    }
                }
                // Stash the selection + the plan's destination on the job for the transfer toil.
                job.SetTarget(ItemInd, thing);
                job.count = count;
                pendingToHands = toHands;
            };
            selectNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return selectNext;

            // A short, settings-driven pause so the unload reads as a deliberate per-stack action, the SAME
            // shipped pacing the carrier unload uses (visualUnloadDelay, vanilla-unload cadence). 0 = instant.
            // Resolved once at job start (the JobDriver reads defaultCompleteMode/defaultDuration when the toil
            // STARTS, before initAction runs, so the delay is fixed for the whole job, fine: a setting change
            // mid-job only affects the next job).
            int delay = Settings?.visualUnloadDelay ?? 0;
            Toil wait = delay > 0 ? Toils_General.Wait(delay) : Toils_General.Label();
            yield return wait;

            Toil transfer = ToilMaker.MakeToil("HD_Utib_Transfer");
            transfer.initAction = delegate
            {
                var comp = Transporter;
                var thing = job.GetTarget(ItemInd).Thing;
                if (comp == null || comp.innerContainer == null || thing == null || thing.Destroyed)
                { JumpToToil(selectNext); return; }
                var hold = comp.innerContainer;
                if (!hold.Contains(thing)) { JumpToToil(selectNext); return; }

                int count = job.count > 0 ? job.count : thing.stackCount;
                count = System.Math.Min(count, thing.stackCount);
                if (count <= 0) { JumpToToil(finalize); return; }

                if (pendingToHands)
                {
                    // Overflow / last stack -> the carry tracker, UNtagged. It ships directly via the finalize's
                    // HaulToStorage job (tagging it would double-ship it through HD's inventory unload pass too).
                    int moved = hold.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, count, out Thing movedThing);
                    if (moved > 0 && movedThing != null)
                    {
                        handTail = movedThing;
                        unitsMoved += moved;
                        comp.Notify_ThingRemoved(thing); // mass cache reset, mirrors vanilla ShipJob_Unload
                    }
                    // One item to hands is enough for this visit (the carry tracker holds one stack); finalize.
                    JumpToToil(finalize);
                    return;
                }

                // Backpack: into the hauler's inventory, tagged in HD's comp so the normal unload ships it.
                // canMergeWithExistingStacks:false (matching the carrier sibling's transfer), a task item must
                // NOT merge into the pawn's personal kit. If it merged, movedBpThing would be the MERGED
                // personal-kit stack, RegisterHauledItem would tag it, and the comp's same-def self-heal would
                // re-tag every matching personal stack -> non-keep-stock the pawn legitimately carries gets
                // shipped to storage.
                var pawnInner = pawn.inventory?.innerContainer;
                if (pawnInner == null) { JumpToToil(finalize); return; }
                int movedBp = hold.TryTransferToContainer(thing, pawnInner, count, out Thing movedBpThing, canMergeWithExistingStacks: false);
                if (movedBp > 0 && movedBpThing != null)
                {
                    unitsMoved += movedBp;
                    comp.Notify_ThingRemoved(thing); // mass cache reset, mirrors vanilla ShipJob_Unload
                    var compHauled = pawn.GetComp<CompHauledToInventory>();
                    compHauled?.RegisterHauledItem(movedBpThing);
                }
                else
                {
                    // Nothing moved (another mod holding the stack, or a full non-mergeable inventory) -> end the
                    // visit rather than spin (the backstop loop count also bounds this). The hold keeps the
                    // stack; the player can re-order later.
                    JumpToToil(finalize);
                    return;
                }
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
                // "Bulk unload all" bookkeeping: when the hold now holds nothing pullable, clear the flag so the
                // autonomous workgiver scan goes quiet (a passenger-only remainder also counts as done, pawns
                // leave on their own). A still-non-empty hold keeps the flag up and the next think cycle sends a
                // hauler back for another trip.
                var compEnd = Transporter;
                if (compEnd != null)
                    HaulersDreamGameComponent.Instance?.BulkUnloadAllClearIfNothingPullable(compEnd);
                // Backpack stock is tagged -> HD's normal storage unload pass ships it (forced recovery). This
                // MUST run BEFORE the prioritized follow-up below queues the next visit: CheckIfShouldUnload
                // places the forced UnloadInventory behind already-queued "real work", so a chained visit queued
                // first would push the storage drop-off behind IT — the pawn would hoard its whole backpack
                // across every chained trip and make one giant delivery at the end. Order here is the contract:
                // queue reads [hand-tail haul, storage unload, next visit].
                PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true, behindQueuedWork: true);
                // PRIORITIZED FOLLOW-UP (the "prioritize bulk unloading over other work" promise): an ORDERED
                // visit chains itself while the hold still holds something pullable. The next visit is queued
                // BEHIND the storage drop-off above, so this same pawn walks the loot in and comes straight back
                // to the shuttle instead of re-rolling its priorities at the next think cycle. Autonomous visits
                // do NOT chain (their repeat is the workgiver's emergent multi-trip loop). Gates mirror the offer:
                // no chaining into a conflict or an open load manifest, never past an emptied hold, and flipping
                // "Bulk unload all" off stops the chain with the current trip. unitsMoved guards the pathological
                // zero-progress loop (a stack that deterministically refuses to transfer would otherwise chain
                // walk-fail visits forever): only a visit that actually moved something earns a successor.
                var compChain = Transporter;
                if (job.playerForced && unitsMoved > 0 && compChain != null
                    && BulkUnloadTransporterGate.UnloadFlagActive(compChain)
                    && !BulkUnloadTransporterGate.ConflictActive(compChain)
                    && !BulkUnloadTransporterGate.LoadSessionHasOpenManifest(compChain)
                    && BulkUnloadTransporterGate.HasPullableContents(compChain))
                {
                    var nextVisit = JobMaker.MakeJob(
                        HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk, job.GetTarget(TransporterInd).Thing);
                    nextVisit.playerForced = true;
                    pawn.jobs.jobQueue.EnqueueLast(nextVisit, JobTag.Misc);
                }
                EndJobWith(JobCondition.Succeeded);
            };
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalize;
        }

        // Carried between the select and transfer toils (which stack goes to hands vs backpack). SCRIBED like its
        // companions (the ItemInd target + job.count are scribed by the base): a save/load mid-visit resumes at the
        // preserved toil index, and an unscribed flag would resume false, rerouting a hands-picked overflow stack
        // through the backpack channel (a transient over-ceiling trip).
        private bool pendingToHands;

        // The conflict scan's last run (TicksGame). Starts at int.MinValue so the FIRST FailOn evaluation scans
        // immediately instead of waiting out the interval, a conflict that landed between offer and job start is
        // caught before the first pull. In-flight only: a fresh driver after a save re-scans at once either way.
        [System.NonSerialized] private int lastConflictScanTick = int.MinValue;

        private const int LordCheckInterval = 60; // how often the conflict seam re-scans (ticks)

        private bool ConflictScanDue()
        {
            int ticks = Find.TickManager.TicksGame;
            if (ticks - lastConflictScanTick < LordCheckInterval)
                return false;
            lastConflictScanTick = ticks;
            // A recorded load session with an OPEN MANIFEST (the player pressed "Set to load" while we were
            // walking; counts even in its early pre-hauler window, when the flow checks below still see nothing).
            // A session whose manifest has since drained does NOT yield: nothing is committed INTO the hold
            // anymore, so finishing the pull-out is correct.
            if (BulkUnloadTransporterGate.LoadSessionHasOpenManifest(Transporter))
                return true;
            return BulkUnloadTransporterGate.ConflictActive(Transporter);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // The backstop loop count survives a save/load like the carrier sibling's does ("hdUcibPullLoops"), 
            // without it a resumed visit restarts the 256-cycle budget. pendingToHands is scribed above (field
            // initializer), matching the scribed ItemInd/count it pairs with.
            Scribe_Values.Look(ref pullLoops, "hdUtibPullLoops", 0);
            Scribe_Values.Look(ref pendingToHands, "hdUtibToHands", false);
        }
    }
}
