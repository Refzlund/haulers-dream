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
    /// One-shot handoff of a resolved <see cref="CraftBatchPlan"/> from the dialog to the freshly-ordered batch
    /// job. Keyed by Job identity and consumed the instant the driver starts (same tick as the order), so it
    /// never needs to survive a save — the driver scribes the resolved values into its own fields after consuming.
    /// </summary>
    public static class BatchCraftHandoff
    {
        private static readonly Dictionary<Job, CraftBatchPlan> pending = new Dictionary<Job, CraftBatchPlan>();

        public static void Set(Job job, CraftBatchPlan plan) => pending[job] = plan;

        /// <summary>Drop all pending handoffs — called when a game finishes initialising: an entry whose
        /// ordered job was wiped before starting (drafted same tick, queue cleared) would otherwise keep its
        /// Job key alive in this static map across the session. Misattachment was already impossible (the
        /// consume guard requires plan.bill == job.bill, and Job.Clear() nulls bill on pooling); this is
        /// purely a memory-hygiene sweep.</summary>
        public static void Clear() => pending.Clear();

        public static CraftBatchPlan Consume(Job job)
        {
            if (job != null && pending.TryGetValue(job, out var p))
            {
                pending.Remove(job);
                return p;
            }
            return null;
        }
    }

    /// <summary>
    /// Crafts a chosen workbench bill a fixed number of times in ONE job, pre-loading every repetition's
    /// ingredients into the pawn's inventory up front (so it makes far fewer fetch trips), then performing the
    /// recipe work repeatedly at the bench, collecting each repetition's products into its inventory, and finally
    /// letting the normal unload pass carry the lot to storage. Subclasses <see cref="JobDriver_DoBill"/> purely so
    /// it can reuse the real <c>Toils_Recipe.DoRecipeWork()</c> work toil (which hard-casts the driver to
    /// JobDriver_DoBill) for full fidelity — effects, sound, progress bar, work-speed stats — but it overrides the
    /// whole toil list and replaces vanilla's job-ending "store one product" finish with a loop that stores into
    /// inventory and continues.
    ///
    /// Faithfulness: the per-rep finish replicates <c>Toils_Recipe.FinishRecipeAndStartStoringProduct</c> exactly
    /// for the non-unfinished-thing path — XP, <c>GenRecipe.MakeRecipeProducts</c> (the public product maker),
    /// dominant-ingredient selection, <c>ConsumeIngredient</c>, bill iteration notify, records/quest notify — then
    /// places products into inventory instead of hauling them. Items are never duplicated (products are made only
    /// after the rep's ingredients are pulled, and a short pull aborts the rep without producing) and never lost
    /// (un-consumed ingredients and made products are registered for the unload pass on any job end).
    /// </summary>
    public class JobDriver_BatchCraft : JobDriver_DoBill
    {
        private const TargetIndex BenchInd = TargetIndex.A;       // the workbench (== JobDriver_DoBill BillGiverInd)
        private const TargetIndex LoadStackInd = TargetIndex.B;   // transient: the floor stack being pre-loaded

        // Resolved plan (scribed so the job survives a save mid-batch).
        private List<ThingDef> ingredientDefs = new List<ThingDef>();
        private List<int> perRepCounts = new List<int>();
        private int repsTarget;
        private int repsDone;
        private int deadlineTick;   // absolute TicksGame after which no NEW rep starts; 0 = no timeout
        private bool planResolved;

        private Building_WorkTable Bench => job.GetTarget(BenchInd).Thing as Building_WorkTable;
        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private CompHauledToInventory Comp => pawn.GetComp<CompHauledToInventory>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ingredientDefs, "hdBatchIngDefs", LookMode.Def);
            Scribe_Collections.Look(ref perRepCounts, "hdBatchPerRep", LookMode.Value);
            Scribe_Values.Look(ref repsTarget, "hdBatchRepsTarget", 0);
            Scribe_Values.Look(ref repsDone, "hdBatchRepsDone", 0);
            Scribe_Values.Look(ref deadlineTick, "hdBatchDeadline", 0);
            Scribe_Values.Look(ref planResolved, "hdBatchPlanResolved", false);
            if (ingredientDefs == null) ingredientDefs = new List<ThingDef>();
            if (perRepCounts == null) perRepCounts = new List<int>();
        }

        // Each batch is its own standalone task — never treat a queued vanilla DoBill on the same bill as a continuation.
        public override bool IsContinuation(Job j) => false;

        public override string GetReport()
        {
            var recipe = job.bill?.recipe;
            string label = recipe?.ProducedThingDef?.label ?? recipe?.label ?? "?";
            return "HaulersDream.PlanCraft.JobReport".Translate(label, (repsDone + 1).ToString(), repsTarget.ToString());
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Deliberately NO plan handoff here: vanilla calls TryMakePreToilReservations on the Job's CACHED
            // driver (Job.GetCachedDriver) during TryTakeOrderedJob, then StartJob builds a FRESH driver via
            // MakeDriver — consuming the handoff here would hand the plan to the throwaway cached instance and
            // the running driver would degrade to a 1-rep recovery plan. The handoff is consumed in
            // Notify_Starting, which StartJob calls exactly once on the REAL running driver.
            var bench = Bench;
            if (bench == null)
                return false;
            if (!pawn.Reserve(job.GetTarget(BenchInd), job, 1, -1, null, errorOnFailed))
                return false;
            if (bench.def.hasInteractionCell && !pawn.ReserveSittableOrSpot(bench.InteractionCell, job, errorOnFailed))
                return false;
            return true;
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            ResolvePlanFromHandoff(); // runs once on the real driver; planResolved is scribed, so a save/load
                                      // mid-batch keeps the resolved fields and never re-consumes
        }

        private void ResolvePlanFromHandoff()
        {
            if (planResolved)
                return;
            var plan = BatchCraftHandoff.Consume(job);
            // Pooled-Job safety: JobMaker pools/reuses Job objects, so a stale handoff entry keyed by a recycled
            // Job instance must never attach to an unrelated job — require the plan to be for THIS job's bill.
            if (plan != null && plan.bill != job.bill)
                plan = null;
            if (plan != null && plan.feasible)
            {
                ingredientDefs = new List<ThingDef>(plan.ingredientDefs);
                perRepCounts = new List<int>(plan.perRepCounts);
                repsTarget = plan.resolvedReps;
                deadlineTick = plan.timeoutTicks > 0 ? Find.TickManager.TicksGame + plan.timeoutTicks : 0;
            }
            else
            {
                // No handoff (e.g. re-resolved after an unexpected path): fall back to a single rep of the bill's
                // current ingredients so the job degrades to "craft once" instead of doing nothing/erroring.
                RecoverPlanFromBill();
            }
            planResolved = true;
        }

        private void RecoverPlanFromBill()
        {
            ingredientDefs.Clear();
            perRepCounts.Clear();
            repsTarget = 0;
            var bench = Bench;
            if (bench == null || job.bill == null)
                return;
            var plan = CraftBatchPlanner.Resolve(pawn, bench, job.bill, 1, 0);
            if (plan.feasible)
            {
                ingredientDefs = new List<ThingDef>(plan.ingredientDefs);
                perRepCounts = new List<int>(plan.perRepCounts);
                repsTarget = plan.resolvedReps;
            }
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate
            {
                RegisterLeftoversForUnload();
                // "When done, THEN unload": queue the unload pass now, FORCED — both because the just-finished
                // batch puts the pawn inside the pickup grace window (a non-forced check would skip), and because
                // with markForUnload OFF every automatic trigger is gated off and the tagged products would
                // otherwise strand in inventory forever. No-ops when nothing is tagged.
                PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true);
            });

            this.FailOn(() =>
            {
                var b = Bench;
                if (b == null || !b.Spawned)
                    return true;
                if (job.bill == null || job.bill.DeletedOrDereferenced)
                    return true;
                return !b.CurrentlyUsableForBills();
            });
            this.FailOnBurningImmobile(BenchInd);

            // ---- PHASE 1: pre-load every rep's ingredients into inventory ----

            Toil gotoBench = Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil loadDecide = ToilMaker.MakeToil("HD_BatchLoadDecide");
            loadDecide.initAction = () =>
            {
                if (repsTarget <= 0 || ingredientDefs.Count == 0) { JumpToToil(gotoBench); return; }
                if (PastDeadline()) { JumpToToil(gotoBench); return; }
                Thing next = FindNeededStack();
                if (next == null) { JumpToToil(gotoBench); return; } // everything that's reachable is loaded
                // Nothing left to take from this stack (only possible in strict-carry-weight mode, where the ceiling
                // can be hit) — stop pre-loading and craft what we loaded, rather than re-selecting it forever. In
                // the normal (overload) mode BatchGatherCount carries freely, so this never spuriously stops.
                if (BatchGatherCount(next) <= 0) { JumpToToil(gotoBench); return; }
                // If the reservation races and fails, stop pre-loading and craft what we have rather than risk
                // re-selecting the same un-reservable stack forever — FindNeededStack already filters by CanReserve.
                if (!pawn.Reserve(next, job, 1, -1, null, false)) { JumpToToil(gotoBench); return; }
                job.SetTarget(LoadStackInd, next);
            };
            loadDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadDecide;

            Toil loadGoto = ToilMaker.MakeToil("HD_BatchLoadGoto");
            loadGoto.initAction = () =>
            {
                var t = job.GetTarget(LoadStackInd).Thing;
                if (t == null || !t.Spawned) { JumpToToil(loadDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            loadGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return loadGoto;

            // No <checkEncumbrance> on the JobDef, so TakeToInventory does not cap at 100% — BatchGatherCount carries
            // the whole still-needed amount (overweight) so the batch's ingredients arrive in one trip.
            yield return Toils_Haul.TakeToInventory(LoadStackInd, () => BatchGatherCount(job.GetTarget(LoadStackInd).Thing));

            yield return Toils_Jump.Jump(loadDecide);

            // ---- PHASE 2: walk to the bench once ----

            yield return gotoBench;

            // ---- PHASE 3: craft repeatedly, collecting products into inventory ----

            Toil done = ToilMaker.MakeToil("HD_BatchDone");
            done.initAction = () => { };
            done.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil craftCheck = ToilMaker.MakeToil("HD_BatchCraftCheck");
            craftCheck.initAction = () =>
            {
                if (repsDone >= repsTarget) { JumpToToil(done); return; }
                if (PastDeadline()) { JumpToToil(done); return; }
                if (job.bill == null || job.bill.suspended || !job.bill.ShouldDoNow()) { JumpToToil(done); return; }
                if (!HasOneRepInInventory()) { JumpToToil(done); return; }
                // Clear B so the reused DoRecipeWork sees no UnfinishedThing and uses GetWorkAmount(null).
                job.SetTarget(LoadStackInd, null);
            };
            craftCheck.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return craftCheck;

            yield return Toils_Recipe.DoRecipeWork()
                .FailOnDespawnedNullOrForbidden(BenchInd)
                .FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            yield return FinishOneRepIntoInventory(done);

            yield return Toils_Jump.Jump(craftCheck);

            yield return done;
        }

        // ---- the per-rep finish: faithful replica of FinishRecipeAndStartStoringProduct (non-UFT path),
        //      but products go into inventory and the job continues ----

        private Toil FinishOneRepIntoInventory(Toil doneToil)
        {
            Toil toil = ToilMaker.MakeToil("HD_BatchFinishRep");
            toil.initAction = () =>
            {
                var actor = pawn;
                var map = actor.Map;
                var bill = job.bill;
                var recipe = bill?.recipe;
                if (recipe == null) { JumpToToil(doneToil); return; }

                // Item-safety net: from the PULL through the inventory PLACEMENT the ingredients/products are
                // standalone "limbo" Things — an unexpected throw ANYWHERE in that window (a modded corpse
                // comp inside the pull's Strip(), a recipe Worker, a comp notify during placement) must not
                // lose them, so the try spans the whole window. The pull writes into `ingredients` as it
                // goes, making partial pulls visible to the catch; RestoreToInventory skips Destroyed things,
                // so an unconditional restore returns exactly the un-consumed remainder even when the throw
                // happened mid-ConsumeIngredient.
                var ingredients = new List<Thing>();
                List<Thing> products = null;
                try
                {
                    // 1. Pull exactly this rep's ingredients out of inventory. Aborts (and restores) if short,
                    //    so we never make products from incomplete ingredients.
                    if (!PullOneRep(ingredients))
                    {
                        RestoreToInventory(ingredients);
                        JumpToToil(doneToil);
                        return;
                    }

                    // 2. XP (the non-unfinished-thing branch of vanilla's finish).
                    if (recipe.workSkill != null && actor.skills != null)
                    {
                        float xp = ticksSpentDoingRecipeWork * 0.1f * recipe.workSkillLearnFactor;
                        actor.skills.GetSkill(recipe.workSkill).Learn(xp);
                    }

                    // 3. Dominant ingredient + ideology style, exactly as vanilla computes them.
                    Thing dominant = CalculateDominantIngredient(recipe, ingredients);
                    ThingStyleDef style = ComputeStyle(bill, recipe);

                    // 4. Make products (the public maker vanilla itself uses).
                    products = GenRecipe.MakeRecipeProducts(recipe, actor, ingredients, dominant,
                        BillGiver, bill.precept, style, bill.graphicIndexOverride).ToList();

                    // 5. Consume the ingredients (Destroy) — only AFTER products are made, mirroring vanilla order.
                    for (int i = 0; i < ingredients.Count; i++)
                        recipe.Worker.ConsumeIngredient(ingredients[i], recipe, map);

                    // 6. Bookkeeping notifies, identical to vanilla — including the resource-count refresh
                    //    vanilla's finish toil performs, so the per-rep ShouldDoNow gate reads fresh storage
                    //    numbers (other pawns hauling products mid-batch).
                    bill.Notify_IterationCompleted(actor, ingredients);
                    RecordsUtility.Notify_BillDone(actor, products);
                    if (recipe.WorkAmountTotal((Thing)null) >= 10000f && products.Count > 0)
                        TaleRecorder.RecordTale(TaleDefOf.CompletedLongCraftingProject, actor, products[0].GetInnerIfMinified().def);
                    if (products.Count > 0)
                        Find.QuestManager.Notify_ThingsProduced(actor, products);
                    // Same gate as vanilla's finish action: only TargetCount bills read these counts.
                    if (bill is Bill_Production bpRefresh && bpRefresh.repeatMode == BillRepeatModeDefOf.TargetCount)
                        map.resourceCounter.UpdateResourceCounts();

                    // 7. Collect products into inventory (tagged for the unload pass); overflow drops at the bench.
                    for (int i = 0; i < products.Count; i++)
                        PlaceProductIntoInventory(products[i]);

                    repsDone++;
                    HDLog.Dbg($"{actor} batch-crafted rep {repsDone}/{repsTarget} of {recipe.defName} " +
                              $"({products.Count} product stack(s) into inventory).");
                }
                catch (System.Exception e)
                {
                    // The ONLY justified catch in this file: this rep's ingredients/products are container-less
                    // "limbo" Things during the window, so a bare throw would LEAK or DESTROY save-affecting items.
                    // Restore the un-consumed ingredients and bank any not-yet-placed products back into inventory
                    // for item-safety, THEN rethrow (wrapped with context) so the failure SURFACES as a red error —
                    // never swallow it into a silent JumpToToil that hides the failed rep.
                    RestoreToInventory(ingredients); // skips Destroyed → returns exactly the un-consumed remainder
                    if (products != null)
                        for (int i = 0; i < products.Count; i++)
                            // Bank only the not-yet-placed: a placed product has a holdingOwner, and an
                            // overflow-dropped one is Spawned — re-adding either would double-place.
                            if (products[i] != null && products[i].holdingOwner == null && !products[i].Spawned)
                                PlaceProductIntoInventory(products[i]);
                    throw new System.Exception(
                        $"[Hauler's Dream] batch-craft rep failed for {recipe.defName} (ingredients restored, products banked)", e);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        // ---- helpers ----

        private bool PastDeadline() => deadlineTick > 0 && Find.TickManager.TicksGame >= deadlineTick;

        /// <summary>
        /// How many units to pull from <paramref name="stack"/> on this trip. The player explicitly batched N reps
        /// and wants the FEWEST trips, so by default the pawn carries the WHOLE still-needed amount — overweight,
        /// accepting the move-speed debuff — rather than stopping at the smart-overload ceiling. Only when the
        /// player chose strict carry weight do we honour the ceiling (and then the batch may take several trips).
        /// </summary>
        private int BatchGatherCount(Thing stack)
        {
            if (stack == null || !stack.Spawned) return 0;
            int need = NeededUnits(stack.def);
            if (need <= 0) return 0;
            var s = HaulersDreamMod.Settings;
            // Honour the ceiling under strict carry weight AND with the overload slider at "Off" ("never
            // overload") — otherwise the pawn would go overweight at FULL speed (the StatPart debuff doesn't
            // apply at Off), contradicting the player's explicit choice. Combat Extended counts as strict
            // too (OverloadGate.NoOverload), and the gate clamps to CE's weight+bulk fit.
            if (s != null && OverloadGate.NoOverload(s))
            {
                int head = OverloadGate.CountToPickUp(pawn, stack, s);
                return Mathf.Min(Mathf.Min(need, head), stack.stackCount);
            }
            return Mathf.Min(need, stack.stackCount); // carry freely (overweight) — fewest trips
        }

        /// <summary>Units of <paramref name="def"/> still to load = (perRep×reps for slots using this def) − inventory.</summary>
        private int NeededUnits(ThingDef def)
        {
            if (def == null) return 0;
            int want = 0;
            for (int i = 0; i < ingredientDefs.Count; i++)
                if (ingredientDefs[i] == def)
                    want += perRepCounts[i] * repsTarget;
            if (want <= 0) return 0;
            return Mathf.Max(0, want - InventoryCountOfDef(def));
        }

        /// <summary>Nearest reachable, non-forbidden, reservable, bill-usable floor stack of any plan def we
        /// still need more of — thing-level bill constraints (rot stage, hit points, the bill's filter) and the
        /// bill's ingredient search radius applied exactly like vanilla's ingredient scan, so the batch never
        /// loads what the bill forbids.</summary>
        private Thing FindNeededStack()
        {
            var map = pawn.Map;
            var bill = job.bill;
            float radiusSq = bill != null ? bill.ingredientSearchRadius * bill.ingredientSearchRadius : float.MaxValue;
            IntVec3 root = Bench?.Position ?? pawn.Position;
            Thing best = null;
            int bestDist = int.MaxValue;
            // Distinct plan defs that still need loading.
            for (int d = 0; d < ingredientDefs.Count; d++)
            {
                var def = ingredientDefs[d];
                if (def == null || NeededUnits(def) <= 0)
                    continue;
                var things = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    if (t == null || !t.Spawned || t.IsForbidden(pawn))
                        continue;
                    if (bill != null && !InventoryShare.IsUsableForBill(t, bill))
                        continue;
                    if ((t.Position - root).LengthHorizontalSquared >= radiusSq)
                        continue;
                    if (!pawn.CanReserve(t) || !pawn.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly))
                        continue;
                    int dist = (t.Position - pawn.Position).LengthHorizontalSquared;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = t;
                    }
                }
            }
            return best;
        }

        private int InventoryCountOfDef(ThingDef def)
        {
            var owner = Inv;
            if (owner == null || def == null) return 0;
            int total = 0;
            for (int i = 0; i < owner.Count; i++)
                if (owner[i]?.def == def)
                    total += owner[i].stackCount;
            return total;
        }

        /// <summary>True iff the inventory holds enough of every slot's def for one more repetition.
        /// Def-level only: bill-disallowed personal stock (rotten meat in a pocket) can overstate this by
        /// one cycle — the pull then reads short and the batch ends cleanly, wasting at most one work
        /// cycle at the very end rather than paying a thing-level scan per rep.</summary>
        private bool HasOneRepInInventory()
        {
            // Sum requirements per def (a def may appear in multiple slots).
            for (int i = 0; i < ingredientDefs.Count; i++)
            {
                var def = ingredientDefs[i];
                int needForDef = 0;
                for (int j = 0; j < ingredientDefs.Count; j++)
                    if (ingredientDefs[j] == def)
                        needForDef += perRepCounts[j];
                if (InventoryCountOfDef(def) < needForDef)
                    return false;
            }
            return ingredientDefs.Count > 0;
        }

        /// <summary>Split one rep's worth of each slot's ingredient out of inventory into standalone Things,
        /// appending each pull to the CALLER's list as it goes (so a mid-pull throw leaves the partial pull
        /// visible for restoration). Returns false (with whatever was pulled) if any slot is short.</summary>
        private bool PullOneRep(List<Thing> ingredients)
        {
            var owner = Inv;
            if (owner == null)
                return false;
            var bill = job.bill;
            var recipe = bill?.recipe;

            // Pull per-slot so the ingredient list reflects each recipe slot (matches vanilla's per-placedThing list).
            for (int s = 0; s < ingredientDefs.Count; s++)
            {
                var def = ingredientDefs[s];
                int need = perRepCounts[s];
                while (need > 0)
                {
                    Thing src = null;
                    // Thing-level bill vetting: the loader only fetches usable stacks, but PRE-EXISTING
                    // personal stock of the same def (rotten meat in a pocket) must not be consumed either.
                    // Only-disallowed-stacks-remain reads as short → the rep aborts and restores cleanly.
                    for (int i = 0; i < owner.Count; i++)
                        if (owner[i]?.def == def
                            && (bill == null || InventoryShare.IsUsableForBill(owner[i], bill)))
                        { src = owner[i]; break; }
                    if (src == null)
                        return false; // short → caller restores
                    // Faithful to vanilla CalculateIngredients: strip a clothed/equipped corpse BEFORE it's consumed,
                    // so its apparel/equipment/inventory drops to the floor (at the pawn's feet — the corpse is still
                    // held here) instead of being destroyed with the corpse. Without this, batch-butchering a geared
                    // corpse silently destroys all its gear (save-affecting item loss).
                    if (recipe != null && recipe.autoStripCorpses && src is IStrippable strippable && strippable.AnythingToStrip())
                        strippable.Strip();
                    int take = Mathf.Min(need, src.stackCount);
                    Thing split = src.SplitOff(take); // removes/decrements from the inventory owner
                    if (split == null)
                        return false;
                    if (Comp != null) Comp.Deregister(src); // src may now be empty/destroyed; the registered set self-heals anyway
                    ingredients.Add(split);
                    need -= split.stackCount;
                }
            }
            return true;
        }

        /// <summary>Put a list of pulled ingredient Things back into inventory (used when a rep aborts).</summary>
        private void RestoreToInventory(List<Thing> things)
        {
            var owner = Inv;
            if (owner == null || things == null)
                return;
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t == null || t.Destroyed)
                    continue;
                if (!owner.TryAddOrTransfer(t, canMergeWithExistingStacks: true) && !t.Destroyed && t.holdingOwner == null)
                {
                    // Couldn't re-add (shouldn't happen — it just came from here): drop near the pawn so it's never lost.
                    GenPlace.TryPlaceThing(t, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
            }
        }

        /// <summary>Place a freshly-made product into inventory (tagged for unload); overflow drops at the bench.</summary>
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
                // Pass the moved count so a merge into an already-tagged stack re-notifies CE's
                // HoldTracker with the growth (same idiom as YieldRouter.RouteIntoInventory).
                int movedCount = moved ? before : before - product.stackCount;
                // A non-stacking product (stackLimit 1 — a crafted weapon/apparel/art piece) carries per-instance
                // quality/HP and never merges, so tag the exact crafted Thing, never a same-def InventoryStackOfDef
                // pick (which could be the pawn's own equipped sidearm of that def). Stackables keep the by-def
                // relink so a merge into an already-tagged stack still re-notifies CE's HoldTracker via movedCount.
                Thing held = product.def.stackLimit == 1
                    ? product
                    : (YieldRouter.InventoryStackOfDef(owner, product.def) ?? (moved ? product : null));
                if (held != null)
                    comp.RegisterHauledItem(held, movedCount);
                comp.NotifyYieldPicked();
            }
            if (!moved && product.stackCount > 0 && !product.Destroyed)
                GenPlace.TryPlaceThing(product, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        }

        /// <summary>Register every still-held plan-ingredient and product stack so the unload pass carries them off
        /// — pre-loaded ingredients are otherwise untagged (so they're never shared mid-batch), so leftovers from an
        /// early exit/timeout must be tagged here or they'd sit in inventory forever.</summary>
        private void RegisterLeftoversForUnload()
        {
            var comp = Comp;
            var owner = Inv;
            if (comp == null || owner == null)
                return;
            var planDefs = new HashSet<ThingDef>(ingredientDefs);
            var productDefs = new HashSet<ThingDef>();
            var products = job.bill?.recipe?.products;
            if (products != null)
                for (int i = 0; i < products.Count; i++)
                    if (products[i]?.thingDef != null)
                        productDefs.Add(products[i].thingDef);

            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t == null) continue;
                if (planDefs.Contains(t.def) || productDefs.Contains(t.def))
                    comp.RegisterHauledItem(t);
            }
        }

        // Vanilla's CalculateDominantIngredient, non-UFT branch (we never run the UFT path).
        private static Thing CalculateDominantIngredient(RecipeDef recipe, List<Thing> ingredients)
        {
            if (ingredients.NullOrEmpty())
                return null;
            if (recipe.productHasIngredientStuff)
                return ingredients[0];
            if (recipe.products.Any(x => x.thingDef.MadeFromStuff) ||
                (recipe.unfinishedThingDef != null && recipe.unfinishedThingDef.MadeFromStuff))
                return ingredients.Where(x => x.def.IsStuff).RandomElementByWeight(x => x.stackCount);
            return ingredients.RandomElementByWeight(x => x.stackCount);
        }

        // Vanilla's ideology style selection from FinishRecipeAndStartStoringProduct.
        private static ThingStyleDef ComputeStyle(Bill bill, RecipeDef recipe)
        {
            if (!ModsConfig.IdeologyActive || recipe.products == null || recipe.products.Count != 1)
                return null;
            if (!bill.globalStyle)
                return bill.style;
            return Faction.OfPlayer.ideos.PrimaryIdeo.style.StyleForThingDef(recipe.ProducedThingDef)?.styleDef;
        }
    }
}
