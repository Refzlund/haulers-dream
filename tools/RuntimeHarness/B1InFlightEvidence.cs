using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HaulersDream.RuntimeHarness
{
    [DataContract] internal sealed class B1InFlightReservation
    {
        [DataMember] public int pawnId, jobToken, jobId, thingId, count;
        [DataMember] public int? maxPawns;
        [DataMember] public string kind, cell, layer;
    }
    [DataContract] internal sealed class B1InFlightProgress
    {
        [DataMember] public int fromObservationSequence, toObservationSequence, fromTick, toTick;
        [DataMember] public bool cellChanged, pathCostDecreased;
    }
    [DataContract] internal sealed class B1InFlightPath
    {
        [DataMember] public bool moving;
        [DataMember] public int pathToken, destinationThing;
        [DataMember] public string nextCell, destinationCell;
        [DataMember] public float costLeft, costTotal;
    }
    [DataContract] internal sealed class B1InFlightPhysical
    {
        [DataMember] public int tick, mapId, actorId, driverToken, currentTargetBThing;
        [DataMember] public string currentTargetBCell;
        [DataMember] public bool spawned, healthy, drafted, driverOwnsCurrent;
        [DataMember] public B1Job current;
        [DataMember] public List<B1Job> queued = new List<B1Job>();
        [DataMember] public B1HealthyThing anchor;
        [DataMember] public B1HealthyState cargo;
        [DataMember] public B1InFlightPath path;
        [DataMember] public List<B1InFlightReservation> reservations = new List<B1InFlightReservation>();
    }
    [DataContract] internal sealed class B1InFlightObservation
    {
        [DataMember] public int sequence, tick;
        [DataMember] public string purpose;
        [DataMember] public B1InFlightPhysical physical;
        [DataMember] public B1Counter counter;
        [DataMember] public B1Cache cache;
    }
    [DataContract] internal sealed class B1InFlightQuery
    {
        [DataMember] public int ordinal, tick, beforeSequence, afterSequence, actualBuilds, successfulBuilds, actualNotes;
        [DataMember] public string kind;
        [DataMember] public bool returned, candidateIsCurrent, stateUnchanged, completeCall, cacheCoherent, selfReservationGateObserved;
        [DataMember] public B1Job candidate;
        [DataMember] public B1InFlightObservation before, after;
    }
    [DataContract] internal sealed class B1InFlightReservationGate
    {
        [DataMember] public int actorId, sourceId, parentCallId, maxPawns, stackCount;
        [DataMember] public int? queryOrdinal;
        [DataMember] public string caller, layer;
        [DataMember] public bool ignoreOtherReservations, returned;
    }
    [DataContract] internal sealed class B1InFlightEvent
    {
        [DataMember] public int sequence, tick;
        [DataMember] public string kind, detail;
        [DataMember] public B1Event call;
        [DataMember] public B1InFlightObservation observation;
        [DataMember] public B1InFlightQuery query;
        [DataMember] public B1InFlightReservationGate reservationGate;
        [DataMember] public B1InFlightProgress progress;
        [DataMember] public B1Assertion assertion;
    }
    [DataContract] internal sealed class B1InFlightResult
    {
        [DataMember] public string caseId = "L04-B1-D2", expectedBehavior, status = "running", counterContract, error;
        [DataMember] public string deliveryRole = "In-flight-query execution witness; not the unperturbed D1 control.";
        [DataMember] public string unexercisedReason, burstStopReason;
        [DataMember] public int startedTick, finishedTick, firstBulkToken, firstBulkJobId = -1, firstBulkDriverToken;
        [DataMember] public int anchorId = -1, approachEntrySequence, firstCellAdvanceSequence, burstEntrySequence;
        [DataMember] public int maximumProbes = 8, initialCount, initialEffectiveCount, thresholdBuildsNeeded, successfulCandidateQueries, actualBuilds, successfulBuilds, actualNotes;
        [DataMember] public string initialRowResetReason;
        [DataMember] public bool fixtureValid, nativeDispatchProven, approachWindowExercised, realApproachProgress;
        [DataMember] public bool withinBurstProgress;
        [DataMember] public int burstProgressFromSequence, burstProgressToSequence;
        [DataMember] public bool probeStateUnchanged, counterPathExercised, postThresholdRejected, deliverySatisfied;
        [DataMember] public bool counterTransitionsValid;
        [DataMember] public bool warningsAttributed;
        [DataMember] public bool requestedBehaviorSatisfied, expectationMatched, baselineGapObserved, timedOut;
        [DataMember] public B1Counter initialCounter;
        [DataMember] public B1Job initialBulkJob;
        [DataMember] public List<string> bindings = new List<string>();
        [DataMember] public List<B1InFlightEvent> events = new List<B1InFlightEvent>();
        [DataMember] public List<B1InFlightQuery> queries = new List<B1InFlightQuery>();
        [DataMember] public List<B1Assertion> assertions = new List<B1Assertion>();
        [DataMember] public B1HealthyResult deliveryWitness;
    }
}
