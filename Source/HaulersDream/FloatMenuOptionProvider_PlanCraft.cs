using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Adds a "Plan prioritized crafting…" option to the right-click float menu on a workbench (a stove, smithy,
    /// etc.) that has at least one bill this pawn can batch. Choosing it opens <see cref="Dialog_PlanCraft"/>, the
    /// station counterpart to the route planner: pick a bill, set how many times to repeat it (resource-capped) and
    /// a timeout, then the pawn pre-loads all the ingredients in one trip and crafts the lot.
    ///
    /// This pairs with the route resolver suppressing its (nonsensical) "Plan prioritized doing bills…" option on
    /// bill stations — see WorkKindResolver — so a station shows the crafting planner, not a route. Auto-discovered
    /// like every FloatMenuOptionProvider, so it needs no Harmony patch.
    /// </summary>
    public class FloatMenuOptionProvider_PlanCraft : FloatMenuOptionProvider
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
            if (pawn == null || things == null)
                yield break;
            if (HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.planCrafting)
                yield break; // crafting planner disabled in mod options
            if (pawn.thinker?.TryGetMainTreeThinkNode<JobGiver_Work>() == null)
                yield break; // only real workers get a crafting plan

            var seen = new HashSet<Building_WorkTable>();
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Building_WorkTable bench))
                    continue;
                if (!seen.Add(bench) || !bench.Spawned || bench.Map != pawn.Map)
                    continue;

                // No try/catch: a throw here is a real bug to surface, not silently hide the option.
                // WorkOverride.CanDoBillsAt: a pawn incapable of the bench's bill work (a non-cooking
                // pawn at a stove) gets no crafting plan — the same capability vanilla requires; the
                // "all pawns can …" overrides flow through it automatically.
                bool offer = WorkOverride.CanDoBillsAt(pawn, bench)
                             && bench.CurrentlyUsableForBills()
                             && CraftBatchPlanner.AnyBatchableBillForPawn(pawn, bench)
                             && pawn.CanReach(bench, PathEndMode.InteractionCell, Danger.Deadly);
                if (!offer)
                    continue;

                var benchLocal = bench;
                var option = new FloatMenuOption("HaulersDream.PlanCraft.Option".Translate(),
                    () => Find.WindowStack.Add(new Dialog_PlanCraft(pawn, benchLocal)))
                {
                    iconThing = bench,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, bench);
            }
        }
    }
}
