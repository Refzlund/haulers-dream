using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    public partial class HaulersDreamSettings
    {
        // ===== enum -> readout label helpers (reused for segmented selectors + slider readouts) =====
        private static string OverloadLevelLabel(int lv)
        {
            lv = OverloadTuning.ClampLevel(lv);
            if (lv == 0) return "HaulersDream.Overload.Free".Translate();
            if (lv >= OverloadTuning.OffLevel) return "HaulersDream.Overload.Off".Translate();
            if (lv == OverloadTuning.FairLevel) return "HaulersDream.Overload.Fair".Translate();
            return lv < OverloadTuning.FairLevel
                ? "HaulersDream.Overload.Eager".Translate(lv)
                : "HaulersDream.Overload.Cautious".Translate(lv);
        }

        // ===== 3-pane settings window (icon nav · options · info panel), styled after Camera+ =====
        // Replaces the old tabbed Listing_Standard window. The scroll height is the TRUE measured content
        // height (SettingsCtx.CurY), and sub-options are GREYED (not hidden) when their master is off, so the
        // page height is constant for a given settings state — no more scroll-height collapse / clipped rows.
        private enum SettingsCat
        {
            Features,     // master on/off hub for every incorporated feature family (cards)
            Hauling,      // carry limit, overload, pickup, bulk haul + triggers, sweep, haul-to-stack, keep-working
            Unloading,    // auto-unload + before-downtime, surplus, item rules, ordering, opportunistic, gizmo
            BuildCraft,   // build-from-inventory, craft-from-carried, construct delivery/tether, meals, spoiling-first
            BulkLoading,  // pack animals, transporters/shuttles, portals, refuel, vehicles, advanced loading
            Routing,      // en-route pickup, consumer-aware routing, storage filters
            Yields,       // which yields to scoop, slaughter/hunt haul, stripping + tainted policy
            Who,          // pawn eligibility + work-incapability overrides
            Planners,     // route + crafting planner tuning
            Advanced,     // unload timing, safety net, dev tools
        }

        private struct CatDef
        {
            public SettingsCat cat;
            public string icon;      // texture under Textures/HaulersDream/Settings/
            public string nameKey;
            public string helpKey;
            public CatDef(SettingsCat cat, string icon, string nameKey, string helpKey)
            { this.cat = cat; this.icon = icon; this.nameKey = nameKey; this.helpKey = helpKey; }
        }

        private static readonly CatDef[] cats =
        {
            new CatDef(SettingsCat.Features,    "Features",    "HaulersDream.Cat.Features",    "HaulersDream.Cat.Features.Help"),
            new CatDef(SettingsCat.Hauling,     "Hauling",     "HaulersDream.Cat.Hauling",     "HaulersDream.Cat.Hauling.Help"),
            new CatDef(SettingsCat.Unloading,   "Unloading",   "HaulersDream.Cat.Unloading",   "HaulersDream.Cat.Unloading.Help"),
            new CatDef(SettingsCat.BuildCraft,  "BuildCraft",  "HaulersDream.Cat.BuildCraft",  "HaulersDream.Cat.BuildCraft.Help"),
            new CatDef(SettingsCat.BulkLoading, "BulkLoading", "HaulersDream.Cat.BulkLoading", "HaulersDream.Cat.BulkLoading.Help"),
            new CatDef(SettingsCat.Routing,     "Routing",     "HaulersDream.Cat.Routing",     "HaulersDream.Cat.Routing.Help"),
            new CatDef(SettingsCat.Yields,      "Yields",      "HaulersDream.Cat.Yields",      "HaulersDream.Cat.Yields.Help"),
            new CatDef(SettingsCat.Who,         "Who",         "HaulersDream.Cat.Who",         "HaulersDream.Cat.Who.Help"),
            new CatDef(SettingsCat.Planners,    "Planners",    "HaulersDream.Cat.Planners",    "HaulersDream.Cat.Planners.Help"),
            new CatDef(SettingsCat.Advanced,    "Advanced",    "HaulersDream.Cat.Advanced",    "HaulersDream.Cat.Advanced.Help"),
        };

        private static SettingsCat currentCat = SettingsCat.Features;
        private static Vector2 contentScroll;
        private static Vector2 helpScroll;
        // Per-category measured content height (seeded large so the first view of a page never under-sizes the
        // scroll viewport; replaced by the true measured height after the first draw and then stable).
        private static readonly float[] catHeight =
            { 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f };
        private static readonly Dictionary<SettingsCat, Texture2D> iconCache = new Dictionary<SettingsCat, Texture2D>();

        private static Texture2D IconFor(CatDef cd)
        {
            if (!iconCache.TryGetValue(cd.cat, out var tex))
            {
                tex = ContentFinder<Texture2D>.Get("HaulersDream/Settings/" + cd.icon, reportFailure: false);
                iconCache[cd.cat] = tex;
            }
            return tex;
        }

        // The settings-header logo (HAULER'S DREAM + character), drawn in place of the vanilla mod-name text.
        private static Texture2D headerTexCache;
        private static bool headerTexResolved;
        private static Texture2D HeaderTex
        {
            get
            {
                if (!headerTexResolved)
                {
                    headerTexCache = ContentFinder<Texture2D>.Get("HaulersDream/Settings/Header", reportFailure: false);
                    headerTexResolved = true;
                }
                return headerTexCache;
            }
        }

        public void DoWindowContents(Rect rect)
        {
            HDSettingsUI.ResetHover();
            // Take over the window chrome: the vanilla Dialog_ModSettings sets a corner X (no padding) + a redundant
            // bottom "Close" button. We suppress both (settings still save on PreClose however the window closes) and
            // draw our own padded X. doCloseButton is read AFTER this method (effective now); doCloseX is read before
            // (effective next frame — a one-frame flash at most). Also keep the large window draggable.
            var win = Find.WindowStack?.currentlyDrawnWindow;
            if (win != null)
            {
                win.draggable = true;
                win.doCloseButton = false;
                win.doCloseX = false;
            }

            // Window content origin is (0,0): the vanilla mod-name label was drawn there and we were handed a rect
            // starting at y=40. OVERDRAW that strip with the logo + profile selector, and reclaim the bottom band the
            // (now-removed) close button occupied.
            float fullW = rect.width;
            const float bandH = 60f;
            var band = new Rect(0f, 0f, fullW, bandH);
            Widgets.DrawBoxSolid(band, Widgets.WindowBGFillColor); // hide the vanilla "Hauler's Dream" text label

            var tex = HeaderTex;
            float logoH = 48f;
            float logoW = tex != null ? logoH * (tex.width / (float)tex.height) : 280f;
            var logoRect = new Rect(2f, (bandH - logoH) / 2f, logoW, logoH);
            if (tex != null)
            {
                GUI.DrawTexture(logoRect, tex, ScaleMode.ScaleToFit);
            }
            else
            {
                var pf = Text.Font;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(4f, 8f, 360f, 40f), "Hauler's Dream");
                Text.Font = pf;
            }

            // Custom padded close X (top-right). CloseButtonFor draws an 18px X at (rect.xMax-22, rect.y+4).
            if (Widgets.CloseButtonFor(new Rect(0f, 6f, fullW - 6f, 40f)))
                win?.Close();

            // Profile selector — to the right of the logo, clear of the X.
            float profX = logoRect.xMax + 16f;
            float profRight = fullW - 40f;
            float profW = Mathf.Min(300f, profRight - profX);
            if (profW >= 120f)
            {
                var profRect = new Rect(profX, (bandH - 30f) / 2f, profW, 30f);
                if (Widgets.ButtonText(profRect, CurrentProfileLabel))
                    OpenProfileMenu();
                TooltipHandler.TipRegion(profRect, "HaulersDream.Profile.SelectorTip".Translate());
            }

            // --- body: three responsive columns; reclaim the removed close-button strip at the bottom ---
            float bodyTop = bandH + 6f;
            float bodyBottom = rect.yMax + 32f; // the close button sat below rect.yMax; reclaim that space
            var body = new Rect(0f, bodyTop, fullW, bodyBottom - bodyTop);
            const float gap = 12f;
            float navW = Mathf.Clamp(body.width * 0.20f, 150f, 192f);
            float helpW = Mathf.Clamp(body.width * 0.26f, 200f, 290f);
            var navRect = new Rect(body.x, body.y, navW, body.height);
            var contentRect = new Rect(navRect.xMax + gap, body.y, body.width - navW - helpW - 2f * gap, body.height);
            var helpRect = new Rect(contentRect.xMax + gap, body.y, helpW, body.height);

            DrawNav(navRect);
            DrawContent(contentRect);
            DrawHelp(helpRect);
        }

        // The profile dropdown: apply a saved profile, reset to the built-in Default, save changes to the active
        // profile, save the current state as a new profile, or delete the active one.
        private void OpenProfileMenu()
        {
            var opts = new List<FloatMenuOption>();
            if (savedProfiles != null)
            {
                foreach (var profile in savedProfiles)
                {
                    var p = profile; // capture for the closure
                    string mark = activeProfileName == p.name ? "  ✓" : "";
                    opts.Add(new FloatMenuOption(p.name + mark, () => ApplyProfile(p)));
                }
            }
            // The built-in Default = reset to defaults (this profile can never be modified).
            opts.Add(new FloatMenuOption("HaulersDream.Profile.Default".Translate(), ApplyDefaultProfile));

            var active = ActiveProfile;
            if (active != null && IsDirty)
                opts.Add(new FloatMenuOption("HaulersDream.Profile.SaveChanges".Translate(active.name), SaveActiveProfile));

            // "Create new profile…" from Default/Custom; "Save as new profile…" once on a named profile.
            opts.Add(new FloatMenuOption(
                (active == null ? "HaulersDream.Profile.CreateNew" : "HaulersDream.Profile.SaveAsNew").Translate(),
                () => Find.WindowStack.Add(new Dialog_NameProfile(name => SaveAsNewProfile(name)))));

            // Copy the current settings as a portable string; paste one to create a profile.
            opts.Add(new FloatMenuOption("HaulersDream.Profile.Copy".Translate(),
                () => Find.WindowStack.Add(new Dialog_CopyProfile(ExportProfileToString(this, activeProfileName ?? "")))));
            opts.Add(new FloatMenuOption("HaulersDream.Profile.Paste".Translate(),
                () => Find.WindowStack.Add(new Dialog_PasteProfile(this))));

            if (active != null)
            {
                var ap = active;
                opts.Add(new FloatMenuOption("HaulersDream.Profile.Delete".Translate(ap.name),
                    () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "HaulersDream.Profile.DeleteConfirm".Translate(ap.name), () => DeleteProfile(ap), destructive: true))));
            }

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void DrawNav(Rect rect)
        {
            // No panel background/outline (clean look); the selected/hover row highlights carry the orientation.
            var inner = rect.ContractedBy(4f);
            float y = inner.y;
            const float rowH = 40f;
            var f = Text.Font;
            var anchor = Text.Anchor;
            foreach (var cd in cats)
            {
                var row = new Rect(inner.x, y, inner.width, rowH);
                bool selected = currentCat == cd.cat;
                if (selected) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.13f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.06f));

                var icon = IconFor(cd);
                const float navIconSize = 22f;
                var iconBox = new Rect(row.x + 9f, row.y + (rowH - navIconSize) / 2f, navIconSize, navIconSize);
                if (icon != null)
                {
                    var col = GUI.color;
                    GUI.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                    GUI.DrawTexture(iconBox, icon, ScaleMode.ScaleToFit);
                    GUI.color = col;
                }

                var labelRect = new Rect(iconBox.xMax + 8f, row.y, row.xMax - iconBox.xMax - 12f, rowH);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                var lcol = GUI.color;
                if (selected) GUI.color = new Color(0.92f, 0.94f, 1f);
                Widgets.Label(labelRect, cd.nameKey.Translate());
                GUI.color = lcol;

                if (Mouse.IsOver(row))
                    HDSettingsUI.SetHelp(cd.nameKey.Translate(), cd.helpKey.Translate());
                if (Widgets.ButtonInvisible(row))
                {
                    currentCat = cd.cat;
                    contentScroll = Vector2.zero;
                }
                y += rowH + 2f;
            }
            Text.Font = f;
            Text.Anchor = anchor;
        }

        private void DrawContent(Rect rect)
        {
            int idx = (int)currentCat;
            // Reserve the scrollbar width, then lay out content a gutter short of it (same 12px gutter as between
            // the nav and the content) so rows/headers never touch the scrollbar.
            const float scrollbarW = 16f;
            const float rightGutter = 12f;
            var view = new Rect(0f, 0f, rect.width - scrollbarW, Mathf.Max(catHeight[idx], rect.height));
            Widgets.BeginScrollView(rect, ref contentScroll, view);
            var c = new SettingsCtx(view.width - rightGutter);
            c.Gap(2f);
            switch (currentCat)
            {
                case SettingsCat.Features: DrawFeaturesCat(c); break;
                case SettingsCat.Hauling: DrawHaulingCat(c); break;
                case SettingsCat.Unloading: DrawUnloadingCat(c); break;
                case SettingsCat.BuildCraft: DrawBuildCraftCat(c); break;
                case SettingsCat.BulkLoading: DrawBulkLoadingCat(c); break;
                case SettingsCat.Routing: DrawRoutingCat(c); break;
                case SettingsCat.Yields: DrawYieldsCat(c); break;
                case SettingsCat.Who: DrawWhoCat(c); break;
                case SettingsCat.Planners: DrawPlannersCat(c); break;
                case SettingsCat.Advanced: DrawAdvancedCat(c); break;
            }
            catHeight[idx] = c.CurY + 8f;
            Widgets.EndScrollView();
        }

        private void DrawHelp(Rect rect)
        {
            // No panel background/outline (clean look). Title, a coloured status line, an optional graph, and the
            // description are ALL drawn inside ONE scroll view, so nothing can clip regardless of text length.
            var inner = rect.ContractedBy(12f);
            string title = HDSettingsUI.HoverTitle ?? cats[(int)currentCat].nameKey.Translate();
            string bodyText = HDSettingsUI.HoverBody ?? cats[(int)currentCat].helpKey.Translate();
            string status = HDSettingsUI.HoverStatus;
            var graph = HDSettingsUI.HoverExtra;

            var f = Text.Font;
            var col = GUI.color;
            float w = inner.width - 16f; // reserve the scrollbar

            const float graphHeight = 132f;
            Text.Font = GameFont.Small;
            float titleH = Text.CalcHeight(title, w);
            Text.Font = GameFont.Tiny;
            float statusH = status.NullOrEmpty() ? 0f : Text.CalcHeight(status, w);
            Text.Font = GameFont.Small;
            float bodyH = bodyText.NullOrEmpty() ? 0f : Text.CalcHeight(bodyText, w);
            float graphH = graph != null ? graphHeight : 0f;

            float total = titleH
                + (statusH > 0f ? statusH + 2f : 0f)
                + 8f
                + (graphH > 0f ? graphH + 10f : 0f)
                + bodyH;
            var view = new Rect(0f, 0f, w, Mathf.Max(total, inner.height));
            Widgets.BeginScrollView(inner, ref helpScroll, view);
            float y = 0f;

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.95f, 0.86f, 0.62f); // soft accent for the title
            Widgets.Label(new Rect(0f, y, w, titleH), title);
            y += titleH;
            GUI.color = col;

            if (statusH > 0f)
            {
                y += 2f;
                Text.Font = GameFont.Tiny;
                GUI.color = HDSettingsUI.HoverStatusColor;
                Widgets.Label(new Rect(0f, y, w, statusH), status);
                y += statusH;
                GUI.color = col;
            }
            y += 8f;

            if (graphH > 0f)
            {
                graph(new Rect(0f, y, w, graphHeight));
                y += graphHeight + 10f;
            }

            if (bodyH > 0f)
            {
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, y, w, bodyH), bodyText);
            }

            Widgets.EndScrollView();
            GUI.color = col;
            Text.Font = f;
        }

        // A small line graph of move-speed multiplier (y) vs carry weight (x, % of max capacity) for the current
        // smart-overload level — the EXACT curve OverloadTuning.SpeedFactor computes — with the carry ceiling and
        // the speed floor marked. Drawn in the info panel while the Smart-overload slider is hovered.
        private static void DrawOverloadGraph(Rect rect, int level)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.05f));
            var f = Text.Font;
            var anchor = Text.Anchor;
            var col = GUI.color;
            Text.Font = GameFont.Tiny;

            const float xMin = 1.0f, xMax = 3.0f; // 100%..300% of capacity
            const float leftAxis = 36f, bottomAxis = 15f, pad = 6f;
            var plot = new Rect(rect.x + leftAxis, rect.y + pad, rect.width - leftAxis - pad,
                rect.height - bottomAxis - pad);

            // grid: 100%-speed line (top), the speed floor, and the y axis
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(plot.x, plot.y, plot.width);
            float floorY = plot.yMax - OverloadTuning.SpeedFloor * plot.height;
            Widgets.DrawLineHorizontal(plot.x, floorY, plot.width);
            Widgets.DrawLineVertical(plot.x, plot.y, plot.height);

            // y labels (speed) + x labels (carry %)
            GUI.color = new Color(0.72f, 0.72f, 0.76f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.x, plot.y - 6f, leftAxis - 4f, 14f), "100%");
            Widgets.Label(new Rect(rect.x, floorY - 7f, leftAxis - 4f, 14f), OverloadTuning.SpeedFloor.ToStringPercent());
            Text.Anchor = TextAnchor.UpperCenter;
            for (int pct = 100; pct <= 300; pct += 100)
            {
                float gx = plot.x + (pct / 100f - xMin) / (xMax - xMin) * plot.width;
                Widgets.Label(new Rect(gx - 20f, plot.yMax + 1f, 40f, 14f), pct + "%");
            }

            // the curve (move speed vs carry ratio)
            const int N = 48;
            var prev = Vector2.zero;
            var curveCol = new Color(0.55f, 0.8f, 1f);
            for (int i = 0; i <= N; i++)
            {
                float r = xMin + (xMax - xMin) * i / N;
                float s = OverloadTuning.SpeedFactor(level, r);
                var p = new Vector2(plot.x + (r - xMin) / (xMax - xMin) * plot.width, plot.yMax - s * plot.height);
                if (i > 0) Widgets.DrawLine(prev, p, curveCol, 2f);
                prev = p;
            }

            // carry ceiling marker — where overloading stops paying off (the smart load target)
            float ceiling = OverloadTuning.MaxOverloadRatio(level);
            if (!float.IsInfinity(ceiling) && ceiling > xMin && ceiling <= xMax)
            {
                float cx = plot.x + (ceiling - xMin) / (xMax - xMin) * plot.width;
                GUI.color = new Color(0.95f, 0.86f, 0.62f);
                Widgets.DrawLineVertical(cx, plot.y, plot.height);
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(Mathf.Min(cx + 3f, plot.xMax - 56f), plot.y, 60f, 14f),
                    "HaulersDream.Setting.Overload.Ceiling".Translate());
            }

            GUI.color = col;
            Text.Anchor = anchor;
            Text.Font = f;
        }

        // ===== helpers for readouts =====
        private static string Tiles(int n) => "HaulersDream.Unit.Tiles".Translate(n);
        private static string Ticks(int n) => "HaulersDream.Unit.Ticks".Translate(n);
        private static string Hours(float h) => "HaulersDream.Unit.Hours".Translate(h.ToString("0.#"));
        private static string OffLabel => "HaulersDream.Common.Off".Translate();

        // ===================== FEATURES (master on/off hub) =====================
        private void DrawFeaturesCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Features.Intro".Translate());

            HDSettingsUI.Header(c, "HaulersDream.FeatGroup.Core".Translate());
            masterEnabled = Card(c, SettingsCat.Features, "HaulersDream.Feat.Master", masterEnabled, "HaulersDream.Setting.MasterEnabledDesc");
            markForUnload = Card(c, SettingsCat.Unloading, "HaulersDream.Feat.AutoUnload", markForUnload, "HaulersDream.Feat.AutoUnload.Blurb");
            bulkHaul = Card(c, SettingsCat.Hauling, "HaulersDream.Feat.BulkHaul", bulkHaul, "HaulersDream.Setting.BulkHaulDesc");
            sweepNearbyWhileWorking = Card(c, SettingsCat.Hauling, "HaulersDream.Feat.Sweep", sweepNearbyWhileWorking, "HaulersDream.Setting.SweepNearbyWhileWorkingDesc");

            HDSettingsUI.Header(c, "HaulersDream.FeatGroup.Loading".Translate());
            enableBulkUnloadCarriers = Card(c, SettingsCat.BulkLoading, "HaulersDream.Feat.UnloadCarriers", enableBulkUnloadCarriers, "HaulersDream.Setting.EnableBulkUnloadCarriersDesc");
            enableBulkLoadTransporters = Card(c, SettingsCat.BulkLoading, "HaulersDream.Feat.LoadTransporters", enableBulkLoadTransporters, "HaulersDream.Setting.EnableBulkLoadTransportersDesc");
            enableBulkLoadPortal = Card(c, SettingsCat.BulkLoading, "HaulersDream.Feat.LoadPortal", enableBulkLoadPortal, "HaulersDream.Setting.EnableBulkLoadPortalDesc");
            enableBulkRefuel = Card(c, SettingsCat.BulkLoading, "HaulersDream.Feat.Refuel", enableBulkRefuel, "HaulersDream.Setting.EnableBulkRefuelDesc");
            if (VehicleFrameworkCompat.IsActive)
                enableVehicleFramework = Card(c, SettingsCat.BulkLoading, "HaulersDream.Feat.Vehicles", enableVehicleFramework, "HaulersDream.Setting.EnableVehicleFrameworkDesc");

            HDSettingsUI.Header(c, "HaulersDream.FeatGroup.BuildCraft".Translate());
            buildFromInventory = Card(c, SettingsCat.BuildCraft, "HaulersDream.Feat.BuildFromInv", buildFromInventory, "HaulersDream.Setting.BuildFromInventoryDesc");
            shareForCrafting = Card(c, SettingsCat.BuildCraft, "HaulersDream.Feat.CraftFromInv", shareForCrafting, "HaulersDream.Setting.ShareForCraftingDesc");
            inventoryConstructDeliver = Card(c, SettingsCat.BuildCraft, "HaulersDream.Feat.ConstructDeliver", inventoryConstructDeliver, "HaulersDream.Setting.InventoryConstructDeliverDesc");
            mealsOnWheels = Card(c, SettingsCat.BuildCraft, "HaulersDream.Feat.Meals", mealsOnWheels, "HaulersDream.Setting.MealsOnWheelsDesc");

            HDSettingsUI.Header(c, "HaulersDream.FeatGroup.Planning".Translate());
            planRoutes = Card(c, SettingsCat.Planners, "HaulersDream.Feat.PlanRoutes", planRoutes, "HaulersDream.Setting.PlanRoutesDesc");
            planCrafting = Card(c, SettingsCat.Planners, "HaulersDream.Feat.PlanCrafting", planCrafting, "HaulersDream.Setting.PlanCraftingDesc");

            HDSettingsUI.Header(c, "HaulersDream.FeatGroup.Wyu".Translate());
            enRoutePickup = Card(c, SettingsCat.Routing, "HaulersDream.Feat.EnRoute", enRoutePickup, "HaulersDream.Setting.EnRoutePickupDesc");
            storageRouting = Card(c, SettingsCat.Routing, "HaulersDream.Feat.StorageRouting", storageRouting, "HaulersDream.Setting.StorageRoutingDesc");
            storageFiltersEnabled = Card(c, SettingsCat.Routing, "HaulersDream.Feat.StorageFilters", storageFiltersEnabled, "HaulersDream.Setting.StorageFiltersDesc");
        }

        // A feature card whose name/blurb come from "<prefix>" + ".Name"/".Blurb" and whose icon is the
        // destination category's icon (so the hub visually links to where the detailed options live).
        private bool Card(SettingsCtx c, SettingsCat iconCat, string prefix, bool value, string helpKey)
        {
            var icon = IconFor(cats[(int)iconCat]);
            return HDSettingsUI.FeatureCard(c, icon, (prefix + ".Name").Translate(), (prefix + ".Blurb").Translate(),
                value, helpKey.Translate());
        }

        // ===================== HAULING =====================
        private void DrawHaulingCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Hauling.Intro".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.CarryWeight".Translate());
            carryLimitFraction = HDSettingsUI.Slider(c, "HaulersDream.Setting.CarryLimit.Lab".Translate(),
                carryLimitFraction, CarryMath.MinFraction, CarryMath.MaxFraction,
                carryLimitFraction.ToStringPercent(), "HaulersDream.Setting.CarryLimitDesc".Translate());
            overloadLevel = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.Overload.Lab".Translate(),
                overloadLevel, 0f, OverloadTuning.MaxLevel, OverloadLevelLabel(overloadLevel),
                "HaulersDream.Setting.OverloadDesc".Translate(), enabled: !strictCarryWeight,
                graph: r => DrawOverloadGraph(r, overloadLevel)));
            strictCarryWeight = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.StrictCarryWeight".Translate(),
                strictCarryWeight, "HaulersDream.Setting.StrictCarryWeightDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Pickup".Translate());
            int pm = HDSettingsUI.Segmented(c, "HaulersDream.Setting.PickupMode.Lab".Translate(),
                pickupMode == PickupMode.DropThenHaul ? 0 : 1,
                new[] { "HaulersDream.Setting.PickupMode.Drop".Translate().ToString(), "HaulersDream.Setting.PickupMode.Direct".Translate().ToString() },
                new[] { "HaulersDream.Setting.PickupMode.DropDesc".Translate().ToString(), "HaulersDream.Setting.PickupMode.DirectDesc".Translate().ToString() },
                "HaulersDream.Setting.PickupMode.Help".Translate());
            pickupMode = pm == 0 ? PickupMode.DropThenHaul : PickupMode.DirectToInventory;
            skipHaulWhileBleeding = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.SkipHaulWhileBleeding".Translate(),
                skipHaulWhileBleeding, "HaulersDream.Setting.SkipHaulWhileBleedingDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.BulkHaul".Translate());
            bulkHaul = HDSettingsUI.Checkbox(c, "HaulersDream.Feat.BulkHaul.Name".Translate(), bulkHaul,
                "HaulersDream.Setting.BulkHaulDesc".Translate());
            int trig = HDSettingsUI.Segmented(c, "HaulersDream.Setting.BulkHaulTrigger.Lab".Translate(),
                bulkHaulTrigger == BulkHaulTrigger.SecondTasked ? 0 : 1,
                new[] { "HaulersDream.Setting.BulkHaulSecond.S".Translate().ToString(), "HaulersDream.Setting.BulkHaulAlways.S".Translate().ToString() },
                new[] { "HaulersDream.Setting.BulkHaulSecondDesc".Translate().ToString(), "HaulersDream.Setting.BulkHaulAlwaysDesc".Translate().ToString() },
                enabled: bulkHaul, indent: 24f);
            bulkHaulTrigger = trig == 0 ? BulkHaulTrigger.SecondTasked : BulkHaulTrigger.Always;
            haulNearbyOption = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulNearbyOption".Translate(),
                haulNearbyOption, "HaulersDream.Setting.HaulNearbyOptionDesc".Translate(), enabled: bulkHaul, indent: 24f);
            haulOversizedInInventory = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulOversized".Translate(),
                haulOversizedInInventory, "HaulersDream.Setting.HaulOversizedDesc".Translate(), enabled: bulkHaul, indent: 24f);
            // "Pick up X" is independent of the bulk sweep (its provider gates only on manualPickupOption).
            manualPickupOption = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ManualPickup".Translate(),
                manualPickupOption, "HaulersDream.Setting.ManualPickupDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.WhileWorking".Translate());
            sweepNearbyWhileWorking = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.SweepNearbyWhileWorking".Translate(),
                sweepNearbyWhileWorking, "HaulersDream.Setting.SweepNearbyWhileWorkingDesc".Translate());
            haulToStack = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulToStack".Translate(),
                haulToStack, "HaulersDream.Setting.HaulToStackDesc".Translate());
            keepWorkingWhenFull = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.KeepWorkingWhenFull".Translate(),
                keepWorkingWhenFull, "HaulersDream.Setting.KeepWorkingWhenFullDesc".Translate());
            keepWorkingMarginCells = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.KeepWorkingMargin.Lab".Translate(),
                keepWorkingMarginCells, 0f, 30f, Tiles(keepWorkingMarginCells),
                "HaulersDream.Setting.KeepWorkingMarginDesc".Translate(), enabled: keepWorkingWhenFull, indent: 24f));
        }

        // ===================== UNLOADING =====================
        private void DrawUnloadingCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Unloading.Intro".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.AutoUnloadTrips".Translate());
            markForUnload = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.MarkForUnload".Translate(),
                markForUnload, "HaulersDream.Feat.AutoUnload.Blurb".Translate());
            unloadBeforeSleep = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadBeforeSleep".Translate(),
                unloadBeforeSleep, "HaulersDream.Setting.UnloadBeforeSleepDesc".Translate(), enabled: markForUnload, indent: 24f);
            unloadBeforeLeisure = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadBeforeLeisure".Translate(),
                unloadBeforeLeisure, "HaulersDream.Setting.UnloadBeforeLeisureDesc".Translate(), enabled: markForUnload, indent: 24f);
            unloadBeforeEating = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadBeforeEating".Translate(),
                unloadBeforeEating, "HaulersDream.Setting.UnloadBeforeEatingDesc".Translate(), enabled: markForUnload, indent: 24f);

            HDSettingsUI.Header(c, "HaulersDream.Head.DropOff".Translate());
            opportunisticUnload = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.OpportunisticUnload".Translate(),
                opportunisticUnload, "HaulersDream.Setting.OpportunisticUnloadDesc".Translate());
            closestDestinationUnloadOrder = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ClosestDestUnloadOrder".Translate(),
                closestDestinationUnloadOrder, "HaulersDream.Setting.ClosestDestUnloadOrderDesc".Translate());
            unloadAllSurplus = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadAllSurplus".Translate(),
                unloadAllSurplus, "HaulersDream.Setting.UnloadAllSurplusDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.ItemRulesDisplay".Translate());
            int ruleCount = ItemRuleCount;
            string itemBtn = ruleCount > 0
                ? "HaulersDream.Setting.ItemUnloadButtonN".Translate(ruleCount)
                : "HaulersDream.Setting.ItemUnloadButton".Translate();
            HDSettingsUI.Button(c, itemBtn, () => Find.WindowStack.Add(new Dialog_ItemUnloadSettings(this)),
                "HaulersDream.Setting.ItemUnload.Help".Translate());
            hideGizmo = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HideGizmo".Translate(),
                hideGizmo, "HaulersDream.Setting.HideGizmo.Help".Translate());
        }

        // ===================== BUILD & CRAFT =====================
        private void DrawBuildCraftCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.BuildCraft.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Head.Construction".Translate());
            shareForBuilding = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ShareForBuilding".Translate(),
                shareForBuilding, "HaulersDream.Setting.ShareForBuilding.Help".Translate());
            buildFromInventory = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.BuildFromInventory".Translate(),
                buildFromInventory, "HaulersDream.Setting.BuildFromInventoryDesc".Translate());
            buildFromInventoryPartial = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.BuildFromInventoryPartial".Translate(),
                buildFromInventoryPartial, "HaulersDream.Setting.BuildFromInventoryPartialDesc".Translate(), enabled: buildFromInventory, indent: 24f);
            inventoryConstructDeliver = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.InventoryConstructDeliver".Translate(),
                inventoryConstructDeliver, "HaulersDream.Setting.InventoryConstructDeliverDesc".Translate());
            multiSiteConstructDeliver = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.MultiSiteConstructDeliver".Translate(),
                multiSiteConstructDeliver, "HaulersDream.Setting.MultiSiteConstructDeliverDesc".Translate(), enabled: inventoryConstructDeliver, indent: 24f);
            batchWorkDeliveries = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.BatchWorkDeliveries".Translate(),
                batchWorkDeliveries, "HaulersDream.Setting.BatchWorkDeliveriesDesc".Translate());
            orderedConstructTether = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ConstructTether".Translate(),
                orderedConstructTether, "HaulersDream.Setting.ConstructTetherDesc".Translate());
            haulToSiteOption = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulToSiteOption".Translate(),
                haulToSiteOption, "HaulersDream.Setting.HaulToSiteOptionDesc".Translate());
            shareHandHauledToStorage = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ShareHandHauled".Translate(),
                shareHandHauledToStorage, "HaulersDream.Setting.ShareHandHauledDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Crafting".Translate());
            shareForCrafting = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ShareForCrafting".Translate(),
                shareForCrafting, "HaulersDream.Setting.ShareForCraftingDesc".Translate());
            inventoryCraftDeliver = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.InventoryCraftDeliver".Translate(),
                inventoryCraftDeliver, "HaulersDream.Setting.InventoryCraftDeliverDesc".Translate(), enabled: shareForCrafting, indent: 24f);
            shareMeetInMiddle = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ShareMeetInMiddle".Translate(),
                shareMeetInMiddle, "HaulersDream.Setting.ShareMeetInMiddle.Help".Translate());
            butcherSpoilingFirst = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ButcherSpoilingFirst".Translate(),
                butcherSpoilingFirst, "HaulersDream.Setting.ButcherSpoilingFirstDesc".Translate());
            cookSpoilingFirst = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.CookSpoilingFirst".Translate(),
                cookSpoilingFirst, "HaulersDream.Setting.CookSpoilingFirstDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Food".Translate());
            mealsOnWheels = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.MealsOnWheels".Translate(),
                mealsOnWheels, "HaulersDream.Setting.MealsOnWheelsDesc".Translate());
        }

        // ===================== BULK LOADING =====================
        private void DrawBulkLoadingCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.BulkLoading.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Head.PackAnimals".Translate());
            enableBulkUnloadCarriers = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableBulkUnloadCarriers".Translate(),
                enableBulkUnloadCarriers, "HaulersDream.Setting.EnableBulkUnloadCarriersDesc".Translate());
            minFreeSpaceToUnloadCarrierPct = Mathf.Round(HDSettingsUI.Slider(c, "HaulersDream.Setting.MinFreeSpaceToUnloadCarrier.Lab".Translate(),
                minFreeSpaceToUnloadCarrierPct, 0.1f, 0.9f, minFreeSpaceToUnloadCarrierPct.ToStringPercent(),
                "HaulersDream.Setting.MinFreeSpaceToUnloadCarrier.Help".Translate(), enabled: enableBulkUnloadCarriers, indent: 24f) * 20f) / 20f;
            reserveCarrierOnUnload = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ReserveCarrierOnUnload".Translate(),
                reserveCarrierOnUnload, "HaulersDream.Setting.ReserveCarrierOnUnloadDesc".Translate(), enabled: enableBulkUnloadCarriers, indent: 24f);
            visualUnloadDelay = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.VisualUnloadDelay.Lab".Translate(),
                visualUnloadDelay, 0f, 30f, Ticks(visualUnloadDelay),
                "HaulersDream.Setting.VisualUnloadDelay.Help".Translate(), enabled: enableBulkUnloadCarriers, indent: 24f));
            loadPackAnimalBulk = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.LoadPackAnimalBulk".Translate(),
                loadPackAnimalBulk, "HaulersDream.Setting.LoadPackAnimalBulkDesc".Translate());
            autoDivertToPackAnimal = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AutoDivertToPackAnimal".Translate(),
                autoDivertToPackAnimal, "HaulersDream.Setting.AutoDivertToPackAnimalDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Transporters".Translate());
            enableBulkLoadTransporters = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableBulkLoadTransporters".Translate(),
                enableBulkLoadTransporters, "HaulersDream.Setting.EnableBulkLoadTransportersDesc".Translate());
            bulkLoadAiUpdateFrequency = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.BulkLoadAiUpdateFrequency.Lab".Translate(),
                bulkLoadAiUpdateFrequency, 10f, 240f, Ticks(bulkLoadAiUpdateFrequency),
                "HaulersDream.Setting.BulkLoadAiUpdateFrequency.Help".Translate(), enabled: enableBulkLoadTransporters, indent: 24f) / 10f) * 10;
            enableBulkLoadPortal = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableBulkLoadPortal".Translate(),
                enableBulkLoadPortal, "HaulersDream.Setting.EnableBulkLoadPortalDesc".Translate());
            enableBulkRefuel = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableBulkRefuel".Translate(),
                enableBulkRefuel, "HaulersDream.Setting.EnableBulkRefuelDesc".Translate());

            if (VehicleFrameworkCompat.IsActive)
            {
                HDSettingsUI.Header(c, "HaulersDream.Head.Vehicles".Translate());
                enableVehicleFramework = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableVehicleFramework".Translate(),
                    enableVehicleFramework, "HaulersDream.Setting.EnableVehicleFrameworkDesc".Translate());
                enableBulkLoadVehicles = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableBulkLoadVehicles".Translate(),
                    enableBulkLoadVehicles, "HaulersDream.Setting.EnableBulkLoadVehiclesDesc".Translate(), enabled: enableVehicleFramework, indent: 24f);
            }

            HDSettingsUI.Header(c, "HaulersDream.Head.AdvancedLoading".Translate());
            enableOpportunisticLoad = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.OpportunisticLoad".Translate(),
                enableOpportunisticLoad, "HaulersDream.Setting.OpportunisticLoadDesc".Translate());
            loadOpportunityScanRadius = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.LoadOpportunityRadius.Lab".Translate(),
                loadOpportunityScanRadius, 5f, 100f, Tiles(loadOpportunityScanRadius),
                "HaulersDream.Setting.LoadOpportunityRadius.Help".Translate(), enabled: enableOpportunisticLoad, indent: 24f));
            enableContinuousLoading = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ContinuousLoading".Translate(),
                enableContinuousLoading, "HaulersDream.Setting.ContinuousLoadingDesc".Translate());
            loadHybridPathing = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.LoadHybridPathing".Translate(),
                loadHybridPathing, "HaulersDream.Setting.LoadHybridPathingDesc".Translate());
            loadPathfindingCandidates = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.LoadPathfindingCandidates.Lab".Translate(),
                loadPathfindingCandidates, 2f, 24f, loadPathfindingCandidates.ToString(),
                "HaulersDream.Setting.LoadPathfindingCandidates.Help".Translate(), enabled: loadHybridPathing, indent: 24f));
            autoOpenTransporterContents = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AutoOpenTransporterContents".Translate(),
                autoOpenTransporterContents, "HaulersDream.Setting.AutoOpenTransporterContentsDesc".Translate());
            autoOpenCarrierGear = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AutoOpenCarrierGear".Translate(),
                autoOpenCarrierGear, "HaulersDream.Setting.AutoOpenCarrierGearDesc".Translate());
        }

        // ===================== ROUTING & STORAGE =====================
        private void DrawRoutingCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Routing.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Head.EnRoute".Translate());
            enRoutePickup = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnRoutePickup".Translate(),
                enRoutePickup, "HaulersDream.Setting.EnRoutePickupDesc".Translate());
            int chk = HDSettingsUI.Segmented(c, "HaulersDream.Setting.EnRoutePathChecker.Lab".Translate(),
                enRoutePathChecker == EnRoutePathChecker.Vanilla ? 0 : (enRoutePathChecker == EnRoutePathChecker.Default ? 1 : 2),
                new[] { "HaulersDream.Setting.EnRoutePathVanilla.S".Translate().ToString(), "HaulersDream.Setting.EnRoutePathDefault.S".Translate().ToString(), "HaulersDream.Setting.EnRoutePathPathfinding.S".Translate().ToString() },
                new[] { "HaulersDream.Setting.EnRoutePathVanilla.H".Translate().ToString(), "HaulersDream.Setting.EnRoutePathDefault.H".Translate().ToString(), "HaulersDream.Setting.EnRoutePathPathfinding.H".Translate().ToString() },
                "HaulersDream.Setting.EnRoutePathCheckerDesc".Translate(), enabled: enRoutePickup, indent: 24f);
            enRoutePathChecker = chk == 0 ? EnRoutePathChecker.Vanilla : (chk == 1 ? EnRoutePathChecker.Default : EnRoutePathChecker.Pathfinding);

            HDSettingsUI.Header(c, "HaulersDream.Head.StorageRouting".Translate());
            storageRouting = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.StorageRouting".Translate(),
                storageRouting, "HaulersDream.Setting.StorageRoutingDesc".Translate());
            routeSupplies = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.RouteSupplies".Translate(),
                routeSupplies, "HaulersDream.Setting.RouteSuppliesDesc".Translate(), enabled: storageRouting, indent: 24f);
            routeIngredients = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.RouteIngredients".Translate(),
                routeIngredients, "HaulersDream.Setting.RouteIngredientsDesc".Translate(), enabled: storageRouting, indent: 24f);
            routeToEqualPriority = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.RouteToEqualPriority".Translate(),
                routeToEqualPriority, "HaulersDream.Setting.RouteToEqualPriorityDesc".Translate(), enabled: storageRouting, indent: 24f);
            routeToStockpiles = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.RouteToStockpiles".Translate(),
                routeToStockpiles, "HaulersDream.Setting.RouteToStockpilesDesc".Translate(), enabled: storageRouting, indent: 24f);

            HDSettingsUI.Header(c, "HaulersDream.Head.StorageFilters".Translate());
            storageFiltersEnabled = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.StorageFilters".Translate(),
                storageFiltersEnabled, "HaulersDream.Setting.StorageFiltersDesc".Translate());
            storageFilterUseDefaults = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.StorageFilterUseDefaults".Translate(),
                storageFilterUseDefaults, "HaulersDream.Setting.StorageFilterUseDefaultsDesc".Translate(), enabled: storageFiltersEnabled, indent: 24f);
            storageFilterDenyLwmForOpportunistic = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.StorageFilterDenyLwm".Translate(),
                storageFilterDenyLwmForOpportunistic, "HaulersDream.Setting.StorageFilterDenyLwmDesc".Translate(), enabled: storageFiltersEnabled, indent: 24f);
            HDSettingsUI.Button(c, "HaulersDream.Setting.StorageFilterButton".Translate(),
                () => Find.WindowStack.Add(new Dialog_StorageBuildingFilter(storageBuildingFilter)),
                "HaulersDream.Setting.StorageFilterButton.Help".Translate(), enabled: storageFiltersEnabled, indent: 24f);
        }

        // ===================== WORK & YIELDS =====================
        private void DrawYieldsCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Yields.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Setting.WorkTypesHeader".Translate());
            haulHarvest = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulHarvest".Translate(), haulHarvest,
                "HaulersDream.Setting.HaulHarvest.Help".Translate());
            haulMining = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulMining".Translate(), haulMining,
                "HaulersDream.Setting.HaulMining.Help".Translate());
            haulDeepDrill = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulDeepDrill".Translate(), haulDeepDrill,
                "HaulersDream.Setting.HaulDeepDrill.Help".Translate());
            haulDeconstruct = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulDeconstruct".Translate(), haulDeconstruct,
                "HaulersDream.Setting.HaulDeconstruct.Help".Translate());
            haulAnimals = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulAnimals".Translate(), haulAnimals,
                "HaulersDream.Setting.HaulAnimals.Help".Translate());
            haulUninstall = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulUninstall".Translate(), haulUninstall,
                "HaulersDream.Setting.HaulUninstall.Help".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.AfterKill".Translate());
            haulTamedSlaughter = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulTamedSlaughter".Translate(),
                haulTamedSlaughter, "HaulersDream.Setting.HaulTamedSlaughterDesc".Translate());
            haulWildKills = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulWildKills".Translate(),
                haulWildKills, "HaulersDream.Setting.HaulWildKillsDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Stripping".Translate());
            int sm = HDSettingsUI.Segmented(c, "HaulersDream.Setting.AutoStripMode.Lab".Translate(),
                autoStripMode == AutoStripMode.AllHauls ? 0 : (autoStripMode == AutoStripMode.DisposalOnly ? 1 : 2),
                new[] { "HaulersDream.Setting.AutoStripAll.S".Translate().ToString(), "HaulersDream.Setting.AutoStripDisposal.S".Translate().ToString(), "HaulersDream.Setting.AutoStripOff.S".Translate().ToString() },
                new[] { "HaulersDream.Setting.AutoStripAll".Translate().ToString(), "HaulersDream.Setting.AutoStripDisposal".Translate().ToString(), "HaulersDream.Setting.AutoStripOff".Translate().ToString() },
                "HaulersDream.Setting.AutoStrip.Help".Translate());
            autoStripMode = sm == 0 ? AutoStripMode.AllHauls : (sm == 1 ? AutoStripMode.DisposalOnly : AutoStripMode.Off);
            stripColonistCorpses = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.StripColonists".Translate(),
                stripColonistCorpses, "HaulersDream.Setting.StripColonistsDesc".Translate(),
                enabled: autoStripMode != AutoStripMode.Off, indent: 24f);
            haulStrip = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulStrip".Translate(),
                haulStrip, "HaulersDream.Setting.HaulStripDesc".Translate());

            bool taintedShown = autoStripMode != AutoStripMode.Off || haulStrip;
            int ts = HDSettingsUI.Segmented(c, "HaulersDream.Setting.TaintedSmeltable.Lab".Translate(),
                (int)taintedSmeltablePolicy, TaintedOptionLabels(), TaintedOptionHelps(),
                "HaulersDream.Setting.Tainted.Help".Translate(), enabled: taintedShown, indent: 24f);
            taintedSmeltablePolicy = (TaintedApparelPolicy)ts;
            int tn = HDSettingsUI.Segmented(c, "HaulersDream.Setting.TaintedNonSmeltable.Lab".Translate(),
                (int)taintedNonSmeltablePolicy, TaintedOptionLabels(), TaintedOptionHelps(),
                "HaulersDream.Setting.Tainted.Help".Translate(), enabled: taintedShown, indent: 24f);
            taintedNonSmeltablePolicy = (TaintedApparelPolicy)tn;
        }

        // TaintedApparelPolicy enum order: Take=0, LeaveOnCorpse=1, DropAndForbid=2, Destroy=3.
        // Short labels for the segment buttons; the full descriptions are the per-option hover help.
        private static string[] TaintedOptionLabels() => new[]
        {
            "HaulersDream.Setting.TaintedTake.S".Translate().ToString(),
            "HaulersDream.Setting.TaintedLeave.S".Translate().ToString(),
            "HaulersDream.Setting.TaintedForbid.S".Translate().ToString(),
            "HaulersDream.Setting.TaintedDestroy.S".Translate().ToString(),
        };

        private static string[] TaintedOptionHelps() => new[]
        {
            "HaulersDream.Setting.TaintedTake".Translate().ToString(),
            "HaulersDream.Setting.TaintedLeave".Translate().ToString(),
            "HaulersDream.Setting.TaintedForbid".Translate().ToString(),
            "HaulersDream.Setting.TaintedDestroy".Translate().ToString(),
        };

        // ===================== WHO WORKS =====================
        private void DrawWhoCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Who.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Head.WhoCanHaul".Translate());
            pauseWhileDrafted = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.PauseWhileDrafted".Translate(),
                pauseWhileDrafted, "HaulersDream.Setting.PauseWhileDrafted.Help".Translate());
            allowMechanoids = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AllowMechanoids".Translate(),
                allowMechanoids, "HaulersDream.AllowMechanoidsDesc".Translate());
            allowAnimals = HDSettingsUI.Checkbox(c, "HaulersDream.AllowAnimals".Translate(),
                allowAnimals, "HaulersDream.AllowAnimalsDesc".Translate());
            allowIncapable = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AllowIncapable".Translate(),
                allowIncapable, "HaulersDream.Setting.AllowIncapable.Help".Translate());
            enableOnNonHomeMaps = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableOnNonHomeMaps".Translate(),
                enableOnNonHomeMaps, "HaulersDream.Setting.EnableOnNonHomeMaps.Help".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Setting.WorkOverrideHeader".Translate());
            allPawnsCanHaul = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AllCanHaul".Translate(),
                allPawnsCanHaul, "HaulersDream.Setting.AllCanHaulDesc".Translate());
            allPawnsCanClean = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AllCanClean".Translate(),
                allPawnsCanClean, "HaulersDream.Setting.AllCanCleanDesc".Translate());
            allPawnsCanCutPlants = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AllCanCutPlants".Translate(),
                allPawnsCanCutPlants, "HaulersDream.Setting.AllCanCutPlantsDesc".Translate());
        }

        // ===================== PLANNERS =====================
        private void DrawPlannersCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Planners.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Head.PlanningTools".Translate());
            planRoutes = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.PlanRoutes".Translate(),
                planRoutes, "HaulersDream.Setting.PlanRoutesDesc".Translate());
            planCrafting = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.PlanCrafting".Translate(),
                planCrafting, "HaulersDream.Setting.PlanCraftingDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Batches".Translate());
            batchByDefault = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.BatchByDefault".Translate(),
                batchByDefault, "HaulersDream.Setting.BatchByDefaultDesc".Translate());
            defaultBatchSize = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.DefaultBatchSize.Lab".Translate(),
                defaultBatchSize, 1f, 200f, defaultBatchSize.ToString(),
                "HaulersDream.Setting.DefaultBatchSize.Help".Translate()));
            craftBatchTimeoutHours = Mathf.Round(HDSettingsUI.Slider(c, "HaulersDream.Setting.CraftBatchTimeout.Lab".Translate(),
                craftBatchTimeoutHours, 0f, 8f, craftBatchTimeoutHours <= 0f ? OffLabel : Hours(craftBatchTimeoutHours),
                "HaulersDream.Setting.CraftBatchTimeout.Help".Translate()) * 2f) / 2f;

            HDSettingsUI.Header(c, "HaulersDream.Head.RoutePlanning".Translate());
            routeMaxAmount = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.RouteMaxAmount.Lab".Translate(),
                routeMaxAmount, 5f, RouteSelection.HardCap, routeMaxAmount.ToString(),
                "HaulersDream.Setting.RouteMaxAmount.Help".Translate()));
            int sel = HDSettingsUI.Segmented(c, "HaulersDream.Setting.RouteSelection.Lab".Translate(),
                routeSelectionMethod == RouteSelectionMethod.MostStopsPerTravel ? 0 : 1,
                new[] { "HaulersDream.PlanRoute.SelMostStops.S".Translate().ToString(), "HaulersDream.PlanRoute.SelNearest.S".Translate().ToString() },
                new[] { "HaulersDream.PlanRoute.SelMostStops.H".Translate().ToString(), "HaulersDream.PlanRoute.SelNearest.H".Translate().ToString() },
                "HaulersDream.Setting.RouteSelection.Help".Translate());
            routeSelectionMethod = sel == 0 ? RouteSelectionMethod.MostStopsPerTravel : RouteSelectionMethod.NearestToTarget;
            int dist = HDSettingsUI.Segmented(c, "HaulersDream.Setting.RouteDistance.Lab".Translate(),
                routeDistanceBasis == RouteDistanceBasis.StraightLine ? 0 : 1,
                new[] { "HaulersDream.PlanRoute.DistStraight.S".Translate().ToString(), "HaulersDream.PlanRoute.DistWalking.S".Translate().ToString() },
                new[] { "HaulersDream.PlanRoute.DistStraight.H".Translate().ToString(), "HaulersDream.PlanRoute.DistWalking.H".Translate().ToString() },
                "HaulersDream.Setting.RouteDistance.Help".Translate());
            routeDistanceBasis = dist == 0 ? RouteDistanceBasis.StraightLine : RouteDistanceBasis.WalkingPath;
            routeOrderExactMax = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.RouteOrderEffort.Lab".Translate(),
                routeOrderExactMax, 8f, 14f, routeOrderExactMax.ToString(),
                "HaulersDream.Setting.RouteOrderEffort.Help".Translate()));
        }

        // ===================== ADVANCED =====================
        private void DrawAdvancedCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Advanced.Intro".Translate());
            HDSettingsUI.Header(c, "HaulersDream.Head.UnloadTiming".Translate());
            unloadGraceTicks = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.UnloadGrace.Lab".Translate(),
                unloadGraceTicks, 0f, 7500f, Ticks(unloadGraceTicks),
                "HaulersDream.Setting.UnloadGrace.Help".Translate()) / 50f) * 50;
            intervalUnloadHours = Mathf.Round(HDSettingsUI.Slider(c, "HaulersDream.Setting.IntervalUnload.Lab".Translate(),
                intervalUnloadHours, 0f, 24f, intervalUnloadHours <= 0f ? OffLabel : Hours(intervalUnloadHours),
                "HaulersDream.Setting.IntervalUnload.Help".Translate()) * 2f) / 2f;

            HDSettingsUI.Header(c, "HaulersDream.Head.SafetyNet".Translate());
            alertCannotUnload = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AlertCannotUnload".Translate(),
                alertCannotUnload, "HaulersDream.Setting.AlertCannotUnloadDesc".Translate());
            alertStuckHours = Mathf.Round(HDSettingsUI.Slider(c, "HaulersDream.Setting.AlertStuckHours.Lab".Translate(),
                alertStuckHours, 1f, 72f, Hours(alertStuckHours),
                "HaulersDream.Setting.AlertStuckHours.Help".Translate(), enabled: alertCannotUnload, indent: 24f) * 2f) / 2f;
            enableSoftlockDrop = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.SoftlockDrop".Translate(),
                enableSoftlockDrop, "HaulersDream.Setting.SoftlockDropDesc".Translate());
            cleanupOnSave = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.CleanupOnSave".Translate(),
                cleanupOnSave, "HaulersDream.Setting.CleanupOnSaveDesc".Translate());

            if (Prefs.DevMode)
            {
                HDSettingsUI.Header(c, "HaulersDream.Head.Developer".Translate());
                verboseLogging = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.VerboseLogging".Translate(),
                    verboseLogging, "HaulersDream.Setting.VerboseLogging.Help".Translate());
                drawDetourLines = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.DrawDetourLines".Translate(),
                    drawDetourLines, "HaulersDream.Setting.DrawDetourLinesDesc".Translate());
            }
            else
            {
                HDSettingsUI.Note(c, "HaulersDream.Advanced.DevHint".Translate());
            }
        }
    }
}
