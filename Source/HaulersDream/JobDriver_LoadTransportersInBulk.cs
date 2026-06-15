using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-load a transporter/shuttle GROUP — the net-new transporter counterpart to
    /// <see cref="JobDriver_LoadPackAnimal"/>. Same three-phase shape (sweep nearby ground stacks into tagged
    /// inventory → walk to the transporter ONCE → deposit every tagged stack the group still needs), but the
    /// deposit goes into a transporter's <c>innerContainer</c> via <c>TryTransferToContainer</c>. The ThingOwner
    /// add auto-fires <c>Notify_ThingAdded → CompTransporter.SubtractFromToLoadList</c>; HD's §F intercept makes
    /// that decrement PRECISE (behind the per-thread <see cref="Global.IsExecutingManagedUnload"/> flag, set in a
    /// try/finally around each transfer — reset even on throw, rethrow, no suppression).
    ///
    /// Concurrency: the CLAIM is recorded in <see cref="Notify_Starting"/> (so a built-but-never-started probe
    /// never claims); on every non-Success end the claim is RELEASED and the carried task item is SALVAGED back
    /// into inventory (re-tagged, rides HD's normal unload) — never dropped on a temp map, never stuck.
    /// </summary>
    public class JobDriver_LoadTransportersInBulk : JobDriver
    {
        private const TargetIndex TransporterInd = TargetIndex.A; // primary transporter (deposit dest)
        private const TargetIndex StackInd = TargetIndex.B;       // scratch: the ground stack being swept

        private int loadIndex;
        private int depositLoops;
        private int passes;
        private const int MaxDepositLoops = 64;
        private const int MaxPasses = 64;

        // Reused snapshot of the tagged set for the deposit loop + salvage finish action, replacing a fresh
        // List<Thing>(GetHashSet()) per deposit cycle / end. The snapshot is required (GetHashSet self-heals and the
        // loop calls Deregister, mutating the underlying set mid-iterate); reusing one [ThreadStatic] buffer makes the
        // steady per-deposit alloc 0. Cleared at use, never trusted empty. SAFETY: each consumer runs to completion in
        // one toil initAction / finish action (sequential on the main thread, no re-entrant tagged-snapshot) before
        // the next reuse.
        [System.ThreadStatic] private static List<Thing> scratchTagged;

        // Resolved on start (Notify_Starting). In-flight only — re-resolved from the live transporter on load.
        [System.NonSerialized] private LoadTransportersAdapter adapter;
        // Set true on a chaining/cleanup end so the finish action RETAINS the claim (no thrash). Currently always
        // false (no chaining in Stage 2), but kept as the documented retain hook for a future smooth-chain.
#pragma warning disable CS0649
        [System.NonSerialized] private bool retainClaimOnEnd;
#pragma warning restore CS0649

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private CompTransporter Transporter => job.GetTarget(TransporterInd).Thing?.TryGetComp<CompTransporter>();

        private static HaulersDreamSettings Settings => HaulersDreamMod.Settings;
        private static int AiUpdateInterval => Mathf.Max(10, Settings?.bulkLoadAiUpdateFrequency ?? 60);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLtibLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLtibDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLtibPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadTransporter.Report".Translate();

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            // Resolve the adapter (copies the group OUT of the shared static buffer) and RECORD the claim now — the
            // job has actually started, so the ledger reflects only real in-flight reservations.
            var comp = Transporter;
            adapter = comp != null ? LoadTransportersAdapter.TryCreate(comp) : null;
            if (adapter != null)
                HaulersDreamGameComponent.Instance?.LoadClaim(pawn, job, adapter);
        }

        private LoadTransportersAdapter EnsureAdapter()
        {
            if (adapter != null)
                return adapter;
            var comp = Transporter;
            adapter = comp != null ? LoadTransportersAdapter.TryCreate(comp) : null;
            return adapter;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Bulk-haul reservation shape: queue[0] strict, the rest best-effort. NEVER reserve the transporter
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
            this.FailOnDespawnedOrNull(TransporterInd);

            Toil fillStart = Toils_General.Label();
            Toil depositStart = Toils_General.Label();
            Toil loopCheck = ToilMaker.MakeToil("HD_Ltib_LoopCheck");

            // ============ FILL: sweep queued ground stacks into tagged inventory, up to the carry ceiling ============
            yield return fillStart;

            Toil sweepDecide = ToilMaker.MakeToil("HD_Ltib_SweepDecide");
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

            Toil sweepGoto = ToilMaker.MakeToil("HD_Ltib_SweepGoto");
            sweepGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(sweepDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            sweepGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return sweepGoto;

            Toil sweepTake = ToilMaker.MakeToil("HD_Ltib_SweepTake");
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

            // ============ DEPOSIT: walk to the transporter ONCE, then transfer every needed tagged stack ============
            yield return depositStart;

            Toil findTransporter = ToilMaker.MakeToil("HD_Ltib_FindTransporter");
            findTransporter.initAction = delegate
            {
                if (++depositLoops > MaxDepositLoops) { JumpToToil(loopCheck); return; }
                var comp = Transporter;
                if (comp == null || comp.parent == null || !comp.parent.Spawned) { JumpToToil(loopCheck); return; }
                // Anything left to deposit? (Tagged surplus the group still needs.)
                if (!HasDepositableForGroup()) { JumpToToil(loopCheck); return; }
            };
            findTransporter.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findTransporter;

            Toil gotoTransporter = Toils_Goto.GotoThing(TransporterInd, PathEndMode.Touch);
            gotoTransporter.FailOnDespawnedOrNull(TransporterInd);
            yield return gotoTransporter;

            Toil deposit = ToilMaker.MakeToil("HD_Ltib_Deposit");
            deposit.initAction = delegate
            {
                var comp = Transporter;
                var inner = pawn.inventory?.innerContainer;
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var adp = EnsureAdapter();
                if (comp == null || comp.parent == null || !comp.parent.Spawned || inner == null || hcomp == null || adp == null)
                { JumpToToil(loopCheck); return; }

                // Re-validate the carried items are still needed (mid-trip redirect within the group) every
                // AiUpdateInterval ticks — a no-op redirect just continues; a fully-stale load falls to loopCheck.
                TransportLoadTargetRedirect.ValidateAndRedirectCurrentTarget(this, adp);

                bool movedAny = false;
                var tagged = scratchTagged ?? (scratchTagged = new List<Thing>());
                tagged.Clear();
                tagged.AddRange(hcomp.GetHashSet());
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
                    // Deposit into ONE specific member, clamped to THAT member's remaining for the def — NOT the
                    // group total. The member's auto-fired SubtractFromToLoadList only subtracts what its own
                    // leftToLoad entry held, so depositing more than one member wants into its container would
                    // under-count the manifest (and over-load that pod). The findTransporter loop re-enters to
                    // service the next member's share on the following pass. The MANIFEST is authoritative — NOT the
                    // group mass cap (vanilla lets a pod load past mass capacity, shown red; the trip-mass budget was
                    // already applied during the sweep).
                    var member = adp.ActiveMemberFor(thing);
                    if (member == null)
                        continue; // no member still wants this exact variant (another pawn filled it) — leave it tagged
                    int memberRemaining = LoadTransportersAdapter.MemberRemainingFor(member, thing);
                    int count = System.Math.Min(surplus, memberRemaining);
                    if (count <= 0)
                        continue;
                    var destInner = member.innerContainer;
                    if (destInner == null)
                        continue;

                    int moved;
                    // Set the per-thread flag so the SubtractFromToLoadList intercept does the PRECISE decrement.
                    // try/finally resets it even on throw; the throw RETHROWS (no suppression).
                    Global.IsExecutingManagedUnload = true;
                    try
                    {
                        moved = inner.TryTransferToContainer(thing, destInner, count, out Thing _, canMergeWithExistingStacks: false);
                    }
                    finally
                    {
                        Global.IsExecutingManagedUnload = false;
                    }
                    if (moved > 0)
                    {
                        movedAny = true;
                        HaulersDreamGameComponent.Instance?.LoadNotifyDeposited(pawn, adp, thing.def, moved);
                        if (!inner.Contains(thing))
                            hcomp.Deregister(thing); // fully moved -> drop the tag; a partial leaves the remainder tagged
                    }
                }
                if (!movedAny) { JumpToToil(loopCheck); return; }
                JumpToToil(findTransporter); // more to deposit (this/another member) or fall to loopCheck (drained)
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
                if (!retainClaimOnEnd)
                    HaulersDreamGameComponent.Instance?.LoadReleaseClaimsForPawn(pawn);
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

        /// <summary>Units the group's manifest still wants for <paramref name="item"/> — summed across members using,
        /// PER MEMBER, the entry vanilla's <see cref="TransferableUtility.TransferableMatchingDesperate"/> (in
        /// <c>PodsOrCaravanPacking</c> mode) would decrement (the SAME 3-tier ladder — identity → <c>TransferAsOne</c>
        /// variant → def-only fallback — that <c>SubtractFromToLoadList</c> and the per-member deposit clamp
        /// <see cref="LoadTransportersAdapter.MemberRemainingFor"/> use). Summing per member (not over the flattened
        /// transferable list) matches how the deposit actually drains the group: each member's own
        /// <c>SubtractFromToLoadList</c> resolves ONE entry against ITS OWN <c>leftToLoad</c>, so an off-quality
        /// fungible item credits each member's def entry via Tier-3 exactly as the deposit will. This keeps the deposit
        /// pre-gate (walk-to-transporter decision) in lock-step with the deposit path — a strict-Tier-2 pre-gate would
        /// wrongly skip the trip for a fungible variant the deposit would still load.</summary>
        private static int GroupRemainingFor(LoadTransportersAdapter adp, Thing item)
        {
            if (adp == null || item?.def == null)
                return 0;
            int sum = 0;
            var group = adp.Group;
            for (int i = 0; i < group.Count; i++)
                sum += LoadTransportersAdapter.MemberRemainingFor(group[i], item);
            return sum;
        }

        /// <summary>True if the pawn holds any tagged surplus stack of a variant the group still wants.</summary>
        private bool HasDepositableForGroup()
        {
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            var adp = EnsureAdapter();
            if (hcomp == null || inner == null || adp == null)
                return false;
            foreach (var t in hcomp.PeekHashSet())
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue;
                if (GroupRemainingFor(adp, t) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>How often (ticks) the carried-item re-validation runs — exposed for the redirect helper.</summary>
        internal int RevalidateInterval => AiUpdateInterval;
    }
}
