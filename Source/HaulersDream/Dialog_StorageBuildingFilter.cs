using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HaulersDream
{
    /// <summary>
    /// Mod-options editor for the shared storage permit/deny filter (the one
    /// <see cref="StorageBuildingFilter"/>, plan G4/G7). Lists every storage BUILDING def — vanilla shelves
    /// plus every modded unit built on <see cref="Building_Storage"/> — grouped by owning mod in foldable
    /// sections. Each mod has a whole-mod tri-state toggle (Default / Allow / Deny, written as its
    /// <c>packageId</c>) and each building under it has its own per-building tri-state (written as its
    /// <c>defName</c>). Both feed the SAME two override sets the policy reads, so a building override beats
    /// the mod's, which beats the curated context default.
    ///
    /// <para>Safe to open from the main-menu mod settings (no game loaded): it builds purely from
    /// <see cref="DefDatabase{ThingDef}"/> + the filter's string sets and never dereferences
    /// <c>Find.CurrentMap</c> / the world (the lesson behind <see cref="Dialog_ItemUnloadSettings"/>'s
    /// rewrite away from <c>ThingFilterUI</c>). Edits a working copy of the two sets and writes them back on
    /// close.</para>
    /// </summary>
    public class Dialog_StorageBuildingFilter : Window
    {
        private readonly StorageBuildingFilter filter;

        // Working copies of the two override sets, edited live and written back to the live filter on close.
        private readonly HashSet<string> denied;
        private readonly HashSet<string> allowed;

        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private readonly HashSet<string> expanded = new HashSet<string>(); // expanded mod sections, keyed by packageId
        private Vector2 scroll;
        private float viewHeight = 500f;

        // Built once — def data never changes at runtime. Storage building defs grouped by owning mod,
        // ordered by mod name then building label, so the tree is stable and Core sorts predictably.
        private static List<ModGroup> modGroups;

        private const float RowHeight = 28f;
        private const float SectionHeight = 28f;
        private const float Indent = 16f;
        private const float StateBtnWidth = 120f;

        public override Vector2 InitialSize => new Vector2(640f, 720f);

        private sealed class ModGroup
        {
            public string packageId;          // already lowercased by ModContentPack; the whole-mod override key
            public string name;               // player-facing mod name
            public List<ThingDef> buildings;  // storage building defs owned by this mod, label-sorted
        }

        public Dialog_StorageBuildingFilter(StorageBuildingFilter filter)
        {
            this.filter = filter;
            // Copy so Cancel-by-Escape is harmless until close; case-insensitive to match the live sets.
            denied = new HashSet<string>(filter?.denied ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            allowed = new HashSet<string>(filter?.allowed ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            EnsureGroups();
        }

        private static void EnsureGroups()
        {
            if (modGroups != null)
                return;
            var byMod = new Dictionary<string, ModGroup>(StringComparer.OrdinalIgnoreCase);
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (!StorageBuildingFilter.IsStorageBuildingDef(def))
                    continue;
                var mcp = def.modContentPack;
                // An unknown owner (null modContentPack — should not happen for a real def) is bucketed under
                // an empty packageId; the policy treats an empty packageId as "no curated/override match".
                string pkg = mcp?.PackageId ?? string.Empty;
                if (!byMod.TryGetValue(pkg, out var grp))
                {
                    grp = new ModGroup
                    {
                        packageId = pkg,
                        name = mcp?.Name ?? pkg,
                        buildings = new List<ThingDef>(),
                    };
                    byMod[pkg] = grp;
                }
                grp.buildings.Add(def);
            }
            modGroups = new List<ModGroup>(byMod.Values);
            foreach (var grp in modGroups)
                grp.buildings.Sort((a, b) => string.Compare(a.label ?? a.defName, b.label ?? b.defName,
                    StringComparison.OrdinalIgnoreCase));
            // Core first (so vanilla shelves are always at the top), then alphabetical by mod name.
            modGroups.Sort((a, b) =>
            {
                bool ac = a.packageId == "ludeon.rimworld", bc = b.packageId == "ludeon.rimworld";
                if (ac != bc)
                    return ac ? -1 : 1;
                return string.Compare(a.name ?? a.packageId, b.name ?? b.packageId, StringComparison.OrdinalIgnoreCase);
            });
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "HaulersDream.StorageFilter.Title".Translate());
            y += 38f;

            Text.Font = GameFont.Small;
            string desc = "HaulersDream.StorageFilter.Desc".Translate();
            float descH = Text.CalcHeight(desc, inRect.width); // measured so long text / small UI scale never clips
            Widgets.Label(new Rect(inRect.x, y, inRect.width, descH), desc);
            y += descH + 6f;

            // Search box + "reset all" button share one row.
            var searchRect = new Rect(inRect.x, y, inRect.width - 130f, 26f);
            search.OnGUI(searchRect);
            var clearRect = new Rect(searchRect.xMax + 6f, y, 124f, 26f);
            if (Widgets.ButtonText(clearRect, "HaulersDream.StorageFilter.ResetAll".Translate()))
            {
                // Reset to defaults = drop every explicit override (the curated context defaults then decide).
                denied.Clear();
                allowed.Clear();
            }
            y += 30f;

            float bottomReserve = CloseButSize.y + 10f;
            var outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - bottomReserve);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float curY = 0f;
            for (int i = 0; i < modGroups.Count; i++)
                DrawModGroup(modGroups[i], viewRect.width, ref curY);
            Widgets.EndScrollView();
            viewHeight = curY;
        }

        private void DrawModGroup(ModGroup grp, float width, ref float curY)
        {
            // While searching, only sections (and rows) whose building label matches are shown, force-opened.
            bool searchActive = search.filter.Active;
            bool sectionMatches = !searchActive || SectionMatches(grp);
            if (!sectionMatches)
                return;
            bool open = searchActive || expanded.Contains(grp.packageId);

            var rowRect = new Rect(0f, curY, width, SectionHeight);
            if (Mouse.IsOver(rowRect))
                Widgets.DrawHighlight(rowRect);
            if (!searchActive)
            {
                var arrowRect = new Rect(0f, curY + (SectionHeight - 18f) / 2f, 18f, 18f);
                if (Widgets.ButtonImage(arrowRect, open ? TexButton.Collapse : TexButton.Reveal))
                    ToggleSection(grp.packageId, open);
            }

            // Whole-mod tri-state on the right of the section header.
            var modBtnRect = new Rect(width - StateBtnWidth - 2f, curY + 2f, StateBtnWidth, SectionHeight - 4f);
            DrawTriState(modBtnRect, grp.packageId, isMod: true);

            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(22f, curY, modBtnRect.x - 22f - 6f, SectionHeight);
            Widgets.Label(labelRect, grp.name);
            Text.Anchor = TextAnchor.UpperLeft;
            if (!searchActive && Widgets.ButtonInvisible(labelRect))
                ToggleSection(grp.packageId, open);
            curY += SectionHeight;

            if (!open)
                return;
            for (int i = 0; i < grp.buildings.Count; i++)
            {
                var def = grp.buildings[i];
                if (searchActive && !search.filter.Matches(def.label ?? def.defName))
                    continue;
                DrawBuildingRow(def, width, ref curY);
            }
        }

        private void DrawBuildingRow(ThingDef def, float width, ref float curY)
        {
            var rowRect = new Rect(0f, curY, width, RowHeight);
            if (Mouse.IsOver(rowRect))
                Widgets.DrawHighlight(rowRect);

            float x = Indent + 4f;
            var iconRect = new Rect(x, curY + 2f, 24f, 24f);
            Widgets.DefIcon(iconRect, def);

            var btnRect = new Rect(width - StateBtnWidth - 2f, curY + 2f, StateBtnWidth, RowHeight - 4f);
            DrawTriState(btnRect, def.defName, isMod: false);

            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(x + 28f, curY, Math.Max(40f, btnRect.x - (x + 28f) - 6f), RowHeight);
            Widgets.Label(labelRect, def.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(rowRect) && !def.description.NullOrEmpty())
                TooltipHandler.TipRegion(rowRect, def.description);

            curY += RowHeight;
        }

        // Draws a Default/Allow/Deny cycle button for one override key (a defName or a packageId). Click
        // cycles Default -> Allow -> Deny -> Default; the key is added to exactly one of the two sets (or
        // neither for Default), so a key can never be in both at once.
        private void DrawTriState(Rect rect, string key, bool isMod)
        {
            bool isDenied = denied.Contains(key);
            bool isAllowed = allowed.Contains(key);
            string label = isDenied
                ? "HaulersDream.StorageFilter.StateDeny".Translate()
                : isAllowed
                    ? "HaulersDream.StorageFilter.StateAllow".Translate()
                    : "HaulersDream.StorageFilter.StateDefault".Translate();
            if (Widgets.ButtonText(rect, label))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                if (!isAllowed && !isDenied) // Default -> Allow
                {
                    allowed.Add(key);
                }
                else if (isAllowed) // Allow -> Deny
                {
                    allowed.Remove(key);
                    denied.Add(key);
                }
                else // Deny -> Default
                {
                    denied.Remove(key);
                }
            }
        }

        private void ToggleSection(string packageId, bool currentlyOpen)
        {
            if (currentlyOpen)
                expanded.Remove(packageId);
            else
                expanded.Add(packageId);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // True if any building under this mod matches the active search (by label).
        private bool SectionMatches(ModGroup grp)
        {
            for (int i = 0; i < grp.buildings.Count; i++)
                if (search.filter.Matches(grp.buildings[i].label ?? grp.buildings[i].defName))
                    return true;
            return false;
        }

        public override void PreClose()
        {
            base.PreClose();
            if (filter != null)
            {
                // Write the edited copies back into the LIVE shared filter (in place — other systems hold the
                // same reference). Replace contents rather than the set objects so the comparer is preserved.
                filter.denied.Clear();
                foreach (var k in denied)
                    filter.denied.Add(k);
                filter.allowed.Clear();
                foreach (var k in allowed)
                    filter.allowed.Add(k);
            }
            HaulersDreamMod.Instance?.WriteSettings(); // persist immediately
        }
    }
}
