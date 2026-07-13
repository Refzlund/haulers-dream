using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Adds a "Plan prioritized removing floor…" option to the right-click float menu when the clicked CELL has a
    /// floor MARKED for removal — the remove-floor analogue of the sow-cell <see cref="FloatMenuOptionProvider_PlanSowRoute"/>.
    /// Removing floor is cell-based (vanilla <see cref="WorkGiver_ConstructRemoveFloor"/> targets cells, not Things),
    /// so it can't be surfaced by clicking a Thing; we hook the clicked CELL instead.
    ///
    /// <para>A vanilla auto-discovered provider (all <see cref="FloatMenuOptionProvider"/> subclasses are found by
    /// reflection), so it needs no Harmony patching. <see cref="GetOptions"/> is the per-MENU hook (it reads
    /// <c>ClickedCell</c>); we emit at most one option, only when the clicked cell's floor is already marked for
    /// removal (issue #110 — it previously appeared over every built floor and cluttered the menu).</para>
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
            if (WorkCapabilityProbe.IsDisabled(pawn, WorkTypeDefOf.Construction))
                yield break;
            // "Plan for unassigned work" off: also hide the option for a pawn CAPABLE of Construction but with
            // Construction unassigned (priority 0) in its Work tab (default on = shown, the permissive behavior).
            if (PlannerGate.HideForUnassigned(pawn, WorkTypeDefOf.Construction))
                yield break;
            if (pawn.thinker?.TryGetMainTreeThinkNode<RimWorld.JobGiver_Work>() == null)
                yield break;

            var cell = context.ClickedCell;
            if (!cell.IsValid || !cell.InBounds(pawn.Map))
                yield break;
            if (pawn.Map.fogGrid.IsFogged(cell))
                yield break;

            // Only offer the option when the clicked cell's floor is already MARKED for removal (issue #110 — it used
            // to appear over every removable floor, cluttering the menu when hauling / cleaning). IsRemovableFloorCell
            // now requires the RemoveFloor designation, so this is both the visibility gate and the route's anchor.
            if (!RemoveFloorRouteSelection.IsRemovableFloorCell(pawn, cell))
                yield break;

            IntVec3 anchor = cell;
            string gerund = "HaulersDream.PlanRoute.RemoveFloorGerund".Translate();
            // "(remembered)": show the one-click option ONLY when this floor type has an explicit saved template
            // (created with the "Remember" button in the dialog) AND the interface toggle is on. The toggle alone is
            // not enough — without a saved template the plain "Plan prioritized …" opens the dialog, even with the
            // toggle on. Keyed by the clicked cell's floor terrain. See Dialog_PlanRemoveFloorRoute.ExecuteRemembered.
            var terrain = pawn.Map.terrainGrid.TerrainAt(anchor);
            var template = HaulersDreamMod.Settings.GetRememberedRemoveFloorRoute(terrain?.defName);
            bool remember = HaulersDreamMod.Settings.rememberPlan && template != null;
            string label = remember
                ? "HaulersDream.PlanRoute.OptionRemembered".Translate(gerund)
                : "HaulersDream.PlanRoute.Option".Translate(gerund);

            var option = new FloatMenuOption(label, () =>
            {
                if (remember)
                    // Plain click REPLACES current work; the Queue Order key (Shift) APPENDS — read at click time.
                    Dialog_PlanRemoveFloorRoute.ExecuteRemembered(pawn, anchor, template, replace: !KeyBindingDefOf.QueueOrder.IsDownEvent);
                else
                    Find.WindowStack.Add(new Dialog_PlanRemoveFloorRoute(pawn, anchor));
            });
            var decorated = FloatMenuUtility.DecoratePrioritizedTask(option, pawn, anchor);
            // Hovering the option blinks the bottom-right "Remember plan" interface toggle (chain any existing action).
            var prevHover = decorated.mouseoverGuiAction;
            decorated.mouseoverGuiAction = r =>
            {
                prevHover?.Invoke(r);
                UIHighlighter.HighlightTag(Patch_PlaySettings.RememberPlanHighlightTag);
            };
            yield return decorated;
        }
    }
}
