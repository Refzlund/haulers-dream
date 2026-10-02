using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HaulersDream.RuntimeHarness
{
    [DataContract] internal sealed class Bg03Origin
    {
        [DataMember] public int thingId { get; set; }
        [DataMember] public string def { get; set; }
        [DataMember] public int count { get; set; }
        [DataMember] public bool initiallyHeld { get; set; }
        [DataMember] public int[] units { get; set; }
    }
    [DataContract] internal sealed class Bg03ThingState
    {
        [DataMember] public int id { get; set; }
        [DataMember] public string def { get; set; }
        [DataMember] public int count { get; set; }
        [DataMember] public string custody { get; set; }
        [DataMember] public string holderType { get; set; }
        [DataMember] public bool spawned { get; set; }
        [DataMember] public bool destroyed { get; set; }
        [DataMember] public int x { get; set; }
        [DataMember] public int z { get; set; }
        [DataMember] public int? mapId { get; set; }
    }
    [DataContract] internal sealed class Bg03Quantity
    {
        [DataMember] public string def { get; set; }
        [DataMember] public int initialHeld { get; set; }
        [DataMember] public int initialFloor { get; set; }
        [DataMember] public int inventory { get; set; }
        [DataMember] public int hands { get; set; }
        [DataMember] public int floor { get; set; }
        [DataMember] public int uniqueAcquired { get; set; }
        [DataMember] public int reenteredHeld { get; set; }
        [DataMember] public int consumed { get; set; }
    }
    [DataContract] internal sealed class Bg03SelectionRow
    {
        [DataMember] public int ordinal { get; set; }
        [DataMember] public Bg03ThingState thing { get; set; }
        [DataMember] public int? selected { get; set; }
        [DataMember] public bool tagged { get; set; }
        [DataMember] public int[] units { get; set; }
    }
    [DataContract] internal sealed class Bg03JobState
    {
        [DataMember] public int? id { get; set; }
        [DataMember] public string def { get; set; }
        [DataMember] public string bill { get; set; }
        [DataMember] public string recipe { get; set; }
        [DataMember] public string workGiver { get; set; }
        [DataMember] public string workGiverClass { get; set; }
        [DataMember] public bool playerForced { get; set; }
        [DataMember] public bool queueLengthsMatch { get; set; }
        [DataMember] public int? targetA { get; set; }
        [DataMember] public List<Bg03SelectionRow> selection { get; set; } = new List<Bg03SelectionRow>();
    }
    [DataContract] internal sealed class Bg03State
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string reason { get; set; }
        [DataMember] public int? jobId { get; set; }
        [DataMember] public string jobDef { get; set; }
        [DataMember] public string driver { get; set; }
        [DataMember] public int x { get; set; }
        [DataMember] public int z { get; set; }
        [DataMember] public float benchDistance { get; set; }
        [DataMember] public float cleanedTotal { get; set; }
        [DataMember] public int meals { get; set; }
        [DataMember] public int billRemaining { get; set; }
        [DataMember] public List<Bg03ThingState> things { get; set; }
        [DataMember] public List<Bg03Quantity> quantities { get; set; }
    }
    [DataContract] internal sealed class Bg03Candidate
    {
        [DataMember] public int id { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public bool forcedArgument { get; set; }
        [DataMember] public string workGiverClass { get; set; }
        [DataMember] public int nativeSequence { get; set; }
        [DataMember] public int routedSequence { get; set; }
        [DataMember] public Bg03JobState native { get; set; }
        [DataMember] public Bg03JobState routed { get; set; }
    }
    [DataContract] internal sealed class Bg03Start
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public int? candidateId { get; set; }
        [DataMember] public Bg03JobState previous { get; set; }
        [DataMember] public Bg03JobState requested { get; set; }
        [DataMember] public string previousDriver { get; set; }
        [DataMember] public string previousToilInit { get; set; }
        [DataMember] public int? previousExecutedGatherId { get; set; }
        [DataMember] public string lastJobEndCondition { get; set; }
        [DataMember] public int actualSequence { get; set; }
        [DataMember] public Bg03JobState actual { get; set; }
        [DataMember] public string actualDriver { get; set; }
        [DataMember] public string actualDriverAssembly { get; set; }
        [DataMember] public string actualDriverMvid { get; set; }
        [DataMember] public Bg03State actualCustody { get; set; }
    }
    [DataContract] internal sealed class Bg03Ancestry
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string operation { get; set; }
        [DataMember] public string method { get; set; }
        [DataMember] public Bg03ThingState sourceBefore { get; set; }
        [DataMember] public Bg03ThingState targetBefore { get; set; }
        [DataMember] public Bg03ThingState sourceAfter { get; set; }
        [DataMember] public Bg03ThingState targetAfter { get; set; }
        [DataMember] public int requested { get; set; }
        [DataMember] public bool? nativeResult { get; set; }
        [DataMember] public bool valid { get; set; }
        [DataMember] public int[] movedUnits { get; set; }
    }
    [DataContract] internal sealed class Bg03Acquisition
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string boundary { get; set; }
        [DataMember] public Bg03State before { get; set; }
        [DataMember] public Bg03State after { get; set; }
        [DataMember] public int[] newlyAcquiredUnits { get; set; }
        [DataMember] public int[] reenteredUnits { get; set; }
        [DataMember] public int[] leftHeldUnits { get; set; }
    }
    [DataContract] internal sealed class Bg03Consumption
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public int jobId { get; set; }
        [DataMember] public Bg03ThingState before { get; set; }
        [DataMember] public Bg03ThingState after { get; set; }
        [DataMember] public bool attributed { get; set; }
        [DataMember] public int[] units { get; set; }
    }
    [DataContract] internal sealed class Bg03Cleaning
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string operation { get; set; }
        [DataMember] public int filthId { get; set; }
        [DataMember] public int jobId { get; set; }
        [DataMember] public bool native { get; set; }
        [DataMember] public string toilTickMethod { get; set; }
        [DataMember] public bool destroyed { get; set; }
        [DataMember] public float recordDelta { get; set; }
        [DataMember] public bool attributed { get; set; }
    }
    [DataContract] internal sealed class Bg03End
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public int jobId { get; set; }
        [DataMember] public string jobDef { get; set; }
        [DataMember] public string condition { get; set; }
        [DataMember] public bool released { get; set; }
    }
    [DataContract] internal sealed class Bg03Product
    {
        [DataMember] public int sequence { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public int jobId { get; set; }
        [DataMember] public int? thingId { get; set; }
        [DataMember] public int count { get; set; }
        [DataMember] public bool native { get; set; }
        [DataMember] public bool cleaningComplete { get; set; }
    }
    [DataContract] internal sealed class Bg03Assertion
    { [DataMember] public string id { get; set; } [DataMember] public string category { get; set; } [DataMember] public bool passed { get; set; } [DataMember] public string detail { get; set; } }
    [DataContract] internal sealed class Bg03Result
    {
        [DataMember] public string caseId { get; set; } = "BG03-P1";
        [DataMember] public string recipe { get; set; } = "CookMealSimpleBulk";
        [DataMember] public string expectedBehavior { get; set; }
        [DataMember] public string status { get; set; }
        [DataMember] public bool fixtureValid { get; set; }
        [DataMember] public bool requestedBehaviorSatisfied { get; set; }
        [DataMember] public bool expectationMatched { get; set; }
        [DataMember] public bool timedOut { get; set; }
        [DataMember] public bool completedNativeRecipe { get; set; }
        [DataMember] public bool fullInventoryBeforeReturn { get; set; }
        [DataMember] public bool changedProvenanceComplete { get; set; }
        [DataMember] public bool baselineProvenanceComplete { get; set; }
        [DataMember] public int startedTick { get; set; }
        [DataMember] public int finishedTick { get; set; }
        [DataMember] public int maximumTicks { get; set; } = 12000;
        [DataMember] public int? departureTick { get; set; }
        [DataMember] public int? firstReturnTick { get; set; }
        [DataMember] public int? stableSinceTick { get; set; }
        [DataMember] public int stableTickCount { get; set; }
        [DataMember] public int stateEvents { get; set; }
        [DataMember] public int gatherJobs { get; set; }
        [DataMember] public int nativeJobs { get; set; }
        [DataMember] public Bg03State initial { get; set; }
        [DataMember] public Bg03State departure { get; set; }
        [DataMember] public Bg03State completeInventory { get; set; }
        [DataMember] public Bg03State firstReturn { get; set; }
        [DataMember] public Bg03State final { get; set; }
        [DataMember] public List<Bg03Origin> origins { get; set; }
        [DataMember] public Bg03OutResult observerOutControl { get; set; }
        [DataMember] public Bg03FreshCargo freshCargo { get; set; }
        [DataMember] public bool initialWorkGateObserved { get; set; }
        [DataMember] public List<Bg03UnloadGate> unloadGates { get; set; } = new List<Bg03UnloadGate>();
        [DataMember] public List<Bg03Candidate> candidates { get; set; } = new List<Bg03Candidate>();
        [DataMember] public List<Bg03Start> starts { get; set; } = new List<Bg03Start>();
        [DataMember] public List<Bg03Ancestry> ancestry { get; set; } = new List<Bg03Ancestry>();
        [DataMember] public List<Bg03Acquisition> transfers { get; set; } = new List<Bg03Acquisition>();
        [DataMember] public List<Bg03Consumption> consumption { get; set; } = new List<Bg03Consumption>();
        [DataMember] public List<Bg03Cleaning> cleaning { get; set; } = new List<Bg03Cleaning>();
        [DataMember] public List<Bg03End> ends { get; set; } = new List<Bg03End>();
        [DataMember] public List<Bg03Product> products { get; set; } = new List<Bg03Product>();
        [DataMember] public List<Bg03Assertion> assertions { get; set; } = new List<Bg03Assertion>();
    }
}
