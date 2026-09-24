using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HaulersDream.RuntimeHarness
{
    [DataContract] internal sealed class Cap03WrapperResult
    {
        [DataMember] public string caseId = "CAP03-C", contract = "native-wrapper-boundary-v1", expectedBehavior, error;
        [DataMember] public string status = "inconclusive";
        [DataMember] public string scope = "Incoming/resident native minified wrapper containment and independent inner Stack/HP counter controls only; not minified support or hauling.";
        [DataMember] public bool fixtureValid, requestedBehaviorSatisfied, expectationMatched, allCap03ComponentsSatisfied = false;
        [DataMember] public string[] pendingControls = { "reviewed-inner-identity-custody-predicate-support", "actual-minified-hauling-and-save-lifecycle", "large-groups-and-lifecycle", "allocation-and-original-report-convergence" };
        [DataMember] public int startedTick, finishedTick, mainThread, mapId, thingDefs, categoryDefs, specialDefs, gameMaps;
        [DataMember] public string session, nativeIdentity, asfIdentity;
        [DataMember] public Cap03WrapperDefinition definition;
        [DataMember] public ProjectionFixtureStatus catalogStatus;
        [DataMember] public List<ProjectionFixtureAssembly> assemblies;
        [DataMember] public List<string> bindings, patchInventory;
        [DataMember] public List<Cap03BudgetFile> inputs = new List<Cap03BudgetFile>();
        [DataMember] public List<Cap03WrapperScene> scenes = new List<Cap03WrapperScene>();
        [DataMember] public List<ProjectionFixtureAssertion> assertions = new List<ProjectionFixtureAssertion>();
        [DataMember] public List<Cap03BudgetRecord> records = new List<Cap03BudgetRecord>();
    }
    [DataContract] internal sealed class Cap03WrapperDefinition
    {
        [DataMember] public string def, runtimeType, package, category, minifiedDef, outerType;
        [DataMember] public bool minifiable, everStorableWhenMinified, everStorableAsBuilding, useHitPoints, withinBuildings, withinNeat;
        [DataMember] public int width, depth, outerStackLimit;
        [DataMember] public List<string> categories;
    }
    [DataContract] internal sealed class Cap03WrapperScene
    {
        [DataMember] public string id, error, parentKey, positiveParentKey, cell, positiveCell;
        [DataMember] public bool resident, asf, completed, restored;
        [DataMember] public int startedTick, finishedTick, zonesBefore, zonesAfter, groupsBefore, groupsAfter;
        [DataMember] public Cap03WrapperSnapshot beforeProtected, afterProtected, afterNative;
        [DataMember] public List<Cap03WrapperCounter> protectedBegin, protectedEnd, nativeEnd, retirementEnd;
        [DataMember] public List<Cap03WrapperMinify> minifications = new List<Cap03WrapperMinify>();
        [DataMember] public List<Cap03WrapperPreparation> preparations = new List<Cap03WrapperPreparation>();
        [DataMember] public List<Cap03WrapperTrial> trials = new List<Cap03WrapperTrial>();
        [DataMember] public List<Cap03WrapperNative> nativeControls = new List<Cap03WrapperNative>();
        [DataMember] public List<int> retiredThingIds = new List<int>();
        [DataMember] public List<Cap03WrapperRetirement> retiredDestinations = new List<Cap03WrapperRetirement>();
    }
    [DataContract] internal sealed class Cap03WrapperRetirement
    { [DataMember] public string parentKey; [DataMember] public List<string> cells; [DataMember] public bool slotsCleared, zoneRegistered; [DataMember] public int? asfCount; }
    [DataContract] internal sealed class Cap03WrapperCounter
    { [DataMember] public int innerId; [DataMember] public string identity; [DataMember] public long stackCalls, hitPointReads; }
    [DataContract] internal sealed class Cap03WrapperState
    {
        [DataMember] public ProjectionFixtureThing outer, inner;
        [DataMember] public string outerIdentity, innerIdentity, ownerIdentity, innerHoldingOwnerIdentity, innerParentIdentity, innerParentType;
        [DataMember] public string outerType, innerType, innerDefPackage;
        [DataMember] public int directCount, setupMaximumHitPoints;
        [DataMember] public int? innerMapHeldId;
        [DataMember] public bool sameInner, directContainsInner, innerOwnerMatches, innerParentIsOuter, innerSpawnedOrParentSpawned;
        [DataMember] public List<int> directIds;
        [DataMember] public List<string> outerComps, innerComps;
    }
    [DataContract] internal sealed class Cap03WrapperMinify
    { [DataMember] public ProjectionFixtureThing before; [DataMember] public Cap03WrapperState after; [DataMember] public List<Cap03WrapperCounter> countersBefore, countersAfter; }
    [DataContract] internal sealed class Cap03WrapperDestination
    {
        [DataMember] public string key, identity, runtimeType, slotIdentity, groupIdentity, registryIdentity, defPackage;
        [DataMember] public ProjectionFixtureThing thing;
        [DataMember] public bool registered, zoneRegistered;
        [DataMember] public List<string> cells, compTypes;
        [DataMember] public Cap03FilterSettings effective, declaredFixed, interfaceFixed;
        [DataMember] public List<Cap03WrapperGrid> grids;
        [DataMember] public Cap03BudgetRegistry registry;
    }
    [DataContract] internal sealed class Cap03WrapperGrid
    { [DataMember] public string cell; [DataMember] public int nativeMaximum; [DataMember] public List<ProjectionFixtureThing> things; }
    [DataContract] internal sealed class Cap03WrapperSnapshot
    {
        [DataMember] public int tick, mapId;
        [DataMember] public ProjectionFixtureThing actor, steel;
        [DataMember] public bool actorIdle;
        [DataMember] public List<Cap03WrapperState> wrappers;
        [DataMember] public List<Cap03WrapperDestination> destinations;
    }
    [DataContract] internal sealed class Cap03WrapperPreparation
    {
        [DataMember] public string parentKey, stage;
        [DataMember] public long allowance, sourceRequired, declaredCost, charged;
        [DataMember] public bool ready, warmup;
        [DataMember] public int indexed;
        [DataMember] public ProjectionFixtureStatus status;
        [DataMember] public List<Cap03WrapperCounter> before, after;
    }
    [DataContract] internal sealed class Cap03WrapperTrial
    {
        [DataMember] public string id, kind, query, parcelId, scopeIdentity, parentKey, cellAddress, error;
        [DataMember] public int subjectId;
        [DataMember] public bool completed, disposed;
        [DataMember] public ProjectionFixtureStatus openStatus;
        [DataMember] public ProjectionFixtureCell cell;
        [DataMember] public ProjectionFixtureEligibility eligibility;
        [DataMember] public List<ProjectionFixtureWork> allowance, openWork, afterOpen, afterCell, afterEligibility, afterDispose;
        [DataMember] public long expectedFilterCharge;
        [DataMember] public List<Cap03WrapperCounter> before, after;
    }
    [DataContract] internal sealed class Cap03WrapperNative
    {
        [DataMember] public string route, filterIdentity, cell;
        [DataMember] public int receiverId, argumentId;
        [DataMember] public bool actual, expected;
        [DataMember] public List<Cap03WrapperCounter> before, after;
    }
}
