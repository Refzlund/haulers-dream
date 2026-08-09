using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The VISIBILITY half of the #235 fix. When <see cref="WorkGiverBlocklist"/> switches a work giver off
    /// because something repeatedly threw from the work scan's unguarded tail, one kind of work simply stops
    /// happening for the rest of the session. That is exactly the invisible degradation which produces the NEXT
    /// false bug report against Hauler's Dream, so the player is told: what was switched off, where the error
    /// pointed, and that it comes back on restart.
    ///
    /// <para><b>Each line says only what its own evidence supports.</b> A line names a mod when the error's own
    /// stack put that mod's code on the path to the throw, and says the source could not be identified when it
    /// did not — see <see cref="WorkGiverNamingPolicy"/> for why hook ownership is not allowed to fill that gap.
    /// The two cases are separate translation keys rather than one key with an optional half, so a translator
    /// cannot accidentally word the unknown case as an accusation.</para>
    ///
    /// <para>Priority <see cref="AlertPriority.High"/>, not Critical: nothing is dying and no cargo is stranded —
    /// the colony is running, one work type is not. Critical is the tier the black-hole alert uses; it adds the
    /// red pulsing background and a REPEATING bell, which would be crying wolf here. (High still rings
    /// <c>TinyBell</c> once on activation — vanilla does that for every priority above Medium — and, like every
    /// plain <see cref="Alert"/>, draws on the default transparent background.)</para>
    ///
    /// <para>No culprits: the fault belongs to a work-giver TYPE, not to any pawn or thing, so there is nothing
    /// on the map to point arrows at or cycle the camera through. Auto-discovered by RimWorld as an
    /// <see cref="Alert"/> leaf subclass — no XML.</para>
    /// </summary>
    public class Alert_WorkGiverQuarantined : Alert
    {
        public Alert_WorkGiverQuarantined()
        {
            defaultPriority = AlertPriority.High;
        }

        /// <summary>Active for as long as anything is quarantined. A single volatile read — safe to call from the
        /// per-frame alert readout.</summary>
        public override AlertReport GetReport()
            => WorkGiverBlocklist.AnyQuarantined ? AlertReport.Active : AlertReport.Inactive;

        /// <summary>The collapsed label: how many work types are off, and nothing about whose fault it is. The
        /// count alone, so the per-frame label path never formats a line.</summary>
        public override string GetLabel()
            => "HaulersDream.Alert.WorkGiverQuarantined".Translate(WorkGiverBlocklist.QuarantinedCount);

        /// <summary>The expanded body: the consequence, the escape hatch, and one line per switched-off work
        /// type. Built only while the alert is expanded on screen.</summary>
        public override TaggedString GetExplanation()
            => "HaulersDream.Alert.WorkGiverQuarantinedDesc".Translate(Lines().ToLineList("  - "));

        // A materialised copy: ConcurrentDictionary.Values is already a snapshot, but the explanation needs a
        // line list, and it is only called while the alert is on screen.
        private static List<string> Lines()
        {
            var lines = new List<string>();
            foreach (var work in WorkGiverBlocklist.Quarantined)
                lines.Add(Line(work));
            return lines;
        }

        /// <summary>
        /// One line of the list, worded by what the error itself established.
        /// </summary>
        /// <param name="work">The switched-off work giver and the attribution captured at its fault.</param>
        /// <returns>The translated line.</returns>
        private static string Line(QuarantinedWork work)
        {
            // Both halves are checked, not just the verdict. They are set together at fault time, but a line
            // that promises a source and then renders an empty one is the false-blame shape this alert exists
            // to stop, so the unknown wording is what an inconsistent pair falls back to.
            if (work.Naming == QuarantineNaming.NameTheMod && !work.Source.NullOrEmpty())
                return "HaulersDream.Alert.QuarantineLine.KnownSource".Translate(work.Work, work.Source);
            return "HaulersDream.Alert.QuarantineLine.UnknownSource".Translate(work.Work);
        }
    }
}
