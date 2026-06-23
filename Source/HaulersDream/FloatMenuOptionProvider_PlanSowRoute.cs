using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Adds a "Plan prioritized sowing…" option to the right-click float menu when the clicked CELL is inside a
    /// <see cref="Zone_Growing"/> that allows sowing and has empty growable cells — the sow analogue of the
    /// Thing-based <see cref="FloatMenuOptionProvider_PlanRoute"/>. Sowing is cell-based (vanilla
    /// <see cref="WorkGiver_GrowerSow"/> targets cells, not Things), so it can't be surfaced by clicking a Thing;
    /// we hook the clicked CELL instead, exactly like vanilla offers "Prioritize sowing" on a cell.
    ///
    /// <para>A vanilla auto-discovered provider (all <see cref="FloatMenuOptionProvider"/> subclasses are found by
    /// reflection), so it needs no Harmony patching. <see cref="GetOptions"/> is the per-MENU hook (it reads
    /// <c>ClickedCell</c>); we emit at most one option.</para>
    /// </summary>
    public class FloatMenuOptionProvider_PlanSowRoute : FloatMenuOptionProvider
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

            // Respect work incapability exactly like vanilla's "Prioritize sowing" does (a pawn incapable of
            // Growing gets no plan-sow option). The "all pawns can …" overrides flow through WorkTypeIsDisabled.
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Growing))
                yield break;
            if (pawn.thinker?.TryGetMainTreeThinkNode<RimWorld.JobGiver_Work>() == null)
                yield break;

            var cell = context.ClickedCell;
            if (!cell.IsValid || !cell.InBounds(pawn.Map))
                yield break;
            if (pawn.Map.fogGrid.IsFogged(cell))
                yield break;

            var zone = cell.GetZone(pawn.Map) as Zone_Growing;
            if (zone == null || !zone.allowSow)
                yield break;

            var plantDef = zone.GetPlantDefToGrow();
            if (plantDef?.plant == null)
                yield break;

            // Only offer the option when there's actually something sowable in the zone (the clicked cell itself, or
            // — if the player clicked an already-sown cell of the field — at least one empty cell). This keeps the
            // option from cluttering the menu over a fully-grown / fully-sown field.
            IntVec3 anchor;
            if (!TryFindAnchor(pawn, cell, zone, plantDef, out anchor))
                yield break;

            string gerund = SowGerund();
            string label = "HaulersDream.PlanRoute.Option".Translate(gerund);

            var option = new FloatMenuOption(label, () =>
                Find.WindowStack.Add(new Dialog_PlanSowRoute(pawn, anchor, zone)));
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, anchor);
        }

        // The route's anchor cell: the clicked cell if it's sowable, else the first sowable cell of the zone (so a
        // click on an already-sown tile of the field still opens a route over the rest of the field). Returns false
        // when the whole zone has nothing to sow.
        private static bool TryFindAnchor(Pawn pawn, IntVec3 clicked, Zone_Growing zone, ThingDef plantDef, out IntVec3 anchor)
        {
            anchor = clicked;
            if (SowRouteSelection.IsSowableCell(pawn, clicked, zone, plantDef))
                return true;
            var cells = zone.cells; // RAW list — never Zone.Cells (it shuffles)
            // Deterministic anchor: the lowest-cell-index sowable cell (stable across clients).
            var indices = pawn.Map.cellIndices;
            bool found = false;
            int bestIndex = int.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (!SowRouteSelection.IsSowableCell(pawn, c, zone, plantDef))
                    continue;
                int idx = indices.CellToIndex(c);
                if (idx < bestIndex) { bestIndex = idx; anchor = c; found = true; }
            }
            return found;
        }

        // The gerund for the menu/dialog label: the vanilla sow WorkGiver's gerund ("sowing") when resolvable, else
        // the localized fallback. Resolved against the live sow scanner's def so it follows the player's language.
        internal static string SowGerund()
        {
            var scanner = SowRouteExecutor.SowScanner();
            string g = scanner?.def?.gerund;
            if (!g.NullOrEmpty())
                return g;
            return "HaulersDream.PlanRoute.SowingGerundFallback".Translate();
        }
    }
}
