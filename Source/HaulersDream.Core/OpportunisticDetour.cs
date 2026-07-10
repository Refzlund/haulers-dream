namespace HaulersDream.Core
{
    /// <summary>
    /// How far out of its way a pawn will step to take a FREE-ish opportunity on a trip it is already making:
    /// grabbing a loose haulable it passes on the way to storage (<c>ShouldGrabOnWay</c>), or shedding a scooped
    /// load at storage while on non-emergency protected work such as an elective surgery (<c>ShouldUnloadZeroDetour</c>).
    /// Both share one budget so a single knob governs "the zero-detour" behavior. The numeric budget each level
    /// maps to is <see cref="OpportunisticUnloadPolicy.DetourBudgetTiles"/>; <see cref="Off"/> is handled by the
    /// callers (they skip the behavior entirely rather than pass a budget of zero).
    /// </summary>
    public enum OpportunisticDetour
    {
        /// <summary>Never take the opportunity: pawns won't detour at all to grab a passing item or drop off a
        /// load. Restores the strict "never delay the work" behavior (a doctor carries its scooped load through the
        /// whole surgery queue; a hauler leaves items it walks past).</summary>
        Off,

        /// <summary>Only when it costs practically nothing: the original fixed budget, roughly an item on the exact
        /// path (<see cref="OpportunisticUnloadPolicy.DetourTilesShort"/> extra tiles).</summary>
        Short,

        /// <summary>The default: a short detour is worth avoiding a second trip
        /// (<see cref="OpportunisticUnloadPolicy.DetourTilesStandard"/> extra tiles).</summary>
        Standard,

        /// <summary>Take worthwhile detours: grab / drop off even a bit out of the way
        /// (<see cref="OpportunisticUnloadPolicy.DetourTilesLong"/> extra tiles). On protected work this trades a
        /// small, deliberate delay for fewer trips.</summary>
        Long
    }
}
