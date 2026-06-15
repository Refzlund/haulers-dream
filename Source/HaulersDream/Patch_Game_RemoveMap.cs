using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// REQUIRED (not optional) ledger cleanup on map removal: a map being torn down (a caravan/encounter map
    /// leaving, a pocket map collapsing) must drop every bulk-load ledger entry tied to it — otherwise those
    /// entries keep dead Pawn/Map references and <c>LoadAnyClaimsInProgress</c> could stay true forever, blocking
    /// boarding/launch (the anti-conflict gates) for any task that reused the id space.
    ///
    /// RimWorld 1.6's removal entry point is <c>Verse.Game.DeinitAndRemoveMap(Map, bool)</c> (there is no
    /// <c>Game.RemoveMap</c> — verified against the decompiled 1.6 assembly). A PREFIX runs while the map ref is
    /// still live, so the by-Map drop resolves correctly before deinit nulls things out.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
    public static class Patch_Game_RemoveMap
    {
        // No try/catch: a cleanup failure is a real bug to surface (Harmony propagates to RimWorld's handler),
        // not a silently-swallowed warning. Null-safe via the component getter + the method's own guards.
        static void Prefix(Map map) => HaulersDreamGameComponent.Instance?.Notify_LoadMapRemoved(map);
    }
}
