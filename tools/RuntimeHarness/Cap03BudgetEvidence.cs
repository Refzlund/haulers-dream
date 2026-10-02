using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HaulersDream.RuntimeHarness
{
    [DataContract] internal sealed class Cap03BudgetResult
    {
        [DataMember] public string caseId = "CAP03-B";
        [DataMember] public string contract = "asf-six-slot-member-budget-v1";
        [DataMember] public string expectedBehavior;
        [DataMember] public string status = "inconclusive";
        [DataMember] public string scope = "Only actual Neat Storage full-member budget component B; A/C, allocator, original reports and independent native call instrumentation remain pending.";
        [DataMember] public string error;
        [DataMember] public bool allCap03ComponentsSatisfied = false;
        [DataMember] public string[] pendingControls = { "CAP03-A", "CAP03-C", "independent-native-call-instrumentation", "large-groups-and-lifecycle", "allocation-and-original-report-convergence" };
        [DataMember] public bool fixtureValid, requestedBehaviorSatisfied, expectationMatched;
        [DataMember] public int startedTick, finishedTick, mainThread, mapId;
        [DataMember] public string sessionId, nativeIdentity, asfIdentity;
        [DataMember] public List<int> gameMapIds = new List<int>();
        [DataMember] public List<ProjectionFixtureAssembly> assemblies = new List<ProjectionFixtureAssembly>();
        [DataMember] public List<string> bindings = new List<string>();
        [DataMember] public List<string> patchInventory = new List<string>();
        [DataMember] public List<Cap03BudgetFile> inputs = new List<Cap03BudgetFile>();
        [DataMember] public ProjectionFixtureStatus catalogStatus, preparationStatus;
        [DataMember] public long preparationAllowance, preparationCharged;
        [DataMember] public bool preparationReady, footprintWarmup;
        [DataMember] public int indexedZoneCells;
        [DataMember] public Cap03BudgetPhysical initialPhysical, finalPhysical;
        [DataMember] public List<Cap03BudgetTrial> trials = new List<Cap03BudgetTrial>();
        [DataMember] public List<ProjectionFixtureAssertion> assertions = new List<ProjectionFixtureAssertion>();
        [DataMember] public List<Cap03BudgetRecord> records = new List<Cap03BudgetRecord>();
    }
    [DataContract] internal sealed class Cap03BudgetFile
    { [DataMember] public string packageId, root, relativePath, sha256; }
    [DataContract] internal sealed class Cap03BudgetRecord
    { [DataMember] public int sequence, tick; [DataMember] public string kind, trial, data; }
    [DataContract] internal sealed class Cap03BudgetTrial
    {
        [DataMember] public string id, query, scopeIdentity, parcelId, error;
        [DataMember] public int subjectId;
        [DataMember] public bool completed, disposed;
        [DataMember] public List<ProjectionFixtureWork> defaultAllowance, pageAllowance, openWork;
        [DataMember] public ProjectionFixtureStatus openStatus;
        [DataMember] public Cap03BudgetPage page;
        [DataMember] public ProjectionFixtureEligibility eligibility;
        [DataMember] public Cap03BudgetPhysical before, after;
        [DataMember] public List<Cap03BudgetUsed> used = new List<Cap03BudgetUsed>();
    }
    [DataContract] internal sealed class Cap03BudgetUsed
    { [DataMember] public string stage; [DataMember] public List<ProjectionFixtureWork> cumulative; }
    [DataContract] internal sealed class Cap03BudgetPage
    {
        [DataMember] public ProjectionFixtureStatus status;
        [DataMember] public bool requestedCellsComplete, wholeGroupComplete, hasCursor;
        [DataMember] public int? nextMember, nextCell;
        [DataMember] public int unresolvedTotal, unresolvedSampleStart, unresolvedSampleCount;
        [DataMember] public List<ProjectionFixtureWork> operationWork;
        [DataMember] public List<ProjectionFixtureCell> cells;
        [DataMember] public List<Cap03BudgetUnresolved> unresolved;
    }
    [DataContract] internal sealed class Cap03BudgetUnresolved
    { [DataMember] public string member, cell, reason; [DataMember] public int? memberOrdinal; }
    [DataContract] internal sealed class Cap03BudgetPhysical
    {
        [DataMember] public int mapId, tick, nativeMaximum, gridCount;
        [DataMember] public string cell, parentKey, groupIdentity, registryIdentity;
        [DataMember] public string settingsIdentity, filterIdentity, declaredFixedIdentity, declaredFilterIdentity, interfaceFixedIdentity, interfaceFilterIdentity;
        [DataMember] public string priority, storageGroupTag, actualCrateClass, crateDefSource, bookClass, bookTitle;
        [DataMember] public int crateWidth, crateDepth, defMaximum, novelShortHash, cargoShortHash;
        [DataMember] public bool registered, unlinked, settingsOwnerIsParent, actorStill, parentEverStorable;
        [DataMember] public bool novelEffectiveAccepted, novelDeclaredAccepted, novelInterfaceAccepted, cargoEffectiveAccepted, cargoDeclaredAccepted, cargoInterfaceAccepted;
        [DataMember] public ProjectionFixtureThing parent, actor, novel, cargo;
        [DataMember] public List<ProjectionFixtureThing> items, allGrid;
        [DataMember] public ProjectionFixtureFilter effectiveFilter, declaredFixedFilter, interfaceFixedFilter;
        [DataMember] public List<string> parentCompTypes, novelCompTypes, cargoCompTypes;
        [DataMember] public Cap03BudgetRegistry registry;
    }
    [DataContract] internal sealed class Cap03BudgetRegistry
    {
        [DataMember] public string collectionType;
        [DataMember] public int count, cellWiseCount, cellCount, slotLimit;
        [DataMember] public bool anyFree, packed, performanceFish, novelOverridesStack, cargoOverridesStack, acceptsNovelDef, acceptsCargoDef;
        [DataMember] public List<string> occupied, acceptedDefs;
        [DataMember] public List<int> acceptedShortHashes;
        [DataMember] public List<Cap03BudgetMember> members = new List<Cap03BudgetMember>();
    }
    [DataContract] internal sealed class Cap03BudgetMember
    {
        [DataMember] public int index, thingId, shortHash, count, stackLimit, indexOf;
        [DataMember] public int? mapId, storingParentId;
        [DataMember] public string def, mapCell, mapPosition, holderType;
        [DataMember] public bool spawned, contains, memberValid, targetValid, everStorable;
        [DataMember] public List<string> compTypes;
    }
}