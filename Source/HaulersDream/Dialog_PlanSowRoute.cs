using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The "Plan prioritized sowing" dialog — the cell-based sibling of <see cref="Dialog_PlanRoute"/> for sowing a
    /// growing zone's empty cells. Pick how to gather the cells (the whole field, the nearest few, or a radius),
    /// how many, a max travel distance (Chained), and whether to route smartly (circle back toward where the field's
    /// future harvest is stored). Live-previews the path on the map (reusing the cell-based
    /// <see cref="MapComponent_RoutePreview"/>). Two confirm buttons: APPEND adds to the pawn's existing queue,
    /// REPLACE interrupts and starts now. Deliberately separate from the Thing-based dialog so the harvest/mine/
    /// clean/construct path stays untouched.
    /// </summary>
    public class Dialog_PlanSowRoute : Window
    {
        private readonly Pawn pawn;
        private readonly IntVec3 anchor;
        private readonly Zone_Growing zone;
        private readonly MapComponent_RoutePreview preview;
        private readonly string gerund;

        private SowRouteMode mode = SowRouteMode.Zone;
        private int maxTravel = 100;   // Chained: max travel span (cells); >= NoLimitStep = no limit
        private int radius = 8;        // Radius: circle size (cells)
        private int amount;            // Radius: stop count; 1..amountMax, then amountMax+1 = "All"
        private bool smart = true;
        private readonly int amountMax;
        private RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel;

        private const int MaxTravelMin = 20;
        private const int NoLimitStep = 500;
        private const int RadiusMax = 30;

        // Player-picked must-include cells (the clicked anchor is always first). Always routed, transient per session.
        private readonly List<IntVec3> picked = new List<IntVec3>();
        private bool pickingMode;
        private bool rearmRequested;
        private Vector2 pickedScroll;
        private const float PickedRowH = 26f;

        private readonly List<SowRouteMode> allowedModes = new List<SowRouteMode>
        {
            SowRouteMode.Zone, SowRouteMode.Chained, SowRouteMode.Radius,
        };

        private string selSig;
        private SowRoutePlan cachedPlan;

        public Dialog_PlanSowRoute(Pawn pawn, IntVec3 anchor, Zone_Growing zone)
        {
            this.pawn = pawn;
            this.anchor = anchor;
            this.zone = zone;
            gerund = FloatMenuOptionProvider_PlanSowRoute.SowGerund();
            picked.Add(anchor); // the clicked cell is always a must-include (and the route's anchor)
            preview = pawn?.Map?.GetComponent<MapComponent_RoutePreview>();

            var s = HaulersDreamMod.Settings;
            amountMax = Mathf.Clamp(s?.routeMaxAmount ?? 50, 5, RouteSelection.HardCap);
            amount = amountMax + 1; // default "All"
            smart = true;
            selectionMethod = s?.routeSelectionMethod ?? RouteSelectionMethod.MostStopsPerTravel;

            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(460f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            const float btnH = 36f;
            if (pickingMode && !Find.Targeter.IsTargeting)
            {
                if (rearmRequested) { rearmRequested = false; BeginPicking(); }
                else pickingMode = false;
            }
            var body = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - btnH - 8f);

            var l = new Listing_Standard();
            l.Begin(body);

            Text.Font = GameFont.Medium;
            l.Label("HaulersDream.PlanRoute.Title".Translate(gerund));
            Text.Font = GameFont.Small;
            l.GapLine();

            for (int i = 0; i < allowedModes.Count; i++)
            {
                var m = allowedModes[i];
                if (l.RadioButton(ModeLabel(m), mode == m, tooltip: ModeDesc(m)))
                    mode = m;
            }

            l.Gap(6f);
            switch (mode)
            {
                case SowRouteMode.Chained:
                    string travelLabel = maxTravel >= NoLimitStep
                        ? "HaulersDream.PlanRoute.MaxTravelNoLimit".Translate().ToString()
                        : "HaulersDream.PlanRoute.Cells".Translate(maxTravel).ToString();
                    l.Label("HaulersDream.PlanRoute.MaxTravel".Translate(travelLabel));
                    maxTravel = Mathf.RoundToInt(l.Slider(maxTravel, MaxTravelMin, NoLimitStep));
                    break;
                case SowRouteMode.Radius:
                    l.Label("HaulersDream.PlanRoute.RadiusLabel".Translate(radius));
                    radius = Mathf.RoundToInt(l.Slider(radius, 2f, RadiusMax));
                    DoAmountSlider(l);
                    break;
                case SowRouteMode.Zone:
                    // No amount slider: "the whole field" sows every sowable cell (capped at HardCap downstream).
                    break;
            }

            l.Gap(6f);
            l.CheckboxLabeled("HaulersDream.PlanRoute.Smart".Translate(), ref smart,
                "HaulersDream.PlanRoute.SmartDesc".Translate());

            l.Gap(8f);
            DoPickerSection(l);

            RefreshPlanIfNeeded();
            l.Gap(4f);
            l.Label(EstimateText());

            l.End();

            float third = (inRect.width - 16f) / 3f;
            float by = inRect.yMax - btnH;
            if (Widgets.ButtonText(new Rect(inRect.x, by, third, btnH), "HaulersDream.PlanRoute.Cancel".Translate()))
                Close();
            bool append = Widgets.ButtonText(new Rect(inRect.x + third + 8f, by, third, btnH), "HaulersDream.PlanRoute.Append".Translate());
            bool replace = Widgets.ButtonText(new Rect(inRect.x + 2f * (third + 8f), by, third, btnH), "HaulersDream.PlanRoute.Replace".Translate());
            if (append) { Execute(replace: false); Close(); }
            if (replace) { Execute(replace: true); Close(); }
        }

        private string EstimateText()
        {
            if (cachedPlan == null || cachedPlan.Empty)
                return "HaulersDream.PlanRoute.EstimateNone".Translate();
            string hours = RouteEstimate.HoursFromTicks(cachedPlan.totalTicks).ToString("0.0");
            return "HaulersDream.PlanRoute.Estimate".Translate(hours, cachedPlan.cells.Count, Mathf.RoundToInt(cachedPlan.travelCells));
        }

        private static string ModeLabel(SowRouteMode m)
        {
            switch (m)
            {
                case SowRouteMode.Chained: return "HaulersDream.PlanRoute.ModeChained".Translate();
                case SowRouteMode.Radius: return "HaulersDream.PlanRoute.ModeRadius".Translate();
                default: return "HaulersDream.PlanRoute.ModeSowZone".Translate();
            }
        }

        private static string ModeDesc(SowRouteMode m)
        {
            switch (m)
            {
                case SowRouteMode.Chained: return "HaulersDream.PlanRoute.ModeChainedDesc".Translate();
                case SowRouteMode.Radius: return "HaulersDream.PlanRoute.ModeRadiusDesc".Translate();
                default: return "HaulersDream.PlanRoute.ModeSowZoneDesc".Translate();
            }
        }

        private void DoAmountSlider(Listing_Standard l)
        {
            string amtLabel = amount > amountMax
                ? "HaulersDream.PlanRoute.VeinAll".Translate().ToString()
                : amount.ToString();
            l.Label("HaulersDream.PlanRoute.Amount".Translate(amtLabel));
            amount = Mathf.RoundToInt(l.Slider(amount, 1f, amountMax + 1));
        }

        private void RefreshPlanIfNeeded()
        {
            int pickHash = picked.Count;
            for (int i = 0; i < picked.Count; i++)
                pickHash = pickHash * 31 + picked[i].x * 4099 + picked[i].z;
            string sig = $"{(int)mode}|{EffectiveAmount()}|{radius}|{maxTravel}|{(smart ? 1 : 0)}|pk{pickHash}|sm{(int)selectionMethod}";
            if (sig == selSig && cachedPlan != null)
                return;
            selSig = sig;
            cachedPlan = SowRoutePlanner.Plan(pawn, anchor, zone, mode, EffectiveAmount(), radius, MaxDistance(), smart,
                picked, selectionMethod, HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax);
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (preview == null)
                return;
            IntVec3? circleCenter = mode == SowRouteMode.Radius ? anchor : (IntVec3?)null;
            if (cachedPlan == null || cachedPlan.cells.Count == 0)
            {
                preview.SetPreview(pawn, null, circleCenter, radius);
                return;
            }
            preview.SetPreview(pawn, new List<IntVec3>(cachedPlan.cells), circleCenter, radius, cachedPlan.storageCell);
        }

        private int EffectiveAmount() => amount > amountMax ? RouteSelection.AllAmount : amount;

        private float MaxDistance() => mode == SowRouteMode.Chained
            ? (maxTravel >= NoLimitStep ? float.PositiveInfinity : maxTravel)
            : float.PositiveInfinity;

        // ── Must-include cell picker ────────────────────────────────────────────────────────────────────────

        private void DoPickerSection(Listing_Standard l)
        {
            if (l.ButtonText(pickingMode
                    ? "HaulersDream.PlanRoute.PickStop".Translate()
                    : "HaulersDream.PlanRoute.PickStart".Translate()))
                TogglePicking();
            l.Label("HaulersDream.PlanRoute.PickHeader".Translate(picked.Count));
            DrawPickedList(l.GetRect(3f * PickedRowH));
        }

        private void DrawPickedList(Rect outRect)
        {
            Widgets.DrawMenuSection(outRect);
            var inner = outRect.ContractedBy(2f);
            float viewH = picked.Count * PickedRowH;
            var viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(viewH, inner.height));
            Widgets.BeginScrollView(inner, ref pickedScroll, viewRect);
            float y = 0f;
            int removeAt = -1;
            const float bw = 22f;
            for (int i = 0; i < picked.Count; i++)
            {
                var c = picked[i];
                var row = new Rect(0f, y, viewRect.width, PickedRowH);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);
                bool isAnchor = c == anchor;
                var rmRect = new Rect(row.xMax - bw, y + 2f, bw, PickedRowH - 4f);
                if (!isAnchor && Widgets.ButtonText(rmRect, "✕"))
                    removeAt = i;
                var labelRect = new Rect(row.x + 4f, y, rmRect.x - row.x - 6f, PickedRowH);
                var prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, $"({c.x}, {c.z})".Truncate(labelRect.width));
                Text.Anchor = prevAnchor;
                y += PickedRowH;
            }
            Widgets.EndScrollView();
            if (removeAt > 0)
                picked.RemoveAt(removeAt);
        }

        private void TogglePicking()
        {
            if (pickingMode)
            {
                pickingMode = false;
                rearmRequested = false;
                if (Find.Targeter.IsTargeting)
                    Find.Targeter.StopTargeting();
            }
            else
            {
                pickingMode = true;
                rearmRequested = true;
            }
        }

        private void BeginPicking()
        {
            var map = pawn?.Map;
            var plantDef = zone.GetPlantDefToGrow();
            var tp = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = true, // sow cells are picked by clicking the map
                validator = ti => map != null && ti.Cell.IsValid && ti.Cell.InBounds(map)
                    && SowRouteSelection.IsSowableCell(pawn, ti.Cell, zone, plantDef)
                    && pawn.CanReach(ti.Cell, PathEndMode.Touch, Danger.Deadly),
            };
            Find.Targeter.BeginTargeting(tp, OnPicked, (Pawn)null);
        }

        private void OnPicked(LocalTargetInfo target)
        {
            var c = target.Cell;
            var plantDef = zone.GetPlantDefToGrow();
            if (c.IsValid && !picked.Contains(c) && SowRouteSelection.IsSowableCell(pawn, c, zone, plantDef))
                picked.Add(c);
            rearmRequested = true;
        }

        private void Execute(bool replace)
        {
            if (cachedPlan == null)
                RefreshPlanIfNeeded();

            // Route the COMMIT through the synced entry point (a [SyncMethod] registered by name in MultiplayerCompat,
            // inert in single-player). MP-serializable args only; the zone is re-derived per client from the anchor
            // cell and the plan is recomputed deterministically (precomputed: null).
            SowRouteExecutor.ExecuteSowRouteSynced(pawn, anchor, mode, EffectiveAmount(), radius, MaxDistance(), smart,
                replace, picked, selectionMethod, HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax);
        }

        public override void PostClose()
        {
            base.PostClose();
            if (pickingMode && Find.Targeter.IsTargeting)
                Find.Targeter.StopTargeting();
            preview?.ClearPreview();
        }
    }
}
