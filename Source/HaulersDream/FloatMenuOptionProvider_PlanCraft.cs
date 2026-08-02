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
            // #243: this order gathers ingredients into inventory exactly like the automatic batch route does, so
            // it has to answer to the same suppressors — it answered to NONE of them, which is how a player who
            // had switched a bench off (or was running under Common Sense) could still be handed the order and
            // still see their pawns pocket everything. Bill-independent half first, hoisted out of the loop: while
            // Common Sense owns the ingredient-gather flow and the batch opt-in is off, no batch runs anywhere, so
            // there is nothing to plan. Same read the batch dropdown and the route conversion use.
            if (CommonSenseCompat.BatchSuppressedByCommonSense)
                yield break;

            var seen = new HashSet<Building_WorkTable>();
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Building_WorkTable bench))
                    continue;
                if (!seen.Add(bench) || !bench.Spawned || bench.Map != pawn.Map)
                    continue;
                // #243, per-bench half: the SAME gate both automatic gather routes consult before they convert a
                // job (BillRouteGate.MayRouteToInventory). It covers two things this provider never checked. The
                // bench's own "Gather ingredients" switch: a player who turned it off asked this bench to behave
                // like vanilla, and an order that pre-loads a whole batch into inventory is the opposite of that.
                // And the bench TYPE: Building_WorkTableAutonomous (the mech gestator family) derives from
                // Building_WorkTable, so the `is Building_WorkTable` test above lets one through, yet an
                // autonomous bench needs its ingredients DEPOSITED into its own container and can never be
                // batch-crafted from inventory.
                if (!BillRouteGate.MayRouteToInventory(bench))
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
                // "Plan for unassigned work" off: hide the crafting plan when NONE of the bench's batchable bills'
                // work types is assigned (priority 0) for this pawn. A bench can host several work types, so ANY one
                // assigned batchable-bill work type still shows it. Capability was already checked (CanDoBillsAt).
                if (WorkOverride.HidePlanCraftForUnassigned(pawn, bench))
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
