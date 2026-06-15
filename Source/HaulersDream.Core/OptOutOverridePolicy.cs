namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether an EXPLICIT player order overrides the per-pawn "Auto-haul yields" opt-out
    /// (<c>CompHauledToInventory.autoHaulYields</c>) for a given producer source type. Pure so it can
    /// be unit-tested without a loaded game; <c>YieldRouter.IsCandidate</c> calls it.
    ///
    /// The only source whose yield arises EXCLUSIVELY from an explicit player order is
    /// <see cref="HaulSourceType.Strip"/>: a <c>JobDriver_Strip</c> is created only by
    /// <c>WorkGiver_Strip</c> (which fires only when the target carries a player-placed
    /// <c>DesignationDefOf.Strip</c> designation — added solely by <c>Designator_Strip</c>, a UI action)
    /// or by the right-click "Strip" float-menu option (a <c>playerForced</c> ordered job). There is NO
    /// autonomous, non-player path that produces a strip yield (decompile-verified against vanilla
    /// Assembly-CSharp). So an explicit player Strip order must scoop+haul the dropped gear even when the
    /// worker's standing auto-haul toggle is OFF — the toggle governs AUTONOMOUS yield scooping (harvest,
    /// mining, deep-drill, deconstruct, animal, and the separate auto-strip-on-haul convenience), not an
    /// order the player issued by hand.
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
