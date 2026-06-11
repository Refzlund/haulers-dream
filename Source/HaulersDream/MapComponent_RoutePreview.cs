using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Draws the planned-route preview on the map in bold translucent PURPLE while the
    /// <see cref="Dialog_PlanRoute"/> is open, so it's clearly distinct from vanilla's white "where the
    /// selected pawn is going" overlay. To MATCH what the route will actually look like once queued, it draws
    /// the same shape vanilla does for a queued route — straight lines from the pawn's LIVE position through
    /// each stop in order (so it tracks the pawn as it moves and lines up with the white overlay after the
    /// player confirms). Auto-instantiated for every map (no def/registration needed).
    /// </summary>
    public class MapComponent_RoutePreview : MapComponent
    {
        private Pawn pawn;
        private List<IntVec3> stops;
        private IntVec3? storageCell;   // smart routing: the haul-back destination (the route's last leg goes here)
        private IntVec3? circleCenter;  // Radius mode: the affected-area ring around the clicked target
        private float circleRadius;
        private static Material purpleLineMat;
        private static Material returnLineMat;

        private static readonly Color PurpleRing = new Color(0.72f, 0.30f, 1f, 0.65f);
        private const float LineWidth = 0.45f; // ~2× vanilla's 0.2 default — clearly visible
        private const float ReturnLineWidth = 0.38f;

        public MapComponent_RoutePreview(Map map) : base(map) { }

        public void SetPreview(Pawn p, List<IntVec3> stopCells, IntVec3? ringCenter = null, float ringRadius = 0f, IntVec3? storage = null)
        {
            pawn = p;
            stops = stopCells;
            storageCell = storage;
            circleCenter = ringCenter;
            circleRadius = ringRadius;
        }

        public void ClearPreview()
        {
            pawn = null;
            stops = null;
            storageCell = null;
            circleCenter = null;
            circleRadius = 0f;
        }

        // Bold translucent purple, same line texture + transparent shader as vanilla's white path line.
        private static Material PurpleLineMat
        {
            get
            {
                if (purpleLineMat == null)
                    purpleLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent,
                        new Color(0.72f, 0.22f, 1f, 0.9f));
                return purpleLineMat;
            }
        }

        // Cyan-tinted, de-emphasised line marking the smart-routing haul-back leg (last stop → storage),
        // so the "circle back toward storage" is clearly visible and distinct from the gathering route.
        private static Material ReturnLineMat
        {
            get
            {
                if (returnLineMat == null)
                    returnLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent,
                        new Color(0.25f, 0.85f, 1f, 0.7f));
                return returnLineMat;
            }
        }

        // Drawn every frame (runs even while paused, which is when the dialog is usually open).
        public override void MapComponentUpdate()
        {
            // Graphics.DrawMesh has no camera arg — without this guard, this map's path would draw into the
            // camera of whatever map is currently shown if the player switches maps with the dialog open.
            if (map != Find.CurrentMap)
                return;

            // Radius mode: show the affected-area ring around the clicked target.
            if (circleCenter.HasValue && circleRadius > 0f)
                GenDraw.DrawRadiusRing(circleCenter.Value, circleRadius, PurpleRing);

            var s = stops;
            var p = pawn;
            if (s == null || s.Count == 0 || p == null || !p.Spawned || p.Map != map)
                return;

            float y = AltitudeLayer.MetaOverlays.AltitudeFor();
            var mat = PurpleLineMat;
            Vector3 prev = p.DrawPos; // live, interpolated pawn position → the route tracks the pawn
            prev.y = y;
            for (int i = 0; i < s.Count; i++)
            {
                Vector3 c = s[i].ToVector3Shifted();
                c.y = y;
                GenDraw.DrawLineBetween(prev, c, mat, LineWidth);
                prev = c;
            }

            // Smart routing: draw the haul-back leg from the route's LAST stop to storage so the
            // "circle back toward storage" is visible (the route is ordered to END nearest storage).
            if (storageCell.HasValue)
            {
                Vector3 dest = storageCell.Value.ToVector3Shifted();
                dest.y = y;
                GenDraw.DrawLineBetween(prev, dest, ReturnLineMat, ReturnLineWidth);
            }
        }

        public override void MapRemoved() => ClearPreview();
    }
}
