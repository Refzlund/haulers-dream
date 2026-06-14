using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Mod-options picker for "items HD must never unload out of a pawn's inventory" — the same stockpile-style
    /// categorized, foldable, searchable tree (<see cref="ThingFilterUI"/>) used for storage/bills. The player's
    /// choices live in <see cref="HaulersDreamSettings.keepDefNames"/> as defName STRINGS (fallback-safe and
    /// restore-on-mod-return); this dialog only builds a transient <see cref="ThingFilter"/> from them for the UI
    /// and writes the strings back on close. Items whose mod is currently absent are preserved untouched, so they
    /// come back when the mod is reinstalled — and nothing here can break save loading.
    /// </summary>
    public class Dialog_KeepFilter : Window
    {
        private readonly HaulersDreamSettings settings;
        private readonly ThingFilter filter = new ThingFilter();
        private readonly ThingFilterUI.UIState uiState = new ThingFilterUI.UIState();

        public override Vector2 InitialSize => new Vector2(540f, 680f);

        public Dialog_KeepFilter(HaulersDreamSettings settings)
        {
            this.settings = settings;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            BuildFilterFromNames();
        }

        // Build the UI filter from the saved defName list — only the defs that currently resolve. Entries for
        // absent mods stay in settings.keepDefNames and are re-merged on close (see SyncNamesFromFilter).
        private void BuildFilterFromNames()
        {
            filter.SetDisallowAll();
            if (settings?.keepDefNames == null)
                return;
            foreach (var name in settings.keepDefNames)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def != null)
                    filter.SetAllow(def, true);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "HaulersDream.KeepFilter.Title".Translate());
            y += 38f;

            Text.Font = GameFont.Small;
            string desc = "HaulersDream.KeepFilter.Desc".Translate();
            float descH = Text.CalcHeight(desc, inRect.width); // measured so long text / small UI scale never clips
            Widgets.Label(new Rect(inRect.x, y, inRect.width, descH), desc);
            y += descH + 6f;

            // Reserve the bottom row for the base class's close button so the tree doesn't draw under it.
            float bottomReserve = CloseButSize.y + 10f;
            var filterRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - bottomReserve);
            // Universe = everything a stockpile could hold (the haulable-item set); hide the HP/quality sliders
            // (irrelevant for "keep this kind of item").
            ThingFilterUI.DoThingFilterConfigWindow(filterRect, uiState, filter,
                StorageSettings.EverStorableFixedSettings().filter, 1, null, null,
                forceHideHitPointsConfig: true, forceHideQualityConfig: true);
        }

        public override void PreClose()
        {
            base.PreClose();
            SyncNamesFromFilter();
            HaulersDreamMod.Instance?.WriteSettings(); // persist immediately
        }

        // Write the UI filter back to the saved defName list, PRESERVING entries whose def isn't currently loaded
        // (a modded item whose mod is absent right now) so re-installing the mod restores the choice.
        private void SyncNamesFromFilter()
        {
            if (settings == null)
                return;
            var newSet = new HashSet<string>();
            if (settings.keepDefNames != null)
                foreach (var name in settings.keepDefNames)
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null)
                        newSet.Add(name); // absent-mod entry — keep it for when the mod returns
            foreach (var def in filter.AllowedThingDefs)
                newSet.Add(def.defName);
            settings.SetKeepDefNames(newSet);
        }
    }
}
