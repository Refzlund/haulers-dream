using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HaulersDream.RuntimeHarness
{
    // Values only. Object tokens are observer-assigned, never Job.loadID edits.
    [DataContract] internal sealed class B1Job
    {
        [DataMember] public int token, loadId, startTick, expiry, targetA;
        [DataMember] public string def, driver, workgiver, workgiverClass;
        [DataMember] public bool forced;
        [DataMember] public List<int> queueIds = new List<int>(), counts = new List<int>();
    }
    [DataContract] internal sealed class B1Counter
    {
        [DataMember] public string contract;
        [DataMember] public int subjectId, tick;
        [DataMember] public bool anchorPresent, backoffPresent, warned, failPresent, backedOff;
        [DataMember] public int? anchorTick, count, stackCount, until, failTick, failCount;
    }
    [DataContract] internal sealed class B1Cache
    {
        [DataMember] public int tick, generation, pawnId, sourceId;
        [DataMember] public long key;
        [DataMember] public bool dictionaryPresent, entryPresent;
        [DataMember] public int? pinnedLoadId, jobState;
        [DataMember] public B1Job actualJob;
    }
    [DataContract] internal sealed class B1Thing
    {
        [DataMember] public int id, count, limit;
        [DataMember] public string cell, holder;
        [DataMember] public int? ownerPawn;
        [DataMember] public bool spawned, destroyed, onMap, mapHolder, inventory, hands;
    }
    [DataContract] internal sealed class B1Actor
    {
        [DataMember] public int id, inventory, hands, handSpace;
        [DataMember] public float carryMass, gearInventoryMass;
        [DataMember] public string cell, faction, driver;
        [DataMember] public bool spawned, healthy, drafted, haulingCapable, haulingActive;
        [DataMember] public B1Job current;
        [DataMember] public List<B1Job> queued = new List<B1Job>();
    }
    [DataContract] internal sealed class B1Reservation
    {
        [DataMember] public int pawnId, jobToken, jobId, thingId, count;
        [DataMember] public string cell, layer;
    }
    [DataContract] internal sealed class B1Claim
    {
        [DataMember] public int pawnId, units;
        [DataMember] public string def, group;
    }
    [DataContract] internal sealed class B1Physical
    {
        [DataMember] public int tick, mapId, source, high, elsewhere, inventory, hands, total;
        [DataMember] public List<B1Thing> things = new List<B1Thing>();
        [DataMember] public List<B1Actor> actors = new List<B1Actor>();
        [DataMember] public List<B1Reservation> reservations = new List<B1Reservation>();
        [DataMember] public List<B1Claim> claims = new List<B1Claim>();
    }
    [DataContract] internal sealed class B1Event
    {
        [DataMember] public int sequence, tick, actorId, sourceId, callId, parentCallId;
        [DataMember] public string kind, scene, caller, method, detail;
        [DataMember] public string warningAttribution;
        [DataMember] public int? queryOrdinal;
        [DataMember] public bool? returned, forced, forceSweep, actualCurrent;
        [DataMember] public B1Job job, inputJob;
        [DataMember] public B1Counter counter;
        [DataMember] public B1Cache cache;
        [DataMember] public B1Physical physical, beforePhysical;
        [DataMember] public B1ZoneRetirement zoneRetirement;
        [DataMember] public B1PoolOperation poolOperation;
    }
    [DataContract] internal sealed class B1PoolOperation
    {
        [DataMember] public int cycle, beforeCount, afterCount, targetToken, priorLoadId;
        [DataMember] public string action;
        [DataMember] public bool targetReused;
    }
    [DataContract] internal sealed class B1ZoneRetirement
    {
        [DataMember] public string zoneId, cell, beforeGridZone, afterGridZone;
        [DataMember] public List<string> beforeCells = new List<string>(), afterCells = new List<string>();
        [DataMember] public bool beforeRegistered, beforeSlotMatches, afterRegistered, afterSlotAbsent, afterGroupRemoved;
    }
    [DataContract] internal sealed class B1Assertion
    {
        [DataMember] public string id, kind, detail;
        [DataMember] public bool passed;
    }
    [DataContract] internal sealed class B1Layout
    {
        [DataMember] public string scene, rectangle, highCell;
        [DataMember] public List<string> sourceCells = new List<string>();
        [DataMember] public List<int> sources = new List<int>(), actors = new List<int>();
        [DataMember] public B1Physical initial;
    }
    [DataContract] internal sealed class L04B1Result
    {
        [DataMember] public string caseId = "L04-B1", expectedBehavior, status = "running", counterContract, error;
        [DataMember] public int startedTick, finishedTick;
        [DataMember] public bool fixtureValid, requestedBehaviorSatisfied, implementedBehaviorSatisfied, baselineGapObserved,
            expectationMatched, queryControlsComplete, healthyDeliveryComplete;
        [DataMember] public List<string> incompleteControls = new List<string>();
        [DataMember] public List<string> bindings = new List<string>();
        [DataMember] public List<B1Layout> layouts = new List<B1Layout>();
        [DataMember] public List<B1Event> events = new List<B1Event>();
        [DataMember] public List<B1Assertion> assertions = new List<B1Assertion>();
        [DataMember] public B1HealthyResult healthyDelivery;
    }
}
