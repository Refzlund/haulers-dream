using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The SAFE "fewer trips" fix for automatic crafting bills. Vanilla DoBill hand-carries ingredients one stack
    /// per round-trip; the earlier attempt to re-drive the whole craft under a custom job def was retired because
    /// vanilla only records placed ingredients (<c>job.placedThings</c>) for its own <c>JobDefOf.DoBill</c> — a
    /// custom-def craft could finish without consuming ingredients (duplication). This driver therefore does the
    /// ONE thing vanilla can't — the gathering — and lets vanilla do 100% of the crafting:
    ///
    ///   1. GATHER: walk the bill's chosen floor ingredient stacks and load them ALL into inventory in one sweep
    ///      (overweight by default — the player opted into the slowdown; strict-carry-weight honours the ceiling),
    ///      TAGGING each loaded stack in <see cref="CompHauledToInventory"/>.
    ///   2. Walk to the bench and END.
    ///   3. The pawn's next work scan re-issues the SAME bill; the ingredient chooser (via the existing
    ///      shared-inventory bill patch, F3c) now sees the pawn's tagged carried stock — and since the chooser sorts
    ///      candidates by <c>PositionHeld</c> distance to the bench and the pawn is STANDING at the bench, the
    ///      carried stacks rank first (decompile-verified comparator). Vanilla's own DoBill then pulls them from
    ///      inventory (its collection natively supports carried things), records placedThings correctly, and crafts.
    ///
    /// Item safety by construction: this job only MOVES things (TakeToInventory = SplitOff+TryAdd) and tags them, so
    /// the unload pass reclaims anything left over if the craft never happens (bill deleted, pawn interrupted). It
    /// never touches the recipe flow, so duplication is impossible. If nothing could be loaded, a short per-pawn
    /// cooldown stops the conversion from re-issuing (the bill then runs vanilla multi-trip — fail-open).
    /// </summary>
    public class JobDriver_BillPrepGather : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;
        private const TargetIndex StackInd = TargetIndex.B;

        private int loadIndex;
        private bool loadedAnything;
        private ThingDef loadDef;
        private int plannedTake;
        private int invCountBeforeTake; // def-count snapshot just before vanilla's TakeToInventory add (transient, like plannedTake)

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdPrepLoadIndex", 0);
            Scribe_Values.Look(ref loadedAnything, "hdPrepLoadedAnything", false);
            Scribe_Defs.Look(ref loadDef, "hdPrepLoadDef");
        }

        public override string GetReport() => "HaulersDream.PrepGather.Report".Translate();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reserve the bench so no other crafter takes the bill while we gather for it; reserve the stacks so
            // no hauler walks off with them mid-sweep.
            if (!pawn.Reserve(job.GetTarget(BenchInd), job, 1, -1, null, errorOnFailed))
                return false;
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(StackInd), job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BenchInd);
            this.FailOn(() => job.bill == null || job.bill.DeletedOrDereferenced || job.bill.suspended);

            // Loop guard: a sweep that loaded NOTHING must not convert again immediately (it would ping-pong
            // prep→DoBill→prep forever); the cooldown lets the bill run vanilla multi-trip instead. Fail-open.
            AddFinishAction(delegate
            {
                if (!loadedAnything)
                    BillPrepTracker.NoteEmptyRun(pawn);
            });

            Toil gotoBench = Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil loadDecide = ToilMaker.MakeToil("HD_Prep_LoadDecide");
            loadDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                var settings = HaulersDreamMod.Settings;
                // Overload slider at "Off" counts as strict too: never overweight a pawn that the player said
                // should never overload (the StatPart debuff doesn't apply at Off — free overweight otherwise).
                // Combat Extended counts as strict as well (OverloadGate.NoOverload — CE's caps govern).
                bool strict = settings != null && OverloadGate.NoOverload(settings);
                while (queue != null && loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    bool valid = t != null && t.Spawned && !t.IsForbidden(pawn)
                                 && !(t.ParentHolder is Pawn_InventoryTracker)
                                 && counts != null && loadIndex < counts.Count && counts[loadIndex] > 0
                                 && (pawn.CanReserve(t) || pawn.Map.reservationManager.ReservedBy(t, pawn, job))
                                 // strict mode: don't walk to a stack the ceiling won't let us take from —
                                 // a full pawn would otherwise tour every stack taking 0 (and re-tour each cooldown)
                                 && (!strict || OverloadGate.CountToPickUp(pawn, t, settings) > 0);
                    if (valid)
                        break;
                    loadIndex++;
                }
                if (queue == null || loadIndex >= queue.Count) { JumpToToil(gotoBench); return; }
                loadDef = queue[loadIndex].Thing.def;
                job.SetTarget(StackInd, queue[loadIndex].Thing);
            };
            loadDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadDecide;

            Toil loadGoto = ToilMaker.MakeToil("HD_Prep_LoadGoto");
            loadGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(loadDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            loadGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return loadGoto;

            // No <checkEncumbrance> on the JobDef: by default the pawn loads the bill's full count for this stack
            // OVERWEIGHT (the speed debuff is the accepted price of one trip); strict mode honours the ceiling.
            yield return Toils_Haul.TakeToInventory(StackInd, () =>
            {
                plannedTake = 0;
                var st = job.GetTarget(StackInd).Thing;
                if (st == null || !st.Spawned) return 0;
                var counts = job.countQueue;
                int need = (counts != null && loadIndex < counts.Count) ? counts[loadIndex] : 0;
                if (need <= 0) return 0;
                var s = HaulersDreamMod.Settings;
                if (s != null && OverloadGate.NoOverload(s))
                    need = Mathf.Min(need, OverloadGate.CountToPickUp(pawn, st, s));
                plannedTake = Mathf.Min(need, st.stackCount);
                // Snapshot the def's inventory count NOW — vanilla's toil invokes this getter in the same
                // initAction immediately before its SplitOff+TryAdd, so (count after − this) in the tag
                // toil is the exact number of units that landed in inventory.
                invCountBeforeTake = YieldRouter.InventoryCountOfDef(Inv, st.def);
                return plannedTake;
            });

            // Tag what landed in inventory (visible to the bill chooser + reclaimed by the unload pass), advance.
            Toil tagAndAdvance = ToilMaker.MakeToil("HD_Prep_Tag");
            tagAndAdvance.initAction = delegate
            {
                if (plannedTake > 0 && loadDef != null)
                {
                    var held = YieldRouter.InventoryStackOfDef(Inv, loadDef);
                    if (held != null)
                    {
                        var comp = pawn.GetComp<CompHauledToInventory>();
                        if (comp != null)
                        {
                            // Vanilla's TakeToInventory MERGES into existing stacks; pass the def-count delta
                            // so a merge into an already-tagged stack re-notifies CE's HoldTracker with the
                            // growth (the tag alone is a no-op for an already-tagged stack).
                            int mergedDelta = YieldRouter.InventoryCountOfDef(Inv, loadDef) - invCountBeforeTake;
                            comp.RegisterHauledItem(held, mergedDelta > 0 ? mergedDelta : 0);
                            comp.NotifyYieldPicked(); // grace period covers the sweep + the handoff to the craft
                        }
                        // AllowMix recipes (meal cooking) sort candidates by t.Position (not PositionHeld); an
                        // unspawned split-off stack can carry an Invalid position and rank LAST. For unspawned
                        // things the Position setter is a plain field write (no grid/region work), and vanilla
                        // itself reads inventory things' Position in JumpToCollectNextIntoHandsForBill — so give
                        // it the pawn's cell so the carried stock ranks where the pawn stands.
                        if (!held.Spawned && !held.Position.IsValid)
                            held.Position = pawn.Position;
                        loadedAnything = true;
                    }
                }
                loadIndex++;
                JumpToToil(loadDecide);
            };
            tagAndAdvance.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return tagAndAdvance;

            // End AT the bench: the next work scan's ingredient chooser ranks candidates by PositionHeld distance to
            // the bench, so standing here makes the carried (tagged) stacks the nearest — vanilla picks them.
            yield return gotoBench;
        }
    }

    /// <summary>Per-pawn cooldown after a prep sweep that loaded nothing, so the conversion can't ping-pong.
    /// Keyed by thingIDNumber (no Pawn refs held). Transient — not saved; worst case one extra try after load.</summary>
    internal static class BillPrepTracker
    {
        private const int CooldownTicks = 2500; // one in-game hour

        private static readonly Dictionary<int, int> skipUntilTick = new Dictionary<int, int>();

        internal static void NoteEmptyRun(Pawn pawn)
        {
            if (pawn != null && Find.TickManager != null)
                skipUntilTick[pawn.thingIDNumber] = Find.TickManager.TicksGame + CooldownTicks;
        }

        internal static bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || Find.TickManager == null)
                return false;
            return skipUntilTick.TryGetValue(pawn.thingIDNumber, out int until)
                   && Find.TickManager.TicksGame < until;
        }
    }
}
