namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision for the one-time pre-#79 yield migration (see HaulersDreamSettings.MigrateLegacyYieldSettings).
    ///
    /// Issue #238: the migration used to run whenever the loaded schema stamp was below the current schema. That is
    /// WRONG, because a missing stamp is indistinguishable from a stamp of 0 — Verse.Scribe_Values.Look OMITS a node
    /// whose value equals the passed default, so a settings object whose stamp is 0 in memory never writes the node
    /// and always reads back 0. A profile SNAPSHOT is exactly that case (the profile system skips [ProfileMeta]
    /// fields when copying, so a snapshot's stamp is stuck at the field initializer forever), and so is a live
    /// config that has not yet survived one load + one write. Result: the migration re-ran on every launch and
    /// overwrote the nine "Collect work results" settings with their defaults.
    ///
    /// The fix is to ask the DATA, not the stamp: only migrate when the node being loaded actually CONTAINS at
    /// least one pre-#79 legacy node. This is the same shape as the older keepDefNames migration, which never had
    /// the bug precisely because it early-returns when the legacy list is absent.
    ///
    /// NOTE the deliberate omission of an "isSnapshot" input. Settings profiles shipped 2026-06-17 (#26) and the
    /// per-category yield model shipped 2026-06-26 (#80), so a profile snapshot written in that window CAN legally
    /// carry the legacy nodes and MUST still migrate exactly once. Keying off "is this a snapshot" would silently
    /// drop that migration; keying off the data is correct for snapshots and live settings alike.
    /// </summary>
    public static class SettingsSchemaPolicy
    {
        /// <summary>
        /// True when the pre-#79 -> per-category yield migration should run for the settings node being loaded.
        /// </summary>
        /// <param name="loadedSchemaVersion">The stamp just read from the node (an absent node reads as 0).</param>
        /// <param name="currentSchema">The schema this build writes (HaulersDreamSettings.CurrentSettingsSchema).</param>
        /// <param name="anyLegacyYieldNodePresent">
        /// True when the node being loaded actually contains at least one pre-#79 node (pickupMode / haulHarvest /
        /// haulMining / haulDeepDrill / haulDeconstruct / haulAnimals / haulStrip / haulUninstall).
        /// </param>
        public static bool ShouldMigrateLegacyYields(
            int loadedSchemaVersion, int currentSchema, bool anyLegacyYieldNodePresent)
            => loadedSchemaVersion < currentSchema && anyLegacyYieldNodePresent;
    }
}
