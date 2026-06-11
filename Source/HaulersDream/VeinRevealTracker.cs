using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Tracks one queued "Touching / vein" mining route whose vein ran into fog, so that as the pawn mines and
    /// uncovers more of it, the newly-revealed cells can be APPENDED to the route — but only while the route's
    /// last task is still the pawn's last queued task (i.e. the player hasn't queued other work after it). Held by
    /// <see cref="HaulersDreamGameComponent"/>; persisted with the save so a reload-mid-mine keeps extending.
    /// </summary>
    public class VeinRevealTracker : IExposable
    {
        public Pawn pawn;
        public ThingDef veinDef;
        public IntVec3 seed;                 // the clicked cell the vein floods out from
        public int cap;                      // max stops (the chosen Amount); never grow the route past this
        public IntVec3 lastCell;             // the route's current tail cell (must stay the pawn's last task)
        public HashSet<IntVec3> included = new HashSet<IntVec3>(); // cells already designated/queued by this tool

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref veinDef, "veinDef");
            Scribe_Values.Look(ref seed, "seed");
            Scribe_Values.Look(ref cap, "cap");
            Scribe_Values.Look(ref lastCell, "lastCell");
            Scribe_Collections.Look(ref included, "included", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && included == null)
                included = new HashSet<IntVec3>();
        }
    }
}
