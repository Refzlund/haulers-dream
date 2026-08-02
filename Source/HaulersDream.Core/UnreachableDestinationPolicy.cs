namespace HaulersDream.Core
{
    /// <summary>What an unload trip does with one stack whose storage destination could not be REACHED.</summary>
    public enum UnreachableDestinationAction
    {
        /// <summary>Set this stack aside for the rest of the trip and carry on with the remaining load. The
        /// stack keeps its tag and its place in the pawn's inventory, so a later trigger retries it.</summary>
        SetAsideAndContinue,

        /// <summary>Stop the trip. Either nothing is left worth trying, or so many destinations have failed to
        /// path that the pawn is plainly cut off from its storage and walking the rest of the load is waste.</summary>
        EndTrip
    }

    /// <summary>
    /// The bound for an unload delivery whose destination passes the storage search but cannot be PATHED to.
    ///
    /// <para><b>The failure this bounds.</b> A destination can be perfectly valid storage and still be
    /// unreachable at the moment the pawn tries to walk there: a shelf sealed behind a wall the player just
    /// finished, a stack another mod moved into a closed room, a doorway blocked by a hauler mid-walk, a
    /// container whose only free side faces solid rock. RimWorld answers a failed path by ending the job as
    /// <c>ErroredPather</c>, and <c>Pawn_JobTracker.EndCurrentJob</c> answers THAT with a hardcoded 250-tick
    /// <c>JobDefOf.Wait</c> (decompile-verified) — the "standing" report a player sees.</para>
    ///
    /// <para>On its own that is a bounded four-second hiccup. What made it unbounded was the shape of the
    /// unload job: ending the job runs the driver's finish action, which RE-TAGS the carried stack, and only
    /// THEN does vanilla drop the stack at the pawn's feet (<c>carryThingAfterJob</c> is false). The floor
    /// stack is picked straight back up, re-tagged and routed at the same unreachable destination, and because
    /// the idle backstop re-queues the unload on the SAME 250-tick period as vanilla's error wait, the retry
    /// is phase-locked to the failure. The pawn stands there for hours.</para>
    ///
    /// <para><b>The bound.</b> Keep the stack in INVENTORY (nothing reaches the floor, so nothing is
    /// re-scooped), set it aside for the rest of the trip, put it on the shared re-offer backoff
    /// (<see cref="HaulChurnPolicy.BackoffTicks"/>, which <see cref="BreaksPhaseLock"/> pins longer than the
    /// re-queue period) and get on with the rest of the load. This policy decides only whether the trip
    /// continues or stops.</para>
    ///
    /// <para><b>Not in scope: a permanently sealed room.</b> That case was never broken and needs no bound —
    /// <c>HaulAIUtility.PawnCanAutomaticallyHaulFast</c>, <c>StoreUtility.IsGoodStoreCell</c> and the unload's
    /// own fallback all reject a destination the pawn cannot reach, so it is never chosen in the first place.
    /// The dangerous destination is the one that PASSES those checks and then fails to path.</para>
    ///
    /// <para>Pure and allocation-free (ints in, enum/bool out); the Verse layer supplies the live trip state.
    /// Count-based with no randomness and no client-local state, so it is multiplayer-deterministic.</para>
    /// </summary>
    public static class UnreachableDestinationPolicy
    {
        /// <summary>
        /// How many destinations may fail to path in ONE unload trip before the trip is abandoned. A single
        /// transient block (a hauler standing in the doorway, a door closing) costs one failure and the next
        /// stack delivers normally, so a small budget still absorbs ordinary contention. Three distinct
        /// destinations failing inside one trip means something structural — the pawn is walled off from its
        /// storage side of the base — and every further stack would walk into the same wall, so the trip stops
        /// and the load rides along until a later trigger, by which time the backoff has expired and the
        /// obstruction has usually cleared.
        /// </summary>
        public const int MaxPathFailuresPerTrip = 3;

        /// <summary>
        /// The idle-backstop scan period in game ticks — how often a pawn standing idle is re-offered its
        /// unload trip. This is the SINGLE SOURCE for that cadence: <c>HaulersDreamGameComponent</c>'s own
        /// interval is defined from this constant, so the phase-lock relationship
        /// (<see cref="BreaksPhaseLock"/>) cannot silently rot when either side is retuned.
        /// </summary>
        public const int IdleScanIntervalTicks = 250;

        /// <summary>
        /// What to do with a stack whose destination just failed to path.
        ///
        /// <para>Precedence: an empty trip stops regardless of budget (there is nothing left to walk to), and
        /// only then does the failure budget apply. Total for every input — a non-positive count of either
        /// kind reads as "none", so a miscounted caller degrades to stopping the trip rather than looping.</para>
        /// </summary>
        /// <param name="pathFailuresThisTrip">Destinations that have failed to path in this trip INCLUDING the
        /// one just failed (the caller counts the failure before asking).</param>
        /// <param name="remainingCandidates">How many tracked stacks the trip could still try after setting
        /// this one aside. An upper bound is fine: over-counting costs one extra pass through the driver's own
        /// "nothing unloadable" branch, which ends the trip anyway.</param>
        public static UnreachableDestinationAction Choose(int pathFailuresThisTrip, int remainingCandidates)
        {
            if (remainingCandidates <= 0)
                return UnreachableDestinationAction.EndTrip;

            return pathFailuresThisTrip >= MaxPathFailuresPerTrip
                ? UnreachableDestinationAction.EndTrip
                : UnreachableDestinationAction.SetAsideAndContinue;
        }

        /// <summary>
        /// Whether a re-offer backoff of <paramref name="backoffTicks"/> outlasts the idle re-queue period, and
        /// so cannot settle into lockstep with it.
        ///
        /// <para>This is the specific property that breaks the stall. A set-aside stack is only protected while
        /// its backoff holds; if the backoff were shorter than (or equal to) the scan period, the very next idle
        /// scan after it expired would re-offer the same unreachable destination on the same cadence that
        /// produced the failure, and the loop would re-form with a longer stride instead of being broken. Strict
        /// inequality, because an equal window expires exactly on a scan tick.</para>
        /// </summary>
        /// <param name="backoffTicks">The re-offer backoff window in game ticks (the driver stamps
        /// <see cref="HaulChurnPolicy.BackoffTicks"/>).</param>
        public static bool BreaksPhaseLock(int backoffTicks)
            => backoffTicks > IdleScanIntervalTicks;
    }
}
