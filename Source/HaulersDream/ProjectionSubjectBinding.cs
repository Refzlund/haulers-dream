using System;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal enum ProjectionSubjectCustody { Floor, Inventory, Carry }
    internal enum ProjectionSubjectResult
    {
        Bound, InvalidRequest, WrongThread, LifetimeChanged, TransferInProgress,
        WorkLimit, StateLimit, BindingFault, InvalidCustody, InvalidSubject,
        UnsupportedOwner, UnsupportedFootprint, UnsupportedWrapper, UnsupportedWrapperQuantity,
        UnsupportedInnerSubject, InvalidInner, InvalidComponents, Changed
    }

    // SourceHolder describes present custody. Carrier describes a possible later
    // transfer and may be a different pawn, or absent. Neither is inferred by
    // recursively walking an arbitrary IThingHolder chain.
    internal sealed class ProjectionCustodyExpectation
    {
        internal Map Map { get; }
        internal ProjectionSubjectCustody Custody { get; }
        internal Pawn SourceHolder { get; }
        internal Pawn Carrier { get; }
        internal Faction Faction { get; }
        internal ProjectionCustodyExpectation(Map map, ProjectionSubjectCustody custody,
            Pawn sourceHolder, Pawn carrier, Faction faction)
        { Map = map; Custody = custody; SourceHolder = sourceHolder; Carrier = carrier; Faction = faction; }
    }

    internal sealed class ProjectionSubjectLimits
    {
        internal int OwnerEntries { get; }
        internal int GridEntries { get; }
        internal int Components { get; }
        internal int DefinitionComponents { get; }
        internal int StateRecords { get; }
        internal ProjectionSubjectLimits(int ownerEntries = 262144, int gridEntries = 4096,
            int components = 256, int definitionComponents = 256, int stateRecords = 8192)
        {
            if (ownerEntries <= 0 || gridEntries <= 0 || components < 0 || definitionComponents < 0 || stateRecords <= 0)
                throw new ArgumentOutOfRangeException();
            OwnerEntries = ownerEntries; GridEntries = gridEntries; Components = components;
            DefinitionComponents = definitionComponents; StateRecords = stateRecords;
        }
    }

    // Direct observed values only: RawHitPoints is deliberately not a virtual
    // getter result. These facts grant no filter, stat, stack or quantity authority.
    internal sealed class ProjectionSubjectFacts
    {
        internal Thing Thing { get; }
        internal ThingDef Def { get; }
        internal ThingDef Stuff { get; }
        internal Faction Faction { get; }
        internal ThingOwner Owner { get; }
        internal int Id { get; }
        internal int Count { get; }
        internal int StackLimit { get; }
        internal IntVec2 Size { get; }
        internal IntVec3 Position { get; }
        internal byte Rotation { get; }
        internal sbyte MapState { get; }
        internal int RawHitPoints { get; }
        internal ProjectionSubjectFacts(Thing thing, ThingDef def, ThingDef stuff, Faction faction,
            ThingOwner owner, int id, int count, int limit, IntVec2 size, IntVec3 position,
            byte rotation, sbyte mapState, int hitPoints)
        {
            Thing = thing; Def = def; Stuff = stuff; Faction = faction; Owner = owner;
            Id = id; Count = count; StackLimit = limit; Size = size; Position = position;
            Rotation = rotation; MapState = mapState; RawHitPoints = hitPoints;
        }
    }

    // An unconnected structural observation, not an eligibility edge. A future
    // predicate plan must recheck immediately before/after its separately reviewed
    // native call. No automatic patch/def generation detection is claimed here.
    internal sealed class ProjectionSubjectBinding
    {
        private readonly ProjectionSubjectReader reader;
        private readonly ProjectionCustodyExpectation expectation;
        private readonly ProjectionSubjectState state;
        private bool invalidated;
        internal ProjectionSubjectFacts Physical { get; }
        internal ProjectionSubjectFacts Filter { get; }
        internal Thing PhysicalSubject => Physical.Thing;
        internal Thing FilterSubject => Filter.Thing;
        internal bool IsMinified => !ReferenceEquals(Physical, Filter);
        internal Pawn SourceHolder => expectation.SourceHolder;
        internal Pawn Carrier => expectation.Carrier;
        internal CompQuality PhysicalQuality { get; }
        internal CompQuality FilterQuality { get; }
        internal QualityCategory? PhysicalQualityValue { get; }
        internal QualityCategory? FilterQualityValue { get; }
        internal ProjectionSubjectBinding(ProjectionSubjectReader reader, ProjectionCustodyExpectation expectation,
            ProjectionSubjectState state, ProjectionSubjectFacts physical, ProjectionSubjectFacts filter,
            CompQuality physicalQuality, QualityCategory? physicalQualityValue,
            CompQuality filterQuality, QualityCategory? filterQualityValue)
        {
            this.reader = reader; this.expectation = expectation; this.state = state;
            Physical = physical; Filter = filter; PhysicalQuality = physicalQuality;
            FilterQuality = filterQuality; PhysicalQualityValue = physicalQualityValue; FilterQualityValue = filterQualityValue;
        }
        internal ProjectionSubjectResult RecheckStructure(ProjectionWorkBudget work)
        {
            if (invalidated) return ProjectionSubjectResult.Changed;
            var result = reader.Recheck(PhysicalSubject, expectation, state, work);
            // A shortage publishes no fresh observation, but a larger allowance
            // may retry. Structural/lifetime failure permanently retires this seal.
            if (result != ProjectionSubjectResult.Bound && result != ProjectionSubjectResult.WorkLimit)
                invalidated = true;
            return result;
        }
    }
}
