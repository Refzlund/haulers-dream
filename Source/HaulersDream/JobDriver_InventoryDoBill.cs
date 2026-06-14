using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// RETIRED — nothing creates this job any more; the class and its JobDef exist only so a save containing one
    /// mid-flight still loads. An adversarial audit found vanilla records placed ingredients (job.placedThings)
    /// ONLY for JobDefOf.DoBill, so this custom-def craft could finish without consuming ingredients (duplication).
    /// Superseded by <see cref="JobDriver_BillPrepGather"/>, which gathers into inventory and lets vanilla craft.
    ///
    /// The driver is now a HARD NO-OP: a resumed (or freshly started) job ends Incompletable on its first driver
    /// tick — before any toil action runs, whatever toil index the save resumed at — via an unconditional global
    /// end condition. The finish action still runs, so any ingredients the pre-retirement run had already loaded
    /// are registered for the unload pass instead of being stranded in inventory.
    ///
    /// (Original description) The "fewer trips" fix for AUTOMATIC crafting bills. Vanilla <see cref="JobDriver_DoBill"/> hand-carries
    /// ingredients ONE stack per round-trip (60 steel, 1 component, 30 cloth = three trips), because the hands hold
    /// one thing at a time. This driver instead PRE-LOADS every ingredient stack into the pawn's INVENTORY in one
    /// sweep (weight-limited, smart-overload aware — so it carries far more per trip, even past 100% with the speed
    /// debuff the player opted into), then runs vanilla's OWN collection from inventory + recipe work + finish. The
    /// inventory fetch is the only thing that changes: everything downstream (placing ingredients at the bench,
    /// unfinished-thing creation, the recipe work toil, skill/quality/notify) is vanilla's exact chain, so it works
    /// for EVERY recipe type — including unfinished-thing recipes (flak vest = UnfinishedArmor, weapons, components,
    /// sculptures) that the explicit batch planner deliberately excludes.
    ///
    /// Subclasses <see cref="JobDriver_DoBill"/> so the reused <c>Toils_Recipe.DoRecipeWork()</c>/
    /// <c>MakeUnfinishedThingIfNeeded()</c> (which hard-cast the driver to JobDriver_DoBill) accept it. Per the
    /// player's choice, the finished product is collected into inventory and unloaded later (like the batch planner),
    /// via a faithful, unfinished-thing-aware replica of <c>FinishRecipeAndStartStoringProduct</c>'s product maker.
    ///
    /// Item safety: ingredients are only ever moved (TakeToInventory = SplitOff+TryAdd; vanilla collection pulls
    /// them back out), and products are made only after their ingredients are consumed by vanilla's own chain. Any
    /// ingredient still in inventory on an early job end is registered for the unload pass, never stranded.
    /// </summary>
    public class JobDriver_InventoryDoBill : JobDriver_DoBill
    {
        private const TargetIndex BillGiverInd = TargetIndex.A;   // the bench (== JobDriver_DoBill convention)
        private const TargetIndex IngredientInd = TargetIndex.B;  // transient: the floor stack being pre-loaded
        private const TargetIndex PlaceCellInd = TargetIndex.C;   // vanilla's ingredient place-cell

        private int loadIndex;       // cursor over targetQueueB during the pre-load sweep
        private ThingDef loadDef;    // def of the stack currently being loaded (so we can relink after the take)

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private CompHauledToInventory Comp => pawn.GetComp<CompHauledToInventory>();

        // Each is its own task; never fold a queued vanilla DoBill on the same bill into this one.
        public override bool IsContinuation(Job j) => false;

        public override IEnumerable<Toil> MakeNewToils()
        {
            // RETIRED hard no-op (see class doc): end the job on the very first driver tick. The global end
            // condition is checked BEFORE any toil's init/tick action runs — both on a fresh start and on a
            // save resumed at ANY toil index (SetupToils re-runs this method on load) — so no pre-load, no
            // collection and no recipe work can ever execute again. The toil list below is kept intact so a
            // mid-flight save's scribed curToilIndex stays valid, and the finish action still registers any
            // already-loaded leftovers for the unload pass.
            AddEndCondition(() => JobCondition.Incompletable);

            // --- vanilla JobDriver_DoBill setup (replicated faithfully) ---
            AddEndCondition(delegate
            {
                Thing thing = GetActor().jobs.curJob.GetTarget(BillGiverInd).Thing;
                return (!(thing is Building) || thing.Spawned) ? JobCondition.Ongoing : JobCondition.Incompletable;
            });
            this.FailOnBurningImmobile(BillGiverInd);
            this.FailOn(delegate
            {
                if (job.GetTarget(BillGiverInd).Thing is IBillGiver billGiver)
                {
                    if (job.bill == null || job.bill.DeletedOrDereferenced)
                        return true;
                    if (!billGiver.CurrentlyUsableForBills())
                        return true;
                }
                return false;
            });

            // Safety: any ingredient we pre-loaded but never deposited (early end/timeout) goes to the unload pass.
            AddFinishAction(delegate { RegisterLeftoverIngredientsForUnload(); });

            Toil gotoBillGiver = Toils_Goto.GotoThing(BillGiverInd, PathEndMode.InteractionCell);

            Toil start = ToilMaker.MakeToil("HD_InvBill_Start");
            start.initAction = delegate
            {
                // Belt-and-braces with the global end condition above (which fires first on every traced
                // path): a retired job must never begin its work.
                EndJobWith(JobCondition.Incompletable);
            };
            start.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return start;

            // --- PHASE 1: pre-load every ingredient stack into inventory (one overloaded sweep) ---
            // Each loaded entry is relinked to its inventory stack so vanilla's collection (below) pulls it from
            // inventory with no walking. If the carry ceiling is hit, remaining entries keep their floor reference
            // and vanilla fetches them normally — strictly fewer trips, never more.

            Toil collectStart = ToilMaker.MakeToil("HD_InvBill_CollectStart");
            collectStart.initAction = () => { };
            collectStart.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil loadDecide = ToilMaker.MakeToil("HD_InvBill_LoadDecide");
            loadDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                if (queue == null) { JumpToToil(collectStart); return; }
                // Skip already-consumed/invalid entries.
                while (loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    if (t == null || !t.Spawned || t.IsForbidden(pawn) || t.ParentHolder is Pawn_InventoryTracker)
                        loadIndex++; // null/destroyed, or already inside an inventory (e.g. an UFT) → don't pre-load
                    else
                        break;
                }
                if (loadIndex >= queue.Count) { JumpToToil(collectStart); return; }
                var stack = queue[loadIndex].Thing;
                if (OverloadGate.CountToPickUp(pawn, stack, HaulersDreamMod.Settings) <= 0) { JumpToToil(collectStart); return; }
                if (!pawn.CanReserve(stack) && !pawn.Map.reservationManager.ReservedBy(stack, pawn, job)) { loadIndex++; JumpToToil(loadDecide); return; }
                loadDef = stack.def;
                job.SetTarget(IngredientInd, stack);
            };
            loadDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadDecide;

            Toil loadGoto = ToilMaker.MakeToil("HD_InvBill_LoadGoto");
            loadGoto.initAction = delegate
            {
                var t = job.GetTarget(IngredientInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(loadDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            loadGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return loadGoto;

            // No <checkEncumbrance> on the JobDef, so TakeToInventory does not cap at 100%. By default the pawn
            // carries the bill's full needed amount for this stack OVERWEIGHT (accepting the speed debuff) so the
            // ingredients arrive in one trip — only no-overload mode (strict carry weight, slider Off, or Combat
            // Extended) honours the carry ceiling, exactly like the live siblings (JobDriver_BillPrepGather).
            yield return Toils_Haul.TakeToInventory(IngredientInd, () =>
            {
                var st = job.GetTarget(IngredientInd).Thing;
                if (st == null || !st.Spawned) return 0;
                int need = (job.countQueue != null && loadIndex < job.countQueue.Count) ? job.countQueue[loadIndex] : st.stackCount;
                if (need <= 0) return 0;
                var s = HaulersDreamMod.Settings;
                if (s != null && OverloadGate.NoOverload(s))
                    need = Mathf.Min(need, OverloadGate.CountToPickUp(pawn, st, s));
                return Mathf.Min(need, st.stackCount);
            });

            // Relink this entry to the inventory stack we just loaded into, then advance.
            Toil relink = ToilMaker.MakeToil("HD_InvBill_Relink");
            relink.initAction = delegate
            {
                var queue = job.targetQueueB;
                if (queue != null && loadIndex < queue.Count && loadDef != null)
                {
                    var held = YieldRouter.InventoryStackOfDef(Inv, loadDef);
                    if (held != null)
                        queue[loadIndex] = held; // vanilla collection will GotoThing(canGotoSpawnedParent)=self + StartCarryThing(canTakeFromInventory)
                }
                loadIndex++;
                JumpToToil(loadDecide);
            };
            relink.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return relink;

            yield return collectStart;

            // --- PHASE 2: vanilla's own collection (now sourcing the relinked entries from inventory) ---
            yield return Toils_Jump.JumpIf(gotoBillGiver, () => job.GetTargetQueue(IngredientInd).NullOrEmpty());
            foreach (Toil t in CollectIngredientsToils(IngredientInd, BillGiverInd, PlaceCellInd,
                subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: true,
                BillGiver is Building_WorkTableAutonomous))
                yield return t;

            // --- PHASE 3: vanilla recipe work, then OUR product-into-inventory finish ---
            yield return gotoBillGiver;
            yield return Toils_Recipe.MakeUnfinishedThingIfNeeded();
            yield return Toils_Recipe.DoRecipeWork()
                .FailOnDespawnedNullOrForbiddenPlacedThings(BillGiverInd)
                .FailOnCannotTouch(BillGiverInd, PathEndMode.InteractionCell);
            yield return Toils_Recipe.CheckIfRecipeCanFinishNow();
            yield return FinishRecipeIntoInventory();
        }

        /// <summary>
        /// Faithful replica of the non-mech path of <c>Toils_Recipe.FinishRecipeAndStartStoringProduct</c>, but the
        /// product is collected into inventory (tagged for the unload pass) instead of hauled to storage. Handles
        /// both unfinished-thing and placed-ingredient recipes via the vanilla-identical CalculateIngredients.
        /// </summary>
        private Toil FinishRecipeIntoInventory()
        {
            Toil toil = ToilMaker.MakeToil("HD_InvBill_Finish");
            toil.initAction = delegate
            {
                var actor = pawn;
                var map = actor.Map;
                var curJob = actor.jobs.curJob;
                var recipe = curJob.bill?.recipe;
                if (recipe == null) { actor.jobs.EndCurrentJob(JobCondition.Errored); return; }

                // XP — vanilla awards the non-UFT lump here; UFT XP was already awarded tick-by-tick in DoRecipeWork.
                if (recipe.workSkill != null && !recipe.UsesUnfinishedThing && actor.skills != null)
                {
                    float xp = ticksSpentDoingRecipeWork * 0.1f * recipe.workSkillLearnFactor;
                    actor.skills.GetSkill(recipe.workSkill).Learn(xp);
                }

                List<Thing> ingredients = CalculateIngredients(curJob, actor);
                Thing dominant = CalculateDominantIngredient(curJob, ingredients);
                ThingStyleDef style = ComputeStyle(curJob.bill, recipe);

                List<Thing> products = GenRecipe.MakeRecipeProducts(recipe, actor, ingredients, dominant,
                    BillGiver, curJob.bill.precept, style, curJob.bill.graphicIndexOverride).ToList();

                ConsumeIngredients(ingredients, recipe, map);
                curJob.bill.Notify_IterationCompleted(actor, ingredients);
                RecordsUtility.Notify_BillDone(actor, products);
                if (recipe.WorkAmountTotal((Thing)null) >= 10000f && products.Count > 0)
                    TaleRecorder.RecordTale(TaleDefOf.CompletedLongCraftingProject, actor, products[0].GetInnerIfMinified().def);
                if (products.Count > 0)
                    Find.QuestManager.Notify_ThingsProduced(actor, products);

                for (int i = 0; i < products.Count; i++)
                    PlaceProductIntoInventory(products[i]);

                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        // ---- vanilla-identical ingredient calculation (UFT-aware) ----

        private static List<Thing> CalculateIngredients(Job job, Pawn actor)
        {
            if (job.GetTarget(TargetIndex.B).Thing is UnfinishedThing uft)
            {
                List<Thing> ingredients = uft.ingredients;
                job.RecipeDef.Worker.ConsumeIngredient(uft, job.RecipeDef, actor.Map); // consume the UFT shell
                job.placedThings = null;
                return ingredients;
            }
            var list = new List<Thing>();
            if (job.placedThings != null)
            {
                for (int i = 0; i < job.placedThings.Count; i++)
                {
                    var placed = job.placedThings[i];
                    if (placed.Count <= 0)
                        continue;
                    Thing thing = (placed.Count >= placed.thing.stackCount) ? placed.thing : placed.thing.SplitOff(placed.Count);
                    placed.Count = 0;
                    if (list.Contains(thing))
                        continue;
                    list.Add(thing);
                    if (job.RecipeDef.autoStripCorpses && thing is IStrippable strippable && strippable.AnythingToStrip())
                        strippable.Strip();
                }
            }
            job.placedThings = null;
            return list;
        }

        private static Thing CalculateDominantIngredient(Job job, List<Thing> ingredients)
        {
            var uft = job.GetTarget(TargetIndex.B).Thing as UnfinishedThing;
            if (uft != null && uft.def.MadeFromStuff)
                return uft.ingredients.First(ing => ing.def == uft.Stuff);
            if (ingredients.NullOrEmpty())
                return null;
            var recipe = job.RecipeDef;
            if (recipe.productHasIngredientStuff)
                return ingredients[0];
            if (recipe.products.Any(x => x.thingDef.MadeFromStuff) ||
                (recipe.unfinishedThingDef != null && recipe.unfinishedThingDef.MadeFromStuff))
                return ingredients.Where(x => x.def.IsStuff).RandomElementByWeight(x => x.stackCount);
            return ingredients.RandomElementByWeight(x => x.stackCount);
        }

        private static void ConsumeIngredients(List<Thing> ingredients, RecipeDef recipe, Map map)
        {
            for (int i = 0; i < ingredients.Count; i++)
                recipe.Worker.ConsumeIngredient(ingredients[i], recipe, map);
        }

        private static ThingStyleDef ComputeStyle(Bill bill, RecipeDef recipe)
        {
            if (!ModsConfig.IdeologyActive || recipe.products == null || recipe.products.Count != 1)
                return null;
            if (!bill.globalStyle)
                return bill.style;
            return Faction.OfPlayer.ideos.PrimaryIdeo.style.StyleForThingDef(recipe.ProducedThingDef)?.styleDef;
        }

        // ---- product placement (same track-by-delta pattern as the batch planner / YieldRouter) ----

        private void PlaceProductIntoInventory(Thing product)
        {
            if (product == null || product.Destroyed)
                return;
            var owner = Inv;
            var comp = Comp;
            if (owner == null)
            {
                GenPlace.TryPlaceThing(product, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                return;
            }
            int before = product.stackCount;
            bool moved = owner.TryAddOrTransfer(product, canMergeWithExistingStacks: true);
            if (comp != null && (moved || product.stackCount < before))
            {
                // A non-stacking product (stackLimit 1 — a crafted weapon/apparel/art piece) carries per-instance
                // quality/HP and never merges, so tag the exact crafted Thing, never a same-def InventoryStackOfDef
                // pick (which could be the pawn's own equipped sidearm of that def). Stackables keep the by-def relink.
                Thing held = product.def.stackLimit == 1
                    ? product
                    : (YieldRouter.InventoryStackOfDef(owner, product.def) ?? (moved ? product : null));
                if (held != null)
                    comp.RegisterHauledItem(held);
                comp.NotifyYieldPicked();
            }
            if (!moved && product.stackCount > 0 && !product.Destroyed)
                GenPlace.TryPlaceThing(product, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        }

        private void RegisterLeftoverIngredientsForUnload()
        {
            var comp = Comp;
            var owner = Inv;
            var recipe = job.bill?.recipe;
            if (comp == null || owner == null || recipe?.ingredients == null)
                return;
            // Only the recipe's own ingredient defs — never a pawn's personal kit beyond what we pre-loaded.
            var defs = new HashSet<ThingDef>();
            for (int i = 0; i < recipe.ingredients.Count; i++)
                foreach (var d in recipe.ingredients[i].filter.AllowedThingDefs)
                    defs.Add(d);
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t != null && defs.Contains(t.def))
                    comp.RegisterHauledItem(t);
            }
        }
    }
}
