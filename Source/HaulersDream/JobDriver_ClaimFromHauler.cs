using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Claims a stack from a colonist hand-hauling it TO STORAGE and delivers it to a build, instead of
    /// letting it reach storage and fetching from there. The handoff is clean (decompile-verified): the
    /// worker walks to the hauler and TRANSFERS the stack out of the hauler's carryTracker into its own via
    /// the vanilla <c>ThingOwner.TryTransferToContainer</c> idiom; on a FULL take it then ends the hauler's
    /// now-empty haul job (so the hauler doesn't reach a deposit toil with a null CarriedThing and log an
    /// error), and on a PARTIAL take it leaves the hauler's job untouched (it keeps the remainder and
    /// continues to storage). No item is ever dropped on the ground in the handoff.
    ///
    /// targetA = the hauler Pawn (reserved → single-claimant mutex); targetB/targetC = the needer (frame/
    /// blueprint). The eligibility + worth-it scan lives in <see cref="CarriedHaulShare"/>.
    /// </summary>
    public class JobDriver_ClaimFromHauler : JobDriver
    {
        private const TargetIndex HaulerInd = TargetIndex.A;        // the hauler pawn (then the claimed stack)
        private const TargetIndex NeederInd = TargetIndex.B;        // the frame/blueprint
        private const TargetIndex PrimaryNeederInd = TargetIndex.C;

        private ThingDef resourceDef;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref resourceDef, "hdResourceDef");
        }

        private Pawn Hauler => job.GetTarget(HaulerInd).Thing as Pawn;
        private Thing Needer => job.GetTarget(NeederInd).Thing;

        public override string GetReport()
        {
            var needer = Needer;
            if (resourceDef == null || needer == null)
                return "ReportHaulingUnknown".Translate();
            return "ReportHaulingTo".Translate(resourceDef.label, needer.LabelShort.Named("DESTINATION"), resourceDef.Named("THING"));
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (resourceDef == null)
                resourceDef = Hauler?.carryTracker?.CarriedThing?.def;

            // Reserve the HAULER pawn — this is the single-claimant mutex: a second worker's Reserve(hauler)
            // fails and it falls back. Deliberately do NOT reserve the carried thing (the hauler holds that
            // reservation until the handoff transfers it out).
            if (job.targetA.HasThing && !pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                return false;

            // Declare intent to the enroute system so others don't over-deliver to this needer (mirrors the
            // construct-deliver driver). GetSpaceRemainingWithEnroute excludes our own claim.
            if (Needer is IHaulEnroute enroute && resourceDef != null && !Needer.DestroyedOrNull() && pawn.Map != null)
            {
                int want = Mathf.Min(job.count, enroute.GetSpaceRemainingWithEnroute(resourceDef, pawn));
                if (want > 0)
                    pawn.Map.enrouteManager.AddEnroute(enroute, pawn, resourceDef, want);
            }
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate
            {
                if (Needer is IHaulEnroute he)
                    pawn.Map?.enrouteManager?.ReleaseFor(he, pawn);
            });

            this.FailOn(() =>
            {
                var n = Needer;
                return n == null || n.Destroyed || !n.Spawned;
            });
            this.FailOnForbidden(NeederInd);

            // ---- walk to the hauler (a moving target); abort cleanly if it deposits / gets re-tasked ----
            Toil gotoHauler = Toils_Goto.GotoThing(HaulerInd, PathEndMode.Touch);
            gotoHauler.FailOn(() => CarriedHaulShare.StorageBoundCarried(Hauler, pawn) == null);
            yield return gotoHauler;

            // ---- the clean handoff: transfer the stack from the hauler's hands into ours ----
            Toil handoff = ToilMaker.MakeToil("HD_ClaimHandoff");
            handoff.initAction = () =>
            {
                var carrier = Hauler;
                var carried = CarriedHaulShare.StorageBoundCarried(carrier, pawn);
                if (carried == null || (resourceDef != null && carried.def != resourceDef))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                int space = pawn.carryTracker.AvailableStackSpace(carried.def);
                int count = Mathf.Min(Mathf.Min(carried.stackCount, job.count), space);
                if (count <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                bool fullTake = count >= carried.stackCount;
                // Clean ThingOwner move (the idiom vanilla's TakeFromOtherInventory uses). Transfer FIRST,
                // then — only on a full take — end the hauler's now-empty job so it never reaches a deposit
                // toil with a null CarriedThing (which would log an error). A partial take leaves the
                // hauler's HaulToCell job valid; it keeps the remainder and finishes its run to storage.
                carrier.carryTracker.innerContainer.TryTransferToContainer(
                    carried, pawn.carryTracker.innerContainer, count, out Thing moved);
                if (moved == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (fullTake)
                    carrier.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);

                var comp = pawn.GetComp<CompHauledToInventory>();
                if (comp != null)
                    comp.lastInterceptedTick = Find.TickManager?.TicksGame ?? 0;

                // The reservation on the hauler PAWN was only the single-claimant mutex — release it the
                // moment the handoff is done (using the `carrier` ref captured before re-pointing), so a
                // downed hauler can be rescued while we're still walking the delivery. ReservedBy guard:
                // Release error-logs when no matching reservation exists.
                if (pawn.Map.reservationManager.ReservedBy(carrier, pawn, job))
                    pawn.Map.reservationManager.Release(carrier, pawn, job);
                job.SetTarget(HaulerInd, moved); // we now carry `moved`; A is no longer the hauler
            };
            handoff.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return handoff;

            // ---- carry the claimed stack to the needer and deposit it ----
            yield return (Needer is Blueprint || Needer is Frame)
                ? Toils_Goto.GotoBuild(NeederInd)
                : Toils_Goto.GotoThing(NeederInd, PathEndMode.Touch);
            // A BLUEPRINT needer is not a container (only Frame has the resource ThingOwner) — convert
            // blueprint→frame before the deposit, exactly like vanilla JobDriver_HaulToContainer's toil
            // order (decompile-verified). Without it the deposit errors and transfers nothing.
            yield return Toils_Construct.MakeSolidThingFromBlueprintIfNecessary(NeederInd, PrimaryNeederInd);
            yield return Toils_Haul.DepositHauledThingInContainer(NeederInd, PrimaryNeederInd);
        }
    }
}
