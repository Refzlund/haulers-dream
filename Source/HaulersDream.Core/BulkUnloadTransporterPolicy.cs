namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for TRANSPORTER/SHUTTLE BULK UNLOAD, the building-side sibling of
    /// <see cref="BulkUnloadCarrierPolicy"/>: a hauler empties many stacks OUT of a landed transporter's cargo
    /// hold (any <c>CompTransporter</c> parent, an Odyssey player shuttle, a transport pod with leftover cargo,
    /// or a modded shuttle-like thing) into its OWN backpack in one visit. No game types, unit-tested headlessly;
    /// the game layer feeds it live world facts and acts on the result.
    ///
    /// The PULL LADDER itself is not duplicated here: once the visit is running, "which stack next, how many,
    /// backpack vs hands" is exactly the carrier problem, same free-mass arithmetic, same overflow shape, so the
    /// driver reuses <see cref="BulkUnloadCarrierPolicy.PlanNextPull"/> verbatim. This policy owns only what is
    /// genuinely transporter-specific: WHEN an unload may be offered at all.
    /// </summary>
    public static class BulkUnloadTransporterPolicy
    {
        /// <summary>
        /// May HD offer (or run) a bulk unload of this transporter right now?
        ///
        /// <para><paramref name="hasPullableContents"/>, the hold holds at least one stack the feature can pull.
        /// Pawns inside the hold are NOT pullable (they leave via their own boarding/exit mechanics), so the caller
        /// counts only non-pawn stacks; a shuttle holding nothing but passengers offers nothing.</para>
        ///
        /// <para><paramref name="loadLordActive"/>, ANYTHING is loading INTO this group right now: a vanilla
        /// <c>LoadAndEnterTransporters</c> lord (<c>TransporterUtility.FindLord</c> found one), an HD bulk-load
        /// courier running against the group, or a vanilla <c>HaulToTransporter</c> job targeting it (the game
        /// layer folds all three into one boolean via <c>BulkUnloadTransporterGate.ConflictActive</c>). Items are
        /// flowing IN; pulling them out would fight the load (haulers racing each other around the same pod).
        /// That state belongs to vanilla's own "Cancel load" gizmo, which drops everything at once, HD stays
        /// out. A STALE ready-to-launch group (groupID set, lord already gone, e.g. a shuttle that landed) is
        /// fine: nothing is loading, so removal conflicts with nothing.</para>
        ///
        /// <para><paramref name="inCaravan"/>, the transporter is currently part of a forming/travelling caravan
        /// on the map (<c>Thing.IsInCaravan</c>). Caravan packing owns its contents during gather; unloading from
        /// under it would strand the caravan manifest mid-formation.</para>
        /// </summary>
        public static bool MayOffer(bool hasPullableContents, bool loadLordActive, bool inCaravan)
            => hasPullableContents && !loadLordActive && !inCaravan;
    }
}
