namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision for "Do forever" (and batches generally) not freezing a pawn to a long batch when it urgently
    /// needs to eat / sleep / or is about to break or fight (issue #1). The batch driver checks this BETWEEN reps
    /// (never mid-item), so a yield finishes the current item first; then it jumps to the job's "done" toil, whose
    /// FinishAction already unloads the carried stock and lets the pawn head off to satisfy the need. The yield model
    /// is therefore: "finish this item, put everything away, then go" — it never abandons work mid-craft and never
    /// drops carried ingredients on the floor.
    ///
    /// <para>Deliberately CONSERVATIVE: a batch is only interrupted at GENUINELY urgent points, mirroring how vanilla
    /// itself prioritises survival needs over work in the think tree (a merely "Hungry"/"Tired" pawn keeps working;
    /// it's the URGENT step below that the survival job-givers preempt work at). Minor/slightly-low needs do NOT
    /// yield — that would shred a batch over a coffee break. A player-FORCED batch ("do now") never yields: the
    /// player explicitly asked for the whole batch, so the driver passes <paramref name="playerForced"/> true and
    /// this returns false regardless of needs.</para>
    /// </summary>
    public static class BatchYieldPolicy
    {
        // Category levels passed in as plain ints so this stays Verse-free. They match the ORDINAL values of the
        // vanilla enums the driver reads (HungerCategory: Fed=0,Hungry=1,UrgentlyHungry=2,Starving=3; RestCategory:
        // Rested=0,Tired=1,VeryTired=2,Exhausted=3), so the driver can pass `(int)pawn.needs.food.CurCategory` etc.
        // directly. "Urgent" is the threshold at which vanilla's survival job-givers preempt work.
        public const int FoodUrgentLevel = 2; // HungerCategory.UrgentlyHungry — below this (Hungry/Fed) keep crafting
        public const int RestUrgentLevel = 2; // RestCategory.VeryTired      — below this (Tired/Rested) keep crafting

        /// <summary>
        /// Should the batch STOP between reps and yield (finish current item → unload → go)?
        /// </summary>
        /// <param name="playerForced">The batch job is a player "do now" order — never yield (run the whole batch).</param>
        /// <param name="foodCategory">(int)pawn.needs.food.CurCategory, or -1 / &lt;0 if the pawn has no food need.</param>
        /// <param name="restCategory">(int)pawn.needs.rest.CurCategory, or -1 / &lt;0 if the pawn has no rest need.</param>
        /// <param name="mentalBreakImminent">pawn.mindState.mentalBreaker.BreakMajorIsImminent (or Extreme) — about to break.</param>
        /// <param name="drafted">pawn.Drafted — the player has taken manual control; release it from the batch.</param>
        /// <param name="inDanger">a genuine danger signal (e.g. pawn.Downed) — stop crafting.</param>
        public static bool ShouldYield(bool playerForced, int foodCategory, int restCategory,
            bool mentalBreakImminent, bool drafted, bool inDanger)
        {
            // A forced batch runs to completion no matter what the pawn needs — the player asked for it explicitly.
            if (playerForced)
                return false;

            // Drafted or genuinely in danger: release the pawn from the batch immediately (after this item).
            if (drafted || inDanger)
                return true;

            // About to suffer a (major/extreme) mental break: stop so the pawn can recreate / eat / sleep before it
            // snaps, rather than being pinned to a long batch right up to the break.
            if (mentalBreakImminent)
                return true;

            // Urgent survival needs — the level at which vanilla's own food/rest job-givers preempt work. A merely
            // Hungry/Tired pawn keeps crafting (so a normal batch isn't chopped up); only UrgentlyHungry+/VeryTired+
            // yields. A negative category means "no such need on this pawn" → never triggers.
            if (foodCategory >= FoodUrgentLevel)
                return true;
            if (restCategory >= RestUrgentLevel)
                return true;

            return false;
        }
    }
}
