using System;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Is this pawn holding food FOR a tame/train job right now, and how much of it?" — the live-job half of the
    /// animal-interaction food keep (<see cref="AnimalInteractFoodKeepMath"/> does the arithmetic).
    ///
    /// <para><b>The bug this exists for.</b> A pawn that fetches kibble to train or tame an animal was shedding it
    /// again: HD's surplus model had no keep covering interaction food (the packable-food keep is structurally
    /// disjoint from it — see <see cref="AnimalInteractFoodKeepMath"/>), so the whole stack read as surplus and the
    /// unload shipped it to storage. Vanilla never drops it because
    /// <c>JobDriver_InteractAnimal.StartFeedAnimal</c> re-arms <c>lastInventoryRawFoodUseTick</c> and the raw-food
    /// drop loop then waits ~2.5 in-game days; HD's unload has no such clock.</para>
    ///
    /// <para><b>Detected by JOB, never by def.</b> Kibble is ordinary haulable food that HD legitimately hauls, and
    /// once a pawn has HD-swept any kibble the by-def tag self-heal auto-tags every later kibble stack it takes.
    /// So the ONLY honest signal that a stack is spoken for is that the pawn has a live animal-interaction job. The
    /// keep therefore SELF-RELEASES: the moment no current or queued interaction job remains,
    /// <see cref="ReserveNutritionFor"/> returns 0, the keep term is 0, and the ordinary unload ships the food.</para>
    ///
    /// <para><b>Current AND queued jobs.</b> The queued half is what makes the reported case work: HD prepends its
    /// own haul in front of the player's order, so during that haul the Tame/Train job sits in the QUEUE, not in
    /// <c>CurJob</c>. Walking the queue by INDEX (its enumerator boxes — see
    /// <c>PawnUnloadChecker.HasPendingRealWork</c>) keeps the scan allocation-free.</para>
    /// </summary>
    internal static class AnimalInteractFood
    {
        // Per-(pawn, tick) memo of the reserve nutrition, so a scan that calls InventorySurplus.SurplusOf once per
        // inventory stack (the gizmo/alert pass) walks the job queue ONCE per pawn per tick instead of per stack.
        // [ThreadStatic] + lazy-init matches this assembly's hook-reachable memo convention (PawnMassCache,
        // TrackedMassCache): a work scan on a threading mod's worker gets its own memo.
        //
        // STALENESS DIRECTION, stated honestly: this is a READ cache with vanilla stat-cache semantics (at most one
        // tick old). A job that STARTS mid-tick is therefore seen from the next tick. The unload driver's deposit
        // toils are many ticks apart and the keep is re-read on each, so a one-tick window cannot shed a stack the
        // pawn is about to need; every subsequent tick sees the live job.
        [ThreadStatic] private static TickKeyedMemo<float> reserveMemo;

        // Self-register the per-session memo clear with the game-load hygiene sweep (CacheRegistry), exactly like
        // PawnMassCache: the static ctor runs the first time any member here is touched, which is also the only way
        // the memo can come to hold cross-session data.
        static AnimalInteractFood() => CacheRegistry.Register(Clear);

        /// <summary>
        /// The nutrition this pawn must hold back for its live animal-interaction work, or 0 when it has none.
        /// The MAXIMUM over every current/queued interaction job, so a pawn with two of them reserves enough for
        /// the hungrier animal rather than the first one found.
        ///
        /// <para>Per job: the animal's own <c>RequiredNutritionPerFeed × 8</c> when the job carries the animal
        /// (a <c>Tame</c>/<c>Train</c> job's targetA), else the
        /// <see cref="AnimalInteractFoodKeepMath.MaxReserveNutrition"/> ceiling. The ceiling branch is not a
        /// fallback for rare inputs — it is the NORMAL case for the separate <c>TakeInventory</c> FETCH job
        /// <c>WorkGiver_InteractAnimal.TakeFoodForAnimalInteractJob</c> issues before training, whose targetA is
        /// the FOOD, not the animal. An animal with no food need yields 0 (vanilla's <c>CanFeedEver</c> is
        /// <c>Animal?.needs?.food != null</c>, so such an animal is never fed and nothing is reserved for it).</para>
        /// </summary>
        /// <param name="pawn">The carrying pawn; null (or a pawn with no job tracker) → 0.</param>
        /// <returns>Nutrition to reserve, never negative; 0 means "no live interaction job".</returns>
        internal static float ReserveNutritionFor(Pawn pawn)
        {
            var jobs = pawn?.jobs;
            if (jobs == null)
                return 0f;

            int tick = Find.TickManager?.TicksGame ?? -1;
            int key = pawn.thingIDNumber;
            if (reserveMemo.TryGet(tick, key, out float cached))
                return cached;

            float reserve = ReserveOf(jobs.curJob, jobs.curDriver);
            var queue = jobs.jobQueue;
            if (queue != null)
                for (int i = 0; i < queue.Count; i++)
                {
                    // The driver is only ever instantiated for the CURRENT job, so a queued job is classified by
                    // its workGiverDef / JobDef alone.
                    float queued = ReserveOf(queue[i]?.job, null);
                    if (queued > reserve)
                        reserve = queued;
                }

            reserveMemo.Store(tick, key, reserve);
            return reserve;
        }

        /// <summary>True if the pawn has any live (current or queued) animal-interaction job that reserves food.
        /// Convenience over <see cref="ReserveNutritionFor"/> for the two tag/adopt call sites.</summary>
        internal static bool HasAnimalInteractJob(Pawn pawn) => ReserveNutritionFor(pawn) > 0f;

        /// <summary>Would vanilla accept this stack as food to hand an animal? The def half of
        /// <c>WorkGiver_InteractAnimal.HasFoodToInteractAnimal</c>, via the unit-pinned Core predicate. Cheap enough
        /// to run first at every call site, so a non-food stack never pays for the job-queue walk.</summary>
        internal static bool IsInteractFood(ThingDef def)
            => def?.ingestible != null
               && AnimalInteractFoodKeepMath.IsInteractFood(def.IsIngestible, def.IsDrug,
                   (int)def.ingestible.preferability);

        /// <summary>
        /// True if <paramref name="thing"/> is food this pawn is holding for a live animal-interaction job — the
        /// shared predicate for the two paths that must not CLAIM such a stack (the by-def tag self-heal and
        /// surplus adoption). Deliberately unbounded (it does not ask "how many units"): those two paths tag whole
        /// <c>Thing</c>s, and the unload that follows a tag is still bounded by
        /// <c>InventorySurplus.SurplusOf</c>, which subtracts the real reserve.
        /// </summary>
        internal static bool IsHeldForInteraction(Pawn pawn, Thing thing)
            => pawn != null && thing != null && IsInteractFood(thing.def) && HasAnimalInteractJob(pawn);

        /// <summary>
        /// The reserve one job asks for, or 0 when it is not an animal-interaction job.
        ///
        /// <para>PRIMARY SIGNAL is <c>workGiverDef.giverClass</c> deriving from
        /// <see cref="WorkGiver_InteractAnimal"/>. All three vanilla issuing paths set it —
        /// <c>JobGiver_Work.TryIssueJobPackage</c>, <c>FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption</c>,
        /// and <c>Pawn_JobTracker.TryTakeOrderedJobPrioritizedWork</c> — and it is the ONLY signal that also covers
        /// the plain <c>TakeInventory</c> FETCH job training issues, which a JobDef-only test would miss entirely
        /// (leaving the fetch→interact gap the whole fix turns on).</para>
        ///
        /// <para>The <c>Tame</c>/<c>Train</c> JobDef test is a FALLBACK for a foreign issuer that left
        /// <c>workGiverDef</c> null; the driver test covers a foreign JobDef whose driver still derives from
        /// <see cref="JobDriver_InteractAnimal"/>.</para>
        /// </summary>
        /// <param name="job">The job to classify; null → 0.</param>
        /// <param name="driver">The job's driver when it is the pawn's CURRENT job, else null.</param>
        private static float ReserveOf(Job job, JobDriver driver)
        {
            if (job == null)
                return 0f;
            var giverClass = job.workGiverDef?.giverClass;
            bool isInteract = (giverClass != null && typeof(WorkGiver_InteractAnimal).IsAssignableFrom(giverClass))
                              || job.def == JobDefOf.Tame
                              || job.def == JobDefOf.Train
                              || driver is JobDriver_InteractAnimal;
            if (!isInteract)
                return 0f;

            // targetA is the animal for a Tame/Train job (JobDriver_InteractAnimal.AnimalInd), and something else
            // entirely for the TakeInventory fetch job — so NO animal means "reserve the ceiling", not "reserve
            // nothing". The two unresolvable/resolvable-but-foodless cases must stay distinct: a resolvable animal
            // with no food need is vanilla's CanFeedEver == false (it is never fed), so it reserves NOTHING.
            var animal = job.targetA.Thing as Pawn;
            if (animal == null)
                return AnimalInteractFoodKeepMath.MaxReserveNutrition;
            var foodNeed = animal.needs?.food;
            if (foodNeed == null)
                return 0f;
            return AnimalInteractFoodKeepMath.ReserveNutrition(foodNeed.MaxLevel);
        }

        /// <summary>Drop the main thread's memo on game load (FinalizeInit), so an equal tick number across a
        /// quickload cannot serve a previous session's value. Other threads' memos are per-tick self-clearing.</summary>
        internal static void Clear() => reserveMemo.Clear();
    }
}
