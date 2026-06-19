using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Pick up X": a Pick-Up-And-Haul-parity right-click order that picks the CLICKED ground stack straight into
    /// the pawn's inventory as a tracked Hauler's Dream haul — then serviced by the normal storage-aware unload —
    /// so it is never lost even with automatic unloading off. Unlike <see cref="FloatMenuOptionProvider_HaulNearby"/>
    /// (which sweeps the surroundings too), this is just the one clicked stack. Additive to vanilla's "Prioritize
    /// hauling" and HD's "Haul everything nearby", which still appear alongside it.
    ///
    /// CRUCIAL — never a black hole: the order routes through HD's forced bulk-haul-into-inventory path
    /// (<see cref="BulkHaul.BuildPickUpJob"/> → a single-stack <see cref="JobDriver_BulkHaul"/>), which TAGS the
    /// picked stack on <see cref="CompHauledToInventory"/> and forces the unload trip when the pickup is done — NOT
    /// a raw untagged TakeInventory (which, under the default unloadAllSurplus=false, the unload side would never
    /// reclaim). The picked stack therefore always reaches storage.
    ///
    /// Auto-discovered FloatMenuOptionProvider (no Harmony, no registration). Mirrors the structure + gates of
    /// <see cref="FloatMenuOptionProvider_HaulNearby"/>.
    /// </summary>
    public class FloatMenuOptionProvider_PickUpIntoInventory : FloatMenuOptionProvider
    {
        public override bool Drafted => false;
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
            if (s == null || !s.manualPickupOption)
                yield break; // this right-click order disabled in mod options
            // Match BuildPickUpJob's non-home gate: with the mod inert on non-home maps the picked stock would have
            // no storage to unload to there, so don't offer it (it would strand in inventory).
            if (!MapGate.HdActiveOnMap(pawn.Map))
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                yield break; // the pickup loads into inventory, tracked via the comp
            // The vanilla can-haul bar for a PLAYER ORDER (matches FloatMenuOptionProvider_HaulNearby and vanilla
            // "Prioritize hauling"): a hauling-capable, manipulation-capable pawn. (Drafted pawns never reach here —
            // Drafted => false; incapable-of-hauling and no-manipulation are excluded below.)
            if (pawn.WorkTagIsDisabled(WorkTags.Hauling) || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                // Plain haulable ground ITEM only. Exclude VF VehiclePawns (a vehicle is a Pawn, not an Item, so the
                // category check already excludes it, but IsVehicle is explicit per the design and returns false when
                // VF is absent) and CORPSES — in 1.6 a corpse def IS ThingCategory.Item + EverHaulable (verified:
                // ThingDefGenerator_Corpses), so the category filter does NOT exclude it; leave corpses to their own
                // hauling/rot flow rather than offering "Pick up corpse".
                if (clicked?.def == null || clicked.def.category != ThingCategory.Item || !clicked.def.EverHaulable)
                    continue;
                if (clicked is Corpse || VehicleFrameworkCompat.IsVehicle(clicked))
                    continue;
                // Already in its best storage: "pick up into inventory" could only end in the pawn re-storing it
                // (the mandatory unload finish-action re-stores any HD-swept stack), a no-op round-trip the user sees
                // as "won't pick it up." Mirrors the bulk driver's loadIndex!=0 in-storage skip and SelfPickup's
                // IsInValidStorage skip. Use IsInValidBestStorage (not IsInValidStorage) so an item in a WORSE
                // stockpile can still be picked up / upgraded.
                if (clicked.IsInValidBestStorage())
                    continue;
                // The same bar vanilla "Prioritize hauling" uses (EverHaulable / fogged / forbidden+allowed-area /
                // reservable / reachable). forced:true mirrors a player order.
                if (!HaulAIUtility.PawnCanAutomaticallyHaul(pawn, clicked, forced: true))
                    continue;

                var clickedLocal = clicked;
                var pawnLocal = pawn;
                var option = new FloatMenuOption("HaulersDream.PickUp.Option".Translate(clicked.LabelCap), () =>
                {
                    // No try/catch: a failure to build the order is a real bug to surface, not mask as the benign
                    // toast. BuildPickUpJob now picks the clicked stack into inventory REGARDLESS of any storage
                    // destination (PUAH parity — the tagged load is serviced by the unload pass later, and the
                    // cannot-unload alert backstops a no-destination load), limited only by what the pawn can carry.
                    // So it returns null ONLY when the pawn's inventory is already at/over its carry ceiling and not
                    // one more unit of this stack fits. In that single case fall back to a plain forced hand-haul
                    // (no mass limit) so a too-laden pawn still relocates the stack if storage exists; only when
                    // even that has no destination is the order genuinely impossible.
                    Job job = BulkHaul.BuildPickUpJob(pawnLocal, clickedLocal)
                              ?? HaulAIUtility.HaulToStorageJob(pawnLocal, clickedLocal, forced: true);
                    if (job == null)
                    {
                        // Pickup-appropriate message (NOT the sweep's "Nothing to haul nearby right now."): the
                        // clicked stack IS haulable and present — the pawn just can't carry more into inventory
                        // (over its carry ceiling) and there's nowhere to hand-haul it to either.
                        Messages.Message("HaulersDream.PickUp.CouldNotStart".Translate(pawnLocal.LabelShort, clickedLocal.LabelCap),
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
                yield break; // one pick-up option per click; vanilla's "Prioritize hauling" still appears alongside
            }
        }
    }
}
