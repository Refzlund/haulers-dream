using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prioritize bulk unloading {0}": a one-click order that walks a hauler to a clicked pack animal once and
    /// pulls many stacks out of it into the hauler's backpack in that single visit (then HD's normal unload ships
    /// them to storage), instead of vanilla's one-stack-in-hands-per-walk. Auto-discovered FloatMenuOptionProvider
    /// — no Harmony. The clicked thing is the CARRIER (a Pawn). Mirrors the order pattern of
    /// <see cref="FloatMenuOptionProvider_BulkLoadPackAnimal"/>.
    /// </summary>
    public class FloatMenuOptionProvider_BulkUnloadCarrier : FloatMenuOptionProvider
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
            if (s == null || !s.enableBulkUnloadCarriers)
                yield break;
            // The hauler must physically be able to pick things up, must have a comp (so the backpack stock can be
            // tagged + shipped), and must have empty hands (the visit ends by overflowing one stack into them).
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null
                || pawn.carryTracker?.innerContainer == null || pawn.carryTracker.innerContainer.Count > 0)
                yield break;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Pawn carrier))
                    continue;
                // [UC2] A VF VehiclePawn carries cargo HD must never bulk-unload — leave it to VF's own unload UI.
                // Gated on IsVehicle ONLY (a safety fix, not a feature): IsVehicle returns false when VF is absent.
                if (VehicleFrameworkCompat.IsVehicle(carrier))
                    continue;
                // Only a pack animal (or any inventory-bearing animal) the player can unload — not a mech gestator
                // (CompMechCarrier stays vanilla) and not a colonist.
                if (carrier == pawn || carrier.inventory?.innerContainer == null
                    || carrier.inventory.innerContainer.Count == 0)
                    continue;
                if (carrier.GetComp<CompMechCarrier>() != null || carrier.IsFreeColonist)
                    continue;
                if (carrier.Faction != pawn.Faction && carrier.HostFaction != pawn.Faction)
                    continue;
                if (!pawn.CanReach(carrier, PathEndMode.Touch, Danger.Deadly))
                    continue;
                if (!pawn.CanReserve(carrier))
                    continue;

                var carrierLocal = carrier;
                var option = new FloatMenuOption(
                    "HaulersDream.UnloadCarrier.Option".Translate(carrier.LabelShort), () =>
                    {
                        // MP: only the pure MakeJob + TryTakeOrderedJob runs here — that ordered-job path IS
                        // auto-synced by MP, so it replays on every client. The carrier's UnloadEverything flag (a
                        // SCRIBED field, synced world state) is NOT set here: a click-time write would only mutate the
                        // clicking client and desync. It is instead set in JobDriver_UnloadCarrierInBulk.Notify_Starting
                        // — in-tick code that runs deterministically on every client when the synced job starts.
                        var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadCarrierInBulk, carrierLocal);
                        job.playerForced = true;
                        if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                            Messages.Message("HaulersDream.UnloadCarrier.CouldNotStart".Translate(), carrierLocal,
                                MessageTypeDefOf.RejectInput, historical: false);
                    })
                {
                    iconThing = carrier,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, carrier);
                yield break; // one bulk-unload option per click
            }
        }
    }
}
