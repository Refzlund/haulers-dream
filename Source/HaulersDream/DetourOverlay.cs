using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// DEV-ONLY diagnostic overlay: draws coloured lines for the active en-route-pickup and storage-routing
    /// detours on the current map, so you can SEE why a pawn diverted. Shown only when
    /// <see cref="HaulersDreamSettings.drawDetourLines"/> is on AND <see cref="Prefs.DevMode"/> — the flag is
    /// non-serialized and resets to off every game start, so this is byte-inert in normal play (the very first
    /// line of the per-frame hook returns when it's off, before any work). Auto-instantiated for every map (no
    /// def/registration needed), mirroring <see cref="MapComponent_RoutePreview"/>; reuses its line-drawing idiom
    /// (<see cref="GenDraw.DrawLineBetween"/> on a cached transparent material at a meta-overlay altitude).
    ///
    /// <para>For each player pawn whose CURRENT job is one HD marked:
    /// <list type="bullet">
    ///   <item><b>En-route pickup</b> (a <c>HaulersDream_BulkHaul</c> tagged <see cref="EnRoutePickup.IsEnRoute"/>):
    ///   pawn → the grabbed item → the bound-for job cell (GREEN — "this rode along on a trip I was making").</item>
    ///   <item><b>Storage routing relocation</b> (a <c>HaulToCell</c> tagged <see cref="StorageRouting.IsRelocation"/>):
    ///   pawn → the stack → the closer store cell → the consumer cell it was moved closer to (CYAN — "moved this
    ///   closer to where it'll be used").</item>
    /// </list></para>
    /// </summary>
    public class DetourOverlay : MapComponent
    {
        public DetourOverlay(Map map) : base(map) { }

        private static readonly Color EnRouteColor = new Color(0.30f, 1f, 0.45f, 0.85f);   // green
        private static readonly Color RoutingColor = new Color(0.25f, 0.85f, 1f, 0.85f);   // cyan
        private const float LineWidth = 0.40f; // ~2× vanilla's 0.2 default — clearly visible

        private static Material enRouteMat;
        private static Material routingMat;

        private static Material EnRouteMat
        {
            get
            {
                if (enRouteMat == null)
                    enRouteMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, EnRouteColor);
                return enRouteMat;
            }
        }

        private static Material RoutingMat
        {
            get
            {
                if (routingMat == null)
                    routingMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, RoutingColor);
                return routingMat;
            }
        }

        public override void MapComponentUpdate()
        {
            // BYTE-INERT WHEN OFF (the very first line): dev flag off / not dev mode -> nothing runs.
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.drawDetourLines || !Prefs.DevMode)
                return;
            // Graphics.DrawMesh has no camera arg — only draw for the map currently shown (matches RoutePreview).
            if (map != Find.CurrentMap)
                return;

            float y = AltitudeLayer.MetaOverlays.AltitudeFor();
            // Iterate ALL spawned pawns and filter to the player faction, so HD-hauling colonists, animals, and
            // mechs are all covered. The spawned-pawns list is the cheap, complete source.
            var all = map.mapPawns?.AllPawnsSpawned;
            if (all == null)
                return;
            for (int i = 0; i < all.Count; i++)
            {
                var pawn = all[i];
                if (pawn == null || !pawn.Spawned || pawn.Faction != Faction.OfPlayerSilentFail)
                    continue;
                var job = pawn.CurJob;
                if (job?.def == null)
                    continue;
                DrawForJob(pawn, job, y);
            }
        }

        private void DrawForJob(Pawn pawn, Job job, float y)
        {
            // Storage routing relocation: pawn -> stack -> store cell -> consumer cell.
            if (job.def == RimWorld.JobDefOf.HaulToCell)
            {
                var info = StorageRouting.RelocationData(job);
                if (info == null)
                    return;
                Vector3 a = pawn.DrawPos;             a.y = y;
                Vector3 stack = CellOrThing(job.targetA, pawn); stack.y = y;
                Vector3 store = job.targetB.Cell.ToVector3Shifted(); store.y = y;
                Vector3 consume = info.consumeCell.ToVector3Shifted(); consume.y = y;
                var mat = RoutingMat;
                GenDraw.DrawLineBetween(a, stack, mat, LineWidth);
                GenDraw.DrawLineBetween(stack, store, mat, LineWidth);
                if (info.consumeCell.IsValid)
                    GenDraw.DrawLineBetween(store, consume, mat, LineWidth);
                return;
            }

            // En-route pickup: pawn -> grabbed item -> bound-for job cell.
            if (job.def == HaulersDreamDefOf.HaulersDream_BulkHaul)
            {
                var er = Patch_Pawn_JobTracker_EnRoutePickup.EnRouteData(job);
                if (er == null)
                    return;
                Vector3 a = pawn.DrawPos; a.y = y;
                Vector3 item = PickupCell(job, pawn); item.y = y;
                Vector3 dest = er.jobCell.ToVector3Shifted(); dest.y = y;
                var mat = EnRouteMat;
                GenDraw.DrawLineBetween(a, item, mat, LineWidth);
                if (er.jobCell.IsValid)
                    GenDraw.DrawLineBetween(item, dest, mat, LineWidth);
            }
        }

        // The grabbed item's position for an en-route bulk-haul: targetA, else the first queued pickup, else the pawn.
        private static Vector3 PickupCell(Job job, Pawn pawn)
        {
            if (job.targetA.IsValid)
                return CellOrThing(job.targetA, pawn);
            var q = job.targetQueueB;
            if (q != null)
                for (int k = 0; k < q.Count; k++)
                    if (q[k].IsValid)
                        return CellOrThing(q[k], pawn);
            return pawn.DrawPos;
        }

        // A target's draw position: a spawned thing's interpolated DrawPos (so it tracks a moving stack), else the
        // cell, else the pawn (so a despawned/picked-up target degrades cleanly instead of drawing to (0,0)).
        private static Vector3 CellOrThing(LocalTargetInfo t, Pawn pawn)
        {
            if (t.HasThing && t.Thing != null && t.Thing.Spawned)
                return t.Thing.DrawPos;
            if (t.Cell.IsValid)
                return t.Cell.ToVector3Shifted();
            return pawn.DrawPos;
        }
    }
}
