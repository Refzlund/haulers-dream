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
            // Show only the word tier (Eager / Fair / Cautious) — the numeric "(N)" is dropped from the readout.
            // The XML values for these keys no longer carry a {0} placeholder, so no argument is passed.
            return lv < OverloadTuning.FairLevel
                ? "HaulersDream.Overload.Eager".Translate()
                : "HaulersDream.Overload.Cautious".Translate();
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
            Migration,    // (conditional, pinned to the BOTTOM) clean-transition guide — only when a replaced mod is active
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
            // Conditional + bottom-pinned: DrawNav hides this row unless ModReplacements.AnyActive and draws it at the
            // BOTTOM of the nav column (warning-amber). Kept last so the int index of every other category is unchanged.
            new CatDef(SettingsCat.Migration,   "Migration",   "HaulersDream.Cat.Migration",   "HaulersDream.Cat.Migration.Help"),
        };

        // Warning-amber for the conditional Migration row + its content header: reads as a caution while fitting
        // RimWorld's warm UI palette. Used for both the nav icon and label (the "you still have a replaced mod" tab).
        private static readonly Color MigrationWarn = new Color(0.95f, 0.79f, 0.28f);

        private static SettingsCat currentCat = SettingsCat.Features;
        private static Vector2 contentScroll;
        private static Vector2 helpScroll;
        // Per-category measured content height (seeded large so the first view of a page never under-sizes the
        // scroll viewport; replaced by the true measured height after the first draw and then stable).
        private static readonly float[] catHeight =
            { 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f, 1700f };
        private static readonly Dictionary<SettingsCat, Texture2D> iconCache = new Dictionary<SettingsCat, Texture2D>();

        // ===== settings search (fuzzy finder over every tab) =====
        // A search box sits above the nav column. While it has text, the content area shows ranked matches across
        // all tabs (instead of the current tab) and the tabs go "inactive"; clicking a match jumps to its tab,
        // scrolls the real control into view, and flashes a yellow highlight. EVERYTHING below is inert (the normal
        // tab UI draws byte-for-byte as before) whenever searchQuery is empty AND no nav/highlight is pending.
        private static string searchQuery = "";
        private static List<OptionEntry> searchRegistry;   // collect-mode snapshot; built lazily, null when inactive
        private static List<OptionEntry> scoredResults;     // sorted desc by score; recomputed when the query text changes
        private static string scoredForQuery;               // guards re-scoring (the query the current scoredResults are for)
        // One-frame (actually 2-frame) scroll defer after a navigate: the target tab must draw once so catHeight is
        // fresh before we clamp the scroll. frames counts down; cleared at 0.
        private static (int catId, int ordinal, float startY, int frames)? pendingNav;
        // The control to flash after a navigate. startY/height are STASHED here because the registry is nulled on
        // navigate (so the highlight can't re-derive them from it).
        private static (int catId, int ordinal, float startY, float height)? highlightTarget;
        private static float highlightStart;                // Time.realtimeSinceStartup captured at navigate

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
                Widgets.Label(new Rect(4f, 8f, 360f, 40f), "HaulersDream.SettingsHeaderFallback".Translate());
                Text.Font = pf;
            }

            // Custom padded close X (top-right). CloseButtonFor draws an 18px X at (rect.xMax-22, rect.y+4).
            if (Widgets.CloseButtonFor(new Rect(0f, 6f, fullW - 6f, 40f)))
                win?.Close();

            // Mid-game multiplayer guard: in an ACTIVE MP session HD's settings are host-authoritative (shipped to
            // clients at join), so a player editing them mid-session would silently desync only THIS client. Draw an
            // explanatory message and skip ALL editable controls (profile selector + the three option columns) — fully
            // removing the desync vector. The window still closes via the X above. MultiplayerCompat.InMultiplayerGame
            // short-circuits on Active, so reading it here touches no Multiplayer.API type when MP is absent (and is
            // always false in single-player). See MultiplayerCompat / the mod description.
            if (MultiplayerCompat.InMultiplayerGame)
            {
                var lockRect = new Rect(40f, bandH + 40f, fullW - 80f, rect.height - bandH - 80f);
                var pf2 = Text.Font;
                var pa2 = Text.Anchor;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(lockRect, "HaulersDream.Settings.MultiplayerLocked".Translate());
                Text.Anchor = pa2;
                Text.Font = pf2;
                return;
            }

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

            // Reserve a 28px strip above the nav column for the search box and shrink the nav's y/height to match.
            // (Suppressed in MP: the body is already locked there, so we'd have returned above — but keep nav full-
            // height in that case for safety; we never reach here in MP because of the early return.)
            var searchRect = new Rect(body.x, body.y, navW, 28f);
            var navRect = new Rect(body.x, body.y + 32f, navW, body.height - 32f);
            var contentRect = new Rect(navRect.xMax + gap, body.y, body.width - navW - helpW - 2f * gap, body.height);
            var helpRect = new Rect(contentRect.xMax + gap, body.y, helpW, body.height);

            DrawSearchBox(searchRect);
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

        // The fuzzy-search box above the nav column. A plain text field full-width; a greyed placeholder when empty;
        // a small "×" clear button (over the right edge) when it has text. Editing the query invalidates the cached
        // registry/results lazily (the registry is (re)built in DrawContent, the scores recomputed when the text
        // changes). Suppressed in an active multiplayer session (the body is locked — we also early-return before
        // ever reaching here in MP, but the guard keeps the search inert if that path ever changes).
        private void DrawSearchBox(Rect rect)
        {
            if (MultiplayerCompat.InMultiplayerGame)
                return;

            bool hadText = !searchQuery.NullOrEmpty();
            // Leave room on the right for the clear button when there's text, so the caret never sits under it.
            float fieldW = hadText ? rect.width - 24f : rect.width;
            var fieldRect = new Rect(rect.x, rect.y, fieldW, rect.height);
            searchQuery = Widgets.TextField(fieldRect, searchQuery ?? "");

            // Greyed placeholder, drawn over the (empty) field.
            if (searchQuery.NullOrEmpty())
            {
                var f = Text.Font;
                var anchor = Text.Anchor;
                var col = GUI.color;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.7f, 0.7f, 0.74f, 0.6f);
                var ph = fieldRect;
                ph.xMin += 6f;
                Widgets.Label(ph, "HaulersDream.Search.Placeholder".Translate());
                GUI.color = col;
                Text.Anchor = anchor;
                Text.Font = f;
            }
            else
            {
                // Clear "×" button overlaying the right side of the strip.
                var clearRect = new Rect(rect.xMax - 22f, rect.y, 22f, rect.height);
                TooltipHandler.TipRegion(clearRect, "HaulersDream.Search.Clear".Translate());
                if (Widgets.ButtonText(clearRect, "×"))
                    searchQuery = "";
            }

            // When the query goes empty (cleared by the × or by the user), drop the cached registry/results so the
            // normal tab UI resumes and a later search rebuilds fresh.
            if (searchQuery.NullOrEmpty())
            {
                searchRegistry = null;
                scoredResults = null;
                scoredForQuery = null;
            }
            // (No action needed when the text merely changes: DrawContent rebuilds the registry if needed and the
            // scorer recomputes whenever searchQuery != scoredForQuery.)
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
                // The Migration row is conditional + bottom-pinned — drawn separately below, not inline.
                if (cd.cat == SettingsCat.Migration)
                    continue;
                DrawNavRow(new Rect(inner.x, y, inner.width, rowH), cd, rowH, tint: null);
                y += rowH + 2f;
            }
            // Bottom-pinned rows ("justify-between" with the scrolling category list above). The "Report issue"
            // action row is ALWAYS shown; the Migration warning row is shown only while a replaced mod is still
            // active and sits at the very BOTTOM, with Report just above it. Mathf.Max keeps them from overlapping
            // the category list on a short window.
            float bottom = inner.yMax;
            if (ModReplacements.AnyActive)
            {
                var mig = cats[(int)SettingsCat.Migration];
                float migY = Mathf.Max(y + rowH + 2f, bottom - rowH);
                float repY = Mathf.Max(y, migY - rowH - 2f);
                DrawReportRow(new Rect(inner.x, repY, inner.width, rowH), rowH);
                DrawNavRow(new Rect(inner.x, migY, inner.width, rowH), mig, rowH, tint: MigrationWarn);
            }
            else
            {
                float repY = Mathf.Max(y, bottom - rowH);
                DrawReportRow(new Rect(inner.x, repY, inner.width, rowH), rowH);
            }
            Text.Font = f;
            Text.Anchor = anchor;
        }

        // One nav row (highlight + icon + label + click). `tint`, when set, colours BOTH the icon and the label in a
        // warning hue whether or not the row is selected (the Migration row); when null the row uses the normal
        // white/dimmed styling.
        private void DrawNavRow(Rect row, CatDef cd, float rowH, Color? tint)
        {
            bool selected = currentCat == cd.cat;
            if (selected) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.13f));
            else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.06f));

            var icon = IconFor(cd);
            const float navIconSize = 22f;
            var iconBox = new Rect(row.x + 9f, row.y + (rowH - navIconSize) / 2f, navIconSize, navIconSize);
            if (icon != null)
            {
                var col = GUI.color;
                GUI.color = tint ?? (selected ? Color.white : new Color(1f, 1f, 1f, 0.75f));
                GUI.DrawTexture(iconBox, icon, ScaleMode.ScaleToFit);
                GUI.color = col;
            }

            var labelRect = new Rect(iconBox.xMax + 8f, row.y, row.xMax - iconBox.xMax - 12f, rowH);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            var lcol = GUI.color;
            if (tint.HasValue)
                GUI.color = selected ? tint.Value : new Color(tint.Value.r, tint.Value.g, tint.Value.b, 0.8f);
            else if (selected)
                GUI.color = new Color(0.92f, 0.94f, 1f);
            Widgets.Label(labelRect, cd.nameKey.Translate());
            GUI.color = lcol;

            // While a search is active the tabs are "inactive": overlay a translucent dim so they read as greyed
            // (the overlay leaves the normal, non-search draw above byte-for-byte unchanged). Clicking a row still
            // switches tabs as usual but also clears the search so the chosen tab shows immediately.
            bool searching = !searchQuery.NullOrEmpty();
            if (searching && !Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, new Color(0.1f, 0.1f, 0.12f, 0.45f));

            if (Mouse.IsOver(row))
                HDSettingsUI.SetHelp(cd.nameKey.Translate(), cd.helpKey.Translate());
            if (Widgets.ButtonInvisible(row))
            {
                currentCat = cd.cat;
                contentScroll = Vector2.zero;
                // A manual tab switch cancels any pending search-jump scroll/highlight (otherwise a stale pendingNav
                // could re-scroll this tab once it's reached, or a queued highlight could flash unexpectedly).
                pendingNav = null;
                highlightTarget = null;
                if (searching)
                {
                    searchQuery = "";
                    searchRegistry = null;
                    scoredResults = null;
                    scoredForQuery = null;
                }
            }
        }

        private static Texture2D reportIconCache;
        private static bool reportIconResolved;
        private static Texture2D ReportIcon
        {
            get
            {
                if (!reportIconResolved)
                {
                    reportIconCache = ContentFinder<Texture2D>.Get("HaulersDream/Settings/Bug", reportFailure: false);
                    reportIconResolved = true;
                }
                return reportIconCache;
            }
        }

        // The always-visible "Report issue" action row. It is NOT a category — it never becomes "selected" and has
        // no content panel; clicking it opens the report dialog. Styled a touch quieter than the tabs (lower-alpha
        // bug icon + label) so it doesn't pull focus, while still showing a hover highlight so it reads as clickable.
        private void DrawReportRow(Rect row, float rowH)
        {
            bool over = Mouse.IsOver(row);
            if (over) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.06f));

            float dim = over ? 0.72f : 0.5f; // quieter than the tabs' 0.75 baseline; lifts a little on hover
            var icon = ReportIcon;
            const float navIconSize = 20f;   // a hair smaller than the 22px category icons
            var iconBox = new Rect(row.x + 10f, row.y + (rowH - navIconSize) / 2f, navIconSize, navIconSize);
            if (icon != null)
            {
                var col = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, dim);
                GUI.DrawTexture(iconBox, icon, ScaleMode.ScaleToFit);
                GUI.color = col;
            }

            var labelRect = new Rect(iconBox.xMax + 8f, row.y, row.xMax - iconBox.xMax - 12f, rowH);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            var lcol = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, dim);
            Widgets.Label(labelRect, "HaulersDream.Report.NavLabel".Translate());
            GUI.color = lcol;

            if (over)
                HDSettingsUI.SetHelp("HaulersDream.Report.NavLabel".Translate(), "HaulersDream.Report.NavLabel.Help".Translate());
            if (Widgets.ButtonInvisible(row))
                Find.WindowStack.Add(new Dialog_MyReports());
        }

        // Reserve the scrollbar width, then lay out content a gutter short of it (same 12px gutter as between the
        // nav and the content) so rows/headers never touch the scrollbar. Shared constants so the SEARCH registry
        // build uses the EXACT same content width as the real draw (StartY/Height alignment depends on it).
        private const float ContentScrollbarW = 16f;
        private const float ContentRightGutter = 12f;

        // Dispatch one tab's content into a SettingsCtx (real draw or — when c.Collecting — a layout-only pass).
        private void DrawCat(SettingsCtx c, SettingsCat cat)
        {
            switch (cat)
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
                case SettingsCat.Migration: DrawMigrationCat(c); break;
            }
        }

        private void DrawContent(Rect rect)
        {
            bool searching = !searchQuery.NullOrEmpty();

            // ---- SEARCH MODE: results across all tabs replace the current tab ----
            if (searching)
            {
                EnsureSearchRegistry(rect);
                EnsureScored();
                DrawSearchResults(rect);
                return;
            }

            // ---- NORMAL MODE: the current tab (byte-for-byte as before, plus an inert scroll/highlight defer) ----
            // The Migration tab is conditional; if it's somehow the current tab while no replaced mod is active
            // (its nav row is hidden then), fall back to Features so nothing draws an empty page.
            if (currentCat == SettingsCat.Migration && !ModReplacements.AnyActive)
                currentCat = SettingsCat.Features;
            int idx = (int)currentCat;

            // Deferred scroll-into-view after a navigate: once the target tab is current, clamp the scroll to bring
            // the target control near the top. Applied for ~2 frames so the clamp uses a fresh catHeight (the target
            // tab must draw once to measure). Done BEFORE BeginScrollView so the position takes effect this frame.
            if (pendingNav.HasValue && pendingNav.Value.catId == idx)
            {
                var pn = pendingNav.Value;
                contentScroll.y = Mathf.Clamp(pn.startY - 12f, 0f,
                    Mathf.Max(0f, catHeight[idx] - rect.height));
                int left = pn.frames - 1;
                pendingNav = left > 0 ? (pn.catId, pn.ordinal, pn.startY, left) : ((int, int, float, int)?)null;
            }

            var view = new Rect(0f, 0f, rect.width - ContentScrollbarW, Mathf.Max(catHeight[idx], rect.height));
            Widgets.BeginScrollView(rect, ref contentScroll, view);
            var c = new SettingsCtx(view.width - ContentRightGutter);
            c.Gap(2f);
            DrawCat(c, currentCat);
            catHeight[idx] = c.CurY + 8f;

            // Highlight flash on the just-navigated control. Drawn INSIDE the scroll view (view-local coords) AFTER
            // the tab content so it overlays the real control. startY/height were stashed at navigate (the registry
            // is nulled then). Blink-then-fade over 1.5s; cleared when done.
            DrawNavHighlight(view, idx);

            Widgets.EndScrollView();
        }

        // Build the collect-mode registry: a layout-only pass over every searchable tab (Migration excluded) using
        // the SAME content width the real draw uses, with the SAME per-tab CurY reset (Gap(2)), so each recorded
        // OptionEntry.StartY/Height lines up with the live draw (scroll + highlight depend on this). Built lazily
        // (only when searchRegistry == null) and honours all mod-present/devmode conditionals automatically (it
        // runs the real Draw*Cat methods, just with side effects suppressed).
        private void EnsureSearchRegistry(Rect rect)
        {
            if (searchRegistry != null)
                return;
            float view = rect.width - ContentScrollbarW;
            var cc = new SettingsCtx(view - ContentRightGutter)
            {
                Collecting = true,
                Sink = new List<OptionEntry>(),
            };
            foreach (var cd in cats)
            {
                if (cd.cat == SettingsCat.Migration)
                    continue;
                cc.CurY = 0f;
                cc.Gap(2f);            // mirror DrawContent's leading gap so StartY matches the real draw
                cc.CurrentCatId = (int)cd.cat;
                cc.CurrentHeader = null;
                cc.Ordinal = 0;
                DrawCat(cc, cd.cat);
            }
            searchRegistry = cc.Sink;
            scoredForQuery = null; // force a re-score against the fresh registry
        }

        // Score the registry against the current query (only when the query text changed). Keep only matches
        // (OptionScore > 0), ordered by descending score. CategoryAndHeaderText feeds the tab name + header into the
        // category-weighted field so "carriers" finds the "Loading & carriers" header even though the DISPLAY shows
        // the icon + header (this string is scoring-only).
        private void EnsureScored()
        {
            if (searchRegistry == null)
            {
                scoredResults = null;
                return;
            }
            if (searchQuery == scoredForQuery && scoredResults != null)
                return;
            var scored = new List<(OptionEntry e, float s)>(searchRegistry.Count);
            foreach (var e in searchRegistry)
            {
                float s = SettingsSearch.OptionScore(searchQuery, e.Name, e.Desc, CategoryAndHeaderText(e));
                if (s > 0f)
                    scored.Add((e, s));
            }
            scored.Sort((a, b) => b.s.CompareTo(a.s));
            scoredResults = new List<OptionEntry>(scored.Count);
            for (int i = 0; i < scored.Count; i++)
                scoredResults.Add(scored[i].e);
            scoredForQuery = searchQuery;
        }

        // Scoring text for the category-weighted field: the tab's display name + " " + the section header (if any).
        private static string CategoryAndHeaderText(OptionEntry e)
        {
            string cat = cats[e.CatId].nameKey.Translate();
            return e.Header.NullOrEmpty() ? cat : cat + " " + e.Header;
        }

        // A group of search matches that share the same (CatId, Header): the REAL editable controls under that
        // section drawn by a filtered re-run of the tab's Draw*Cat (see SettingsCtx.RenderOrdinals). Best = the
        // top-scoring member (the navigate target for the section-header click); ordinals = the matching controls.
        private sealed class ResultGroup
        {
            public int CatId;
            public string Header;        // section header text (may be null)
            public OptionEntry Best;     // first (= highest-scoring) member encountered — the navigate target
            public readonly HashSet<int> Ordinals = new HashSet<int>();
        }

        // Render the ranked matches as REAL, EDITABLE controls grouped under clean section headers. Results are
        // grouped by (CatId, Header) and the groups ordered by max member score DESC (= first-appearance order, since
        // scoredResults is already sorted desc). For each group we draw an HDSettingsUI-style section header (icon +
        // heading; the WHOLE bar navigates to the best member) then re-run the tab's Draw*Cat filtered to that group's
        // ordinals (SettingsCtx.RenderOrdinals), so the controls are the genuine bindings — fully editable. Section
        // height is measured/cached (searchResultsHeight) the same way the tabs use catHeight, since the filtered
        // controls' height isn't known until drawn.
        private void DrawSearchResults(Rect rect)
        {
            var results = scoredResults;
            float pad = 6f;
            float contentW = rect.width - ContentScrollbarW - ContentRightGutter;

            // ---- empty: centred no-results note (unchanged) ----
            if (results == null || results.Count == 0)
            {
                var view0 = new Rect(0f, 0f, rect.width - ContentScrollbarW, Mathf.Max(pad + 40f, rect.height));
                Widgets.BeginScrollView(rect, ref contentScroll, view0);
                var f0 = Text.Font;
                var a0 = Text.Anchor;
                var c0 = GUI.color;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.72f, 0.72f, 0.76f);
                Widgets.Label(new Rect(0f, pad, contentW, 40f), "HaulersDream.Search.NoResults".Translate());
                GUI.color = c0;
                Text.Anchor = a0;
                Text.Font = f0;
                Widgets.EndScrollView();
                return;
            }

            // ---- group by (CatId, Header), preserving first-appearance order (= max-score-desc) ----
            var groups = new List<ResultGroup>();
            var byKey = new Dictionary<(int, string), ResultGroup>();
            foreach (var e in results)
            {
                var key = (e.CatId, e.Header ?? "");
                if (!byKey.TryGetValue(key, out var g))
                {
                    g = new ResultGroup { CatId = e.CatId, Header = e.Header, Best = e };
                    byKey[key] = g;
                    groups.Add(g);
                }
                g.Ordinals.Add(e.Ordinal);
            }

            // The view height isn't known until the filtered controls draw, so seed from the cached measure (like
            // catHeight). The cursor's final CurY refreshes it after this frame's draw.
            var view = new Rect(0f, 0f, rect.width - ContentScrollbarW,
                Mathf.Max(searchResultsHeight, rect.height));
            Widgets.BeginScrollView(rect, ref contentScroll, view);

            // One shared layout cursor over the SAME content width as the real tab draw, so the filtered controls land
            // at identical x/width (input lands at the drawn rects).
            var c = new SettingsCtx(view.width - ContentRightGutter);
            c.Gap(pad);

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                // Generous breathing room between sections — more than the normal 32px Header gap — so the top
                // results stand out. A lighter lead-in before the very first group; a wider gap between sections.
                c.Gap(gi == 0 ? 22f : 40f);

                DrawResultSectionHeader(c, g);
                c.Gap(10f); // pad below the section header before its controls

                // Re-draw the tab filtered to ONLY this group's matching ordinals: the real, editable bindings run.
                c.RenderOrdinals = g.Ordinals;
                c.Ordinal = 0;
                c.CurrentCatId = g.CatId;
                DrawCat(c, (SettingsCat)g.CatId);
                c.RenderOrdinals = null; // never leaks into a normal draw
            }

            searchResultsHeight = c.CurY + pad;
            Widgets.EndScrollView();
        }

        // Cached measured height of the current search-results layout (seeded large so the first frame never
        // under-sizes the scroll viewport; replaced by the true CurY after the first draw, then stable per query).
        private static float searchResultsHeight = 1700f;

        private const float HeaderRowH = 26f;

        // A search-results SECTION header drawn through the shared layout cursor, mirroring HDSettingsUI.Header's bar:
        // a 26px grey box, the category icon (~text height, vertically centred) + ONLY the section heading title (no
        // "<Tab> — " prefix). The WHOLE bar is clickable -> Navigate to the group's best member (switch tab + scroll
        // + highlight). A faint hover wash signals it's clickable.
        private void DrawResultSectionHeader(SettingsCtx c, ResultGroup g)
        {
            var r = c.Row(HeaderRowH);
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.09f)); // same bar as HDSettingsUI.Header
            if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.06f)); // faint hover wash

            var cd = cats[g.CatId];
            var icon = IconFor(cd);
            const float iconSize = 18f; // ~text height — not much taller than the label
            var iconBox = new Rect(r.x + 6f, r.y + (r.height - iconSize) / 2f, iconSize, iconSize);
            if (icon != null)
            {
                var icol = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(iconBox, icon, ScaleMode.ScaleToFit);
                GUI.color = icol;
            }

            // ONLY the heading title (drop the "<Tab> — " prefix). Fall back to the tab name for headerless controls
            // (rare — most controls live under a section header).
            string title = g.Header.NullOrEmpty() ? cd.nameKey.Translate().ToString() : g.Header;
            var labelRect = new Rect(iconBox.xMax + 8f, r.y, r.xMax - iconBox.xMax - 10f, r.height);
            var f = Text.Font;
            var anchor = Text.Anchor;
            var lcol = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft; // vertically centre the label in the bar
            GUI.color = new Color(0.86f, 0.88f, 0.95f);
            Widgets.Label(labelRect, title);
            GUI.color = lcol;
            Text.Anchor = anchor;
            Text.Font = f;

            if (Mouse.IsOver(r))
                HDSettingsUI.SetHelp(cd.nameKey.Translate(),
                    g.Header.NullOrEmpty() ? cd.helpKey.Translate().ToString() : g.Header);
            if (Widgets.ButtonInvisible(r))
                Navigate(g.Best);
        }

        // Jump to the control behind a search result: switch to its tab, clear the search, queue the scroll-into-view
        // (2-frame defer so catHeight is fresh) and the highlight flash. startY/height are stashed for the highlight
        // because the registry is nulled here.
        private void Navigate(OptionEntry e)
        {
            currentCat = (SettingsCat)e.CatId;
            searchQuery = "";
            searchRegistry = null;
            scoredResults = null;
            scoredForQuery = null;
            contentScroll = Vector2.zero;
            pendingNav = (e.CatId, e.Ordinal, e.StartY, 2);
            highlightTarget = (e.CatId, e.Ordinal, e.StartY, e.Height);
            highlightStart = Time.realtimeSinceStartup;
        }

        // The post-navigate yellow flash. Drawn inside the scroll view (view-local), padded ~4px around the stashed
        // target rect, blinking then fading over 1.5s. Self-clears when done or when the tab no longer matches.
        private void DrawNavHighlight(Rect view, int currentIdx)
        {
            if (!highlightTarget.HasValue || highlightTarget.Value.catId != currentIdx)
                return;
            float t = (Time.realtimeSinceStartup - highlightStart) / 1.5f;
            if (t >= 1f)
            {
                highlightTarget = null;
                return;
            }
            var ht = highlightTarget.Value;
            float alpha = (1f - t) * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 5f)));
            var box = new Rect(0f, ht.startY - 4f, view.width, ht.height + 8f);
            var col = GUI.color;
            // Translucent yellow wash + a brighter yellow border. DrawBoxSolid takes an explicit fill colour; the
            // DrawBox outline reads GUI.color.
            Widgets.DrawBoxSolid(box, new Color(1f, 0.92f, 0.4f, alpha * 0.35f));
            GUI.color = new Color(1f, 0.92f, 0.4f, Mathf.Min(1f, alpha));
            Widgets.DrawBox(box, 2);
            GUI.color = col;
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
        // (Ticks readout helper removed — its three callers now show in-game hours / seconds directly.)
        private static string Hours(float h) => "HaulersDream.Unit.Hours".Translate(h.ToString("0.#"));
        private static string OffLabel => "HaulersDream.Common.Off".Translate();

        // ===================== MIGRATION (conditional clean-transition guide) =====================
        // Reachable only when ModReplacements.AnyActive (the nav row is hidden + DrawContent redirects otherwise), so
        // ActiveNames is non-empty here. Lists the replaced mods the user still has active, offers a one-click
        // disable-and-restart, and gives the safe manual order to remove them — a switcher running a replaced mod
        // alongside HD is the usual "pickup looks broken after switching" cause (they fight over the same hauling
        // jobs; see COMPATIBILITY.md).
        private void DrawMigrationCat(SettingsCtx c)
        {
            var names = ModReplacements.ActiveNames;

            HDSettingsUI.Header(c, "HaulersDream.Migration.DetectedHead".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Migration.Intro".Translate(), color: MigrationWarn);
            c.Gap(4f);
            for (int i = 0; i < names.Count; i++)
                HDSettingsUI.Note(c, "•  " + names[i], indent: 12f, color: MigrationWarn);
            c.Gap(6f);

            // One-click: disable every detected replaced mod and restart (the only way a mod-list change takes
            // effect — exactly what the vanilla Mods menu does). Confirmed first, with a clear save/draft warning,
            // because it restarts RimWorld immediately.
            HDSettingsUI.Button(c, "HaulersDream.Migration.DisableButton".Translate(),
                () =>
                {
                    string list = names.ToCommaList(useAnd: true);
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "HaulersDream.Migration.DisableConfirm".Translate(list),
                        ModReplacements.DisableAllAndRestart,
                        destructive: true,
                        "HaulersDream.Migration.DisableTitle".Translate()));
                },
                help: "HaulersDream.Migration.DisableButton.Help".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Migration.StepsHead".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Migration.Step1".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Migration.Step2".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Migration.Step3".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Migration.Step4".Translate());
        }

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

            // Pick-up handling moved to per-category yield behaviour (Work & yields tab). The lone bleeding-skip
            // toggle that remained no longer warrants its own one-item "Pick-up" header — it now sits with the
            // carry-weight controls above it (header removed; the toggle itself is unchanged).
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

            // The "While working" group (sweep-nearby + keep-working-when-full) moved to the Work & yields tab, next
            // to the per-category yield behaviour it governs. "Top up existing stacks" moved to the Unloading tab.
        }

        // ===================== UNLOADING =====================
        private void DrawUnloadingCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Unloading.Intro".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.AutoUnloadTrips".Translate());
            // The most behavior-defining unloading knob (moved here from Advanced). Same field/key/serialization;
            // only the location and the numeric readout changed (raw ticks -> in-game hours, ticks/2500).
            unloadGraceTicks = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.UnloadGrace.Lab".Translate(),
                unloadGraceTicks, 0f, 7500f, string.Format("~{0:0.0} h", unloadGraceTicks / 2500f),
                "HaulersDream.Setting.UnloadGrace.Help".Translate()) / 50f) * 50;
            markForUnload = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.MarkForUnload".Translate(),
                markForUnload, "HaulersDream.Feat.AutoUnload.Blurb".Translate());
            unloadBeforeSleep = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadBeforeSleep".Translate(),
                unloadBeforeSleep, "HaulersDream.Setting.UnloadBeforeSleepDesc".Translate(), enabled: markForUnload, indent: 24f);
            unloadBeforeLeisure = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadBeforeLeisure".Translate(),
                unloadBeforeLeisure, "HaulersDream.Setting.UnloadBeforeLeisureDesc".Translate(), enabled: markForUnload, indent: 24f);
            unloadBeforeEating = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.UnloadBeforeEating".Translate(),
                unloadBeforeEating, "HaulersDream.Setting.UnloadBeforeEatingDesc".Translate(), enabled: markForUnload, indent: 24f);

            HDSettingsUI.Header(c, "HaulersDream.Head.DropOff".Translate());
            // "Top up existing stacks" governs WHERE goods get put away (prefer a partial stack), so it lives with the
            // drop-off options. Moved here from the Hauling tab's old "While working" section; field/key/behaviour unchanged.
            haulToStack = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HaulToStack".Translate(),
                haulToStack, "HaulersDream.Setting.HaulToStackDesc".Translate());
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
            // The two per-pawn gizmo toggles both read as "Show the … button". hideGizmo's binding is INVERTED so the
            // checkbox means "show": checked => button shown. Field default unchanged (hideGizmo=false => box checked).
            HDSettingsUI.Note(c, "HaulersDream.Setting.PerPawnButtonsNote".Translate());
            bool showUnloadBtn = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.HideGizmo".Translate(),
                !hideGizmo, "HaulersDream.Setting.HideGizmo.Help".Translate());
            hideGizmo = !showUnloadBtn;
            showAutoHaulGizmo = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.ShowAutoHaulGizmo".Translate(),
                showAutoHaulGizmo, "HaulersDream.Setting.ShowAutoHaulGizmo.Help".Translate());
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
                visualUnloadDelay, 0f, 30f, string.Format("~{0:0.0}s", visualUnloadDelay / 60f),
                "HaulersDream.Setting.VisualUnloadDelay.Help".Translate(), enabled: enableBulkUnloadCarriers, indent: 24f));
            loadPackAnimalBulk = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.LoadPackAnimalBulk".Translate(),
                loadPackAnimalBulk, "HaulersDream.Setting.LoadPackAnimalBulkDesc".Translate());
            autoDivertToPackAnimal = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AutoDivertToPackAnimal".Translate(),
                autoDivertToPackAnimal, "HaulersDream.Setting.AutoDivertToPackAnimalDesc".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Head.Transporters".Translate());
            enableBulkLoadTransporters = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableBulkLoadTransporters".Translate(),
                enableBulkLoadTransporters, "HaulersDream.Setting.EnableBulkLoadTransportersDesc".Translate());
            bulkLoadAiUpdateFrequency = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.BulkLoadAiUpdateFrequency.Lab".Translate(),
                bulkLoadAiUpdateFrequency, 10f, 240f, string.Format("~{0:0.0}s", bulkLoadAiUpdateFrequency / 60f),
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
            // Storage Network bulk-load (experimental, default off) — only shown when Storage Network is installed.
            if (StorageNetworkCompat.IsActive)
                enableStorageNetworkBulkLoad = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.EnableStorageNetworkBulkLoad".Translate(),
                    enableStorageNetworkBulkLoad, "HaulersDream.Setting.EnableStorageNetworkBulkLoadDesc".Translate());
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
        // The three shared segment labels + per-option descriptions for a yield-behaviour row. The integer order
        // [Off, Drop & haul, To inventory] lines up with YieldBehavior (Disabled=0, DropThenHaul=1,
        // DirectToInventory=2), so a 3-way row maps (YieldBehavior)index directly. Cached as .ToString() arrays
        // (mirroring the other segmented controls) so the strings resolve once per draw.
        private static string[] YieldLabels3() => new[]
        {
            "HaulersDream.Setting.Yield.Off".Translate().ToString(),
            "HaulersDream.Setting.Yield.Drop".Translate().ToString(),
            "HaulersDream.Setting.Yield.Direct".Translate().ToString(),
        };

        private static string[] YieldHelps3() => new[]
        {
            "HaulersDream.Setting.Yield.OffDesc".Translate().ToString(),
            "HaulersDream.Setting.Yield.DropDesc".Translate().ToString(),
            "HaulersDream.Setting.Yield.DirectDesc".Translate().ToString(),
        };

        private void DrawYieldsCat(SettingsCtx c)
        {
            HDSettingsUI.Note(c, "HaulersDream.Cat.Yields.Intro".Translate());

            HDSettingsUI.Header(c, "HaulersDream.Setting.Yield.Header".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Setting.Yield.Intro".Translate());

            var yLab = YieldLabels3();
            var yHelp = YieldHelps3();

            // One radio-matrix row per work-result category; the three columns (the shared YieldBehavior options) are
            // labelled once at the top. STRIP is 2-way only: removed gear can never go straight to inventory
            // (YieldRouter force-drops it), so AllowDirect=false hides its "collect directly" column; a stored
            // DirectToInventory (migrated/odd value) shows as DropThenHaul (Value 1).
            var rHarvest = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Harvest".Translate(), Help = "HaulersDream.Setting.Yield.HarvestHelp".Translate(), Value = (int)yieldHarvest };
            var rLogging = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Logging".Translate(), Help = "HaulersDream.Setting.Yield.LoggingHelp".Translate(), Value = (int)yieldLogging };
            var rMining = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Mining".Translate(), Help = "HaulersDream.Setting.Yield.MiningHelp".Translate(), Value = (int)yieldMining };
            var rChunks = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Chunks".Translate(), Help = "HaulersDream.Setting.Yield.ChunksHelp".Translate(), Value = (int)yieldChunks };
            var rDeepDrill = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.DeepDrill".Translate(), Help = "HaulersDream.Setting.Yield.DeepDrillHelp".Translate(), Value = (int)yieldDeepDrill };
            var rDeconstruct = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Deconstruct".Translate(), Help = "HaulersDream.Setting.Yield.DeconstructHelp".Translate(), Value = (int)yieldDeconstruct };
            var rAnimals = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Animals".Translate(), Help = "HaulersDream.Setting.Yield.AnimalsHelp".Translate(), Value = (int)yieldAnimals };
            var rStrip = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Strip".Translate(), Help = "HaulersDream.Setting.Yield.StripHelp".Translate(), Value = yieldStrip == YieldBehavior.Disabled ? 0 : 1, AllowDirect = false };
            var rUninstall = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Uninstall".Translate(), Help = "HaulersDream.Setting.Yield.UninstallHelp".Translate(), Value = (int)yieldUninstall };
            // Fishing (Odyssey only): the catch from a fishing spot. Shown ONLY when ModsConfig.OdysseyActive — the
            // mechanic needs the Odyssey DLC, so without it the row would just confuse the player.
            var rFishing = new YieldMatrixRow { Label = "HaulersDream.Setting.Yield.Fishing".Translate(), Help = "HaulersDream.Setting.Yield.FishingHelp".Translate(), Value = (int)yieldFishing };

            var yieldRows = new List<YieldMatrixRow>
            {
                rHarvest, rLogging, rMining, rChunks, rDeepDrill, rDeconstruct, rAnimals, rStrip, rUninstall,
            };
            if (ModsConfig.OdysseyActive)
                yieldRows.Add(rFishing);
            HDSettingsUI.YieldMatrix(c, yLab, yHelp, yieldRows);

            yieldHarvest = (YieldBehavior)rHarvest.Value;
            yieldLogging = (YieldBehavior)rLogging.Value;
            yieldMining = (YieldBehavior)rMining.Value;
            yieldChunks = (YieldBehavior)rChunks.Value;
            yieldDeepDrill = (YieldBehavior)rDeepDrill.Value;
            yieldDeconstruct = (YieldBehavior)rDeconstruct.Value;
            yieldAnimals = (YieldBehavior)rAnimals.Value;
            yieldStrip = rStrip.Value == 0 ? YieldBehavior.Disabled : YieldBehavior.DropThenHaul;
            yieldUninstall = (YieldBehavior)rUninstall.Value;
            // Only read the Fishing row back when it was actually shown (otherwise its untouched Value would just
            // re-assign the current default — harmless, but only persist a real player edit on Odyssey).
            if (ModsConfig.OdysseyActive)
                yieldFishing = (YieldBehavior)rFishing.Value;

            // While working — what a pawn does about its load WHILE producing results. Moved here from the Hauling tab
            // to sit next to the yields it governs; same fields/keys/behaviour, only the location changed.
            HDSettingsUI.Header(c, "HaulersDream.Head.WhileWorking".Translate());
            sweepNearbyWhileWorking = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.SweepNearbyWhileWorking".Translate(),
                sweepNearbyWhileWorking, "HaulersDream.Setting.SweepNearbyWhileWorkingDesc".Translate());
            keepWorkingWhenFull = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.KeepWorkingWhenFull".Translate(),
                keepWorkingWhenFull, "HaulersDream.Setting.KeepWorkingWhenFullDesc".Translate());
            keepWorkingMarginCells = Mathf.RoundToInt(HDSettingsUI.Slider(c, "HaulersDream.Setting.KeepWorkingMargin.Lab".Translate(),
                keepWorkingMarginCells, 0f, 30f, Tiles(keepWorkingMarginCells),
                "HaulersDream.Setting.KeepWorkingMarginDesc".Translate(), enabled: keepWorkingWhenFull, indent: 24f));

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

            // Tainted-apparel policy applies to gear removed during ANY strip (auto-strip-while-hauling above, or a
            // manual Strip order — the "Stripping (removed gear)" yield row at the top of this tab).
            bool taintedShown = autoStripMode != AutoStripMode.Off || yieldStrip != YieldBehavior.Disabled;
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
            // Snap to 0.1 steps so the "×N.N" readout equals the value actually applied (a raw slider float like
            // 1.03 would show "×1.0" yet still scale capacity, and would skip the ==1f no-op fast-path) and exact
            // ×1.0 stays reachable by dragging to the minimum.
            mechHaulMultiplier = Mathf.Round(HDSettingsUI.Slider(c, "HaulersDream.Setting.MechHaulMultiplier.Lab".Translate(),
                mechHaulMultiplier, 1.0f, 5.0f, "×" + mechHaulMultiplier.ToString("0.0"),
                "HaulersDream.Setting.MechHaulMultiplierDesc".Translate(), enabled: allowMechanoids, indent: 24f) * 10f) / 10f;
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
            // Common Sense compat opt-in — only meaningful (and only shown) when Common Sense is installed.
            if (CommonSenseCompat.IsActive)
                allowBatchUnderCommonSense = HDSettingsUI.Checkbox(c, "HaulersDream.Setting.AllowBatchUnderCommonSense".Translate(),
                    allowBatchUnderCommonSense, "HaulersDream.Setting.AllowBatchUnderCommonSenseDesc".Translate());

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
            // unloadGraceTicks (the most behavior-defining unloading knob) was moved to the TOP of DrawUnloadingCat.
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

            HDSettingsUI.Header(c, "HaulersDream.Head.Notifications".Translate());
            HDSettingsUI.Note(c, "HaulersDream.Setting.Notify.Intro".Translate());
            int notify = HDSettingsUI.Segmented(c, "HaulersDream.Setting.NotifyThreshold.Lab".Translate(),
                (int)notifyThreshold,
                new[]
                {
                    "HaulersDream.Setting.NotifyThreshold.All".Translate().ToString(),
                    "HaulersDream.Setting.NotifyThreshold.Comments".Translate().ToString(),
                    "HaulersDream.Setting.NotifyThreshold.Fixed".Translate().ToString(),
                    "HaulersDream.Setting.NotifyThreshold.Never".Translate().ToString()
                },
                new[]
                {
                    "HaulersDream.Setting.NotifyThreshold.All.Desc".Translate().ToString(),
                    "HaulersDream.Setting.NotifyThreshold.Comments.Desc".Translate().ToString(),
                    "HaulersDream.Setting.NotifyThreshold.Fixed.Desc".Translate().ToString(),
                    "HaulersDream.Setting.NotifyThreshold.Never.Desc".Translate().ToString()
                },
                "HaulersDream.Setting.NotifyThreshold.Help".Translate());
            notifyThreshold = (NotifyThreshold)Mathf.Clamp(notify, 0, 3);

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
