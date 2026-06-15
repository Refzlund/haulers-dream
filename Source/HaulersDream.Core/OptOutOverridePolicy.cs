namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether an EXPLICIT player order overrides the per-pawn "Auto-haul yields" opt-out
    /// (<c>CompHauledToInventory.autoHaulYields</c>) for a given producer source type. Pure so it can
    /// be unit-tested without a loaded game; <c>YieldRouter.IsCandidate</c> calls it.
    ///
    /// The only source whose yield ALWAYS traces back to an explicit player order is
    /// <see cref="HaulSourceType.Strip"/>: a <c>JobDriver_Strip</c> is created only from a
    /// <c>DesignationDefOf.Strip</c> designation — placed solely by the player via <c>Designator_Strip</c>
    /// (a UI action) — or from the right-click "Strip" float-menu option (a <c>playerForced</c> ordered job).
    /// In BOTH cases the player has explicitly designated the target for stripping; there is no source that
    /// fabricates a Strip designation on its own.
    ///
    /// NOTE on autonomy: a Strip designation is NOT only fulfilled by a hand-issued forced job. Decompile-
    /// verified against vanilla Assembly-CSharp, <c>RimWorld.WorkGiver_Strip</c> is a <c>WorkGiver_Scanner</c>
    /// (its <c>HasJobOnThing</c>/<c>JobOnThing</c> default <c>forced = false</c>, and
    /// <c>PotentialWorkThingsGlobal</c> enumerates every <c>DesignationDefOf.Strip</c> designation), so once the
    /// player places a Strip designation ANY eligible hauler fulfills it AUTONOMOUSLY (<c>forced == false</c>) —
    /// it is not a per-pawn forced order. This override is therefore an intentional DESIGN choice, not a claim
    /// that the path is non-autonomous: the player's Strip DESIGNATION is itself treated as explicit player
    /// intent ("collect this gear"), so the gear is scooped+hauled even by a worker whose standing auto-haul
    /// toggle is OFF — consistent with "an explicit player order overrides standing automation." The toggle
    /// still governs the genuinely self-initiated yield sources (harvest, mining, deep-drill, deconstruct,
    /// animal, and the separate auto-strip-on-haul convenience).
    ///
    /// Every other source (<see cref="HaulSourceType.Harvest"/> … <see cref="HaulSourceType.Animal"/>)
    /// is ordinary autonomous work whose scoop stays gated by the per-pawn opt-out, so this returns false
    /// for them.
    /// </summary>
    public static class OptOutOverridePolicy
    {
        /// <summary>
        /// True if a yield of this source type comes from an explicit player order that should override
        /// the per-pawn auto-haul opt-out. Only <see cref="HaulSourceType.Strip"/> qualifies.
        /// </summary>
        public static bool ExplicitOrderOverridesOptOut(HaulSourceType type)
        {
            return type == HaulSourceType.Strip;
        }
    }
}
