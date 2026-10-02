using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
    internal enum ExplicitHaulState { Queued, Active, Blocked, Cancelled, Complete, Captured }
    internal enum ExplicitHaulDestination { BareCell, Shelf }

    // Reference plus exact quantity and observed resident count: a cleanup merge does not grant
    // ownership of the rest of a stack. Changed custody blocks instead of finding replacement stock.
    public sealed class ExplicitHaulRemainder : IExposable
    {
        internal Thing thing;
        internal int units, observedCount;
        internal IntVec3 cell;
        public void ExposeData()
        {
            ExplicitHaulOrder.LookThing(ref thing, "thing");
            Scribe_Values.Look(ref units, "units");
            Scribe_Values.Look(ref observedCount, "observedCount");
            Scribe_Values.Look(ref cell, "cell");
        }
    }

    public sealed class ExplicitHaulOrder : IExposable
    {
        internal long id;
        internal int mapId, sourceId, requested, delivered, jobId = -1, tripUnits, pathStarted;
        internal Thing source, piece;
        internal bool sourceForbiddenAtIssue;
        internal IntVec3 destination;
        internal ExplicitHaulDestination destinationKind;
        internal Thing shelf;
        internal int shelfId = -1, shelfRotation;
        internal IntVec3 shelfPosition, tripDestination;
        internal StorageExclusiveCellAllocation shelfAllocation; // runtime only; native claims are reacquired after load
        internal bool IsShelf => destinationKind == ExplicitHaulDestination.Shelf;
        internal IntVec3 DeliveryCell => IsShelf ? tripDestination : destination;
        internal ExplicitHaulState state;
        internal string reason;
        internal List<ExplicitHaulRemainder> remainders = new List<ExplicitHaulRemainder>();
        internal bool Terminal => state == ExplicitHaulState.Cancelled || state == ExplicitHaulState.Complete;
        internal int Remaining => requested - delivered;
        internal void Block(string why) { if (!Terminal) { state = ExplicitHaulState.Blocked; reason = why; } }

        internal static void LookThing(ref Thing thing, string label)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Thing saved = thing?.Destroyed == true ? null : thing;
                Scribe_References.Look(ref saved, label);
            }
            else Scribe_References.Look(ref thing, label);
        }

        public void ExposeData()
        {
            // Immutable numeric source history survives a completely delivered/destroyed source.
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref mapId, "map", -1);
            Scribe_Values.Look(ref sourceId, "sourceId", -1);
            Scribe_Values.Look(ref sourceForbiddenAtIssue, "sourceForbiddenAtIssue");
            LookThing(ref source, "source");
            LookThing(ref piece, "piece");
            Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref destinationKind, "destinationKind");
            LookThing(ref shelf, "shelf");
            Scribe_Values.Look(ref shelfId, "shelfId", -1);
            Scribe_Values.Look(ref shelfPosition, "shelfPosition");
            Scribe_Values.Look(ref shelfRotation, "shelfRotation");
            Scribe_Values.Look(ref tripDestination, "tripDestination");
            Scribe_Values.Look(ref requested, "requested");
            Scribe_Values.Look(ref delivered, "delivered");
            Scribe_Values.Look(ref jobId, "jobId", -1);
            Scribe_Values.Look(ref tripUnits, "tripUnits");
            Scribe_Values.Look(ref pathStarted, "pathStarted");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref reason, "reason");
            Scribe_Collections.Look(ref remainders, "remainders", LookMode.Deep);
            remainders ??= new List<ExplicitHaulRemainder>();
        }
    }
}
