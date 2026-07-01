using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The "Plan prioritized route" dialog: pick how to gather nearby same-kind targets (a radius, the N
    /// nearest, or the touching vein), how many, a max travel distance that ceilings the route, and whether to
    /// route smartly (circle back toward the product's storage). It live-previews the actual navigation path on
    /// the map in purple and shows the estimated completion time. Two confirm buttons: APPEND adds the route to
    /// the pawn's existing manual queue; REPLACE interrupts and starts it now.
    /// </summary>
    public class Dialog_PlanRoute : Window
    {
        private readonly Pawn pawn;
        private readonly Thing clicked;
        private readonly RouteWorkKind kind;
        private readonly MapComponent_RoutePreview preview;

        private RouteMode mode = RouteMode.Chained;
        private int maxTravel = 100;   // Chained: max span first→last stop (cells); >= NoLimitStep = no limit
        private int radius = 8;        // Radius: circle size (cells)
        private int amount;            // Radius/Vein: stop count; 1..amountMax, then amountMax+1 = "All"
        private bool smart = true;
        private bool allowHarvest;     // harvest only — loaded from settings (persisted across opens)
        private int growthThreshold;   // harvest only — loaded from settings
        private readonly bool isHarvest; // the clicked work is plant-harvesting (the only kind with a growth gate)
        private readonly int amountMax;  // top numeric step of the Amount slider (configurable in mod options)
        private readonly int harvestMinPct; // harvest only — the plant's harvestMinGrowth as %, the growth slider's floor

        private const int MaxTravelMin = 20;
        private const int NoLimitStep = 500;    // max-travel slider top step; at/above this = "no limit"
        private const int RadiusMax = 30;

        // Build tag logged with the verbose-gated route diagnostic (see RefreshPlanIfNeeded's HDLog line), so a
        // "still broken" report can be told apart from a stale DLL (mod C# only reloads at game startup — an
        // un-restarted game runs the OLD assembly). Bump this whenever the route planner's behaviour changes.
        public const string BuildTag = "F12t";

        private RouteLegs cachedLegs;   // expensive (pathfound) part, recomputed only when the selection changes
        private string legsSig;
        private int lastBudget = int.MinValue;
        private RoutePlan cachedPlan;
        // Debounce for the expensive legs recompute while a selection slider is being dragged.
        private string pendingSelSig;
        private int pendingSelSigFrames;
        private const int SelSigDebounceFrames = 9; // ~0.15 s at 60 fps

        // Player-picked "must include" targets (the clicked anchor is always first). These are ALWAYS routed,
        // regardless of mode / amount / max-travel, and bypass the growth threshold. Transient per dialog session
        // (live Things can't be portably persisted), so not saved with the per-def prefs.
        private readonly List<Thing> picked = new List<Thing>();
        private bool pickingMode;        // the picker tool is armed: clicking a same-kind target on the map adds it
        // Arm/re-arm the targeter ONLY when we asked for it (the picker button, or right after a consumed
        // pick — for multi-pick). A targeter found stopped WITHOUT this request means the player cancelled
        // (vanilla's Targeter consumes ESC/right-click itself), so picking must EXIT, not instantly re-arm —
        // re-arming every frame locked the player in the picker until "Done picking".
        private bool rearmRequested;
        private Vector2 pickedScroll;
        private const float PickedRowH = 26f;

        // Secondary target DEFS (issue #96 multi-target): additional ThingDefs of the SAME work kind to also gather
        // (e.g. berries + rice in one harvest route, or steel + component veins in one mining route). The clicked
        // anchor's own def is always the primary target and is NOT listed here. Only offered for the def-based scopes
        // (SameDef / SameDefOrDesignated) — filth/blueprints/designated-only already gather every same-work target
        // regardless of def. Transient per dialog session, so (like `picked`) not saved with the per-def prefs.
        private readonly List<ThingDef> secondaryDefs = new List<ThingDef>();
        private bool pickingSecondary;   // the "add target type" picker is armed
        private Vector2 secondaryScroll;
        private bool SupportsMultiTarget => kind != null
            && (kind.scope == RouteTargetScope.SameDef || kind.scope == RouteTargetScope.SameDefOrDesignated);

        // Which selection modes this kind of work offers (default first) — see RouteSelection.AllowedModes.
        // Chained/Touching make no sense for cleaning; Rooms makes no sense for an ore vein.
        private readonly List<RouteMode> allowedModes;

        // Rooms mode: ANCHOR CELLS whose rooms bound the selection. Cells, not Room references — Room objects
        // regenerate whenever regions rebuild (a wall built while the dialog is open must not leave us holding
        // dead rooms). The clicked target's cell is always the first anchor (its room is pre-included).
        private readonly List<IntVec3> roomAnchors = new List<IntVec3>();
        private bool pickingRoom;        // the room picker is armed: clicking a cell on the map adds its room
        private Vector2 roomsScroll;

        // Optional pinned START / END stops (a node from the must-include list). The route is the shortest path
        // pawn → [start] → … → [end] → storage; an untagged anchor is just a must-include the router places wherever
        // it's cheapest (NOT forced first). Transient per session (live Things), like `picked`. null = unpinned.
        private Thing startNode;
        private Thing endNode;
        private string lastPinSig;
        private static readonly Color RoleActive = new Color(0.5f, 1f, 0.5f); // tint for the active S/E toggle

        // Route-calc overrides (loaded from this target type's prefs, or seeded from the global mod-settings default).
        private RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel;
        private RouteDistanceBasis distanceBasis = RouteDistanceBasis.StraightLine;

        public Dialog_PlanRoute(Pawn pawn, Thing clicked, RouteWorkKind kind)
        {
            this.pawn = pawn;
            this.clicked = clicked;
            this.kind = kind;
            if (clicked != null)
                picked.Add(clicked); // the right-clicked target is always a must-include (and the route's anchor)
            allowedModes = RouteSelection.AllowedModes(kind, clicked, pawn?.Map);
            if (clicked != null)
                roomAnchors.Add(clicked.Position); // the room that contains the clicked target is pre-included
            preview = pawn?.Map?.GetComponent<MapComponent_RoutePreview>();
            isHarvest = kind?.designation == DesignationDefOf.HarvestPlant;
            // A plant isn't harvestable below its harvestMinGrowth (default ~65%), so the growth-threshold slider
            // can't sensibly go lower than that — anything below is a no-op (those plants aren't harvestable yet).
            harvestMinPct = isHarvest ? Mathf.Clamp(Mathf.RoundToInt((clicked?.def?.plant?.harvestMinGrowth ?? 0f) * 100f), 0, 99) : 0;
            var s = HaulersDreamMod.Settings;
            amountMax = Mathf.Clamp(s?.routeMaxAmount ?? 50, 5, RouteSelection.HardCap);

            // Restore the options last used on THIS kind of target (berries remember berry settings, etc.);
            // a brand-new kind falls back to defaults — Chained, "All" amount, harvest options seeded from settings.
            var prefs = s?.GetRoutePrefs(clicked?.def?.defName);
            if (prefs != null)
            {
                mode = prefs.mode;
                // maxTravel persists -1 as a portable "no limit" sentinel (survives a changed NoLimitStep, the way
                // amount stores "All" as -1) — restore any sub-minimum saved value to the current no-limit top.
                maxTravel = prefs.maxTravel < MaxTravelMin ? NoLimitStep : Mathf.Clamp(prefs.maxTravel, MaxTravelMin, NoLimitStep);
                radius = Mathf.Clamp(prefs.radius, 2, RadiusMax);
                amount = (prefs.amount < 1 || prefs.amount > amountMax) ? amountMax + 1 : prefs.amount; // <1 = "All"
                smart = prefs.smart;
                allowHarvest = prefs.allowHarvest;
                growthThreshold = Mathf.Clamp(prefs.growthThreshold, harvestMinPct, 100);
                selectionMethod = prefs.selectionMethod;
                distanceBasis = prefs.distanceBasis;
            }
            else
            {
                amount = amountMax + 1; // default to "All"
                allowHarvest = s?.routeAllowHarvest ?? true;
                growthThreshold = Mathf.Clamp(s?.routeGrowthThreshold ?? 80, harvestMinPct, 100);
                selectionMethod = s?.routeSelectionMethod ?? RouteSelectionMethod.MostStopsPerTravel;
                distanceBasis = s?.routeDistanceBasis ?? RouteDistanceBasis.StraightLine;
            }
            // The restored (or default) mode may not exist for this kind of work — a stale pref, or simply the
            // global Chained default on a kind that doesn't offer Chained (cleaning). Fall to the kind's default.
            if (!allowedModes.Contains(mode))
                mode = allowedModes[0];
            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = false; // keep open while the player pans the map to view the preview
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        // Rooms mode adds a picker button + a 3-row room list, so kinds that offer it need the taller layout too.
        public override Vector2 InitialSize => new Vector2(460f,
            isHarvest || allowedModes.Contains(RouteMode.Rooms) ? 860f : 772f);

        public override void DoWindowContents(Rect inRect)
        {
            const float btnH = 36f;
            const float rememberH = 30f;   // the "Remember" button row, just above Cancel/Append/Replace
            // (Re-)arm the pickers only on request (button press / right after a consumed pick — RimWorld's
            // Targeter fires its action once then stops, so multi-pick needs a re-arm). A stop WITHOUT a
            // pending request is the player cancelling via ESC/right-click: exit picking cleanly.
            if (pickingMode && !Find.Targeter.IsTargeting)
            {
                if (rearmRequested) { rearmRequested = false; BeginPicking(); }
                else pickingMode = false;
            }
            if (pickingRoom && !Find.Targeter.IsTargeting)
            {
                if (rearmRequested) { rearmRequested = false; BeginRoomPicking(); }
                else pickingRoom = false;
            }
            if (pickingSecondary && !Find.Targeter.IsTargeting)
            {
                if (rearmRequested) { rearmRequested = false; BeginSecondaryPicking(); }
                else pickingSecondary = false;
            }
            var body = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - btnH - rememberH - 6f - 8f);

            var l = new Listing_Standard();
            l.Begin(body);

            Text.Font = GameFont.Medium;
            l.Label("HaulersDream.PlanRoute.Title".Translate(kind.gerund));
            Text.Font = GameFont.Small;
            l.GapLine();

            // Only the modes that make sense for THIS kind of work (default first): cleaning offers Rooms+Radius,
            // mining offers Chained/Touching/Radius, a plant in a growing zone adds Zone, etc.
            for (int i = 0; i < allowedModes.Count; i++)
            {
                var m = allowedModes[i];
                if (l.RadioButton(ModeLabel(m), mode == m, tooltip: ModeDesc(m)))
                {
                    if (mode != m && pickingRoom)
                        StopRoomPicking(); // leaving Rooms mode shouldn't leave the room picker armed
                    mode = m;
                }
            }

            l.Gap(6f);

            switch (mode)
            {
                case RouteMode.Chained:
                    // Chained is bounded by how far the pawn actually walks gathering the targets — the length of
                    // the real route from the pawn through every stop (not counting the haul-back to storage).
                    string travelLabel = maxTravel >= NoLimitStep
                        ? "HaulersDream.PlanRoute.MaxTravelNoLimit".Translate().ToString()
                        : "HaulersDream.PlanRoute.Cells".Translate(maxTravel).ToString();
                    l.Label("HaulersDream.PlanRoute.MaxTravel".Translate(travelLabel));
                    maxTravel = Mathf.RoundToInt(l.Slider(maxTravel, MaxTravelMin, NoLimitStep));
                    break;
                case RouteMode.Radius:
                    l.Label("HaulersDream.PlanRoute.RadiusLabel".Translate(radius));
                    radius = Mathf.RoundToInt(l.Slider(radius, 2f, RadiusMax));
                    DoAmountSlider(l);
                    break;
                case RouteMode.Vein:
                    DoAmountSlider(l);
                    break;
                case RouteMode.Rooms:
                    DoRoomsSection(l);
                    DoAmountSlider(l);
                    break;
                case RouteMode.Zone:
                    DoAmountSlider(l);
                    break;
            }

            l.Gap(6f);
            l.CheckboxLabeled("HaulersDream.PlanRoute.Smart".Translate(), ref smart,
                "HaulersDream.PlanRoute.SmartDesc".Translate());

            if (IsConstruction)
            {
                l.Gap(6f);
                l.CheckboxLabeled("HaulersDream.PlanRoute.AlsoBuild".Translate(), ref alsoBuild,
                    "HaulersDream.PlanRoute.AlsoBuildDesc".Translate());
            }

            if (isHarvest)
            {
                l.Gap(6f);
                l.CheckboxLabeled("HaulersDream.PlanRoute.AllowHarvest".Translate(), ref allowHarvest,
                    "HaulersDream.PlanRoute.AllowHarvestDesc".Translate());
                if (allowHarvest)
                {
                    growthThreshold = Mathf.Clamp(growthThreshold, harvestMinPct, 100); // never below the harvestable floor
                    l.Label("HaulersDream.PlanRoute.GrowthThreshold".Translate(growthThreshold));
                    growthThreshold = Mathf.RoundToInt(l.Slider(growthThreshold, harvestMinPct, 100f));
                }
            }

            // How the route is CALCULATED (per-target-type overrides of the global mod-settings defaults):
            // selection method (most-stops-per-travel vs nearest-to-clicked) and distance basis (straight-line vs
            // real walking path). The exact-solve effort for the visiting order is a global mod setting.
            l.Gap(8f);
            if (l.ButtonText("HaulersDream.PlanRoute.Selection".Translate(HaulersDreamSettings.SelectionMethodLabel(selectionMethod))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(HaulersDreamSettings.SelectionMethodLabel(RouteSelectionMethod.MostStopsPerTravel), () => selectionMethod = RouteSelectionMethod.MostStopsPerTravel),
                    new FloatMenuOption(HaulersDreamSettings.SelectionMethodLabel(RouteSelectionMethod.NearestToTarget), () => selectionMethod = RouteSelectionMethod.NearestToTarget),
                }));
            if (l.ButtonText("HaulersDream.PlanRoute.DistanceBasis".Translate(HaulersDreamSettings.DistanceBasisLabel(distanceBasis))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(HaulersDreamSettings.DistanceBasisLabel(RouteDistanceBasis.StraightLine), () => distanceBasis = RouteDistanceBasis.StraightLine),
                    new FloatMenuOption(HaulersDreamSettings.DistanceBasisLabel(RouteDistanceBasis.WalkingPath), () => distanceBasis = RouteDistanceBasis.WalkingPath),
                }));

            // Secondary target TYPES (issue #96): add other defs of this same work kind so the route gathers them
            // all in one go (berries + rice, steel + components). Only meaningful for the def-based scopes.
            if (SupportsMultiTarget)
            {
                l.Gap(8f);
                DoSecondarySection(l);
            }

            // Must-include picker: click specific same-kind targets that MUST be in the route (they're always
            // routed regardless of mode/amount/max-travel, and bypass the growth threshold). The clicked anchor
            // is pre-listed. Order doesn't matter — the route still visits them by the shortest path.
            l.Gap(8f);
            DoPickerSection(l);

            // Live estimate — recompute the plan (and refresh the purple preview) whenever an input changes.
            RefreshPlanIfNeeded();
            l.Gap(4f);
            l.Label(EstimateText());

            // Vein only: warn when the visible cluster runs into fog — not all of the vein is counted (anti-abuse),
            // and the deferred-reveal tracker may extend the route as the pawn mines and uncovers more of it.
            if (mode == RouteMode.Vein && cachedPlan != null && cachedPlan.fogCaution)
            {
                GUI.color = new Color(1f, 0.85f, 0.4f);
                l.Label("HaulersDream.PlanRoute.FogCaution".Translate());
                GUI.color = Color.white;
            }

            l.End();

            // "Remember" — full-width button at the bottom, just above Cancel/Append/Replace (a separate layer from
            // the per-instance settings PostClose auto-restores). Once saved, this kind of target's right-click menu
            // reads "(remembered)" and runs the template in one click while the interface toggle is on.
            DoRememberButton(new Rect(inRect.x, inRect.yMax - btnH - 6f - rememberH, inRect.width, rememberH));

            float third = (inRect.width - 16f) / 3f;
            float by = inRect.yMax - btnH;
            // A construction route is always confirmable, even with no materials deliverable this instant: the route
            // delivers to whichever stops have free materials (in route order) and the rest build as materials become
            // available — matching WorkKindResolver's "plannable even when materials aren't deliverable" design. The
            // executor reports the outcome (queued / waiting for materials), so we never silently fail on click.
            if (Widgets.ButtonText(new Rect(inRect.x, by, third, btnH), "HaulersDream.PlanRoute.Cancel".Translate()))
                Close();
            bool append = Widgets.ButtonText(new Rect(inRect.x + third + 8f, by, third, btnH), "HaulersDream.PlanRoute.Append".Translate());
            bool replace = Widgets.ButtonText(new Rect(inRect.x + 2f * (third + 8f), by, third, btnH), "HaulersDream.PlanRoute.Replace".Translate());
            if (append)
            {
                Execute(replace: false);
                Close();
            }
            if (replace)
            {
                Execute(replace: true);
                Close();
            }
        }

        private string EstimateText()
        {
            if (cachedPlan == null || cachedPlan.Empty)
                return "HaulersDream.PlanRoute.EstimateNone".Translate();
            string hours = RouteEstimate.HoursFromTicks(cachedPlan.totalTicks).ToString("0.0");
            string baseText = "HaulersDream.PlanRoute.Estimate".Translate(hours, cachedPlan.stops.Count, Mathf.RoundToInt(cachedPlan.travelCells));
            // No "trimmed to the travel limit" note: the stop count already shows how many fit, and raising Max
            // travel (or picking targets) is the obvious knob — the message just read as noise. Unreachable targets
            // are still surfaced (a genuinely different reason a target you'd expect isn't in the route).
            if (cachedPlan.cappedByReach)
                baseText += " " + "HaulersDream.PlanRoute.EstimateUnreachable".Translate();
            return baseText;
        }

        private static string ModeLabel(RouteMode m)
        {
            switch (m)
            {
                case RouteMode.Chained: return "HaulersDream.PlanRoute.ModeChained".Translate();
                case RouteMode.Vein: return "HaulersDream.PlanRoute.ModeVein".Translate();
                case RouteMode.Rooms: return "HaulersDream.PlanRoute.ModeRooms".Translate();
                case RouteMode.Zone: return "HaulersDream.PlanRoute.ModeZone".Translate();
                default: return "HaulersDream.PlanRoute.ModeRadius".Translate();
            }
        }

        private static string ModeDesc(RouteMode m)
        {
            switch (m)
            {
                case RouteMode.Chained: return "HaulersDream.PlanRoute.ModeChainedDesc".Translate();
                case RouteMode.Vein: return "HaulersDream.PlanRoute.ModeVeinDesc".Translate();
                case RouteMode.Rooms: return "HaulersDream.PlanRoute.ModeRoomsDesc".Translate();
                case RouteMode.Zone: return "HaulersDream.PlanRoute.ModeZoneDesc".Translate();
                default: return "HaulersDream.PlanRoute.ModeRadiusDesc".Translate();
            }
        }

        // ── Rooms mode: picked-rooms list + the room picker ─────────────────────────────────────────────────

        private void DoRoomsSection(Listing_Standard l)
        {
            if (l.ButtonText(pickingRoom
                    ? "HaulersDream.PlanRoute.PickRoomsStop".Translate()
                    : "HaulersDream.PlanRoute.PickRooms".Translate()))
                ToggleRoomPicking();
            l.Label("HaulersDream.PlanRoute.RoomsHeader".Translate(roomAnchors.Count));
            DrawRoomsList(l.GetRect(3f * PickedRowH)); // ~3 rows visible, scroll for more
            l.Gap(4f);
        }

        private void DrawRoomsList(Rect outRect)
        {
            Widgets.DrawMenuSection(outRect);
            var inner = outRect.ContractedBy(2f);
            float viewH = roomAnchors.Count * PickedRowH;
            var viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(viewH, inner.height));
            Widgets.BeginScrollView(inner, ref roomsScroll, viewRect);
            float y = 0f;
            int removeAt = -1;
            const float bw = 22f;
            var map = pawn?.Map;
            for (int i = 0; i < roomAnchors.Count; i++)
            {
                var cell = roomAnchors[i];
                var row = new Rect(0f, y, viewRect.width, PickedRowH);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);

                // Far-right ✕ (remove) — not for the clicked target's own room (always part of the selection).
                var rmRect = new Rect(row.xMax - bw, y + 2f, bw, PickedRowH - 4f);
                if (i > 0 && Widgets.ButtonText(rmRect, "✕"))
                    removeAt = i;

                // Rooms are resolved from the anchor cell LIVE (regions may have rebuilt since the pick), so the
                // label always reflects what the route would actually cover right now.
                var room = map != null && cell.InBounds(map) ? cell.GetRoom(map) : null;
                string roomLabel = room == null
                    ? "HaulersDream.PlanRoute.RoomGone".Translate().ToString()
                    : room.GetRoomRoleLabel().CapitalizeFirst();
                var labelRect = new Rect(row.x + 4f, y, rmRect.x - row.x - 6f, PickedRowH);
                var prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, $"{roomLabel} ({cell.x}, {cell.z})".Truncate(labelRect.width));
                Text.Anchor = prevAnchor;
                y += PickedRowH;
            }
            Widgets.EndScrollView();
            if (removeAt > 0)
                roomAnchors.RemoveAt(removeAt);
        }

        private void ToggleRoomPicking()
        {
            if (pickingRoom)
            {
                StopRoomPicking();
            }
            else
            {
                if (pickingMode)
                    TogglePicking(); // one targeter — arming the room picker disarms the must-include picker
                if (pickingSecondary)
                    StopSecondaryPicking();
                pickingRoom = true;
                rearmRequested = true; // armed by DoWindowContents' targeter check
            }
        }

        private void StopRoomPicking()
        {
            pickingRoom = false;
            rearmRequested = false;
            if (Find.Targeter.IsTargeting)
                Find.Targeter.StopTargeting();
        }

        private void BeginRoomPicking()
        {
            var map = pawn?.Map;
            var tp = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = true, // rooms are picked by clicking any cell inside them
                validator = ti => map != null && ti.Cell.IsValid && ti.Cell.InBounds(map)
                    && !map.fogGrid.IsFogged(ti.Cell) && ti.Cell.GetRoom(map) != null,
            };
            // caster:null — see BeginPicking (the targeter must not auto-cancel when the pawn isn't selected).
            Find.Targeter.BeginTargeting(tp, OnRoomPicked, (Pawn)null);
        }

        private void OnRoomPicked(LocalTargetInfo target)
        {
            // The Targeter fires once then stops; request a re-arm so DoWindowContents keeps the picker armed
            // (multi-pick) — set on EVERY consumed pick, even a duplicate/invalid one (ESC/right-click never
            // invokes this action, so a stop WITHOUT the request still reads as the player cancelling).
            rearmRequested = true;
            var map = pawn?.Map;
            if (map == null || !target.Cell.IsValid || !target.Cell.InBounds(map))
                return;
            var room = target.Cell.GetRoom(map);
            if (room == null)
                return;
            // Dedupe by the ROOM the cell resolves to (not the cell): clicking two corners of the same bedroom
            // must not list it twice. Room identity is checked live against every existing anchor.
            for (int i = 0; i < roomAnchors.Count; i++)
            {
                var existing = roomAnchors[i].InBounds(map) ? roomAnchors[i].GetRoom(map) : null;
                if (existing != null && existing.ID == room.ID)
                    return;
            }
            roomAnchors.Add(target.Cell);
        }

        // Amount slider (Radius + Vein): 1 .. amountMax, then one more step = "All".
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
            // Selection inputs (mode/amount/radius/smart + the harvest gate) drive the EXPENSIVE pathfinding —
            // recompute the legs only when they change. The Chained max-travel slider only re-truncates the
            // cached legs, which is cheap, so it updates LIVE on every drag step without a pathfinding storm.
            int pickHash = picked.Count;
            for (int i = 0; i < picked.Count; i++)
                pickHash = pickHash * 31 + (picked[i]?.thingIDNumber ?? 0);
            int roomHash = roomAnchors.Count;
            for (int i = 0; i < roomAnchors.Count; i++)
                roomHash = roomHash * 31 + roomAnchors[i].x * 4099 + roomAnchors[i].z;
            int defHash = secondaryDefs.Count;
            for (int i = 0; i < secondaryDefs.Count; i++)
                defHash = defHash * 31 + (secondaryDefs[i]?.shortHash ?? 0);
            string selSig = $"{(int)mode}|{EffectiveAmount()}|{radius}|{(smart ? 1 : 0)}|{(allowHarvest ? 1 : 0)}|{growthThreshold}|pk{pickHash}|rm{roomHash}|sd{defHash}|sm{(int)selectionMethod}|db{(int)distanceBasis}";
            // DEBOUNCE the expensive recompute: ComputeLegs runs up to ~100 pathfinds (more with walking-path),
            // and the amount/radius/growth SLIDERS step selSig on every integer of a drag — an uncontrolled drag
            // would fire a pathfinding storm per frame. Recompute only once the signature has held still for a
            // few frames; the previous plan stays on screen during the ~0.15 s in between.
            bool legsChanged = false;
            if (selSig != legsSig)
            {
                if (selSig == pendingSelSig)
                    pendingSelSigFrames++;
                else
                {
                    pendingSelSig = selSig;
                    pendingSelSigFrames = 0;
                }
                if (pendingSelSigFrames >= SelSigDebounceFrames || cachedLegs == null)
                {
                    legsSig = selSig;
                    cachedLegs = RoutePlanner.ComputeLegs(pawn, clicked, kind, mode, EffectiveAmount(), radius, smart, allowHarvest, growthThreshold, picked, selectionMethod, distanceBasis, roomAnchors, secondaryDefs);
                    legsChanged = true;
                }
            }
            // The budget only varies for Chained (Radius/Vein have no max-travel); key the re-truncate on it. The
            // pinned start/end stops also only affect the (cheap) ORDER step, so re-truncate when they change too.
            int budgetKey = mode == RouteMode.Chained ? maxTravel : int.MaxValue;
            string pinSig = $"{(startNode?.thingIDNumber ?? -1)}|{(endNode?.thingIDNumber ?? -1)}";
            if (legsChanged || budgetKey != lastBudget || pinSig != lastPinSig || cachedPlan == null)
            {
                lastBudget = budgetKey;
                lastPinSig = pinSig;
                cachedPlan = RoutePlanner.Truncate(cachedLegs, MaxDistance(), startNode, endNode,
                    HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax);
                UpdatePreview();
                // Diagnostic breakdown (enable "verbose logging" in mod settings to see it): why a target is or
                // isn't in the route — selected vs reachable vs kept-at-budget, and which cap fired. Lets a
                // "missing bushes" report be pinned to growth-gate / unreachable / budget-trim in one in-game look.
                HDLog.Dbg($"PlanRoute[{BuildTag}] {kind?.gerund}: mode={mode} selected={cachedPlan.selectedCount} " +
                    $"reachable={cachedLegs?.Reachable ?? 0} kept={cachedPlan.stops.Count} budget={MaxDistance():0} " +
                    $"cappedDistance={cachedPlan.cappedByDistance} cappedReach={cachedPlan.cappedByReach}");
            }
        }

        private void UpdatePreview()
        {
            if (preview == null)
                return;
            // Radius mode shows the affected circle around the clicked target.
            IntVec3? circleCenter = mode == RouteMode.Radius ? clicked.Position : (IntVec3?)null;
            if (cachedPlan == null || cachedPlan.stops.Count == 0)
            {
                preview.SetPreview(pawn, null, circleCenter, radius);
                return;
            }
            var cells = new List<IntVec3>(cachedPlan.stops.Count);
            for (int i = 0; i < cachedPlan.stops.Count; i++)
                if (cachedPlan.stops[i] != null)
                    cells.Add(cachedPlan.stops[i].Position);
            preview.SetPreview(pawn, cells, circleCenter, radius, cachedPlan.storageCell);
        }

        // The Amount slider value resolved for the planner (the "All" step → unbounded sentinel).
        private int EffectiveAmount() => amount > amountMax ? RouteSelection.AllAmount : amount;

        // Max-travel budget applies to Chained only (the span between the first and last stop); the other modes
        // are bounded by their Amount, so they pass "no limit".
        private float MaxDistance() => mode == RouteMode.Chained
            ? (maxTravel >= NoLimitStep ? float.PositiveInfinity : maxTravel)
            : float.PositiveInfinity;

        // ── Must-include picker ──────────────────────────────────────────────────────────────────────────

        private void DoPickerSection(Listing_Standard l)
        {
            if (l.ButtonText(pickingMode
                    ? "HaulersDream.PlanRoute.PickStop".Translate()
                    : "HaulersDream.PlanRoute.PickStart".Translate()))
                TogglePicking();
            l.Label("HaulersDream.PlanRoute.PickHeader".Translate(picked.Count));
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            l.Label("HaulersDream.PlanRoute.PickRoleHint".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            DrawPickedList(l.GetRect(3f * PickedRowH)); // ~3 rows visible, scroll for more
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
            const float bw = 22f; // S / E / ✕ button width
            for (int i = 0; i < picked.Count; i++)
            {
                var t = picked[i];
                var row = new Rect(0f, y, viewRect.width, PickedRowH);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);
                bool isAnchor = t == clicked;
                bool isStart = t != null && t == startNode;
                bool isEnd = t != null && t == endNode;

                // Far-right ✕ (remove) — not for the clicked anchor (it's always part of the route). Its slot is
                // reserved even for the anchor so the S/E toggles line up across every row.
                var rmRect = new Rect(row.xMax - bw, y + 2f, bw, PickedRowH - 4f);
                if (!isAnchor && Widgets.ButtonText(rmRect, "✕"))
                    removeAt = i;

                // S / E toggles: tag this stop as the route's start or end (visited first / last). Mutually
                // exclusive per stop, and one stop can't be both — tagging clears any conflicting tag.
                var eRect = new Rect(rmRect.x - bw - 2f, y + 2f, bw, PickedRowH - 4f);
                var sRect = new Rect(eRect.x - bw - 2f, y + 2f, bw, PickedRowH - 4f);
                GUI.color = isStart ? RoleActive : Color.white;
                if (Widgets.ButtonText(sRect, "HaulersDream.PlanRoute.PickStartBtn".Translate()))
                {
                    startNode = isStart ? null : t;
                    if (startNode != null && endNode == t) endNode = null;
                }
                GUI.color = isEnd ? RoleActive : Color.white;
                if (Widgets.ButtonText(eRect, "HaulersDream.PlanRoute.PickEndBtn".Translate()))
                {
                    endNode = isEnd ? null : t;
                    if (endNode != null && startNode == t) startNode = null;
                }
                GUI.color = Color.white;
                TooltipHandler.TipRegion(sRect, "HaulersDream.PlanRoute.PickStartTip".Translate());
                TooltipHandler.TipRegion(eRect, "HaulersDream.PlanRoute.PickEndTip".Translate());

                var labelRect = new Rect(row.x + 4f, y, sRect.x - row.x - 6f, PickedRowH);
                string role = isStart ? "  " + "HaulersDream.PlanRoute.PickStartBadge".Translate()
                            : isEnd ? "  " + "HaulersDream.PlanRoute.PickEndBadge".Translate() : "";
                string label = t != null ? $"{t.LabelCap.ToString()} ({t.Position.x}, {t.Position.z}){role}" : "?";
                var prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label.Truncate(labelRect.width));
                Text.Anchor = prevAnchor;
                y += PickedRowH;
            }
            Widgets.EndScrollView();
            if (removeAt >= 0)
            {
                var removed = picked[removeAt];
                if (removed == startNode) startNode = null; // don't leave a pin dangling on a removed stop
                if (removed == endNode) endNode = null;
                picked.RemoveAt(removeAt);
            }
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
                if (pickingRoom)
                    StopRoomPicking(); // one targeter — arming the must-include picker disarms the room picker
                if (pickingSecondary)
                    StopSecondaryPicking();
                pickingMode = true;
                rearmRequested = true; // armed by DoWindowContents' targeter check
            }
        }

        private void BeginPicking()
        {
            var tp = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = true,  // mineables (ThingCategory.Building)
                canTargetPlants = true,     // harvest / cut plants
                // canTargetItems is vanilla's CATCH-ALL category branch: blueprints (ThingCategory.Ethereal)
                // and filth (ThingCategory.Filth) pass ONLY through it — without it the picker silently
                // no-ops for construction and cleaning kinds. The validator below already constrains picks
                // to same-kind route targets, so this cannot over-admit.
                canTargetItems = true,
                canTargetLocations = false,
                mapObjectTargetsMustBeAutoAttackable = false, // mineables aren't "auto-attackable" — don't reject them
                validator = ti => ti.Thing != null
                    && RouteSelection.IsValidRouteTarget(pawn, clicked, kind, ti.Thing, secondaryDefs)
                    && pawn.CanReach(ti.Thing, PathEndMode.Touch, Danger.Deadly),
            };
            // caster:null so the targeter doesn't auto-cancel when the pawn isn't selected (Targeter.ConfirmStillValid).
            // The (Pawn)null cast pins the (params, action, caster, …) overload (bare nulls are ambiguous with the
            // highlightAction/validator overload).
            Find.Targeter.BeginTargeting(tp, OnPicked, (Pawn)null);
        }

        private void OnPicked(LocalTargetInfo target)
        {
            var thing = target.Thing;
            if (thing != null && !picked.Contains(thing)
                && RouteSelection.IsValidRouteTarget(pawn, clicked, kind, thing, secondaryDefs))
                picked.Add(thing);
            // The Targeter fires once then stops; request a re-arm so DoWindowContents keeps the picker
            // armed for the next pick (multi-pick) — a stop WITHOUT this request is the player cancelling.
            rearmRequested = true;
        }

        // ── Secondary target-type picker (issue #96 multi-target) ──────────────────────────────────────────

        private void DoSecondarySection(Listing_Standard l)
        {
            if (l.ButtonText(pickingSecondary
                    ? "HaulersDream.PlanRoute.AddTargetStop".Translate()
                    : "HaulersDream.PlanRoute.AddTarget".Translate()))
                ToggleSecondaryPicking();
            l.Label("HaulersDream.PlanRoute.TargetTypesHeader".Translate(secondaryDefs.Count + 1));
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            l.Label("HaulersDream.PlanRoute.AddTargetHint".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            DrawSecondaryList(l.GetRect(3f * PickedRowH)); // ~3 rows visible, scroll for more
        }

        private void DrawSecondaryList(Rect outRect)
        {
            Widgets.DrawMenuSection(outRect);
            var inner = outRect.ContractedBy(2f);
            int rows = secondaryDefs.Count + 1; // row 0 is the primary (clicked) def
            float viewH = rows * PickedRowH;
            var viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(viewH, inner.height));
            Widgets.BeginScrollView(inner, ref secondaryScroll, viewRect);
            float y = 0f;
            int removeAt = -1;
            const float bw = 22f;
            for (int i = 0; i < rows; i++)
            {
                var def = i == 0 ? clicked?.def : secondaryDefs[i - 1];
                var row = new Rect(0f, y, viewRect.width, PickedRowH);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);
                // Far-right ✕ (remove) — only for secondaries; the primary (clicked) def is always in the route.
                var rmRect = new Rect(row.xMax - bw, y + 2f, bw, PickedRowH - 4f);
                if (i > 0 && Widgets.ButtonText(rmRect, "✕"))
                    removeAt = i - 1;
                string label = def?.LabelCap.ToString() ?? "?";
                if (i == 0)
                    label += "  " + "HaulersDream.PlanRoute.PrimaryBadge".Translate();
                var labelRect = new Rect(row.x + 4f, y, rmRect.x - row.x - 6f, PickedRowH);
                var prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label.Truncate(labelRect.width));
                Text.Anchor = prevAnchor;
                y += PickedRowH;
            }
            Widgets.EndScrollView();
            if (removeAt >= 0)
                secondaryDefs.RemoveAt(removeAt);
        }

        private void ToggleSecondaryPicking()
        {
            if (pickingSecondary)
            {
                StopSecondaryPicking();
            }
            else
            {
                if (pickingMode) TogglePicking();   // one targeter — free it from the must-include picker…
                if (pickingRoom) StopRoomPicking(); // …or the room picker
                pickingSecondary = true;
                rearmRequested = true; // armed by DoWindowContents' targeter check
            }
        }

        private void StopSecondaryPicking()
        {
            pickingSecondary = false;
            rearmRequested = false;
            if (Find.Targeter.IsTargeting)
                Find.Targeter.StopTargeting();
        }

        private void BeginSecondaryPicking()
        {
            var tp = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = true,  // mineables (ThingCategory.Building)
                canTargetPlants = true,     // other harvest / cut plants
                canTargetItems = true,      // vanilla catch-all category; the validator constrains to same-work targets
                canTargetLocations = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = ti => ti.Thing != null && IsSecondaryPick(ti.Thing),
            };
            // caster:null — see BeginPicking (the targeter must not auto-cancel when the pawn isn't selected).
            Find.Targeter.BeginTargeting(tp, OnSecondaryPicked, (Pawn)null);
        }

        // A valid secondary "target type": a DIFFERENT def than the anchor (and not already added) whose OWN work
        // resolves to the SAME work kind — same designation AND scope. So berries + rice both resolve to "harvest
        // plants" and merge, but a tree to CUT (a different designation) can never join a harvest route.
        private bool IsSecondaryPick(Thing t)
        {
            if (t?.def == null || t.def == clicked?.def || secondaryDefs.Contains(t.def))
                return false;
            var k2 = WorkKindResolver.Resolve(pawn, t);
            return k2 != null && kind != null && k2.designation == kind.designation && k2.scope == kind.scope;
        }

        private void OnSecondaryPicked(LocalTargetInfo target)
        {
            var t = target.Thing;
            if (t?.def != null && IsSecondaryPick(t))
                secondaryDefs.Add(t.def);
            // The Targeter fires once then stops; request a re-arm so DoWindowContents keeps the picker armed for the
            // next type (multi-pick) — a stop WITHOUT this request is the player cancelling.
            rearmRequested = true;
        }

        /// <summary>
        /// One-click "remembered" execution: replay the EXPLICIT template the player saved for this target type with
        /// the "Remember" button — stored per <see cref="ThingDef"/> in <see cref="HaulersDreamSettings.rememberedRoutesByDef"/>
        /// — WITHOUT opening the dialog. Reconstructs the exact synced commit <see cref="Execute"/> would build,
        /// seeding the transient per-session inputs (must-include list, pinned start/end, secondary target defs) to
        /// their defaults, since only the persisted knobs are remembered. <paramref name="replace"/> follows the
        /// vanilla queued-order convention (plain click REPLACES current work; the Queue Order key / Shift APPENDS) —
        /// the caller derives it from <see cref="KeyBindingDefOf.QueueOrder"/> at click time. Returns false (and does
        /// nothing) when there is no saved template for the clicked def, so the caller opens the dialog instead.
        /// </summary>
        public static bool ExecuteRemembered(Pawn pawn, Thing clicked, RouteWorkKind kind, bool replace)
        {
            if (pawn?.Map == null || clicked?.def == null || kind == null)
                return false;
            var s = HaulersDreamMod.Settings;
            var prefs = s?.GetRememberedRoute(clicked.def.defName);
            if (prefs == null)
                return false; // no saved template — the caller opens the dialog to establish one

            // Coerce the remembered mode to one this kind actually offers (a stale pref, or the global default on a
            // kind that doesn't offer it) — exactly like the constructor does.
            var allowedModes = RouteSelection.AllowedModes(kind, clicked, pawn.Map);
            var mode = allowedModes.Contains(prefs.mode) ? prefs.mode : allowedModes[0];

            int amountMax = Mathf.Clamp(s.routeMaxAmount, 5, RouteSelection.HardCap);
            int effAmount = (prefs.amount < 1 || prefs.amount > amountMax) ? RouteSelection.AllAmount : prefs.amount;

            int restoredMaxTravel = prefs.maxTravel < MaxTravelMin ? NoLimitStep : Mathf.Clamp(prefs.maxTravel, MaxTravelMin, NoLimitStep);
            float maxDistance = mode == RouteMode.Chained
                ? (restoredMaxTravel >= NoLimitStep ? float.PositiveInfinity : restoredMaxTravel)
                : float.PositiveInfinity;

            int radius = Mathf.Clamp(prefs.radius, 2, RadiusMax);

            int harvestMinPct = kind.designation == DesignationDefOf.HarvestPlant
                ? Mathf.Clamp(Mathf.RoundToInt((clicked.def?.plant?.harvestMinGrowth ?? 0f) * 100f), 0, 99) : 0;
            int growthThreshold = Mathf.Clamp(prefs.growthThreshold, harvestMinPct, 100);

            // Transient per-session inputs default to "just the clicked anchor / no pins / no secondary types".
            var picked = new List<Thing> { clicked };
            var roomAnchors = new List<IntVec3> { clicked.Position };
            bool isConstruction = kind.scanner is WorkGiver_ConstructDeliverResourcesToBlueprints;

            RouteExecutor.ExecuteRouteSynced(pawn, clicked, WorkKindResolver.WorkKindId(kind), mode, effAmount, radius,
                maxDistance, prefs.smart, prefs.allowHarvest, growthThreshold, replace, picked,
                prefs.selectionMethod, prefs.distanceBasis, s.routeOrderExactMax, null, null, isConstruction,
                roomAnchors, null);
            return true;
        }

        private void Execute(bool replace)
        {
            // The plan is kept current by RefreshPlanIfNeeded each frame; ensure it exists before queueing. (The
            // preview/planning above stays local to this client — only the COMMIT below is synced.)
            if (cachedPlan == null)
                RefreshPlanIfNeeded();

            // Route the COMMIT through the synced entry point, NOT RouteExecutor.Execute directly: Execute mutates
            // synced world/save state (designations, the pawn's job queue, the scribed vein tracker) that RimWorld
            // Multiplayer does not auto-sync, so calling it here would apply only on the clicking client → desync.
            // ExecuteRouteSynced is a [SyncMethod] (a command replayed identically on every client; inert in single-
            // player). It takes only MP-serializable args, so the un-serializable RouteWorkKind travels as its stable
            // id (re-resolved per client) and the cached preview RoutePlan is NOT shipped — every client recomputes
            // the plan deterministically (precomputed: null). picked/roomAnchors are already concrete List<> (the
            // wire-serializable form); pass them directly.
            RouteExecutor.ExecuteRouteSynced(pawn, clicked, WorkKindResolver.WorkKindId(kind), mode, EffectiveAmount(),
                radius, MaxDistance(), smart, allowHarvest, growthThreshold, replace, picked, selectionMethod,
                distanceBasis, HaulersDreamMod.Settings?.routeOrderExactMax ?? RouteOrderPolicy.ExactMax,
                startNode, endNode, IsConstruction && alsoBuild, roomAnchors, secondaryDefs);
        }

        // Construction routes offer two behaviours: HAUL-ONLY (fill the sites with materials — others can build)
        // or HAUL+BUILD (deliver to a site, build it, move to the next — wood stays in inventory). The pawn never
        // hard-reserves the sites (enroute claims only), so other haulers keep supplying the rest in parallel.
        private bool IsConstruction => kind?.scanner is WorkGiver_ConstructDeliverResourcesToBlueprints;
        private bool alsoBuild = true;

        // The "Remember" button's LOCAL rect + hover, captured each frame so ExtraOnGUI can draw a screen-space
        // pointer arrow from it to the bottom-right interface toggle (drawing the arrow inside DoWindowContents is no
        // good — the window's GUI group clips it at the window edge, well short of the toggle).
        private Rect rememberBtnLocalRect;
        private bool rememberBtnHovered;

        // Draw the bottom "Remember" button: "Already remembered" (faint, disabled) when the current settings already
        // equal this type's saved template, otherwise a live button that saves them as the template.
        private void DoRememberButton(Rect row)
        {
            var s = HaulersDreamMod.Settings;
            var saved = s?.GetRememberedRoute(clicked?.def?.defName);
            bool alreadyRemembered = saved != null && saved.ValueEquals(CurrentPrefs());
            Patch_PlaySettings.DrawRememberButton(row, alreadyRemembered, () =>
            {
                if (s == null || clicked?.def == null)
                    return;
                s.SetRememberedRoute(clicked.def.defName, CurrentPrefs());
                HaulersDreamMod.Instance?.WriteSettings();
                Messages.Message("HaulersDream.PlanRoute.RememberedConfirm".Translate(clicked.def.LabelCap),
                    MessageTypeDefOf.TaskCompletion, historical: false);
            });
            rememberBtnLocalRect = row;
            rememberBtnHovered = Mouse.IsOver(row);
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (rememberBtnHovered)
                Patch_PlaySettings.DrawRememberArrow(windowRect, rememberBtnLocalRect);
        }

        /// <summary>Snapshot the dialog's current knobs as a portable <see cref="RouteDialogPrefs"/> (the "no limit"
        /// max-travel and "All" amount stored as -1 sentinels). Shared by <see cref="PostClose"/> (the per-instance
        /// auto-restore into <see cref="HaulersDreamSettings.routePrefsByDef"/>) and the "Remember" button (the
        /// explicit one-click template into <see cref="HaulersDreamSettings.rememberedRoutesByDef"/>).</summary>
        private RouteDialogPrefs CurrentPrefs() => new RouteDialogPrefs
        {
            mode = mode,
            maxTravel = maxTravel >= NoLimitStep ? -1 : maxTravel, // store "no limit" as -1 (portable across NoLimitStep)
            radius = radius,
            amount = amount > amountMax ? -1 : amount,             // store "All" as -1 (portable across the slider's max)
            smart = smart,
            allowHarvest = allowHarvest,
            growthThreshold = growthThreshold,
            selectionMethod = selectionMethod,
            distanceBasis = distanceBasis,
        };

        public override void PostClose()
        {
            base.PostClose();
            if ((pickingMode || pickingRoom || pickingSecondary) && Find.Targeter.IsTargeting)
                Find.Targeter.StopTargeting(); // don't leave a picker armed after the dialog closes
            preview?.ClearPreview();

            // Auto-restore layer: remember these options for this kind of target so they don't need re-applying next
            // time the DIALOG opens (this session and across restarts). Keyed by the clicked thing's ThingDef. This is
            // NOT the "(remembered)" one-click template — that is written only by the Remember button (CurrentPrefs →
            // SetRememberedRoute); this write happens on every close and just repopulates the window.
            var s = HaulersDreamMod.Settings;
            if (s != null && clicked?.def != null)
            {
                s.SetRoutePrefs(clicked.def.defName, CurrentPrefs());
                HaulersDreamMod.Instance?.WriteSettings();
            }
        }
    }
}
