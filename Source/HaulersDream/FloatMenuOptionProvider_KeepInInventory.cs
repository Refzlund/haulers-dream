using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Keep X in inventory": a right-click order that takes the clicked ground stack into the pawn's inventory and
    /// HOLDS it — HD never hauls it to storage and vanilla's drop-unused never sheds it (see
    /// <see cref="JobDriver_KeepInInventory"/> / <see cref="CompHauledToInventory.RegisterKept"/>). The counterpart to
    /// <see cref="FloatMenuOptionProvider_PickUpIntoInventory"/> ("Pick up X" = pick up to HAUL): the same shape and
    /// gates, but with two deliberate differences — no storage/map gate (a pawn can hold an item on any map), and it
    /// does NOT skip an item already in storage (the player may want to take a stored item out to hold it). Both
    /// options appear side by side on a haulable ground item so the player chooses hold-vs-haul. Release a kept item
    /// by consuming it or dropping it from the pawn's gear tab. Auto-discovered FloatMenuOptionProvider (no Harmony).
    /// </summary>
    public class FloatMenuOptionProvider_KeepInInventory : FloatMenuOptionProvider
    {
        public override bool Drafted => true;   // like "Pick up X": a drafted pawn can grab a dropped item to hold
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
            if (s == null || !s.keepInInventoryOption)
                yield break; // this right-click order disabled in mod options
            // No MapGate here (unlike "Pick up X"): keeping does not unload, so it needs no storage and works on any
            // map (a caravan/raid pawn can hold an item too).
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                yield break; // the keep loads into inventory, tracked via the comp
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;
            if (!pawn.Drafted && pawn.WorkTagIsDisabled(WorkTags.Hauling))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                // Spawned ground ITEMs only (a contained thing reaches ClickedThings without a map position and would
                // NRE the spawned-only checks below — see the "Pick up X" provider).
                if (clicked == null || !clicked.Spawned)
                    continue;
                if (clicked.def == null || clicked.def.category != ThingCategory.Item || !clicked.def.EverHaulable)
                    continue;
                if (clicked is Corpse || VehicleFrameworkCompat.IsVehicle(clicked))
                    continue;
                // NOTE: unlike "Pick up X", we do NOT skip a stack already in valid storage — the player may want to
                // take a stored item out and hold it. Still require the basics: not fogged, not burning, reservable,
                // reachable. A forced player order, so a FORBIDDEN stack is allowed (the driver takes it regardless).
                if (clicked.Position.Fogged(pawn.Map)
                    || clicked.IsBurning()
                    || !pawn.CanReserve(clicked, 1, -1, null, ignoreOtherReservations: true)
                    || !pawn.CanReach(clicked, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;

                var clickedLocal = clicked;
                var pawnLocal = pawn;
                var option = new FloatMenuOption("HaulersDream.Keep.Option".Translate(clicked.LabelCap), () =>
                {
                    // No try/catch: a failure to build the order is a real bug to surface. BuildKeepJob returns null
                    // ONLY when the pawn's inventory is already at/over its carry ceiling and not one more unit fits.
                    Job job = BulkHaul.BuildKeepJob(pawnLocal, clickedLocal);
                    if (job == null)
                    {
                        Messages.Message("HaulersDream.Keep.CouldNotStart".Translate(pawnLocal.LabelShort, clickedLocal.LabelCap),
                            clickedLocal, MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }
                    job.playerForced = true;
                    pawnLocal.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                })
                {
                    iconThing = clicked,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clicked);
                yield break; // one keep option per click
            }
        }
    }
}
