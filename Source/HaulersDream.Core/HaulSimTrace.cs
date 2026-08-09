using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HaulersDream.Core
{
    /*
        ──────────────────────────────────────────────
                  Haul simulation observation
        ──────────────────────────────────────────────
        What a run of HaulConcurrencySim leaves behind: one record per event, plus the handful of totals a
        verdict is actually taken on.

        → KEY: two peaks, not one, and they mean different things. PeakSubscription is what the ACCOUNTING
          believed (landed + live commitments) and PeakInbound is what was PHYSICALLY on its way (landed +
          cargo in hands). They agree until a claim is released while its cargo is still coming, and the gap
          between them is the exact shape of that failure.
        → GOTCHA: a run whose totals look clean may simply never have delivered anything. Read Deposited
          alongside the peaks — a rule that stands every hauler down satisfies the invariant perfectly and
          is a worse bug than the one it replaced.
    */

    /// <summary>
    /// One event in a simulated run, with both what the hauler saw and what the destination looked like
    /// afterwards. Written once by the simulation and never modified.
    ///
    /// <para>Fields that an action does not use stay zero (a deposit has no <see cref="Sight"/>, a decision
    /// deposits nothing). The renderer prints those as dashes so a reader is never invited to interpret a
    /// zero that was never measured.</para>
    /// </summary>
    public sealed class HaulSimStep
    {
        /// <summary>Simulation clock when this happened. Only advances on an explicit tick.</summary>
        public int Tick { get; }

        /// <summary>What happened.</summary>
        public HaulSimAction Action { get; }

        /// <summary>Which hauler it happened to; zero for a clock advance.</summary>
        public int PawnId { get; }

        /// <summary>For a decision: the complete view the rule was given. Default (all zeroes) otherwise —
        /// keeping the whole sight rather than a few copied numbers means a failing run can be replayed
        /// against a candidate rule without re-deriving its inputs.</summary>
        public HaulSight Sight { get; }

        /// <summary>For a decision: the rule's return value VERBATIM, including a negative one. Recorded
        /// unrepaired so a rule that answers nonsense is visible in the trace rather than silently
        /// corrected into looking reasonable.</summary>
        public int Decided { get; }

        /// <summary>For a decision: what the simulation actually booked, which is <see cref="Decided"/>
        /// clamped at zero and clamped nowhere else. It is never limited to the destination's capacity;
        /// letting an over-commitment stand is the only way the run can report one.</summary>
        public int Committed { get; }

        /// <summary>Units this hauler was holding when the event began. Non-zero on a deposit means it made
        /// a real trip; zero on a deposit means it had stood down and never set off.</summary>
        public int CargoBefore { get; }

        /// <summary>Units that actually landed in the destination during this event.</summary>
        public int Deposited { get; }

        /// <summary>Units that rode back because they did not fit. The reported symptom, in units.</summary>
        public int CarriedBack { get; }

        /// <summary>Units in the destination after this event.</summary>
        public int Landed { get; }

        /// <summary>Sum of every hauler's live commitment after this event — what the accounting believes is
        /// still coming.</summary>
        public int LiveCommitments { get; }

        /// <summary>Sum of every hauler's cargo after this event — what is physically still coming, which
        /// can exceed <see cref="LiveCommitments"/> once a claim has been released early.</summary>
        public int LiveCargo { get; }

        /// <summary>Record one event. Called from a single place inside the simulation, which fills the
        /// trailing state fields from its own bookkeeping so a caller cannot describe a world that did not
        /// happen.</summary>
        /// <param name="tick">Clock at the event.</param>
        /// <param name="action">The event.</param>
        /// <param name="pawnId">Hauler it applied to.</param>
        /// <param name="sight">The rule's inputs, for a decision.</param>
        /// <param name="decided">The rule's raw answer, for a decision.</param>
        /// <param name="committed">The booked commitment, for a decision.</param>
        /// <param name="cargoBefore">Units held entering the event.</param>
        /// <param name="deposited">Units landed during the event.</param>
        /// <param name="carriedBack">Units that rode back during the event.</param>
        /// <param name="landed">Destination contents after the event.</param>
        /// <param name="liveCommitments">Sum of live commitments after the event.</param>
        /// <param name="liveCargo">Sum of cargo in hands after the event.</param>
        public HaulSimStep(
            int tick, HaulSimAction action, int pawnId, HaulSight sight,
            int decided, int committed, int cargoBefore, int deposited, int carriedBack,
            int landed, int liveCommitments, int liveCargo)
        {
            Tick = tick;
            Action = action;
            PawnId = pawnId;
            Sight = sight;
            Decided = decided;
            Committed = committed;
            CargoBefore = cargoBefore;
            Deposited = deposited;
            CarriedBack = carriedBack;
            Landed = landed;
            LiveCommitments = liveCommitments;
            LiveCargo = liveCargo;
        }
    }

    /// <summary>
    /// The result of one simulated run: every step, and the totals a test takes its verdict on.
    ///
    /// <para>Every total is derived from <see cref="Steps"/> in the constructor rather than accumulated by
    /// the simulation, so a trace cannot report a summary its own steps contradict.</para>
    /// </summary>
    public sealed class HaulSimTrace
    {
        private readonly HaulSimStep[] steps;

        /// <summary>Units the destination could take at the start of the run — the ceiling the invariant is
        /// measured against.</summary>
        public int FreeCapacity { get; }

        /// <summary>How in-flight commitments were made visible to deciding haulers during this run.</summary>
        public EnrouteVisibility Visibility { get; }

        /// <summary>Every event, in the order it happened.</summary>
        public IReadOnlyList<HaulSimStep> Steps => steps;

        /// <summary>Distinct haulers that took a decision during the run, whatever they decided.</summary>
        public int HaulerCount { get; }

        /// <summary>The highest the accounting ever went: the largest <c>landed + live commitments</c> seen
        /// at any point. <b>This is the invariant's subject</b> — the sum of units committed toward the
        /// destination, counting what already arrived, must never exceed <see cref="FreeCapacity"/>.</summary>
        public int PeakSubscription { get; }

        /// <summary>The highest the physical truth ever went: the largest <c>landed + cargo in hands</c>
        /// seen at any point. Exceeds <see cref="FreeCapacity"/> exactly when more goods were on their way
        /// than the destination could ever take, which is what carrying loads back is made of.</summary>
        public int PeakInbound { get; }

        /// <summary>Units that reached the destination over the whole run.</summary>
        public int TotalDeposited { get; }

        /// <summary>Units that rode back over the whole run. The wasted work, in units.</summary>
        public int TotalCarriedBack { get; }

        /// <summary>Deposits by a hauler that was actually carrying something — the trips that happened.
        /// A hauler that stood down is not counted, since it never set off.</summary>
        public int Trips { get; }

        /// <summary>Trips that put down nothing at all: the hauler walked there holding cargo and every
        /// unit of it rode back. The purest form of the reported symptom.</summary>
        public int EmptyHandedTrips { get; }

        /// <summary>Units the accounting over-promised, or zero. Non-zero means the invariant broke.</summary>
        public int OverSubscription => Math.Max(0, PeakSubscription - FreeCapacity);

        /// <summary>Units of goods that were on their way beyond what the destination could ever take, or
        /// zero. Can be non-zero while <see cref="OverSubscription"/> is zero — that combination is the
        /// signature of a claim released before its cargo landed.</summary>
        public int OverInbound => Math.Max(0, PeakInbound - FreeCapacity);

        /// <summary>True when the colony never committed more than the destination could take. Necessary,
        /// and on its own not sufficient: standing every hauler down also satisfies it.</summary>
        public bool HoldsInvariant => PeakSubscription <= FreeCapacity;

        /// <summary>Assemble a trace and derive its totals from the steps.</summary>
        /// <param name="freeCapacity">The destination's free capacity at the start of the run.</param>
        /// <param name="visibility">How commitments were made visible to deciding haulers.</param>
        /// <param name="steps">Events in order; the array is taken as-is and never handed out.</param>
        public HaulSimTrace(int freeCapacity, EnrouteVisibility visibility, HaulSimStep[] steps)
        {
            FreeCapacity = freeCapacity;
            Visibility = visibility;
            this.steps = steps ?? new HaulSimStep[0];

            var deciders = new HashSet<int>();
            int peakSubscription = 0;
            int peakInbound = 0;
            int deposited = 0;
            int carriedBack = 0;
            int trips = 0;
            int emptyHanded = 0;
            foreach (var step in this.steps)
            {
                if (step.Action == HaulSimAction.Decide)
                    deciders.Add(step.PawnId);

                peakSubscription = Math.Max(peakSubscription, step.Landed + step.LiveCommitments);
                peakInbound = Math.Max(peakInbound, step.Landed + step.LiveCargo);
                deposited += step.Deposited;
                carriedBack += step.CarriedBack;

                if (step.Action != HaulSimAction.Deposit || step.CargoBefore <= 0)
                    continue;
                trips++;
                if (step.Deposited == 0)
                    emptyHanded++;
            }

            HaulerCount = deciders.Count;
            PeakSubscription = peakSubscription;
            PeakInbound = peakInbound;
            TotalDeposited = deposited;
            TotalCarriedBack = carriedBack;
            Trips = trips;
            EmptyHandedTrips = emptyHanded;
        }

        /// <summary>
        /// The run rendered for a human reading a failed assertion at 2am six months from now: a header, one
        /// aligned row per event, and the two peaks stated against the capacity they are judged by.
        ///
        /// <para>Column widths are measured from the data rather than fixed, so a boundary value wide enough
        /// to break the alignment does not also break the reader's ability to scan the column it is in.
        /// Numbers are formatted invariantly, which keeps the text identical on any machine — a trace is
        /// also compared against itself to prove the simulation is deterministic.</para>
        /// </summary>
        /// <returns>A multi-line report. Never empty: a run with no steps still states its capacity.</returns>
        public string Describe()
        {
            var rows = new List<string[]>
            {
                new[] { "#", "tick", "action", "pawn", "free", "enroute", "want", "commit", "cargo", "dep", "back", "landed", "live", "inflight" }
            };
            for (int i = 0; i < steps.Length; i++)
                rows.Add(RowFor(i, steps[i]));

            var widths = new int[rows[0].Length];
            foreach (var row in rows)
                for (int col = 0; col < row.Length; col++)
                    widths[col] = Math.Max(widths[col], row[col].Length);

            var report = new StringBuilder();
            report.Append("destination: ").Append(Num(FreeCapacity)).Append(" free units")
                .Append("   haulers: ").Append(Num(HaulerCount))
                .Append("   visibility: ").Append(Visibility).AppendLine();
            report.AppendLine();
            foreach (var row in rows)
            {
                for (int col = 0; col < row.Length; col++)
                    report.Append("  ").Append(row[col].PadLeft(widths[col]));
                report.AppendLine();
            }
            report.AppendLine();
            report.Append("  peak subscription (landed + live commitments)  ").AppendLine(Verdict(PeakSubscription));
            report.Append("  peak inbound      (landed + cargo in hands)    ").AppendLine(Verdict(PeakInbound));
            report.Append("  deposited ").Append(Num(TotalDeposited))
                .Append("   carried back ").Append(Num(TotalCarriedBack))
                .Append("   trips ").Append(Num(Trips))
                .Append("   of which empty-handed ").Append(Num(EmptyHandedTrips)).AppendLine();
            report.AppendLine();
            report.AppendLine("  a deposit row with cargo 0 is a hauler that stood down and never set off");
            return report.ToString();
        }

        /// <summary>One event as pre-formatted cells, dashes where the action measured nothing.</summary>
        /// <param name="index">Position in the run, printed as the row's ordinal.</param>
        /// <param name="step">The event.</param>
        /// <returns>Cells in the header's column order.</returns>
        private static string[] RowFor(int index, HaulSimStep step)
        {
            bool decided = step.Action == HaulSimAction.Decide;
            bool moved = step.Action == HaulSimAction.Deposit || step.Action == HaulSimAction.DropCargo;
            return new[]
            {
                Num(index),
                Num(step.Tick),
                NameOf(step.Action),
                step.Action == HaulSimAction.Tick ? "-" : Num(step.PawnId),
                decided ? Num(step.Sight.FreeCapacity) : "-",
                decided ? Num(step.Sight.UnitsEnroute) : "-",
                decided ? Num(step.Sight.Desire) : "-",
                decided ? Num(step.Decided) : "-",
                moved ? Num(step.CargoBefore) : "-",
                step.Action == HaulSimAction.Deposit ? Num(step.Deposited) : "-",
                step.Action == HaulSimAction.Deposit ? Num(step.CarriedBack) : "-",
                Num(step.Landed),
                Num(step.LiveCommitments),
                Num(step.LiveCargo)
            };
        }

        /// <summary>A peak stated against the capacity it is judged by, naming the overshoot when there is
        /// one so the number a reader needs is never left as subtraction homework.</summary>
        /// <param name="peak">The measured peak.</param>
        /// <returns>A single line fragment, e.g. <c>15 of 3 free — OVER BY 12</c>.</returns>
        private string Verdict(int peak)
        {
            int over = Math.Max(0, peak - FreeCapacity);
            return Num(peak) + " of " + Num(FreeCapacity) + " free"
                + (over > 0 ? "   OVER BY " + Num(over) : "   ok");
        }

        /// <summary>Short lower-case label for an action, sized for a table column.</summary>
        /// <param name="action">The action.</param>
        /// <returns>Its column label.</returns>
        private static string NameOf(HaulSimAction action)
        {
            switch (action)
            {
                case HaulSimAction.Decide: return "decide";
                case HaulSimAction.Deposit: return "deposit";
                case HaulSimAction.ReleaseCommitment: return "release";
                case HaulSimAction.DropCargo: return "drop";
                case HaulSimAction.Tick: return "tick";
                default: return action.ToString();
            }
        }

        /// <summary>Culture-independent integer text, so the same run renders byte-identically anywhere.</summary>
        /// <param name="value">The number.</param>
        /// <returns>Its invariant decimal text.</returns>
        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
