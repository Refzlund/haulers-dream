namespace HaulersDream.Core
{
    /// <summary>
    /// What KIND of work a pawn just picked, as far as its ACCUMULATE RUN is concerned. The mod lets a pawn keep
    /// scooping into inventory across a whole run of related work and unload once at the end; this enum names the
    /// three kinds of work that mean "the run is still going" plus the catch-all that means it is over.
    /// </summary>
    public enum WorkRunKind
    {
        /// <summary>Yield-producing work — mining, plant harvest/cut, deep drilling, gathering animal resources,
        /// stripping, deconstructing. Every one of these drops goods the pawn is meant to accumulate.</summary>
        Yield,

        /// <summary>A storage-bound haul. It already delivers the pack to storage itself (a bulk haul sweeps the
        /// carried load along), so diverting to unload first would be a redundant trip.</summary>
        Haul,

        /// <summary>Construction — finishing a frame, or delivering material to one. The pawn is mid-BUILD run and
        /// the material it carries is what the next frame is about to eat, so a storage trip in the middle of it is
        /// pure loss (the reported "walks to the stockpile after every wall tile"). Split out from
        /// <see cref="Haul"/> because the material-hold guard needs to recognise construction specifically.</summary>
        Construction,

        /// <summary>Anything else — cleaning, cooking, doctoring, research. Picking one of these means the pawn's
        /// accumulate run is OVER, so the relaxed run-end unload criteria apply.</summary>
        Other
    }

    /// <summary>
    /// Pure classification of a chosen work job into a <see cref="WorkRunKind"/>, and the single question the
    /// unload machinery asks of it: did the pawn's accumulate run just END? The Verse adapter
    /// (<c>HaulersDream.OpportunisticUnload.ClassifyJobDef</c>) answers the three driver-type questions from the
    /// job's <c>driverClass</c> and calls in here, so the mapping itself is unit-pinned rather than buried in a
    /// chain of <c>IsAssignableFrom</c> walks.
    ///
    /// <para>The reason this exists as its own concept: construction used to fall through to <see cref="WorkRunKind.Other"/>,
    /// so a builder finishing a frame was treated as having ENDED its run and took the relaxed run-end unload bar
    /// (which has no minimum-trip floor) between every single wall tile.</para>
    /// </summary>
    public static class WorkRunPolicy
    {
        /// <summary>
        /// Map the three driver-type answers to a <see cref="WorkRunKind"/>.
        /// </summary>
        /// <param name="isConstructionDriver">The job's driver is construction work (finish a frame, place a
        /// no-cost frame, or the mod's own inventory construct-delivery).</param>
        /// <param name="isYieldDriver">The job's driver is one of the yield producers (mine / plant work / deep
        /// drill / gather animal resources / strip / deconstruct).</param>
        /// <param name="isHaulDriver">The job's driver is a storage-bound haul (haul-to-cell, haul-to-container,
        /// the mod's bulk haul).</param>
        /// <returns>The matching kind; <see cref="WorkRunKind.Other"/> when none of the three apply.</returns>
        /// <remarks>Precedence Construction &gt; Yield &gt; Haul only decides the LABEL for a driver that satisfies
        /// more than one question (vanilla construct delivery is a haul-to-container, and a modded driver may
        /// subclass anything): all three continue the run, so <see cref="ContinuesRun"/> is precedence-independent.
        /// Construction wins first because the material-hold guard keys off that specific label.</remarks>
        public static WorkRunKind Classify(bool isConstructionDriver, bool isYieldDriver, bool isHaulDriver)
        {
            if (isConstructionDriver)
                return WorkRunKind.Construction;
            if (isYieldDriver)
                return WorkRunKind.Yield;
            if (isHaulDriver)
                return WorkRunKind.Haul;
            return WorkRunKind.Other;
        }

        // The two complements of one question — "is the pawn still mid-run?" — named for whichever way round the
        // call site reads. Yield, Haul and Construction all CONTINUE the run (keep accumulating, apply the strict
        // "storage is genuinely on the way" bar); only Other ENDS it (apply the relaxed run-end bar).

        /// <summary>True while the pawn is still in an accumulate run and must keep its load.</summary>
        public static bool ContinuesRun(WorkRunKind kind) => kind != WorkRunKind.Other;

        /// <summary>True once the run is over and the relaxed run-end unload criteria apply.</summary>
        public static bool IsRunOver(WorkRunKind kind) => !ContinuesRun(kind);
    }
}
