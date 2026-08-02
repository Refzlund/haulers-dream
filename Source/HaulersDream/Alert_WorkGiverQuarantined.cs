using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The VISIBILITY half of the #235 fix. When <see cref="WorkGiverBlocklist"/> switches a work giver off
    /// because another mod repeatedly threw from the work scan's unguarded tail, one kind of work simply stops
    /// happening for the rest of the session. That is exactly the invisible degradation which produces the NEXT
    /// false bug report against Hauler's Dream, so the player is told: what was switched off, which mod caused
    /// it, and that it comes back on restart.
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

        public override string GetLabel()
            => "HaulersDream.Alert.WorkGiverQuarantined".Translate(Snapshot().Count);

        public override TaggedString GetExplanation()
            => "HaulersDream.Alert.WorkGiverQuarantinedDesc".Translate(Snapshot().ToLineList("  - "));

        // A materialised copy: ConcurrentDictionary.Values is already a snapshot, but the label needs a Count and
        // the explanation needs a line list, and both are called only while the alert is on screen.
        private static List<string> Snapshot()
        {
            var names = new List<string>();
            foreach (var description in WorkGiverBlocklist.Quarantined)
                names.Add(description);
            return names;
        }
    }
}
