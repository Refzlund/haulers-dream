using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Adds a "Plan prioritized removing floor…" option to the right-click float menu when the clicked CELL has a
    /// removable floor — the remove-floor analogue of the sow-cell <see cref="FloatMenuOptionProvider_PlanSowRoute"/>.
    /// Removing floor is cell-based (vanilla <see cref="WorkGiver_ConstructRemoveFloor"/> targets cells, not Things),
    /// so it can't be surfaced by clicking a Thing; we hook the clicked CELL instead.
    ///
    /// <para>A vanilla auto-discovered provider (all <see cref="FloatMenuOptionProvider"/> subclasses are found by
    /// reflection), so it needs no Harmony patching. <see cref="GetOptions"/> is the per-MENU hook (it reads
    /// <c>ClickedCell</c>); we emit at most one option, only when the clicked cell itself has a removable floor.</para>
    /// </summary>
    public class FloatMenuOptionProvider_PlanRemoveFloorRoute : FloatMenuOptionProvider
    {
        public override bool Drafted => false;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => false;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            if (pawn?.Map == null)
                yield break;
            if (HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.planRoutes)
                yield break; // route planner disabled in mod options

            // Respect work incapability exactly like vanilla's construction work does (a pawn incapable of
            // Construction gets no plan-remove-floor option). The "all pawns can …" overrides flow through
            // WorkTypeIsDisabled.
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                yield break;
            if (pawn.thinker?.TryGetMainTreeThinkNode<RimWorld.JobGiver_Work>() == null)
                yield break;

            var cell = context.ClickedCell;
            if (!cell.IsValid || !cell.InBounds(pawn.Map))
                yield break;
            if (pawn.Map.fogGrid.IsFogged(cell))
                yield break;

            // Only offer the option when the clicked cell itself has a removable floor (the route's anchor).
            if (!RemoveFloorRouteSelection.IsRemovableFloorCell(pawn, cell))
                yield break;

            IntVec3 anchor = cell;
            string gerund = "HaulersDream.PlanRoute.RemoveFloorGerund".Translate();
            string label = "HaulersDream.PlanRoute.Option".Translate(gerund);

            var option = new FloatMenuOption(label, () =>
                Find.WindowStack.Add(new Dialog_PlanRemoveFloorRoute(pawn, anchor)));
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, anchor);
        }
    }
}
