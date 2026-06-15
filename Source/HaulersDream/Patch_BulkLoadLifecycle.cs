using HarmonyLib;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Defense-in-depth claim release on every interrupt exit path (in addition to the driver's own finish action):
    ///   • <c>Pawn_JobTracker.EndCurrentJob</c> — when an HD bulk-load job ends, release the pawn's claims. The
    ///     carried task items are already tagged in <see cref="CompHauledToInventory"/> (swept via the fill toils),
    ///     so they ride HD's normal storage unload — never dropped on a temp map, never stuck. (Salvage = the
    ///     items already being tagged inventory; nothing extra to move.)
    ///   • <c>Pawn.DeSpawn</c> — a despawning hauler (downed-and-removed, captured, caravan-pack) returns its claims
    ///     so another pawn can re-claim the slice, with no quota leak.
    /// Both are idempotent (<c>Core.LoadLedger.Release</c> clamps ≥0 + drops the pawn), so a double-release is a
    /// no-op and a missed one only over-reserves until the next event — never crashes.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_Pawn_JobTracker_EndCurrentJob_ReleaseClaim
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> PawnOf =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        // No try/catch: a release fault is a real bug to surface. Only acts when the ENDING job is HD's bulk-load.
        static void Prefix(Pawn_JobTracker __instance)
        {
            var pawn = PawnOf(__instance);
            if (pawn == null || __instance.curJob == null)
                return;
            if (__instance.curJob.def != HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk)
                return;
            HaulersDreamGameComponent.Instance?.LoadReleaseClaimsForPawn(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Patch_Pawn_DeSpawn_ReleaseClaim
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static void Prefix(Pawn __instance)
            => HaulersDreamGameComponent.Instance?.LoadReleaseClaimsForPawn(__instance);
    }
}
