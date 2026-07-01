using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The "Plan prioritized removing floor" dialog — the cell-based sibling of <see cref="Dialog_PlanSowRoute"/> for
    /// removing flooring around a clicked cell. Pick how to gather the cells (the whole connected floor, the nearest
    /// few, or a radius), how many (Radius), and a max travel distance (Chained). Live-previews the path on the map
    /// (reusing the cell-based <see cref="MapComponent_RoutePreview"/>). Two confirm buttons: APPEND adds to the
    /// pawn's existing queue, REPLACE interrupts and starts now.
    ///
    /// <para>No zone and no "Smart" routing (removing a floor produces no haulable product to circle back to), so the
    /// smart field is dropped entirely; the planner has no smart param. Otherwise structurally identical to the sow
    /// dialog, reusing the same shared "HaulersDream.PlanRoute.*" lang keys.</para>
    /// </summary>
    public class Dialog_PlanRemoveFloorRoute : Window
    {
        private readonly Pawn pawn;
        private readonly IntVec3 anchor;
        private readonly MapComponent_RoutePreview preview;
        private readonly string gerund;

        private RemoveFloorRouteMode mode = RemoveFloorRouteMode.Area;
        private int maxTravel = 100;   // Chained: max travel span (cells); >= NoLimitStep = no limit
        private int radius = 8;        // Radius: circle size (cells)
        private int amount;            // Radius: stop count; 1..amountMax, then amountMax+1 = "All"
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

        private readonly List<RemoveFloorRouteMode> allowedModes = new List<RemoveFloorRouteMode>
        {
            RemoveFloorRouteMode.Area, RemoveFloorRouteMode.Chained, RemoveFloorRouteMode.Radius,
        };

        private string selSig;
        private RemoveFloorRoutePlan cachedPlan;

        public Dialog_PlanRemoveFloorRoute(Pawn pawn, IntVec3 anchor)
        {
            this.pawn = pawn;
            this.anchor = anchor;
            gerund = "HaulersDream.PlanRoute.RemoveFloorGerund".Translate();
            picked.Add(anchor); // the clicked cell is always a must-include (and the route's anchor)
            preview = pawn?.Map?.GetComponent<MapComponent_RoutePreview>();

            var s = HaulersDreamMod.Settings;
            amountMax = Mathf.Clamp(s?.routeMaxAmount ?? 50, 5, RouteSelection.HardCap);
            amount = amountMax + 1; // default "All"
            selectionMethod = s?.routeSelectionMethod ?? RouteSelectionMethod.MostStopsPerTravel;

            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(460f, 634f);

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
                case RemoveFloorRouteMode.Chained:
                    string travelLabel = maxTravel >= NoLimitStep
                        ? "HaulersDream.PlanRoute.MaxTravelNoLimit".Translate().ToString()
                        : "HaulersDream.PlanRoute.Cells".Translate(maxTravel).ToString();
                    l.Label("HaulersDream.PlanRoute.MaxTravel".Translate(travelLabel));
                    maxTravel = Mathf.RoundToInt(l.Slider(maxTravel, MaxTravelMin, NoLimitStep));
                    break;
                case RemoveFloorRouteMode.Radius:
                    l.Label("HaulersDream.PlanRoute.RadiusLabel".Translate(radius));
                    radius = Mathf.RoundToInt(l.Slider(radius, 2f, RadiusMax));
                    DoAmountSlider(l);
                    break;
                case RemoveFloorRouteMode.Area:
                    // No amount slider: "the connected floor" removes every removable cell in the cluster (capped at
                    // HardCap downstream) — same as the sow Zone mode.
                    break;
            }

            l.Gap(8f);
            DoPickerSection(l);

            RefreshPlanIfNeeded();
            l.Gap(4f);
            l.Label(EstimateText());

            // "Remember plan" — the in-dialog twin of the bottom-right interface toggle (hovering it blinks it).
            l.Gap(6f);
            Patch_PlaySettings.DrawRememberPlanRow(l);

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

        private static string ModeLabel(RemoveFloorRouteMode m)
        {
            switch (m)
            {
                case RemoveFloorRouteMode.Chained: return "HaulersDream.PlanRoute.ModeChained".Translate();
                case RemoveFloorRouteMode.Radius: return "HaulersDream.PlanRoute.ModeRadius".Translate();
                default: return "HaulersDream.PlanRoute.ModeFloorArea".Translate();
            }
        }

        private static string ModeDesc(RemoveFloorRouteMode m)
        {
            switch (m)
            {
                case RemoveFloorRouteMode.Chained: return "HaulersDream.PlanRoute.ModeChainedDesc".Translate();
                case RemoveFloorRouteMode.Radius: return "HaulersDream.PlanRoute.ModeRadiusDesc".Translate();
                default: return "HaulersDream.PlanRoute.ModeFloorAreaDesc".Translate();
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
            string sig = $"{(int)mode}|{EffectiveAmount()}|{radius}|{maxTravel}|pk{pickHash}|sm{(int)selectionMethod}";
            if (sig == selSig && cachedPlan != null)
                return;
            selSig = sig;
            cachedPlan = RemoveFloorRoutePlanner.Plan(pawn, anchor, mode, EffectiveAmount(), radius, MaxDistance(),
                picked, selectionMethod, HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax);
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (preview == null)
                return;
            IntVec3? circleCenter = mode == RemoveFloorRouteMode.Radius ? anchor : (IntVec3?)null;
            if (cachedPlan == null || cachedPlan.cells.Count == 0)
            {
                preview.SetPreview(pawn, null, circleCenter, radius);
                return;
            }
            // No storage anchor for a remove-floor route (nothing to haul back).
            preview.SetPreview(pawn, new List<IntVec3>(cachedPlan.cells), circleCenter, radius, storage: null);
        }

        private int EffectiveAmount() => amount > amountMax ? RouteSelection.AllAmount : amount;

        private float MaxDistance() => mode == RemoveFloorRouteMode.Chained
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
            var tp = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = true, // remove-floor cells are picked by clicking the map
                validator = ti => map != null && ti.Cell.IsValid && ti.Cell.InBounds(map)
                    && RemoveFloorRouteSelection.IsRemovableFloorCell(pawn, ti.Cell)
                    && pawn.CanReach(ti.Cell, PathEndMode.Touch, Danger.Deadly),
            };
            Find.Targeter.BeginTargeting(tp, OnPicked, (Pawn)null);
        }

        private void OnPicked(LocalTargetInfo target)
        {
            var c = target.Cell;
            if (c.IsValid && !picked.Contains(c) && RemoveFloorRouteSelection.IsRemovableFloorCell(pawn, c))
                picked.Add(c);
            rearmRequested = true;
        }

        private void Execute(bool replace)
        {
            if (cachedPlan == null)
                RefreshPlanIfNeeded();

            // Route the COMMIT through the synced entry point (a [SyncMethod] registered by name in MultiplayerCompat,
            // inert in single-player). MP-serializable args only; everything is recomputed deterministically per
            // client from the anchor (precomputed: null).
            RemoveFloorRouteExecutor.ExecuteRemoveFloorRouteSynced(pawn, anchor, mode, EffectiveAmount(), radius,
                MaxDistance(), replace, picked, selectionMethod,
                HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax);

            // Remember this confirmed plan so the "Remember plan" toggle can replay it in one click. Floors have no
            // ThingDef to key per-type prefs by, so a single session-scoped last-plan is kept. It is only READ on the
            // clicking client to build the synced command above (whose args then replicate), so it stays MP-safe.
            lastPlan = new Remembered
            {
                mode = mode, effAmount = EffectiveAmount(), radius = radius, maxDistance = MaxDistance(),
                selectionMethod = selectionMethod,
            };
        }

        // Session-scoped memory of the last confirmed remove-floor plan (see Execute). null until one is confirmed.
        // The Append/Replace choice is NOT stored — it follows the Queue Order key (Shift) at one-click time, vanilla.
        private static Remembered lastPlan;
        private sealed class Remembered
        {
            public RemoveFloorRouteMode mode;
            public int effAmount;
            public int radius;
            public float maxDistance;
            public RouteSelectionMethod selectionMethod;
        }

        /// <summary>True once a remove-floor plan has been confirmed this session, so the "Remember plan" one-click
        /// has something to replay (otherwise the float menu opens the dialog to establish it).</summary>
        public static bool HasRemembered => lastPlan != null;

        /// <summary>Replay the last confirmed remove-floor plan on <paramref name="anchor"/> without opening the
        /// dialog (the "Remember plan" one-click). <paramref name="replace"/> follows the vanilla queued-order
        /// convention (plain click replaces, Shift appends), derived from <see cref="KeyBindingDefOf.QueueOrder"/> by
        /// the caller at click time.</summary>
        public static void ExecuteRemembered(Pawn pawn, IntVec3 anchor, bool replace)
        {
            var r = lastPlan;
            if (r == null || pawn?.Map == null || !anchor.IsValid || !anchor.InBounds(pawn.Map))
                return;
            RemoveFloorRouteExecutor.ExecuteRemoveFloorRouteSynced(pawn, anchor, r.mode, r.effAmount, r.radius,
                r.maxDistance, replace, new List<IntVec3> { anchor }, r.selectionMethod,
                HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax);
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
