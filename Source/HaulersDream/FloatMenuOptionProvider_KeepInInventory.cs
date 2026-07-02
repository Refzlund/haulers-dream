using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Keep X in inventory": a right-click order that takes the clicked stack into the pawn's inventory and
    /// HOLDS it — HD never hauls it to storage and vanilla's drop-unused never sheds it (see
    /// <see cref="JobDriver_KeepInInventory"/> / <see cref="CompHauledToInventory.RegisterKept"/>). Works on a ground
    /// stack AND on a stack held inside a spawned container building (vanilla's egg box — the only vanilla def with
    /// containedItemsSelectable — plus any modded container storage that flags its contents selectable), which the driver extracts from the holder's
    /// inner ThingOwner. The counterpart to <see cref="FloatMenuOptionProvider_PickUpIntoInventory"/> ("Pick up X" =
    /// pick up to HAUL): the same shape and gates, but with two deliberate differences — no storage/map gate (a pawn
    /// can hold an item on any map), and it does NOT skip an item already in storage (the player may want to take a
    /// stored item out to hold it — the whole reason the container branch exists). Both options appear side by side
    /// on a haulable ground item so the player chooses hold-vs-haul. Release a kept item by consuming it or dropping
    /// it from the pawn's gear tab. Auto-discovered FloatMenuOptionProvider (no Harmony).
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
                if (clicked == null)
                    continue;
                // CONTAINER STORAGE: an item held inside a spawned container building (vanilla: only the egg box sets
                // containedItemsSelectable; storage mods add their own) reaches ClickedThings UNSPAWNED via vanilla SelectableContainedThings —
                // the spawned-only ground path below would NRE on it (see the "Pick up X" provider). "Keep"
                // explicitly supports taking a STORED item out to hold it, and container storage is storage, so
                // offer it there too: the driver's container branch walks to the holder and extracts from its inner
                // ThingOwner. Anything else unspawned stays not orderable, and a PAWN holder is never pulled from
                // here (another pawn's inventory is Meals-on-Wheels / gear-tab territory; VF vehicle cargo is a
                // Pawn holder too, since VehiclePawn is a Pawn).
                Thing container = null;
                if (!clicked.Spawned)
                {
                    var parent = clicked.SpawnedParentOrMe;
                    if (parent == null || parent == clicked || parent is Pawn || parent.Map != pawn.Map)
                        continue;
                    var inner = parent.TryGetInnerInteractableThingOwner();
                    if (inner == null || !inner.Contains(clicked))
                        continue;
                    container = parent;
                }
                if (clicked.def == null || clicked.def.category != ThingCategory.Item || !clicked.def.EverHaulable)
                    continue;
                // CORPSES are allowed, like "Pick up X" (a corpse def IS ThingCategory.Item + EverHaulable in 1.6):
                // a kept corpse is simply held whole — no auto-strip (stripping fires on HAUL pickups; a keep is a
                // deliberate "hold this" order), released like any kept item from the gear tab.
                if (VehicleFrameworkCompat.IsVehicle(clicked))
                    continue;
                // NOTE: unlike "Pick up X", we do NOT skip a stack already in valid storage — the player may want to
                // take a stored item out and hold it. Still require the basics: not fogged, not burning, reservable,
                // reachable. A forced player order, so a FORBIDDEN stack is allowed (the driver takes it regardless).
                // For a contained item the position-based basics are checked on the CONTAINER (the item has no map
                // position of its own); the reservation stays on the ITEM (the stack is what two orders could race).
                var reachTarget = container ?? clicked;
                if (reachTarget.Position.Fogged(pawn.Map)
                    || reachTarget.IsBurning()
                    || !pawn.CanReserve(clicked, 1, -1, null, ignoreOtherReservations: true)
                    || !pawn.CanReach(reachTarget, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;

                var clickedLocal = clicked;
                var containerLocal = container;
                var pawnLocal = pawn;
                var option = new FloatMenuOption("HaulersDream.Keep.Option".Translate(clicked.LabelCap), () =>
                {
                    // No try/catch: a failure to build the order is a real bug to surface. Both builders return null
                    // ONLY when the pawn's inventory is already at/over its carry ceiling and not one more unit fits
                    // (the container builder also when the item already left the container).
                    Job job = containerLocal != null
                        ? BulkHaul.BuildKeepFromContainerJob(pawnLocal, clickedLocal, containerLocal)
                        : BulkHaul.BuildKeepJob(pawnLocal, clickedLocal);
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
                // One option PER DISTINCT clicked thing (a pile offers each; matches "Pick up X" and vanilla's
                // per-thing "Prioritize hauling").
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, reachTarget);
            }
        }
    }
}
