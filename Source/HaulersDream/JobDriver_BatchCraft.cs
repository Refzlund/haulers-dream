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

        // Self-register the per-session handoff-map clear with the game-load hygiene sweep (see CacheRegistry), so
        // it can never be forgotten. The static ctor runs once on first use — the only way an entry can outlive the
        // session — so a never-used handoff is never registered (and is empty anyway).
        static BatchCraftHandoff() => CacheRegistry.Register(Clear);

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
    /// ingredients into the pawn's inventory up front (so it makes far fewer FETCH trips), then — for EACH rep —
    /// carrying that rep's ingredient(s) out of the pocket into the hands and SETTING THEM DOWN on the bench's
    /// ingredient place-cell (visibly, exactly like vanilla single-rep crafting) before doing the recipe work,
    /// collecting each repetition's products into its inventory, and finally letting the normal unload pass carry
    /// the lot to storage. Subclasses <see cref="JobDriver_DoBill"/> purely so it can reuse the real
    /// <c>Toils_Recipe.DoRecipeWork()</c> work toil (which hard-casts the driver to JobDriver_DoBill) for full
    /// fidelity — effects, sound, progress bar, work-speed stats — but it overrides the whole toil list and replaces
    /// vanilla's job-ending "store one product" finish with a loop that stores into inventory and continues.
    ///
    /// Why carry+place (R1 fix): vanilla <c>JobDriver_DoBill</c> ALWAYS takes even an inventory-sourced ingredient
    /// into the hands, walks it to the bench, and PLACES it on the ingredient cell (recording <c>job.placedThings</c>)
    /// before the recipe consumes from placedThings. An earlier version of this driver split each rep straight out of
    /// the pocket and consumed it — so the corpse/chunk was never set down on the butcher spot/table. Phase 3 now
    /// restores that visible place step per rep and consumes from <c>job.placedThings</c>, reusing vanilla's own
    /// placement bookkeeping (<c>HaulAIUtility.UpdateJobWithPlacedThings</c>) and consume logic
    /// (<c>Toils_Recipe.CalculateIngredients</c>). NOTE: vanilla's <c>PlaceHauledThingInCell</c> records placedThings
    /// ONLY for <c>JobDefOf.DoBill</c>, and this is the custom <c>HaulersDream_BatchCraft</c> def — exactly the trap
    /// the retired <see cref="JobDriver_InventoryDoBill"/> documented — so this driver records each placement itself.
    ///
    /// Faithfulness: the per-rep finish replicates <c>Toils_Recipe.FinishRecipeAndStartStoringProduct</c> exactly
    /// for the non-unfinished-thing path — XP, <c>GenRecipe.MakeRecipeProducts</c> (the public product maker),
    /// dominant-ingredient selection, <c>ConsumeIngredient</c>, bill iteration notify, records/quest notify — then
    /// places products into inventory instead of hauling them. Items are never duplicated (products are made only
    /// AFTER the rep's ingredients are placed on the bench and consumed from placedThings, and a short carry aborts
    /// the rep without producing) and never lost (placed-but-unconsumed ingredients sit Spawned on the bench floor
    /// for normal hauling, and pre-loaded leftovers + made products are registered for the unload pass on any job
    /// end). The corpse <c>autoStripCorpses</c> strip happens at consume time on the PLACED corpse (gear drops at the
    /// bench, never destroyed), exactly as vanilla's <c>CalculateIngredients</c> does.
    /// </summary>
    public class JobDriver_BatchCraft : JobDriver_DoBill
    {
        private const TargetIndex BenchInd = TargetIndex.A;       // the workbench (== JobDriver_DoBill BillGiverInd)
        private const TargetIndex LoadStackInd = TargetIndex.B;   // transient: the floor stack being pre-loaded
                                                                  // (== JobDriver_DoBill IngredientInd; cleared
                                                                  // before DoRecipeWork so it sees no UnfinishedThing)
        private const TargetIndex PlaceCellInd = TargetIndex.C;   // transient: vanilla's ingredient place-cell

        // Resolved plan (scribed so the job survives a save mid-batch).
        private List<ThingDef> ingredientDefs = new List<ThingDef>();
        private List<int> perRepCounts = new List<int>();
        private int repsTarget;
        private int repsDone;
        private int deadlineTick;   // absolute TicksGame after which no NEW rep starts; 0 = no timeout
        private bool planResolved;
        // Cursor over the recipe slots while carrying+placing THIS rep's ingredients onto the bench cell (Phase 3).
        // Scribed so a save taken mid-place resumes the carry/place loop at the same slot instead of double-placing
        // or skipping one. Reset to 0 at the start of each rep.
        private int placeSlotCursor;
        // Units of the CURRENT slot (placeSlotCursor) still to carry+place this rep. A single slot can need more
        // than one handful (its per-rep count can exceed the pawn's carry/stack ceiling), so we carry+place the slot
        // in multiple passes until this reaches 0, then advance the cursor. -1 = "not yet initialised for this slot"
        // (the decision toil seeds it from perRepCounts when it first reaches a slot). Scribed for save mid-place.
        private int placeSlotRemaining = -1;
        // Transient (NOT scribed): true only while the pawn is at the bench in the craft loop, so the rot-freeze
        // patch (Patch_CompRottable_BatchFreeze) spoils carried ingredients normally during the gather/walk
        // phases and freezes them only while actually working. Re-established on the next craftCheck after a
        // load; it never needs to persist.
        public bool ActivelyCrafting;

        private Building_WorkTable Bench => job.GetTarget(BenchInd).Thing as Building_WorkTable;
        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private CompHauledToInventory Comp => pawn.GetComp<CompHauledToInventory>();

        /// <summary>True for an allowMixingIngredients recipe (every vanilla cooked meal, plus kibble/pemmican/
        /// chemfuel/beer): the rep's ingredients are NOT a frozen per-slot def list but a value-fill chosen from
        /// current stock per rep (<see cref="BuildMixForRep"/>). Every (def,count)-assuming helper branches on this;
        /// the non-mixing branch is the unchanged original code.</summary>
        private bool MixingRecipe => job.bill?.recipe?.allowMixingIngredients == true;

        // ---- MIXING-recipe slot model (cached; derived from job.bill.recipe, so no scribe field is needed — the
        //      recipe is always available at runtime, and ingredientDefs/perRepCounts already scribe the CURRENT
        //      rep's mix for a save taken mid-place) ----
        //
        // One MixSlot per recipe ingredient slot: the slot's per-rep VALUE target (GetBaseCount) and the bill-usable
        // candidate defs assigned to it. A def is assigned to its FIRST matching slot (mixDefToSlot) so its value /
        // inventory units are counted toward exactly one slot. ASSUMPTION: vanilla mixing recipes have DISJOINT slot
        // filters (cooking = a single "meals" slot; kibble/pemmican = two disjoint slots — e.g. plant + meat), so
        // first-match assignment is exact; a (non-vanilla) overlapping-filter recipe would attribute a shared def to
        // only its first slot, which is a safe, conservative value accounting (never double-counts a unit).
        private sealed class MixSlot
        {
            public int slotIndex;
            public double perRepValue;          // GetBaseCount() — total value this slot consumes per rep
            public readonly List<ThingDef> defs = new List<ThingDef>(); // bill-usable candidate defs assigned here
        }
        private List<MixSlot> mixSlotsCache;
        private Dictionary<ThingDef, int> mixDefToSlotCache; // def -> its assigned MixSlot index (first match)
        private Bill mixModelBuiltFor;                        // invalidate the cache if job.bill ever changes

        /// <summary>Build (or return the cached) per-slot mix model for the current bill's mixing recipe: each recipe
        /// ingredient slot's per-rep value target + the bill-usable candidate defs assigned to it (first-match), and
        /// the def→slot map used for value accounting. Mirrors the planner's candidate filter exactly (skip null;
        /// fixed slots bypass the bill filter; else require bill.ingredientFilter.Allows). Returns an empty model when
        /// not a mixing recipe / no bill.</summary>
        private List<MixSlot> MixSlots()
        {
            var bill = job.bill;
            var recipe = bill?.recipe;
            if (recipe == null || !recipe.allowMixingIngredients)
                return mixSlotsCache ?? (mixSlotsCache = new List<MixSlot>());
            if (mixSlotsCache != null && mixModelBuiltFor == bill)
                return mixSlotsCache;

            var slots = new List<MixSlot>();
            var defToSlot = new Dictionary<ThingDef, int>();
            var ings = recipe.ingredients;
            for (int s = 0; s < ings.Count; s++)
            {
                var ing = ings[s];
                var slot = new MixSlot { slotIndex = s, perRepValue = ing.GetBaseCount() };
                foreach (var cand in ing.filter.AllowedThingDefs)
                {
                    if (cand == null)
                        continue;
                    // Same rule as CraftBatchPlanner's candidate filter + vanilla AllowMix: fixed slots bypass the
                    // bill's player-set allowed-ingredient list; otherwise require it (implicit-costList recipes have
                    // an empty bill filter, handled by the null check).
                    if (!ing.IsFixedIngredient && bill.ingredientFilter != null && !bill.ingredientFilter.Allows(cand))
                        continue;
                    // Assign each def to its FIRST matching slot only (disjoint-filter assumption above).
                    if (defToSlot.ContainsKey(cand))
                        continue;
                    defToSlot[cand] = s;
                    slot.defs.Add(cand);
                }
                slots.Add(slot);
            }
            mixSlotsCache = slots;
            mixDefToSlotCache = defToSlot;
            mixDefUnionCache = null; // invalidate the derived union so it rebuilds for this (possibly new) bill
            mixModelBuiltFor = bill;
            return mixSlotsCache;
        }

        /// <summary>The value-per-unit of <paramref name="def"/> for the current mixing recipe (nutrition for food;
        /// 1 for a count-based value getter), mirroring vanilla's <c>IngredientValueGetter.ValuePerUnitOf</c>.</summary>
        private float ValuePerUnit(ThingDef def)
        {
            var recipe = job.bill?.recipe;
            if (recipe == null || def == null)
                return 0f;
            return recipe.IngredientValueGetter.ValuePerUnitOf(def);
        }

        /// <summary>True iff <paramref name="def"/> is a candidate ingredient for SOME mix slot of the current recipe
        /// (used for leftover-unload + rot-freeze, which must cover the FULL mix-allowed-def set, not just the current
        /// rep's chosen mix). Builds the model on first use.</summary>
        private bool IsMixDef(ThingDef def)
        {
            if (def == null)
                return false;
            MixSlots(); // ensure mixDefToSlotCache is populated for the current bill
            return mixDefToSlotCache != null && mixDefToSlotCache.ContainsKey(def);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ingredientDefs, "hdBatchIngDefs", LookMode.Def);
            Scribe_Collections.Look(ref perRepCounts, "hdBatchPerRep", LookMode.Value);
            Scribe_Values.Look(ref repsTarget, "hdBatchRepsTarget", 0);
            Scribe_Values.Look(ref repsDone, "hdBatchRepsDone", 0);
            Scribe_Values.Look(ref deadlineTick, "hdBatchDeadline", 0);
            Scribe_Values.Look(ref planResolved, "hdBatchPlanResolved", false);
            Scribe_Values.Look(ref placeSlotCursor, "hdBatchPlaceSlotCursor", 0);
            Scribe_Values.Look(ref placeSlotRemaining, "hdBatchPlaceSlotRemaining", -1);
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

        /// <summary>
        /// Order a batch-craft job — the SYNCED entry point invoked by <see cref="Dialog_PlanCraft"/> on confirm.
        ///
        /// <para>Multiplayer correctness: this runs as a COMMAND replayed on EVERY client (the mod's
        /// <see cref="MultiplayerCompat"/> shim auto-registers every <c>[SyncMethod]</c> via <c>MP.RegisterAll</c>),
        /// so the <see cref="CraftBatchPlan"/> is RE-RESOLVED here from MP-serializable primitives — NOT shipped. The
        /// old path computed the plan only on the ordering client and stashed it in the static
        /// <see cref="BatchCraftHandoff"/> map, which does NOT travel over the wire: other clients then found no
        /// handoff and degraded to a 1-rep <see cref="RecoverPlanFromBill"/>, crafting 1 rep while the issuer crafted
        /// N → a hard desync (different resource consumption + job duration). Because this method runs on every
        /// client, <see cref="BatchCraftHandoff.Set"/> populates the map on every client, so each one's
        /// <see cref="ResolvePlanFromHandoff"/> consumes a matching plan and the 1-rep fallback never fires for a
        /// properly-ordered batch. Determinism of the re-resolved plan is guaranteed because every client sees
        /// identical world state at the synced tick and <see cref="CraftBatchPlanner.Resolve"/> is fully
        /// deterministic (no <c>Rand</c>/time/unordered-collection selection — its one HashSet-order tiebreak now
        /// resolves via <c>shortHash</c>). The nested <see cref="Pawn_JobTracker.TryTakeOrderedJob"/> runs DIRECTLY
        /// (no re-sync) since we are already executing inside the synced command.</para>
        ///
        /// <para>Args are all MP-serializable (Pawn, Building_WorkTable, Bill, int) — never the un-serializable
        /// <see cref="CraftBatchPlan"/>. In a non-MP game this is a plain static method called directly, so
        /// single-player behaviour is unchanged. The method BODY references NO <c>Multiplayer.API</c> type (only the
        /// fully-qualified attribute), so it stays vanilla-safe per the soft-dep isolation rules.</para>
        /// </summary>
        [Multiplayer.API.SyncMethod]
        public static void StartBatchCraftSynced(Pawn pawn, Building_WorkTable bench, Bill bill, int requestedReps, int timeoutTicks)
        {
            if (pawn?.jobs == null || bench == null || bill == null)
                return;

            // Re-resolve against the (identical-on-every-client) current stock. The dialog already previewed a plan
            // locally for its UI, but we deliberately re-resolve here so the AUTHORITATIVE plan is computed inside the
            // synced command on every client — and so a feasibility race between preview and commit is caught the same
            // way everywhere.
            var plan = CraftBatchPlanner.Resolve(pawn, bench, bill, requestedReps, timeoutTicks);
            if (plan == null || !plan.feasible)
            {
                // Stock changed out from under a still-feasible-looking preview (a rare last-frame race). Toast only on
                // the issuing client so a synced no-op doesn't notify every player.
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanCraft.CouldNotStart".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // Re-confirming while a batch on this same bill is already running: vanilla's JobIsSameAs check would
            // silently swallow the new order (and leak the handoff). End the running batch first — its leftovers are
            // tagged + flushed by its finish action — so the new order genuinely takes over with fresh params. (Runs
            // identically on every client because each client's pawn is on the same job at this tick.)
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BatchCraft && pawn.CurJob?.bill == bill)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BatchCraft, bench);
            job.count = 1; // sentinel: Job.count defaults to -1 and vanilla TakeToInventory's ErrorCheckForCarry
                           // red-errors on count <= 0 (the driver's amounts come from its own getter)
            job.bill = bill;
            job.playerForced = true;
            // Set the handoff on EVERY client (this method runs everywhere) so each driver's ResolvePlanFromHandoff
            // consumes a matching plan → no 1-rep RecoverPlanFromBill fallback → no desync.
            BatchCraftHandoff.Set(job, plan);

            bool ok = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            if (ok)
            {
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanCraft.Started".Translate(plan.resolvedReps,
                        bill.recipe.ProducedThingDef?.label ?? bill.LabelCap),
                        pawn, MessageTypeDefOf.TaskCompletion, historical: false);
                HDLog.Dbg($"[{Dialog_PlanCraft.BuildTag}] {pawn} batch crafting {plan.resolvedReps}× {bill.recipe.defName} " +
                          $"(timeout {timeoutTicks} ticks, mass/rep {plan.massPerRepKg:0.0}kg).");
            }
            else
            {
                BatchCraftHandoff.Consume(job); // clear the orphaned handoff
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanCraft.CouldNotStart".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate
            {
                ActivelyCrafting = false; // left the bench / job ended — carried ingredients spoil normally again
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
            // Honest count: once gathering is done (we've arrived at the bench), cap the batch to what the loaded
            // ingredients ACTUALLY support, so the "(n/X)" report shows how many CAN be made. The plan's
            // availability estimate counts reservable in-radius stock but does NOT path-check it, so it can
            // over-promise reps the gather then couldn't reach; this reflects the real load. Runs once (gotoBench
            // is entered once, after Phase 1) and only ever LOWERS the target — a fully-stocked batch is unchanged.
            gotoBench.AddFinishAction(() =>
            {
                int loadable = MaxRepsLoadable();
                if (loadable < repsTarget)
                    repsTarget = loadable;
            });

            Toil loadDecide = ToilMaker.MakeToil("HD_BatchLoadDecide");
            loadDecide.initAction = () =>
            {
                // "Nothing to plan?" — NON-mixing: the frozen per-slot def list is empty (no plan). MIXING: the plan
                // freezes NO defs (ingredientDefs is empty until each rep's BuildMixForRep), so the emptiness signal is
                // instead "the recipe has no mix slots" — checking ingredientDefs here would wrongly skip ALL pre-load.
                bool nothingToPlan = MixingRecipe ? (MixSlots().Count == 0) : (ingredientDefs.Count == 0);
                if (repsTarget <= 0 || nothingToPlan) { JumpToToil(gotoBench); return; }
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

            // ---- PHASE 3: per rep, CARRY+PLACE this rep's ingredients on the bench cell (mirroring vanilla
            //      DoBill's CollectIngredientsToils), do the recipe work, then consume the PLACED things and
            //      collect products into inventory ----
            //
            // Why carry+place at all (R1 fix): vanilla JobDriver_DoBill ALWAYS takes even an inventory-sourced
            // ingredient into the hands, walks it to the bench InteractionCell, and SETS IT DOWN on the ingredient
            // place-cell (recording job.placedThings) before the recipe consumes from placedThings. The old batch
            // driver skipped that entirely — it split each rep straight out of the pocket and consumed it, so the
            // corpse/chunk was never visibly set down on the butcher spot/table. This phase restores the visible
            // place step per rep, faithfully reusing vanilla's own placement bookkeeping (UpdateJobWithPlacedThings)
            // and consume path (CalculateIngredients reads job.placedThings) so item-safety is identical to vanilla.

            Toil done = ToilMaker.MakeToil("HD_BatchDone");
            done.initAction = () => { };
            done.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil craftCheck = ToilMaker.MakeToil("HD_BatchCraftCheck");
            craftCheck.initAction = () =>
            {
                ActivelyCrafting = true; // at the bench now → freeze carried ingredients' rot until the job ends
                if (repsDone >= repsTarget) { JumpToToil(done); return; }
                if (PastDeadline()) { JumpToToil(done); return; }
                if (job.bill == null || job.bill.suspended) { JumpToToil(done); return; }
                // "Do until you have X": the products this batch already banked sit in the pawn's INVENTORY, which
                // vanilla's CountProducts can't see — so a plain ShouldDoNow() reads a stale count and the batch
                // runs every planned rep past the target and never pauses. Gate TargetCount bills on the EFFECTIVE
                // count (world + in-flight banked) instead; vanilla pauses the bill once they're delivered. Other
                // repeat modes (RepeatCount decrements its own field; Forever) count correctly → use vanilla's gate.
                if (job.bill is Bill_Production bpTc && bpTc.repeatMode == BillRepeatModeDefOf.TargetCount
                    && bpTc.recipe.WorkerCounter.CanCountProducts(bpTc))
                {
                    if (!BatchPausePolicy.MayCraftMore(CraftBatchPlanner.EffectiveProductCount(bpTc), bpTc.targetCount, bpTc.paused))
                    { JumpToToil(done); return; }
                }
                else if (!job.bill.ShouldDoNow()) { JumpToToil(done); return; }
                // Decide THIS rep's per-slot ingredient list. For a MIXING recipe, recompute the mix from CURRENT
                // inventory by value (greedy value-fill, mirroring vanilla AllowMix) and write it into ingredientDefs/
                // perRepCounts — the carry/place/consume loop below then runs UNCHANGED on that per-def list. If the
                // mix can't be filled from what's on hand, end the batch cleanly. For a NON-mixing recipe the frozen
                // per-slot plan is used and we just check one rep is loaded (the original behaviour, byte-identical).
                if (MixingRecipe)
                {
                    if (!BuildMixForRep()) { JumpToToil(done); return; }
                }
                else if (!HasOneRepInInventory()) { JumpToToil(done); return; }
                // Start this rep's carry+place loop at slot 0 (uninitialised remaining) with empty placedThings.
                placeSlotCursor = 0;
                placeSlotRemaining = -1;
                job.placedThings = null;
                job.SetTarget(LoadStackInd, null);
                job.SetTarget(PlaceCellInd, null);
            };
            craftCheck.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return craftCheck;

            // --- carry+place sub-loop: one slot per pass, until every slot of this rep is on the bench cell ---

            // placeDecide: pick this rep's NEXT not-yet-placed slot, pull its per-rep amount out of inventory into
            // the hands (carry tracker), and target the place cell. When all slots are placed, fall through to the
            // recipe work. A short pull (inventory ran out — bill-disallowed personal stock, or another job took it)
            // aborts the rep cleanly: any already-placed ingredients of this rep stay Spawned on the bench floor
            // (reservable/haulable — picked up by normal hauling) so nothing is consumed-without-product and nothing
            // is lost, then the batch ends.
            Toil placeDecide = ToilMaker.MakeToil("HD_BatchPlaceDecide");
            Toil doRecipe = ToilMaker.MakeToil("HD_BatchDoRecipeMarker");
            doRecipe.initAction = () =>
            {
                // Clear B (LoadStackInd) so the reused DoRecipeWork sees no UnfinishedThing on TargetIndex.B and
                // uses GetWorkAmount(null) — this batch never runs the unfinished-thing path. The ingredients are
                // on the bench cell + recorded in job.placedThings, which the finish consumes.
                job.SetTarget(LoadStackInd, null);
            };
            doRecipe.defaultCompleteMode = ToilCompleteMode.Instant;
            placeDecide.initAction = () =>
            {
                // Advance the cursor past any finished/zero-count slots, seeding the remaining-to-place for the slot
                // we land on. placeSlotRemaining == -1 means "this slot not yet seeded"; a slot with 0 remaining is
                // done → advance and re-seed the next.
                while (placeSlotCursor < ingredientDefs.Count)
                {
                    if (placeSlotRemaining < 0)
                        placeSlotRemaining = perRepCounts[placeSlotCursor]; // seed from the per-rep count
                    if (placeSlotRemaining > 0)
                        break;          // this slot still has units to carry+place
                    placeSlotCursor++;  // slot complete (0 or non-positive) → move on
                    placeSlotRemaining = -1;
                }
                if (placeSlotCursor >= ingredientDefs.Count)
                {
                    // Every slot of this rep is on the bench cell → go craft.
                    JumpToToil(doRecipe);
                    return;
                }
                var def = ingredientDefs[placeSlotCursor];
                // Carry as much of this slot's REMAINING units as fits in the hands this pass (a slot can exceed the
                // carry/stack ceiling, so it may take several carry+place passes). Mirrors vanilla
                // StartCarryThing(canTakeFromInventory:true): SplitOff the inventory stack(s) into the carry tracker.
                int got = StartCarryRepSlot(def, placeSlotRemaining);
                if (got <= 0)
                {
                    // Inventory ran short for this slot (bill-disallowed personal stock, or another job took it):
                    // this rep can't be completed. Abort it without producing — dropping any partial hands and
                    // leaving the already-placed ingredients as Spawned bench-floor stock — and end the batch.
                    AbortRepCarryPlace();
                    JumpToToil(done);
                    return;
                }
            };
            placeDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return placeDecide;

            // placeGoto: walk to the bench interaction cell (the pawn is normally already here, so this is a no-op
            // step in the common case, but it keeps the animation correct after a save/interruption that moved it).
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(BenchInd);

            // setPlaceCell: choose the ingredient place-cell exactly like vanilla SetTargetToIngredientPlaceCell.
            yield return Toils_JobTransforms.SetTargetToIngredientPlaceCell(BenchInd, LoadStackInd, PlaceCellInd);

            // placeOnCell: set the carried ingredient down on the bench cell AND record it in job.placedThings
            // (via the public, NON-def-gated HaulAIUtility.UpdateJobWithPlacedThings) so vanilla's own consume path
            // (CalculateIngredients, replicated in FinishOneRepIntoInventory) reads it. A bare vanilla
            // PlaceHauledThingInCell would NOT record placedThings here — it only does so for JobDefOf.DoBill, and
            // this is the custom HaulersDream_BatchCraft def (the exact trap the retired JobDriver_InventoryDoBill
            // documented), so we record it ourselves.
            yield return PlaceRepSlotOnCell();

            // Next slot of this rep.
            yield return Toils_Jump.Jump(placeDecide);

            // --- all of this rep's ingredients are now physically on the bench cell ---
            yield return doRecipe;

            // FailOnDespawnedNullOrForbiddenPlacedThings (vanilla DoBill passes the bill-giver index, TargetIndex.A)
            // guards the long DoRecipeWork window: if ANY of this rep's placed ingredients is despawned/null/forbidden
            // or no longer on the pawn's map mid-work — i.e. another pawn (or a script) hauled/grabbed it despite the
            // reservation, or it was destroyed — the rep FAILS CLEANLY before FinishOneRepIntoInventory can consume a
            // stale husk from job.placedThings. The placedThings are Spawned on the bench floor (no container), so the
            // condition's container lookup at BenchInd is irrelevant here; the Spawned check is what carries it.
            yield return Toils_Recipe.DoRecipeWork()
                .FailOnDespawnedNullOrForbiddenPlacedThings(BenchInd)
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
                if (recipe == null) { job.placedThings = null; JumpToToil(doneToil); return; }

                // Item-safety net: the ingredients are now PLACED on the bench cell (Spawned, in job.placedThings),
                // and the made products are standalone "limbo" Things until banked into inventory — an unexpected
                // throw ANYWHERE in this window (a modded corpse comp inside the placed-thing Strip(), a recipe
                // Worker, a comp notify during product placement) must not lose them, so the try spans the whole
                // window. CalculateIngredientsFromPlacedThings consumes job.placedThings (matching vanilla's
                // CalculateIngredients exactly: it SplitOffs the placed stacks, strips clothed corpses, and clears
                // placedThings); a throw before consume leaves the placed ingredients ON THE BENCH FLOOR (Spawned,
                // reservable/haulable — never lost), and a throw mid/after consume + product make banks the
                // not-yet-placed products. Either way nothing vanishes and nothing is duplicated.
                List<Thing> ingredients = null;
                List<Thing> products = null;
                try
                {
                    // 1. Consume from job.placedThings — vanilla's exact CalculateIngredients flow (non-UFT path):
                    //    SplitOff the placed stacks into standalone ingredient Things, strip a clothed/equipped
                    //    placed corpse (so its gear drops AT THE BENCH, not destroyed with the corpse), and null
                    //    placedThings. If nothing was placed (shouldn't happen — the carry+place loop ran first),
                    //    abort the rep WITHOUT producing rather than make products from no ingredients.
                    ingredients = CalculateIngredientsFromPlacedThings(recipe);
                    if (ingredients.Count == 0)
                    {
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
                    // The ONLY justified catch in this file: this rep's ingredients/products are bench-floor or
                    // container-less "limbo" Things during the window, so a bare throw would LEAK or DESTROY
                    // save-affecting items. The placed ingredients (if the throw came before consume) are already
                    // Spawned on the bench floor (reservable/haulable — safe). Bank any not-yet-placed products
                    // back into inventory, THEN rethrow (wrapped with context) so the failure SURFACES as a red
                    // error — never swallow it into a silent JumpToToil that hides the failed rep.
                    if (products != null)
                        for (int i = 0; i < products.Count; i++)
                            // Bank only the not-yet-placed: a placed product has a holdingOwner, and an
                            // overflow-dropped one is Spawned — re-adding either would double-place.
                            if (products[i] != null && products[i].holdingOwner == null && !products[i].Spawned)
                                PlaceProductIntoInventory(products[i]);
                    throw new System.Exception(
                        $"{HDLog.Tag}batch-craft rep failed for {recipe.defName} (placed ingredients on bench floor, products banked)", e);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>Consume this rep's placed ingredients from <c>job.placedThings</c> into standalone Things,
        /// replicating vanilla <c>Toils_Recipe.CalculateIngredients</c> EXACTLY for the non-unfinished-thing path:
        /// SplitOff each placed stack (the whole stack when the placed count covers it), de-duplicate, strip a
        /// clothed/equipped placed corpse (so its gear drops AT THE BENCH instead of being destroyed with the
        /// corpse), and null out <c>placedThings</c>. SplitOff removes the consumed units from the bench cell.</summary>
        private List<Thing> CalculateIngredientsFromPlacedThings(RecipeDef recipe)
        {
            var list = new List<Thing>();
            var placed = job.placedThings;
            if (placed != null)
            {
                for (int i = 0; i < placed.Count; i++)
                {
                    var pt = placed[i];
                    if (pt?.thing == null || pt.Count <= 0)
                        continue;
                    Thing thing = (pt.Count >= pt.thing.stackCount) ? pt.thing : pt.thing.SplitOff(pt.Count);
                    pt.Count = 0;
                    if (list.Contains(thing)) // vanilla guards against the same Thing being recorded twice
                        continue;
                    list.Add(thing);
                    // Faithful to vanilla CalculateIngredients: strip a clothed/equipped corpse BEFORE it's consumed
                    // so its apparel/equipment/inventory drops to the floor (at the bench cell — the corpse is
                    // Spawned there) instead of being destroyed with the corpse.
                    if (recipe != null && recipe.autoStripCorpses && thing is IStrippable strippable && strippable.AnythingToStrip())
                        strippable.Strip();
                }
            }
            job.placedThings = null;
            return list;
        }

        // ---- helpers ----

        private bool PastDeadline() => deadlineTick > 0 && Find.TickManager.TicksGame >= deadlineTick;

        /// <summary>Should this carried thing's rot be frozen right now? True only while actively crafting at the
        /// bench AND the thing is one of this batch's ingredient defs — so the rot-freeze patch leaves the pawn's
        /// unrelated personal stock (and the gather/travel phases) rotting normally. Read by
        /// <see cref="Patch_CompRottable_BatchFreeze"/>. For a MIXING recipe the current rep's chosen mix
        /// (ingredientDefs) is only a SUBSET of the carried mix stock, so freeze any def that belongs to the recipe's
        /// mix-allowed set (IsMixDef) — otherwise pre-loaded stock not in this rep's pick would rot while crafting.</summary>
        public bool ShouldFreezeRot(Thing t)
            => ActivelyCrafting && t != null && (MixingRecipe ? IsMixDef(t.def) : ingredientDefs.Contains(t.def));

        /// <summary>
        /// How many units to pull from <paramref name="stack"/> on this trip. The player explicitly batched N reps
        /// and wants the FEWEST trips, so by default the pawn carries the WHOLE still-needed amount — overweight,
        /// accepting the move-speed debuff — rather than stopping at the smart-overload ceiling. Only when the
        /// player chose strict carry weight do we honour the ceiling (and then the batch may take several trips).
        /// </summary>
        private int BatchGatherCount(Thing stack)
        {
            if (stack == null || !stack.Spawned) return 0;
            // Units still needed of this def. NON-mixing: the def's per-rep×reps demand minus inventory (NeededUnits).
            // MIXING: the def's SLOT still needs `SlotValueNeeded` value, which this def supplies at vpu per unit, so
            // need = CeilToInt(slotValueNeeded / vpu) — enough units of THIS def to cover the slot's remaining value.
            int need = MixingRecipe ? MixNeededUnits(stack.def) : NeededUnits(stack.def);
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
        /// loads what the bill forbids. For a MIXING recipe the def set is the FULL union of mix-slot candidate
        /// defs and "needed" means the def's SLOT still needs value (MixNeededUnits > 0); for a NON-mixing recipe
        /// it is the frozen per-slot plan defs (the original, byte-identical behaviour).</summary>
        private Thing FindNeededStack()
        {
            var map = pawn.Map;
            var bill = job.bill;
            float radiusSq = bill != null ? bill.ingredientSearchRadius * bill.ingredientSearchRadius : float.MaxValue;
            IntVec3 root = Bench?.Position ?? pawn.Position;
            // Spoiling-first preference among the already-valid candidates, gated on the two toggles. With
            // both off, cmpOn is false and the pick reduces to the exact original nearest-to-pawn behaviour
            // (non-food batch bills byte-identical). The spoiling RANK is the new primary key; distance stays
            // the secondary key (the comparator's index-equivalent tiebreak).
            bool cmpOn = SpoilingFirst.AnyToggleOn(HaulersDreamMod.Settings);
            Thing best = null;
            int bestDist = int.MaxValue;
            // The candidate def list: NON-mixing = the frozen per-slot plan defs; MIXING = the union of every mix
            // slot's candidate defs. Either way we de-duplicate and process each def ONCE (NeededUnits/MixNeededUnits
            // and the per-stack scan are identical for every occurrence, and the scan only replaces `best` on a STRICT
            // improvement, so a re-scan of the same def can never change the result — HD-INVCOUNT).
            var defList = MixingRecipe ? MixDefUnion() : ingredientDefs;
            for (int d = 0; d < defList.Count; d++)
            {
                var def = defList[d];
                if (def == null)
                    continue;
                bool seenEarlier = false;
                for (int e = 0; e < d; e++)
                    if (defList[e] == def) { seenEarlier = true; break; }
                if (seenEarlier)
                    continue;
                // Still need more of this def? NON-mixing: per-rep×reps demand minus inventory. MIXING: the def's
                // SLOT still needs value (MixNeededUnits is CeilToInt(slotValueNeeded / vpu), 0 when the slot is full).
                int stillNeeded = MixingRecipe ? MixNeededUnits(def) : NeededUnits(def);
                if (stillNeeded <= 0)
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
                    if (!pawn.CanReserve(t) || !pawn.CanReach(t, PathEndMode.ClosestTouch, ExtraSweepReach.Ceiling(pawn)))
                        continue; // bonus ingredient: cap reach at Some (don't fetch crafting stock from vacuum/fire)
                    int dist = (t.Position - pawn.Position).LengthHorizontalSquared;
                    if (best == null
                        || (cmpOn ? SpoilingFirst.BetterThan(t, dist, best, bestDist, HaulersDreamMod.Settings)
                                  : dist < bestDist))
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
        /// cycle at the very end rather than paying a thing-level scan per rep. For a MIXING recipe "enough"
        /// is VALUE-based: every slot's in-inventory value must reach its per-rep value target (the craftCheck
        /// branches to BuildMixForRep instead of calling this, but other callers may reach it).</summary>
        private bool HasOneRepInInventory()
        {
            if (MixingRecipe)
            {
                var slots = MixSlots();
                if (slots.Count == 0)
                    return false;
                for (int s = 0; s < slots.Count; s++)
                {
                    double per = slots[s].perRepValue;
                    if (per > 0.0 && CurrentInventorySlotValue(slots[s]) + 1e-4 < per)
                        return false;
                }
                return true;
            }
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

        /// <summary>How many FULL reps the pawn's current inventory supports — the honest achievable count after
        /// the gather phase. The plan's availability estimate counts reservable in-radius stock but does NOT
        /// path-check reachability, so it can over-promise; this reflects what was really loaded. Def-level
        /// (matches <see cref="HasOneRepInInventory"/>): a non-bill-usable personal stack of an ingredient def can
        /// overstate it by one rep, which the per-rep <see cref="StartCarryRepSlot"/> carry then catches (a short
        /// carry aborts the rep). 0 = nothing loadable.</summary>
        private int MaxRepsLoadable()
        {
            if (MixingRecipe)
            {
                // VALUE-based: per slot, floor(currentInventorySlotValue / perRepValue); the batch is bounded by the
                // scarcest slot. A slot with no value need (perRepValue <= 0) imposes no limit.
                var slots = MixSlots();
                int maxMix = int.MaxValue;
                for (int s = 0; s < slots.Count; s++)
                {
                    double per = slots[s].perRepValue;
                    if (per <= 0.0)
                        continue;
                    int can = (int)System.Math.Floor((CurrentInventorySlotValue(slots[s]) + 1e-4) / per);
                    if (can < maxMix)
                        maxMix = can;
                }
                return maxMix == int.MaxValue ? 0 : (maxMix < 0 ? 0 : maxMix);
            }
            int max = int.MaxValue;
            for (int i = 0; i < ingredientDefs.Count; i++)
            {
                var def = ingredientDefs[i];
                int perRepForDef = 0;
                for (int j = 0; j < ingredientDefs.Count; j++)
                    if (ingredientDefs[j] == def)
                        perRepForDef += perRepCounts[j];
                if (perRepForDef <= 0)
                    continue;
                int can = InventoryCountOfDef(def) / perRepForDef;
                if (can < max)
                    max = can;
            }
            return max == int.MaxValue ? 0 : max;
        }

        // ========================= MIXING-recipe helpers (used only when MixingRecipe) =========================

        private List<ThingDef> mixDefUnionCache;

        /// <summary>The de-duplicated union of every mix slot's candidate defs (the full set of defs this batch may
        /// pre-load / carry / freeze). Cached; rebuilt whenever the slot model is rebuilt.</summary>
        private List<ThingDef> MixDefUnion()
        {
            var slots = MixSlots();
            if (mixDefUnionCache != null && mixModelBuiltFor == job.bill)
                return mixDefUnionCache;
            var union = new List<ThingDef>();
            var seen = new HashSet<ThingDef>();
            for (int s = 0; s < slots.Count; s++)
            {
                var defs = slots[s].defs;
                for (int i = 0; i < defs.Count; i++)
                    if (seen.Add(defs[i]))
                        union.Add(defs[i]);
            }
            mixDefUnionCache = union;
            return mixDefUnionCache;
        }

        /// <summary>Total bill-usable inventory VALUE currently held toward <paramref name="slot"/> — Σ over inventory
        /// stacks ASSIGNED to this slot (def→first-matching-slot, so a def is never double-counted across slots) of
        /// stackCount × value-per-unit(def). Bill-vetted (InventoryShare.IsUsableForBill) so disallowed personal stock
        /// of a mix def doesn't inflate the estimate — this matches what BuildMixForRep can actually fill (slightly
        /// stricter than the NoMix path's def-level count, deliberately, to avoid under-gathering a mix slot).</summary>
        private double CurrentInventorySlotValue(MixSlot slot)
        {
            var owner = Inv;
            if (owner == null || slot == null)
                return 0.0;
            MixSlots(); // ensure mixDefToSlotCache populated
            var bill = job.bill;
            double total = 0.0;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t?.def == null)
                    continue;
                if (mixDefToSlotCache == null || !mixDefToSlotCache.TryGetValue(t.def, out int sidx) || sidx != slot.slotIndex)
                    continue;
                if (bill != null && !InventoryShare.IsUsableForBill(t, bill))
                    continue;
                float vpu = ValuePerUnit(t.def);
                if (vpu <= 0f)
                    continue;
                total += t.stackCount * (double)vpu;
            }
            return total;
        }

        /// <summary>The VALUE still to load into THIS slot for the whole batch = perRepValue × repsTarget −
        /// currentInventorySlotValue (floored at 0).</summary>
        private double SlotValueNeeded(MixSlot slot)
        {
            if (slot == null || slot.perRepValue <= 0.0)
                return 0.0;
            double want = slot.perRepValue * repsTarget;
            double have = CurrentInventorySlotValue(slot);
            double need = want - have;
            return need > 0.0 ? need : 0.0;
        }

        /// <summary>Units of <paramref name="def"/> still to load for the batch (MIXING): enough of THIS def to cover
        /// its slot's remaining VALUE need — CeilToInt(slotValueNeeded / value-per-unit(def)). 0 when the def is not a
        /// mix def, its value-per-unit is 0, or its slot is already fully loaded.</summary>
        private int MixNeededUnits(ThingDef def)
        {
            if (def == null)
                return 0;
            MixSlots();
            if (mixDefToSlotCache == null || !mixDefToSlotCache.TryGetValue(def, out int sidx))
                return 0;
            var slots = MixSlots();
            if (sidx < 0 || sidx >= slots.Count)
                return 0;
            float vpu = ValuePerUnit(def);
            if (vpu <= 0f)
                return 0;
            double need = SlotValueNeeded(slots[sidx]);
            if (need <= 0.0)
                return 0;
            int units = Mathf.CeilToInt((float)(need / vpu));
            return units < 0 ? 0 : units;
        }

        /// <summary>
        /// Recompute THIS rep's per-def mix from the pawn's CURRENT inventory and write it into
        /// <see cref="ingredientDefs"/> / <see cref="perRepCounts"/> (cleared first), mirroring vanilla's AllowMix
        /// value-fill: for each recipe slot, value-fill its per-rep value target greedily from the bill-usable mix
        /// stock in inventory, taking units of each candidate def in turn. Candidate ORDER = spoiling-first (most-
        /// perishable carried stock used first) when a spoiling toggle is on, else cheapest-value-first (vanilla's
        /// value-ascending order — use up the less valuable food first). Returns FALSE iff some slot cannot be filled
        /// from what's on hand (the rep is impossible → the caller ends the batch cleanly); otherwise TRUE with at
        /// least one (def,count) entry. READ-ONLY w.r.t. the world — it only inspects inventory and writes the plan
        /// lists; the existing carry/place loop performs the actual consumption.
        /// </summary>
        private bool BuildMixForRep()
        {
            ingredientDefs.Clear();
            perRepCounts.Clear();
            var owner = Inv;
            var bill = job.bill;
            if (owner == null || bill == null)
                return false;
            var slots = MixSlots();
            if (slots.Count == 0)
                return false;

            bool spoilingOn = SpoilingFirst.AnyToggleOn(HaulersDreamMod.Settings);

            for (int s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];
                double perRepValue = slot.perRepValue;
                if (perRepValue <= 0.0)
                    continue; // this slot needs no value (e.g. a 0-count fixed ingredient) → nothing to place

                // Gather this slot's candidate defs that are PRESENT (bill-usable) in inventory, with their summed
                // usable inventory count, value-per-unit, and a representative most-spoiled stack (for ordering).
                var cands = new List<MixCand>();
                var slotDefs = slot.defs;
                for (int di = 0; di < slotDefs.Count; di++)
                {
                    var def = slotDefs[di];
                    if (def == null)
                        continue;
                    float vpu = ValuePerUnit(def);
                    if (vpu <= 0f)
                        continue;
                    int count = 0;
                    Thing rep = null; // most-spoiled bill-usable stack of this def (for spoiling-first ordering)
                    for (int i = 0; i < owner.Count; i++)
                    {
                        var t = owner[i];
                        if (t?.def != def)
                            continue;
                        if (!InventoryShare.IsUsableForBill(t, bill))
                            continue;
                        count += t.stackCount;
                        if (rep == null || MoreSpoiled(t, rep))
                            rep = t;
                    }
                    if (count <= 0)
                        continue;
                    cands.Add(new MixCand { def = def, vpu = vpu, count = count, rep = rep });
                }

                if (cands.Count == 0)
                    return false; // no usable stock for this slot in inventory → can't make this rep

                // Order the candidates. Spoiling-first (most-perishable carried stock first) when enabled, then by
                // value ascending (the vanilla AllowMix key — cheaper food first); else purely value ascending. The
                // spoiling comparison reuses SpoilingFirst.BetterThan (honours the cook/butcher toggle distinction
                // and the Fresh/Active gating) on the representative stacks, with no distance tiebreak (dist 0,0).
                cands.Sort((a, b) =>
                {
                    if (spoilingOn && a.rep != null && b.rep != null)
                    {
                        // BetterThan(x, _, y, _) is "x should rank before y". Translate to a sign for List.Sort.
                        if (SpoilingFirst.BetterThan(a.rep, 0, b.rep, 0, HaulersDreamMod.Settings)) return -1;
                        if (SpoilingFirst.BetterThan(b.rep, 0, a.rep, 0, HaulersDreamMod.Settings)) return 1;
                    }
                    int c = a.vpu.CompareTo(b.vpu); // value ascending (vanilla AllowMix order)
                    if (c != 0) return c;
                    return a.def.shortHash.CompareTo(b.def.shortHash); // stable, deterministic final tiebreak
                });

                // Greedy value-fill via the pure Core math (mirrors vanilla's per-slot AllowMix fill exactly).
                var vpuList = new double[cands.Count];
                var availList = new int[cands.Count];
                for (int i = 0; i < cands.Count; i++) { vpuList[i] = cands[i].vpu; availList[i] = cands[i].count; }
                var fill = CraftBatchMath.MixFillSlot(perRepValue, vpuList, availList);
                if (!fill.filled)
                    return false; // not enough usable value on hand to complete this slot → end the batch cleanly
                for (int i = 0; i < cands.Count; i++)
                {
                    if (fill.counts[i] <= 0)
                        continue;
                    ingredientDefs.Add(cands[i].def);
                    perRepCounts.Add(fill.counts[i]);
                }
            }

            // A rep must place SOMETHING (an all-zero-value recipe is excluded upstream — CanBatch requires products,
            // and a meal always has a positive nutrition target). If nothing was assigned, treat the rep as impossible.
            return ingredientDefs.Count > 0;
        }

        /// <summary>True iff carried stack <paramref name="a"/> is more spoiled than <paramref name="b"/> (sooner to
        /// rot). Used only to pick a representative stack per def for spoiling-first ordering; a missing/inactive
        /// CompRottable sorts as "never rots" (least spoiled).</summary>
        private static bool MoreSpoiled(Thing a, Thing b)
        {
            int ta = RotTicks(a), tb = RotTicks(b);
            return ta < tb;
        }

        private static int RotTicks(Thing t)
        {
            var rot = t?.TryGetComp<CompRottable>();
            return (rot != null && rot.Active) ? rot.TicksUntilRotAtCurrentTemp : int.MaxValue;
        }

        /// <summary>One mix-slot candidate def present in inventory: its value-per-unit, summed bill-usable inventory
        /// count, and a representative most-spoiled stack (for spoiling-first ordering).</summary>
        private struct MixCand
        {
            public ThingDef def;
            public float vpu;
            public int count;
            public Thing rep;
        }

        // =======================================================================================================

        /// <summary>Pull up to <paramref name="maxToCarry"/> units of <paramref name="def"/> out of inventory into the
        /// pawn's HANDS (carry tracker), mirroring vanilla's <c>StartCarryThing(canTakeFromInventory:true)</c>, capped
        /// by the carry/stack ceiling, and point target B at the carried Thing so the subsequent place toils set it
        /// down on the bench cell. Tops up across same-def usable pocket stacks until the hands hold the requested
        /// amount, the carry ceiling is hit, or inventory runs dry. Returns the number of units now in the hands for
        /// THIS pass (0 = nothing usable left in inventory → the caller aborts the rep). A non-zero result short of
        /// <paramref name="maxToCarry"/> means the carry ceiling capped it — the slot is finished across multiple
        /// carry+place passes. Does NOT strip a corpse here — vanilla strips the PLACED thing at consume time
        /// (<see cref="CalculateIngredientsFromPlacedThings"/>), so the gear drops at the bench, not at the pull.</summary>
        private int StartCarryRepSlot(ThingDef def, int maxToCarry)
        {
            var owner = Inv;
            if (owner == null || def == null || maxToCarry <= 0)
                return 0;
            var bill = job.bill;
            int already = pawn.carryTracker?.CarriedThing != null && pawn.carryTracker.CarriedThing.def == def
                ? pawn.carryTracker.CarriedThing.stackCount
                : 0;
            // Clamp this pass's carry target to what the hands can hold (pure helper, unit-tested in Core): top up to
            // min(maxToCarry, already + freeStackSpace). A slot exceeding the ceiling finishes over multiple passes.
            int want = CraftBatchMath.CarryPassTarget(maxToCarry, already, pawn.carryTracker?.AvailableStackSpace(def) ?? 0);
            int remaining = want - already;
            // Top up the carried stack across same-def usable inventory stacks until `want` is reached or dry.
            while (remaining > 0)
            {
                Thing src = null;
                // Thing-level bill vetting: the gather phase only fetches usable stacks, but PRE-EXISTING personal
                // stock of the same def (rotten meat in a pocket) must not be consumed either — only-disallowed
                // stacks remaining reads as nothing-usable → the rep aborts cleanly.
                for (int i = 0; i < owner.Count; i++)
                {
                    var t = owner[i];
                    if (t?.def == def && (bill == null || InventoryShare.IsUsableForBill(t, bill)))
                    { src = t; break; }
                }
                if (src == null)
                    break; // inventory dry for this def → carry what we already have (0 aborts the rep upstream)
                int take = Mathf.Min(remaining, src.stackCount);
                // Drive the pull through the carry tracker so the carried Thing is the standalone in-hands stack
                // (SplitOff + innerContainer.TryAdd, identical to StartCarryThing's inventory pull). reserve:false —
                // the bench cell is reserved instead, and a carried thing needs no spawned-target reservation.
                int got = pawn.carryTracker.TryStartCarry(src, take, reserve: false);
                if (got <= 0)
                    break;
                if (Comp != null) Comp.Deregister(src); // src may now be empty/destroyed; the tag set self-heals anyway
                remaining -= got;
            }
            // Point B at the carried Thing so SetTargetToIngredientPlaceCell / PlaceHauledThingInCell operate on it,
            // and set the job count so any vanilla place bookkeeping sees the right amount.
            var hands = pawn.carryTracker?.CarriedThing;
            if (hands == null)
                return 0;
            job.SetTarget(LoadStackInd, hands);
            job.count = hands.stackCount;
            return hands.stackCount;
        }

        /// <summary>Set the carried ingredient DOWN on the bench's ingredient place-cell (target C), and record the
        /// dropped stack in <c>job.placedThings</c> via the public, NON-def-gated
        /// <c>HaulAIUtility.UpdateJobWithPlacedThings</c> — so vanilla's consume path
        /// (<see cref="CalculateIngredientsFromPlacedThings"/>) reads it. A plain vanilla
        /// <c>Toils_Haul.PlaceHauledThingInCell</c> would NOT record placedThings for this custom JobDef (it gates
        /// the bookkeeping on <c>JobDefOf.DoBill</c>), so we drop + record explicitly here.</summary>
        private Toil PlaceRepSlotOnCell()
        {
            Toil toil = ToilMaker.MakeToil("HD_BatchPlaceOnCell");
            toil.initAction = () =>
            {
                var carried = pawn.carryTracker?.CarriedThing;
                if (carried == null)
                {
                    // Nothing in hands (shouldn't happen — StartCarryRepSlot ran first). Skip this slot.
                    placeSlotRemaining = 0;
                    job.SetTarget(LoadStackInd, null);
                    job.SetTarget(PlaceCellInd, null);
                    return;
                }
                int carriedCount = carried.stackCount;
                IntVec3 cell = job.GetTarget(PlaceCellInd).Cell;
                if (!cell.IsValid)
                    cell = Bench?.InteractionCell ?? pawn.Position;
                int placedCount = 0;
                // The placedAction callback records EXACTLY what landed (Thing + count) into job.placedThings via
                // vanilla's own helper (appends/increments a ThingCountClass keyed by the placed Thing) — the precise
                // bookkeeping vanilla PlaceHauledThingInCell performs for DoBill, replicated here because the custom
                // HaulersDream_BatchCraft def is not in PlaceHauledThingInCell's def whitelist. Using the callback
                // (rather than the out-Thing) faithfully credits partial/merged placements stack-by-stack.
                void Record(Thing th, int added)
                {
                    placedCount += added;
                    HaulAIUtility.UpdateJobWithPlacedThings(job, th, added);
                    // RESERVE the placed stack via the physical-interaction reservation manager — EXACTLY what
                    // vanilla JobDriver_DoBill.CollectIngredientsToils does immediately after PlaceHauledThingInCell
                    // (it reserves job.GetTarget(ingredientInd) = the placed thing). This blocks every other pawn's
                    // CanReserve on it (ReservationManager.CanReserve early-returns false when
                    // physicalInteractionReservationManager.IsReserved(target) && !IsReservedBy(claimant)), so another
                    // colonist's WorkGiver_Haul / WorkGiver_DoBill can no longer grab the placed ingredient during the
                    // many-tick DoRecipeWork window — closing the theft→duplication hole. The reservation is keyed by
                    // (pawn, job, thing) and released automatically on job cleanup via
                    // Pawn.ClearReservationsForJob → physicalInteractionReservationManager.ReleaseClaimedBy(pawn, job),
                    // so it never leaks into the next bill. (Reserve is idempotent: IsReservedBy short-circuits a repeat,
                    // so re-recording a merged stack across passes is safe.)
                    if (th != null)
                        pawn.Map?.physicalInteractionReservationManager.Reserve(pawn, job, th);
                }
                // Drop the carried stack onto the bench cell.
                if (!pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _, Record)
                    && carried.holdingOwner == null && !carried.Spawned && !carried.Destroyed)
                {
                    // Direct drop failed (cell blocked): fall back to a near-bench drop so the ingredient is never
                    // stranded in hands, and still record it so the rep consumes it rather than leaking it.
                    GenPlace.TryPlaceThing(carried, cell, pawn.Map, ThingPlaceMode.Near, out _, Record);
                }
                // R3 residue robustness: a place can leave UN-SEATABLE residue in the hands (e.g. the bench cell
                // already held a near-full stack that absorbed only part of the carry, and ThingPlaceMode.Near found
                // no adjacent room either). If we credited+advanced the cursor with a different-def stack still in the
                // hands, the NEXT slot's StartCarryRepSlot (a DIFFERENT def) would target that stale wrong-def residue
                // (it can't merge into a mismatched carried thing) and place the WRONG def for that slot, corrupting
                // job.placedThings. So whenever the carry tracker still holds something after this place, drop it
                // safely near the bench (mirroring AbortRepCarryPlace) so the hands are empty before we advance — the
                // dropped residue is Spawned/reservable/haulable (never lost) and rides the normal unload like any
                // other leftover. The placed portion stays recorded+reserved; only the unplaceable remainder is shed.
                var leftover = pawn.carryTracker?.CarriedThing;
                if (leftover != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                // Account for what actually went down this pass. Decrement the slot's remaining by the placed amount;
                // when it hits 0 the slot is fully on the bench → advance the cursor (re-seed next slot). If a drop
                // somehow placed nothing (carried thing still in hand), fall back to the carried count so we never
                // loop forever on the same slot — but record nothing extra (placedThings already reflects reality).
                // With the residue shed above, advancing the cursor can never carry a stale wrong-def stack forward.
                int credit = placedCount > 0 ? placedCount : carriedCount;
                placeSlotRemaining -= credit;
                if (placeSlotRemaining <= 0)
                {
                    placeSlotCursor++;
                    placeSlotRemaining = -1; // re-seed the next slot in placeDecide
                }
                job.SetTarget(LoadStackInd, null);
                job.SetTarget(PlaceCellInd, null);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>Abort the current rep's carry+place because a slot ran short: drop anything still in the pawn's
        /// hands and convert this rep's already-placed ingredients back into reservable/haulable floor stacks for the
        /// unload pass (they are already Spawned on the bench cell — just clear the placedThings record so the next
        /// rep/finish doesn't consume them). Nothing is destroyed or duplicated — the leftover ingredients ride the
        /// normal unload, exactly like a timed-out batch's pre-loaded leftovers.</summary>
        private void AbortRepCarryPlace()
        {
            // Drop whatever is in hands near the bench (never leave a carried thing stranded on job end).
            var carried = pawn.carryTracker?.CarriedThing;
            if (carried != null)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            // The already-placed ingredients of this aborted rep are Spawned on the bench floor; they are NOT
            // consumed (no products were made), so just forget the placedThings record — RegisterLeftoversForUnload
            // (run in the finish action) re-tags any of our plan-def stock still in inventory, and the spawned
            // bench-floor stacks are picked up by normal hauling. Clearing placedThings prevents the finish/next rep
            // from consuming ingredients that produced nothing.
            job.placedThings = null;
            placeSlotCursor = 0;
            placeSlotRemaining = -1;
            job.SetTarget(LoadStackInd, null);
            job.SetTarget(PlaceCellInd, null);
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
            // For a MIXING recipe, ingredientDefs holds only the CURRENT rep's chosen mix — pre-loaded stock of defs
            // NOT used in the final rep would otherwise be missed. Tag the FULL mix-allowed-def set so every leftover
            // mix ingredient still in inventory rides the unload pass.
            if (MixingRecipe)
            {
                var slots = MixSlots();
                for (int sx = 0; sx < slots.Count; sx++)
                {
                    var defs = slots[sx].defs;
                    for (int di = 0; di < defs.Count; di++)
                        planDefs.Add(defs[di]);
                }
            }
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
