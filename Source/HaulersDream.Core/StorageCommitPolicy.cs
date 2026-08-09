using System;

namespace HaulersDream.Core
{
    /*
        ──────────────────────────────────────────────
             Storage commitment decision rule
        ──────────────────────────────────────────────
        THE production rule for "how many units may this pawn send to this storage group right now". The
        Verse glue (StorageCommitments) gathers the four numbers; this turns them into an answer, and the
        concurrency harness grades THIS function rather than a look-alike written for the test.

        → KEY: the harness binding is the whole point. Three shipped fixes for this bug were each verified
          against a rule in isolation and each was correct in isolation; what was never graded was several
          pawns running it at once. A stand-in rule in the tests would restore that blind spot exactly.
        → KEY: PLANNING vs DELIVERING is not a preference, it is which question is being asked. A pawn
          deciding a new pickup must subtract its OWN in-flight load too — that load is going to land in the
          very space it is now pricing. A pawn already holding cargo must not: it is asking where to put
          what it already reserved room for, and charging it twice is how a hauler ends up unable to put
          anything down anywhere (the carry-back the reports describe).
        → GOTCHA: an unknown capacity passes the appetite through UNCHANGED. Treating "not measured" as
          "full" satisfies the invariant perfectly and stops the colony hauling, which is a worse bug than
          the one this replaces. The runtime's job is to hand over as few unknowns as it can; this rule's
          job is not to invent a number it was not given.
    */

    /// <summary>
    /// The rule every storage commitment passes through: appetite, clamped by what the destination has left
    /// once the loads already promised to it are taken off the top.
    /// </summary>
    public static class StorageCommitPolicy
    {
        /// <summary>
        /// Units this pawn may commit toward the destination it is looking at.
        /// </summary>
        /// <param name="sight">The pawn's complete view: free capacity read live, other pawns' committed
        /// units, its own committed units held separately, and what it would take against a bottomless
        /// destination. Nothing else reaches this rule — a figure missing from here is a finding, not a
        /// reason to look it up elsewhere.</param>
        /// <param name="delivering">Whether the pawn is already holding the cargo it is placing. False (it
        /// is planning a fresh pickup) subtracts its own in-flight units as well; true leaves them, because
        /// the space they occupy is the pawn's own.</param>
        /// <returns>Units to commit. 0 means stand down — no job, rather than a job that would carry most
        /// of its load home again. Never negative, never larger than the appetite it was given.</returns>
        public static int Commit(HaulSight sight, bool delivering)
        {
            if (sight.Desire <= 0)
                return 0;

            int others = Math.Max(0, sight.UnitsEnroute);
            int mine = delivering ? 0 : Math.Max(0, sight.OwnUnitsEnroute);
            int spokenFor = DestinationEnroutePolicy.SaturatingAdd(others, mine);

            int free = DestinationEnroutePolicy.FreeAfterEnroute(sight.FreeCapacity, spokenFor);
            return free == int.MaxValue ? sight.Desire : Math.Min(sight.Desire, free);
        }
    }
}
