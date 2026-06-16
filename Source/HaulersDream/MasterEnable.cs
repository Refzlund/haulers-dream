namespace HaulersDream
{
    /// <summary>
    /// Master on/off for Hauler's Dream's AUTOMATIC behaviors, read live so it takes effect without a restart.
    /// When <see cref="Active"/> is false, HD stops INITIATING new behavior at the scoop / sweep / bulk-haul /
    /// work-override entry points (a troubleshooting kill switch for "is HD interfering with my pawns?"). It does
    /// NOT block an already-carrying pawn from unloading and does NOT hide the Unload gizmo (conflict guard G1 —
    /// gating the shared unload funnel would strand carried goods). Per-feature toggles still govern the rest.
    /// Defaults true when settings are not loaded yet (e.g. very early init).
    /// </summary>
    public static class MasterEnable
    {
        public static bool Active => HaulersDreamMod.Settings == null || HaulersDreamMod.Settings.masterEnabled;
    }
}
