using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Shared three-phase scaffold for the ledger-backed bulk-load JobDrivers (transporter group, map portal, VF
    /// vehicle). The three drivers share an IDENTICAL shape — sweep nearby ground stacks into tagged inventory → walk
    /// to the deposit target ONCE → deposit every tagged stack the target still needs → loop — diverging ONLY in:
    /// the concrete adapter (<see cref="BuildLoadable"/>), the "anything left?" pre-gate
    /// (<see cref="HasDepositable"/>), the target-still-valid spawn check (<see cref="FindTargetStillValid"/>), the
    /// per-thing deposit core (<see cref="DepositOne"/>), and a few optional hooks (an extra claim, a pre-loop
    /// redirect, an extra release). This base pulls the byte-identical scaffold up so each subclass is small and the
    /// conservation-critical deposit cores stay copied verbatim per-family.
    ///
    /// Concurrency: the CLAIM is recorded in <see cref="Notify_Starting"/> (so a built-but-never-started probe never
    /// claims); on every non-Success end the claim is RELEASED and the carried task item is SALVAGED back into
    /// inventory (re-tagged, rides HD's normal unload) — never dropped on a temp map, never stuck.
    ///
    /// SAVE-COMPAT: the three scribed int fields (<see cref="loadIndex"/>/<see cref="depositLoops"/>/
    /// <see cref="passes"/>) are deliberately NOT scribed here — each subclass scribes them with its OWN historic
    /// labels in its <c>ExposeData</c> (the labels differ per driver and MUST NOT change). The base
    /// <see cref="ExposeData"/> only chains <c>base.ExposeData()</c>.
    /// </summary>
    public abstract class JobDriver_LoadInBulkBase : JobDriver
    {
        protected const TargetIndex TargetInd = TargetIndex.A; // primary deposit target
        protected const TargetIndex StackInd = TargetIndex.B;  // scratch: the ground stack being swept

        protected int loadIndex;
        protected int depositLoops;
        protected int passes;
        protected const int MaxDepositLoops = 64;
        protected const int MaxPasses = 64;

        // Reused snapshot of the tagged set for the deposit loop + salvage finish action, replacing a fresh
        // List<Thing>(GetHashSet()) per deposit cycle / end. The snapshot is required (GetHashSet self-heals and the
        // loop calls Deregister, mutating the underlying set mid-iterate); reusing one [ThreadStatic] buffer makes the
        // steady per-deposit alloc 0. Cleared at use, never trusted empty. SAFETY: each consumer runs to completion in
        // one toil initAction / finish action (sequential on the main thread, no re-entrant tagged-snapshot) before
        // the next reuse.
        [System.ThreadStatic] protected static List<Thing> scratchTagged;

        // Resolved on start (Notify_Starting). In-flight only — re-resolved from the live target on load.
        [System.NonSerialized] protected IManagedLoadable adapter;
        // Set true on a chaining/cleanup end so the finish action RETAINS the claim (no thrash). Currently always
        // false (no chaining in Stage 2), but kept as the documented retain hook for a future smooth-chain.
#pragma warning disable CS0649
        [System.NonSerialized] protected bool retainClaimOnEnd;
#pragma warning restore CS0649

        protected ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();

        protected static HaulersDreamSettings Settings => HaulersDreamMod.Settings;

        public override void ExposeData()
        {
            base.ExposeData();
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            // Resolve the adapter and RECORD the claim now — the job has actually started, so the ledger reflects only
            // real in-flight reservations.
            adapter = BuildLoadable();
            if (adapter != null)
            {
                HaulersDreamGameComponent.Instance?.LoadClaim(pawn, job, adapter);
                OnExtraClaim();
            }
        }

        protected IManagedLoadable EnsureAdapter()
        {
            if (adapter != null)
                return adapter;
            adapter = BuildLoadable();
            return adapter;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Bulk-haul reservation shape: queue[0] strict, the rest best-effort. NEVER reserve the deposit target
            // (re-found each deposit). A deposit-only job (empty queue) reserves nothing.
            var queue = job.GetTargetQueue(StackInd);
            if (queue == null || queue.Count == 0)
                return true;
            if (!pawn.Reserve(queue[0], job, 1, -1, null, errorOnFailed))
                return false;
            pawn.ReserveAsManyAsPossible(queue, job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            AddTopLevelFailConditions();

            Toil fillStart = Toils_General.Label();
            Toil depositStart = Toils_General.Label();
            Toil loopCheck = ToilMaker.MakeToil(ToilPrefix + "_LoopCheck");

            // ============ FILL: sweep queued ground stacks into tagged inventory, up to the carry ceiling ============
            yield return fillStart;

            Toil sweepDecide = ToilMaker.MakeToil(ToilPrefix + "_SweepDecide");
            sweepDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                if (queue == null || queue.Count == 0 || loadIndex >= queue.Count) { JumpToToil(depositStart); return; }
                float ceiling = PackAnimalLoad.CeilingKg(pawn, Settings);
                bool roomLeft = float.IsPositiveInfinity(ceiling)
                                || MassUtility.GearAndInventoryMass(pawn) < ceiling - 0.0001f;
                if (roomLeft && CECompat.IsActive && CECompat.AvailableBulk(pawn) <= 0f)
                    roomLeft = false;
                if (!roomLeft) { JumpToToil(depositStart); return; }
                while (loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    bool valid = t != null && t.Spawned && !t.IsForbidden(pawn)
                                 && !(t.ParentHolder is Pawn_InventoryTracker)
                                 && counts != null && loadIndex < counts.Count && counts[loadIndex] > 0;
                    if (valid && !pawn.Map.reservationManager.ReservedBy(t, pawn, job)
                        && (!pawn.CanReserve(t) || !pawn.Reserve(t, job, errorOnFailed: false)))
                        valid = false;
                    if (valid) break;
                    loadIndex++;
                }
                if (loadIndex >= queue.Count) { JumpToToil(depositStart); return; }
                job.SetTarget(StackInd, queue[loadIndex].Thing);
            };
            sweepDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepDecide;

            Toil sweepGoto = ToilMaker.MakeToil(ToilPrefix + "_SweepGoto");
            sweepGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(sweepDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            sweepGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return sweepGoto;

            Toil sweepTake = ToilMaker.MakeToil(ToilPrefix + "_SweepTake");
            sweepTake.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                var counts = job.countQueue;
                int planned = counts != null && loadIndex < counts.Count ? counts[loadIndex] : 0;
                if (t == null || !t.Spawned || planned <= 0 || t.IsForbidden(pawn)) { loadIndex++; JumpToToil(sweepDecide); return; }
                int count = BulkHaulPolicy.CountWithinCeiling(PackAnimalLoad.CeilingKg(pawn, Settings),
                    MassUtility.GearAndInventoryMass(pawn), t.GetStatValue(StatDefOf.Mass),
                    System.Math.Min(planned, t.stackCount));
                count = System.Math.Min(count, CECompat.MaxFitCount(pawn, t));
                if (count <= 0) { JumpToToil(depositStart); return; }
                int groundBefore = t.stackCount;
                var split = t.SplitOff(count);
                var inv = Inv;
                if (inv != null && inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    if (comp != null) { comp.RegisterHauledItem(split); comp.NotifyYieldPicked(); }
                    if (!split.Spawned) split.Position = pawn.Position;
                    if (counts != null && loadIndex < counts.Count) counts[loadIndex] = planned - count;
                    bool itemDone = counts == null || loadIndex >= counts.Count || counts[loadIndex] <= 0 || count >= groundBefore;
                    if (itemDone) loadIndex++;
                }
                else if (split != null && !split.Destroyed && !split.Spawned)
                {
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    loadIndex++;
                }
                JumpToToil(sweepDecide);
            };
            sweepTake.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepTake;

            // ============ DEPOSIT: walk to the target ONCE, then transfer every needed tagged stack ============
            yield return depositStart;

            Toil findTarget = ToilMaker.MakeToil(ToilPrefix + "_FindTarget");
            findTarget.initAction = delegate
            {
                if (++depositLoops > MaxDepositLoops) { JumpToToil(loopCheck); return; }
                if (!FindTargetStillValid()) { JumpToToil(loopCheck); return; }
                // Anything left to deposit? (Tagged surplus the target still needs.)
                if (!HasDepositable()) { JumpToToil(loopCheck); return; }
            };
            findTarget.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findTarget;

            Toil gotoTarget = Toils_Goto.GotoThing(TargetInd, PathEndMode.Touch);
            gotoTarget.FailOnDespawnedOrNull(TargetInd);
            yield return gotoTarget;

            Toil deposit = ToilMaker.MakeToil(ToilPrefix + "_Deposit");
            deposit.initAction = delegate
            {
                var inner = pawn.inventory?.innerContainer;
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var adp = EnsureAdapter();
                if (inner == null || hcomp == null || adp == null || !FindTargetStillValid())
                { JumpToToil(loopCheck); return; }

                // Per-family pre-loop hook (transporter: mid-trip redirect within the group). No-op for others.
                OnPreDepositLoop(adp);

                bool movedAny = false;
                var tagged = scratchTagged ?? (scratchTagged = new List<Thing>());
                tagged.Clear();
                tagged.AddRange(hcomp.GetHashSet());
                // MP determinism: process tagged stacks in thingIDNumber order so a capacity-bound loop deposits/drops the same subset on every client.
                tagged.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
                for (int i = 0; i < tagged.Count; i++)
                {
                    var thing = tagged[i];
                    if (thing == null || thing.Destroyed || !inner.Contains(thing))
                        continue;
                    if (!pawn.CanReserve(thing))
                        continue;
                    int surplus = InventorySurplus.SurplusOf(pawn, thing);
                    if (surplus <= 0)
                        continue; // personal kit stays with the pawn
                    DepositOne(thing, inner, hcomp, adp, ref movedAny);
                }
                if (!movedAny) { JumpToToil(loopCheck); return; }
                JumpToToil(findTarget); // more to deposit or fall to loopCheck (drained)
            };
            deposit.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deposit;

            // ============ LOOP: deposit freed inventory room; refill if queued stacks remain ============
            loopCheck.initAction = delegate
            {
                if (++passes > MaxPasses) { EndJobWith(JobCondition.Incompletable); return; }
                depositLoops = 0;
                var queue = job.targetQueueB;
                if (queue != null && loadIndex < queue.Count) { JumpToToil(fillStart); return; }
                EndJobWith(JobCondition.Succeeded);
            };
            loopCheck.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loopCheck;

            // Release the claim + salvage any still-carried task items on every non-Success end (idempotent).
            AddFinishAction(delegate (JobCondition condition)
            {
                // B4 continuous loading (opt-in, default OFF): on a player-forced SUCCESS, chain to the nearest OTHER
                // target of the same family that still has work (dedup excludes THIS target). Byte-inert when the
                // setting is off (ShouldChain short-circuits). The chained job targets a different ledger key, so we
                // still RELEASE this target's claims below (retainClaimOnEnd stays false — retaining would leak the
                // finished claim); the chained job re-claims its own target in its Notify_Starting next tick.
                if (ContinuousLoad.ShouldChain(condition, job))
                    ContinuousLoad.TryChainFrom(pawn, EnsureAdapter());
                if (!retainClaimOnEnd)
                {
                    HaulersDreamGameComponent.Instance?.LoadReleaseClaimsForPawn(pawn);
                    OnReleaseExtraClaims();
                }
                // Re-tag survivors (idempotent self-heal) so any swept-but-undeposited stacks ride HD's normal unload.
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var inner = pawn.inventory?.innerContainer;
                if (hcomp != null && inner != null)
                {
                    var snapshot = scratchTagged ?? (scratchTagged = new List<Thing>());
                    snapshot.Clear();
                    snapshot.AddRange(hcomp.GetHashSet());
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        var t = snapshot[i];
                        if (t != null && !t.Destroyed && inner.Contains(t))
                            hcomp.RegisterHauledItem(t);
                    }
                }
            });
        }

        // ============================== Per-family hooks ==============================

        /// <summary>The <c>ToilMaker.MakeToil</c> name prefix for this driver's toils (e.g. "HD_Ltib").</summary>
        protected abstract string ToilPrefix { get; }

        /// <summary>Build the concrete adapter from the live deposit target (re-resolves on load). Null when the
        /// target is gone.</summary>
        protected abstract IManagedLoadable BuildLoadable();

        /// <summary>True if the pawn holds any tagged surplus stack of a variant the target still wants — the cheap
        /// "anything to deposit?" pre-gate.</summary>
        protected abstract bool HasDepositable();

        /// <summary>True if the deposit target is still spawned/valid (the per-family find-target spawn check).</summary>
        protected abstract bool FindTargetStillValid();

        /// <summary>Deposit ONE surviving tagged surplus stack into the target — the conservation-critical per-family
        /// core copied VERBATIM from each original deposit toil. Sets <paramref name="movedAny"/> true when anything
        /// physically moved.</summary>
        protected abstract void DepositOne(Thing thing, ThingOwner inner, CompHauledToInventory hcomp, IManagedLoadable adp, ref bool movedAny);

        /// <summary>Per-family pre-loop hook run once inside the deposit toil before the per-thing loop (transporter:
        /// mid-trip group redirect). No-op by default.</summary>
        protected virtual void OnPreDepositLoop(IManagedLoadable adp) { }

        /// <summary>Per-family extra claim recorded in <see cref="Notify_Starting"/> after the ledger claim (vehicle:
        /// the VF VehicleReservationManager claim). No-op by default.</summary>
        protected virtual void OnExtraClaim() { }

        /// <summary>Per-family extra claim release run inside the finish action when the ledger claims are released
        /// (vehicle: VF VRM release). No-op by default.</summary>
        protected virtual void OnReleaseExtraClaims() { }

        /// <summary>Top-level fail conditions registered at the head of <see cref="MakeNewToils"/>. All three drivers
        /// fail on the deposit target despawning/nulling.</summary>
        protected virtual void AddTopLevelFailConditions()
        {
            this.FailOnDespawnedOrNull(TargetInd);
        }
    }
}
