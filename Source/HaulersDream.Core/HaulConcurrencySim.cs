using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /*
        ──────────────────────────────────────────────
                Haul concurrency simulation
        ──────────────────────────────────────────────
        N haulers, ONE destination with K free units, and a rule supplied by the caller. Deterministic and
        free of game types, so the one question the unit suite has never been able to ask can be asked
        headlessly: not "is the rule right" but "what happens when several pawns run it at once".

        The invariant every scenario is graded on:

            the sum of units committed by all haulers toward a destination
            never exceeds that destination's free capacity

        → KEY: a hauler PICKS UP in one job and DEPOSITS in a later one, so its commitment is live across a
          gap in which other haulers plan against the same destination. Decide and Deposit are separate
          commands for that reason alone. A model where a hauler decides and deposits in one indivisible
          step cannot produce the bug and would grade every rule as correct.
        → GOTCHA: nothing here is clamped to capacity. A rule may commit ten times what fits, and the run
          will say so. The moment the simulation starts repairing a bad answer it can only ever report
          success, which is precisely how this bug passed review three times.
        → NOTE: the destination's free space is re-read live at every decision while in-flight commitments
          may be a per-tick snapshot (see EnrouteVisibility). That asymmetry is not a simplification — it is
          copied from the mod, where cell space is measured per plan and the in-flight figure is memoised
          for the tick, and it is where the residual over-haul survives.
    */

    /// <summary>
    /// How a deciding hauler gets to see what other haulers have already committed. The difference is worth
    /// a whole axis because a rule that holds under one can fail under the other, and the real mod runs the
    /// weaker of the two.
    /// </summary>
    public enum EnrouteVisibility
    {
        /// <summary>Every commitment is visible the instant it is made. The ideal a ledger aims at, and the
        /// right default for grading a rule on its own merits.</summary>
        Immediate,

        /// <summary>Commitments are frozen at the first decision of each tick, so haulers deciding in the
        /// same tick cannot see one another and only a clock advance reveals them. This is what the mod's
        /// per-tick in-flight snapshot actually provides, and the known residual: several haulers planned in
        /// one instant still each read a destination nobody has committed to yet.</summary>
        TickSnapshot
    }

    /// <summary>
    /// Runs a scripted multi-hauler scenario against one destination and reports what happened.
    ///
    /// <para>Deterministic by construction: there is no clock, no randomness and no scheduler. Every event
    /// comes from the caller's ordered script, and the only aggregation over unordered state is integer
    /// addition, whose result does not depend on the order it was summed in. The same inputs therefore
    /// produce a byte-identical trace on any machine and any run.</para>
    /// </summary>
    public static class HaulConcurrencySim
    {
        /// <summary>
        /// Play a scenario and return everything observed.
        /// </summary>
        /// <param name="freeCapacity">Units the destination can take before anything arrives. Negative reads
        /// as full — the invariant is then simply that nobody may commit anything.</param>
        /// <param name="script">The events, in order. Enumerated exactly once. An empty script is a valid
        /// run (a colony where nobody was given work) and produces an empty trace.</param>
        /// <param name="decision">The rule under test. Called once per decision and never second-guessed.</param>
        /// <param name="visibility">How commitments reach a deciding hauler. Defaults to
        /// <see cref="EnrouteVisibility.Immediate"/>, which grades the rule itself rather than the mod's
        /// snapshot staleness; choose <see cref="EnrouteVisibility.TickSnapshot"/> to model that too.</param>
        /// <returns>The run's steps and totals.</returns>
        /// <exception cref="ArgumentNullException">Either the script or the rule is null. Both are caller
        /// mistakes with no sensible fallback: silently treating a missing rule as "commit nothing" would
        /// make a broken test pass the invariant perfectly.</exception>
        public static HaulSimTrace Run(
            int freeCapacity,
            IEnumerable<HaulSimCommand> script,
            HaulCommitDecision decision,
            EnrouteVisibility visibility = EnrouteVisibility.Immediate)
        {
            if (script == null)
                throw new ArgumentNullException(nameof(script), "a haul scenario needs a script; pass an empty one for a colony with no work");
            if (decision == null)
                throw new ArgumentNullException(nameof(decision), "a haul scenario needs the rule under test; there is no default rule on purpose");

            var simulation = new Simulation(freeCapacity, decision, visibility);
            foreach (var command in script)
                simulation.Apply(command);
            return simulation.Finish();
        }

        /// <summary>
        /// Build the standard scenario shape: haulers 1..N each take one trip, with a controllable number of
        /// them planning before the first delivery lands.
        ///
        /// <para><paramref name="decideAhead"/> is the whole concurrency dial. At 1 the colony is perfectly
        /// serialised — each hauler decides only after the previous one has unloaded — and even a rule with
        /// no cross-pawn term looks correct. At <paramref name="pawnCount"/> every hauler plans before
        /// anything arrives, which is the reported scenario. Values in between are the realistic middle,
        /// and a rule that holds at both ends can still fail there.</para>
        /// </summary>
        /// <param name="pawnCount">How many haulers take part. Ids are 1..N. Zero or less yields an empty
        /// script.</param>
        /// <param name="desirePerPawn">What each hauler would take against an unlimited destination.</param>
        /// <param name="decideAhead">How many haulers plan before the first deposit. Clamped into
        /// 1..<paramref name="pawnCount"/>.</param>
        /// <returns>A fresh, mutable script the caller may extend.</returns>
        public static List<HaulSimCommand> Interleave(int pawnCount, int desirePerPawn, int decideAhead)
        {
            var script = new List<HaulSimCommand>();
            if (pawnCount <= 0)
                return script;

            int ahead = Math.Max(1, Math.Min(decideAhead, pawnCount));
            int nextToDecide = 1;
            for (; nextToDecide <= ahead; nextToDecide++)
                script.Add(HaulSimCommand.Decide(nextToDecide, desirePerPawn));

            // Each deposit is followed by the next hauler's decision, which keeps exactly `ahead` plans in
            // flight for the rest of the run rather than only at the start.
            for (int pawn = 1; pawn <= pawnCount; pawn++)
            {
                script.Add(HaulSimCommand.Deposit(pawn));
                if (nextToDecide <= pawnCount)
                    script.Add(HaulSimCommand.Decide(nextToDecide++, desirePerPawn));
            }
            return script;
        }

        /// <summary>One hauler's live position: what the accounting thinks it owes the destination, and what
        /// it is physically carrying. The two are equal until a claim is released early or a load is lost,
        /// and every interesting failure lives in the gap between them.</summary>
        private sealed class HaulerState
        {
            /// <summary>Units this hauler has committed and not yet delivered, as far as the accounting
            /// knows. What other haulers subtract from the destination's free space.</summary>
            public int Commitment;

            /// <summary>Units this hauler is physically carrying toward the destination. What will actually
            /// try to land, whatever the accounting believes.</summary>
            public int Cargo;
        }

        /// <summary>The mutable world of one run: the destination, the haulers, the clock and the trace.
        /// Private because a half-played run is not a meaningful thing to hand out.</summary>
        private sealed class Simulation
        {
            private readonly int capacity;
            private readonly HaulCommitDecision decision;
            private readonly EnrouteVisibility visibility;
            private readonly List<HaulSimStep> steps = new List<HaulSimStep>();
            private readonly Dictionary<int, HaulerState> haulers = new Dictionary<int, HaulerState>();

            // Commitments as a deciding hauler is allowed to see them. Under Immediate this is rebuilt
            // before every decision; under TickSnapshot only when the clock has moved, which is what makes
            // haulers planning in the same tick invisible to one another.
            private readonly Dictionary<int, int> visible = new Dictionary<int, int>();
            private int visibleAtTick = int.MinValue;

            private int tick;
            private int landed;

            /// <summary>Start a run with an empty destination and no haulers.</summary>
            /// <param name="freeCapacity">Units the destination can take; negative reads as full.</param>
            /// <param name="decision">The rule under test.</param>
            /// <param name="visibility">How commitments reach a deciding hauler.</param>
            public Simulation(int freeCapacity, HaulCommitDecision decision, EnrouteVisibility visibility)
            {
                capacity = Math.Max(0, freeCapacity);
                this.decision = decision;
                this.visibility = visibility;
            }

            /// <summary>Play one event, recording exactly one step for it.</summary>
            /// <param name="command">The event to play.</param>
            public void Apply(HaulSimCommand command)
            {
                switch (command.Action)
                {
                    case HaulSimAction.Decide:
                        Decide(command.PawnId, command.Desire);
                        break;
                    case HaulSimAction.Deposit:
                        Deposit(command.PawnId);
                        break;
                    case HaulSimAction.ReleaseCommitment:
                        ReleaseCommitment(command.PawnId);
                        break;
                    case HaulSimAction.DropCargo:
                        DropCargo(command.PawnId);
                        break;
                    case HaulSimAction.Tick:
                        AdvanceClock();
                        break;
                }
            }

            /// <summary>Seal the run.</summary>
            /// <returns>The immutable trace of everything that happened.</returns>
            public HaulSimTrace Finish() => new HaulSimTrace(capacity, visibility, steps.ToArray());

            /// <summary>A hauler plans against the destination and picks up whatever it commits.
            ///
            /// <para>A re-plan replaces the hauler's whole position rather than adding to it, mirroring how
            /// the mod's own claim bookkeeping swaps a pawn's old claim for its new one. Pickup is immediate
            /// because the interval this simulation exists to model is the one before the DEPOSIT.</para></summary>
            /// <param name="pawnId">The planning hauler.</param>
            /// <param name="desire">What it would take against an unlimited destination.</param>
            private void Decide(int pawnId, int desire)
            {
                var hauler = HaulerFor(pawnId);
                RefreshVisibility();

                var sight = new HaulSight(pawnId, tick, FreeNow, EnrouteExcluding(pawnId), OwnEnroute(pawnId), desire);
                int decided = decision(sight);

                // Clamped at zero and nowhere else: negative cargo is not physically expressible, but an
                // over-commitment is exactly what this run may need to report.
                int committed = Math.Max(0, decided);
                hauler.Commitment = committed;
                hauler.Cargo = committed;

                Record(HaulSimAction.Decide, pawnId, sight, decided, committed, 0, 0, 0);
            }

            /// <summary>A hauler arrives and puts down as much as still fits; the rest rides back.</summary>
            /// <param name="pawnId">The arriving hauler. One holding nothing is still recorded, so the trace
            /// shows which haulers stood down instead of leaving them out of the story.</param>
            private void Deposit(int pawnId)
            {
                var hauler = HaulerFor(pawnId);
                int cargoBefore = hauler.Cargo;
                int deposited = Math.Min(cargoBefore, FreeNow);

                landed += deposited;
                hauler.Cargo = 0;
                hauler.Commitment = 0;

                Record(HaulSimAction.Deposit, pawnId, default, 0, 0, cargoBefore, deposited, cargoBefore - deposited);
            }

            /// <summary>Forget a hauler's claim while its cargo stays on the way — the early release.</summary>
            /// <param name="pawnId">The hauler whose claim is dropped.</param>
            private void ReleaseCommitment(int pawnId)
            {
                HaulerFor(pawnId).Commitment = 0;
                Record(HaulSimAction.ReleaseCommitment, pawnId, default, 0, 0, 0, 0, 0);
            }

            /// <summary>Lose a hauler's cargo while its claim stays live — the phantom claim.</summary>
            /// <param name="pawnId">The hauler whose load never arrives.</param>
            private void DropCargo(int pawnId)
            {
                var hauler = HaulerFor(pawnId);
                int cargoBefore = hauler.Cargo;
                hauler.Cargo = 0;

                // Not counted as carried back: the load left the colony's hands somewhere else entirely, and
                // conflating it with a wasted round trip would overstate the symptom being measured.
                Record(HaulSimAction.DropCargo, pawnId, default, 0, 0, cargoBefore, 0, 0);
            }

            /// <summary>Move the clock on, which is the only thing that can refresh a per-tick view.</summary>
            private void AdvanceClock()
            {
                tick++;
                Record(HaulSimAction.Tick, 0, default, 0, 0, 0, 0, 0);
            }

            /// <summary>Units the destination can still physically take. Never negative, even if a rule
            /// over-committed and more landed than the destination was ever measured to hold.</summary>
            private int FreeNow => Math.Max(0, capacity - landed);

            /// <summary>Get or create a hauler's position. Creating on first mention means a scenario can
            /// deposit or release for a hauler that never decided, which is a state worth being able to
            /// script rather than one worth rejecting.</summary>
            /// <param name="pawnId">The hauler.</param>
            /// <returns>Its live position.</returns>
            private HaulerState HaulerFor(int pawnId)
            {
                if (!haulers.TryGetValue(pawnId, out var hauler))
                    haulers[pawnId] = hauler = new HaulerState();
                return hauler;
            }

            /// <summary>Bring the visible-commitment view up to date, subject to the visibility model.
            /// Under a per-tick snapshot this is a no-op until the clock moves, which is exactly the
            /// staleness being modelled.</summary>
            private void RefreshVisibility()
            {
                if (visibility == EnrouteVisibility.TickSnapshot && visibleAtTick == tick)
                    return;

                visible.Clear();
                foreach (var entry in haulers)
                    if (entry.Value.Commitment != 0)
                        visible[entry.Key] = entry.Value.Commitment;
                visibleAtTick = tick;
            }

            /// <summary>Units committed by haulers other than this one, as this one can see them. Summed
            /// over an unordered map, which is safe because integer addition does not care about order —
            /// the trace stays identical whatever the map's internal layout.</summary>
            /// <param name="pawnId">The asking hauler, excluded from its own answer.</param>
            /// <returns>Other haulers' visible commitments.</returns>
            private int EnrouteExcluding(int pawnId)
            {
                int units = 0;
                foreach (var entry in visible)
                    if (entry.Key != pawnId)
                        units += entry.Value;
                return units;
            }

            /// <summary>This hauler's own visible commitment, held out of the figure above.</summary>
            /// <param name="pawnId">The asking hauler.</param>
            /// <returns>Its own visible commitment, or zero.</returns>
            private int OwnEnroute(int pawnId) => visible.TryGetValue(pawnId, out int units) ? units : 0;

            /// <summary>Append one step, filling the world-state columns from the simulation's own
            /// bookkeeping so a step can never describe a destination that did not exist.</summary>
            /// <param name="action">What happened.</param>
            /// <param name="pawnId">Who it happened to.</param>
            /// <param name="sight">The rule's inputs, for a decision.</param>
            /// <param name="decided">The rule's raw answer, for a decision.</param>
            /// <param name="committed">The booked commitment, for a decision.</param>
            /// <param name="cargoBefore">Units held entering the event.</param>
            /// <param name="deposited">Units landed during the event.</param>
            /// <param name="carriedBack">Units that rode back during the event.</param>
            private void Record(
                HaulSimAction action, int pawnId, HaulSight sight,
                int decided, int committed, int cargoBefore, int deposited, int carriedBack)
            {
                int liveCommitments = 0;
                int liveCargo = 0;
                foreach (var entry in haulers)
                {
                    liveCommitments += entry.Value.Commitment;
                    liveCargo += entry.Value.Cargo;
                }

                steps.Add(new HaulSimStep(
                    tick, action, pawnId, sight,
                    decided, committed, cargoBefore, deposited, carriedBack,
                    landed, liveCommitments, liveCargo));
            }
        }
    }
}
