using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HaulersDream.RuntimeHarness
{
    [DataContract] internal sealed class Cap03FilterResult
    {
        [DataMember] public string caseId = "CAP03-A", contract = "linked-fixed-filter-dispatch-v1", expectedBehavior;
        [DataMember] public string status = "inconclusive", error;
        [DataMember] public string scope = "Only native/ASF linked fixed-filter dispatch and custom-worker containment. No full CAP03, allocation, native-call instrumentation or hauling compatibility claim.";
        [DataMember] public bool allCap03ComponentsSatisfied = false, fixtureValid, requestedBehaviorSatisfied, expectationMatched;
        [DataMember] public string[] pendingControls = { "CAP03-C", "independent-native-call-instrumentation", "large-groups-and-lifecycle", "allocation-and-original-report-convergence" };
        [DataMember] public int startedTick, finishedTick, mapId, mainThread, thingDefs, categoryDefs, specialDefs, gameMaps;
        [DataMember] public string session, nativeIdentity, asfIdentity;
        [DataMember] public ProjectionFixtureStatus catalogStatus;
        [DataMember] public List<ProjectionFixtureAssembly> assemblies;
        [DataMember] public List<string> bindings, patchInventory;
        [DataMember] public List<Cap03BudgetFile> inputs = new List<Cap03BudgetFile>();
        [DataMember] public List<Cap03FilterScene> scenes = new List<Cap03FilterScene>();
        [DataMember] public List<ProjectionFixtureAssertion> assertions = new List<ProjectionFixtureAssertion>();
        [DataMember] public List<Cap03BudgetRecord> records = new List<Cap03BudgetRecord>();
        [DataMember] public Cap03FilterCounter initialCounter, finalCounter;
    }
    [DataContract] internal sealed class Cap03FilterScene
    {
        [DataMember] public string id, error;
        [DataMember] public bool unknownWorker, asfFirst, completed, restored;
        [DataMember] public int startedTick, finishedTick, storageGroupsBefore, storageGroupsAfter;
        [DataMember] public string riceRottableType, riceRotStage, freshWorkerType;
        [DataMember] public Cap03FilterSnapshot unlinked, linked, afterProtected, afterNativeControl;
        [DataMember] public Cap03FilterCounter beforeProtected, afterProjection, afterControl;
        [DataMember] public List<Cap03FilterPreparation> preparations = new List<Cap03FilterPreparation>();
        [DataMember] public List<Cap03FilterTrial> trials = new List<Cap03FilterTrial>();
        [DataMember] public List<Cap03FilterNative> nativeControls = new List<Cap03FilterNative>();
        [DataMember] public List<int> retiredThingIds = new List<int>();
    }
    [DataContract] internal sealed class Cap03FilterSettings
    {
        [DataMember] public string settingsIdentity, filterIdentity, ownerIdentity, ownerType, priority;
        [DataMember] public ProjectionFixtureFilter contents;
    }
    [DataContract] internal sealed class Cap03FilterMember
    {
        [DataMember] public ProjectionFixtureThing thing;
        [DataMember] public string runtimeType, defPackage, groupTag, slotIdentity, groupIdentity;
        [DataMember] public int ordinal, definitionMaximum, nativeMaximum;
        [DataMember] public bool registered, contentsPacked;
        [DataMember] public int? registryCount;
        [DataMember] public List<string> cells, compTypes;
        [DataMember] public List<Cap03FilterGrid> grid;
        [DataMember] public Cap03FilterSettings local, effective, declaredFixed, interfaceFixed;
    }
    [DataContract] internal sealed class Cap03FilterGrid
    { [DataMember] public string cell; [DataMember] public List<ProjectionFixtureThing> things; }
    [DataContract] internal sealed class Cap03FilterSnapshot
    {
        [DataMember] public int tick, mapId, groupLoadId;
        [DataMember] public string groupIdentity;
        [DataMember] public List<int> memberOrder;
        [DataMember] public Cap03FilterSettings groupEffective, groupFixed;
        [DataMember] public List<Cap03FilterMember> members;
        [DataMember] public List<ProjectionFixtureThing> parcels;
        [DataMember] public ProjectionFixtureThing actor;
        [DataMember] public bool actorIdle, groupRegistered;
    }
    [DataContract] internal sealed class Cap03FilterCounter
    {
        [DataMember] public long constructed, matches, alwaysMatches, canEverMatch;
        [DataMember] public bool workerExists;
        [DataMember] public string actualWorkerType, workerIdentity;
    }
    [DataContract] internal sealed class Cap03FilterPreparation
    {
        [DataMember] public string stage;
        [DataMember] public int parentId;
        [DataMember] public long allowance, sourceRequired, declaredCost, charged;
        [DataMember] public bool ready, warmup;
        [DataMember] public int indexed;
        [DataMember] public ProjectionFixtureStatus status;
        [DataMember] public Cap03FilterCounter before, after;
    }
    [DataContract] internal sealed class Cap03FilterTrial
    {
        [DataMember] public string id, stage, query, parcelId, scopeIdentity, error;
        [DataMember] public int parentId, subjectId;
        [DataMember] public bool completed, disposed;
        [DataMember] public ProjectionFixtureStatus openStatus;
        [DataMember] public ProjectionFixtureCell cell;
        [DataMember] public ProjectionFixtureEligibility eligibility;
        [DataMember] public List<ProjectionFixtureWork> allowance, openWork, afterOpen, afterCell, afterEligibility, afterDispose;
        [DataMember] public long expectedFilterCharge;
        [DataMember] public Cap03FilterCounter before, after;
    }
    [DataContract] internal sealed class Cap03FilterNative
    {
        [DataMember] public string route;
        [DataMember] public int parentId, subjectId;
        [DataMember] public bool actual, expected;
        [DataMember] public Cap03FilterCounter before, after;
    }
}
