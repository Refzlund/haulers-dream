using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prioritize bulk unloading {0}": a one-click order that walks a hauler to a clicked transporter ONCE and
    /// pulls many stacks out of its cargo hold into the hauler's backpack in that single visit (then HD's normal
    /// unload ships them to storage), instead of vanilla's dump-everything-on-the-floor gizmo / one-thing-per-second
    /// ship-job drop followed by ordinary hauling. Auto-discovered FloatMenuOptionProvider, no Harmony. The clicked
    /// thing is ANY <see cref="CompTransporter"/> parent holding cargo, an Odyssey shuttle, a pod with leftover
    /// load, a modded shuttle-like building, never a specific shuttle class. Mirrors the order patterns of
    /// <see cref="FloatMenuOptionProvider_BulkUnloadCarrier"/> / <see cref="FloatMenuOptionProvider_BulkLoadTransporter"/>.
    ///
    /// PLAYER-ORDERED, FLAG-GATED: the option appears only while "Bulk unload all" is active on THIS transporter
    /// (<see cref="BulkUnloadTransporterGate.UnloadFlagActive"/>); ordering does not touch the flag. Ordering tells
    /// THAT pawn to prioritize the shuttle over other work: the first visit is taken immediately as an ordered job,
    /// and the driver chains each follow-up visit behind the storage drop-off so the same pawn keeps returning
    /// until nothing pullable remains (autonomous trips still flow to any hauler via
    /// <see cref="WorkGiver_BulkUnloadTransporters"/>). The rest of the offer gate IS
    /// <see cref="BulkUnloadTransporterPolicy.MayOffer"/>, and the driver re-checks the conflicting states as a
    /// FailOn. No faction gate: unlike a PAWN's inventory, a hold's contents are lootable goods, and clicking a
    /// foreign landed shuttle to strip it is exactly what a player wants an order for.
    /// </summary>
    public class FloatMenuOptionProvider_BulkUnloadTransporter : FloatMenuOptionProvider
    {
        public override bool Drafted => true;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => false;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            var things = context?.ClickedThings;
            if (pawn == null || things == null || pawn.Map == null)
                yield break;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkUnloadTransporters)
                yield break;
            // The hauler must physically be able to pick things up, must have a comp (so the backpack stock can be
            // tagged + shipped), and must have empty hands (the visit ends by overflowing one stack into them).
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null
                || pawn.carryTracker?.innerContainer == null || pawn.carryTracker.innerContainer.Count > 0)
                yield break;
            // Unloading a transporter IS hauling work (#229 parity with the load order): WorkGiverDef
            // LoadTransporters declares <workType>Hauling</workType>, and HD must not become a way around a
            // disabled Hauling work type. HaulOrderGate reads the WORK TYPE, not the WorkTags.Hauling bit an
            // "incapable of dumb labor" backstory leaves clear.
            if (HaulOrderGate.Blocks(pawn))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                var comp = clicked?.TryGetComp<CompTransporter>();
                if (comp == null)
                    continue;
                // [UC2-parity] A VF VehiclePawn carrying cargo HD must never bulk-unload, leave it to VF's own
                // cargo/unload UI. Gated on IsVehicle ONLY (a safety guard, not a feature): returns false when VF
                // is absent.
                if (VehicleFrameworkCompat.IsVehicle(clicked))
                    continue;
                // THE FLAG IS THE SWITCH: the order exists only while "Bulk unload all" is active on THIS
                // transporter. Ordering does NOT touch the flag; it tells THIS pawn to prioritize the shuttle
                // over other work, trip after trip, until the hold is done (the driver chains the follow-up
                // visits behind the storage drop-offs).
                if (!BulkUnloadTransporterGate.UnloadFlagActive(comp))
                    continue;
                // PULLABLE stacks only, via the one shared rule, pawns in the hold are not ours to move (they
                // leave via their own boarding/exit mechanics) and dead entries don't count; a passenger-only or
                // effectively-empty shuttle offers nothing.
                bool hasPullableContents = BulkUnloadTransporterGate.HasPullableContents(comp);
                // PERMISSION/state seam, shared with the driver's FailOn: never offer while ANYTHING is committed
                // INTO the group, a vanilla load lord, an HD bulk-load courier, a vanilla HaulToTransporter job,
                // or a recorded load session with an open manifest (covers both the early pre-hauler window and
                // top-up loads that never re-fire InitiateLoading), or while a caravan owns the hold. Vanilla's
                // cancel-load gizmo and caravan packing own those states.
                if (!BulkUnloadTransporterPolicy.MayOffer(hasPullableContents,
                        BulkUnloadTransporterGate.ConflictActive(comp)
                        || BulkUnloadTransporterGate.LoadSessionHasOpenManifest(comp), clicked.IsInCaravan()))
                    continue;
                if (!pawn.CanReach(clicked, PathEndMode.Touch, Danger.Deadly))
                    continue;
                if (!pawn.CanReserve(clicked))
                    continue;

                var pawnLocal = pawn;
                var clickedLocal = clicked;
                var option = new FloatMenuOption(
                    "HaulersDream.UnloadTransporter.Option".Translate(clicked.LabelShort), () =>
                    {
                        // The order does NOT touch the flag (it is already on; visibility is gated on it). Only
                        // the pure MakeJob + TryTakeOrderedJob runs here, that ordered-job path IS auto-synced by
                        // MP, so it replays on every client; everything the driver does (the transfers and the
                        // prioritized follow-up chaining) then runs deterministically in-tick.
                        var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk, clickedLocal);
                        job.playerForced = true;
                        if (!pawnLocal.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                            Messages.Message("HaulersDream.UnloadTransporter.CouldNotStart".Translate(), clickedLocal,
                                MessageTypeDefOf.RejectInput, historical: false);
                    })
                {
                    iconThing = clicked,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clicked);
                yield break; // one bulk-unload option per click
            }
        }
    }
}
