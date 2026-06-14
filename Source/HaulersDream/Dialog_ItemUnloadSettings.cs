using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HaulersDream
{
    /// <summary>
    /// Mod-options picker for per-item unload rules, shown as a stockpile-style categorized, foldable, searchable
    /// tree. For each item the player picks a mode — keep all (never unload), keep at most N, or always unload —
    /// layered on top of HD's automatic keep detection. Rules live in
    /// <see cref="HaulersDreamSettings.itemUnloadRules"/> as defName-keyed strings, so they are fully
    /// fallback-safe: a rule for an item from a mod you've removed simply never matches a live item, is preserved
    /// untouched, and is restored automatically if the mod returns — and nothing here can break save loading.
    ///
    /// Built directly on <see cref="ThingCategoryNodeDatabase"/> rather than vanilla
    /// <see cref="ThingFilterUI.DoThingFilterConfigWindow"/>. That helper calls <c>Find.HiddenItemsManager</c>,
    /// which is null when mod settings are opened from the main menu (no game loaded) — the source of the
    /// per-frame <c>NullReferenceException</c> flood the old binary picker threw — and it can only render an
    /// allow/disallow checkbox, not the per-item mode + amount controls this dialog needs.
    /// </summary>
    public class Dialog_ItemUnloadSettings : Window
    {
        private readonly HaulersDreamSettings settings;

        // Working copy keyed by defName. Includes entries whose mod is currently absent — those are NOT shown in
        // the tree (no live def to draw) but are preserved verbatim and written back on close. Edited live.
        private readonly Dictionary<string, ItemUnloadRule> working;
        private readonly Dictionary<string, string> amountBuffers = new Dictionary<string, string>();
        private readonly HashSet<ThingCategoryDef> expanded = new HashSet<ThingCategoryDef>();
        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private Vector2 scroll;
        private float viewHeight = 500f;

        // Built once — def data never changes at runtime. Which categories contain any ever-storable item, and the
        // ever-storable defs directly under each category (sorted by label). Lets us skip empty categories and
        // avoids re-walking + re-filtering the whole def tree every frame.
        private static Dictionary<ThingCategoryDef, List<ThingDef>> storableDirect;
        private static HashSet<ThingCategoryDef> hasStorableDescendant;

        private const float RowHeight = 28f;
        private const float CatHeight = 26f;
        private const float Indent = 14f;
        private const float ModeBtnWidth = 132f;
        private const float AmountWidth = 58f;

        public override Vector2 InitialSize => new Vector2(640f, 720f);

        public Dialog_ItemUnloadSettings(HaulersDreamSettings settings)
        {
            this.settings = settings;
            working = settings?.GetItemRulesCopy() ?? new Dictionary<string, ItemUnloadRule>();
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            EnsureCaches();
        }

        private static void EnsureCaches()
        {
            if (storableDirect != null)
                return;
            storableDirect = new Dictionary<ThingCategoryDef, List<ThingDef>>();
            hasStorableDescendant = new HashSet<ThingCategoryDef>();
            var root = ThingCategoryNodeDatabase.RootNode;
            if (root?.catDef != null)
                BuildCaches(root.catDef);
        }

        // Records each category's direct ever-storable defs; returns true if this category or any descendant holds one.
        private static bool BuildCaches(ThingCategoryDef cat)
        {
            if (cat == null)
                return false;
            var direct = new List<ThingDef>();
            if (cat.childThingDefs != null)
                foreach (var def in cat.childThingDefs)
                    if (def != null && def.EverStorable(false))
                        direct.Add(def);
            direct.Sort((a, b) => string.Compare(a.label ?? a.defName, b.label ?? b.defName,
                System.StringComparison.OrdinalIgnoreCase));
            storableDirect[cat] = direct;
            bool any = direct.Count > 0;
            if (cat.childCategories != null)
                foreach (var child in cat.childCategories)
                    any |= BuildCaches(child); // recurse ALL children — never short-circuit (must record every node)
            if (any)
                hasStorableDescendant.Add(cat);
            return any;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "HaulersDream.ItemUnload.Title".Translate());
            y += 38f;

            Text.Font = GameFont.Small;
            string desc = "HaulersDream.ItemUnload.Desc".Translate();
            float descH = Text.CalcHeight(desc, inRect.width); // measured so long text / small UI scale never clips
            Widgets.Label(new Rect(inRect.x, y, inRect.width, descH), desc);
            y += descH + 6f;

            // Search box + "clear all rules" button share one row.
            var searchRect = new Rect(inRect.x, y, inRect.width - 130f, 26f);
            search.OnGUI(searchRect);
            var clearRect = new Rect(searchRect.xMax + 6f, y, 124f, 26f);
            if (Widgets.ButtonText(clearRect, "HaulersDream.ItemUnload.ClearAll".Translate()))
            {
                // Clear only rules for items that currently exist. Rules for items from mods that aren't loaded
                // right now are invisible here and must survive (same as the normal edit path), so we never touch
                // them — otherwise "Clear all" would silently wipe choices for temporarily-disabled mods.
                var live = new List<string>();
                foreach (var key in working.Keys)
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(key) != null)
                        live.Add(key);
                foreach (var key in live)
                    working.Remove(key);
                amountBuffers.Clear();
            }
            y += 30f;

            float bottomReserve = CloseButSize.y + 10f;
            var outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - bottomReserve);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float curY = 0f;
            var root = ThingCategoryNodeDatabase.RootNode;
            if (root != null)
                DrawCategoryChildren(root, 0, viewRect.width, ref curY);
            Widgets.EndScrollView();
            viewHeight = curY;
        }

        private void DrawCategoryChildren(TreeNode_ThingCategory node, int depth, float width, ref float curY)
        {
            foreach (var child in node.ChildCategoryNodes)
            {
                var cat = child.catDef;
                if (cat == null || hasStorableDescendant == null || !hasStorableDescendant.Contains(cat))
                    continue;
                if (search.filter.Active && !SubtreeMatches(cat))
                    continue;
                DrawCategory(child, depth, width, ref curY);
            }
            if (storableDirect != null && node.catDef != null && storableDirect.TryGetValue(node.catDef, out var defs))
                foreach (var def in defs)
                {
                    if (search.filter.Active && !search.filter.Matches(def.label))
                        continue;
                    DrawThingRow(def, depth, width, ref curY);
                }
        }

        private void DrawCategory(TreeNode_ThingCategory node, int depth, float width, ref float curY)
        {
            var cat = node.catDef;
            // While searching, every matching subtree is force-opened so results are visible.
            bool open = search.filter.Active || expanded.Contains(cat);
            var rowRect = new Rect(0f, curY, width, CatHeight);
            if (Mouse.IsOver(rowRect))
                Widgets.DrawHighlight(rowRect);
            float x = depth * Indent;
            if (!search.filter.Active)
            {
                var arrowRect = new Rect(x, curY + (CatHeight - 18f) / 2f, 18f, 18f);
                if (Widgets.ButtonImage(arrowRect, open ? TexButton.Collapse : TexButton.Reveal))
                    ToggleCategory(cat, open);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(x + 22f, curY, width - x - 22f, CatHeight);
            Widgets.Label(labelRect, node.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            if (!search.filter.Active && Widgets.ButtonInvisible(labelRect))
                ToggleCategory(cat, open);
            curY += CatHeight;
            if (open)
                DrawCategoryChildren(node, depth + 1, width, ref curY);
        }

        private void ToggleCategory(ThingCategoryDef cat, bool currentlyOpen)
        {
            if (currentlyOpen)
                expanded.Remove(cat);
            else
                expanded.Add(cat);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void DrawThingRow(ThingDef def, int depth, float width, ref float curY)
        {
            var rowRect = new Rect(0f, curY, width, RowHeight);
            if (Mouse.IsOver(rowRect))
                Widgets.DrawHighlight(rowRect);

            float x = depth * Indent + 4f;
            var iconRect = new Rect(x, curY + 2f, 24f, 24f);
            Widgets.DefIcon(iconRect, def);

            bool has = working.TryGetValue(def.defName, out var rule);
            bool keepAtMost = has && rule.mode == ItemUnloadMode.KeepAtMost;

            // Right-aligned controls: the mode button, then (for "keep at most") the amount field to its right,
            // so the row reads "… [Keep at most ▾] [25]".
            float rightEdge = width - 2f;
            Rect amtRect = default;
            if (keepAtMost)
            {
                amtRect = new Rect(rightEdge - AmountWidth, curY + 3f, AmountWidth, RowHeight - 6f);
                rightEdge = amtRect.x - 6f;
            }
            var modeRect = new Rect(rightEdge - ModeBtnWidth, curY + 2f, ModeBtnWidth, RowHeight - 4f);
            float labelRight = modeRect.x - 6f;

            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(x + 28f, curY, System.Math.Max(40f, labelRight - (x + 28f)), RowHeight);
            Widgets.Label(labelRect, def.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(rowRect) && !def.description.NullOrEmpty())
                TooltipHandler.TipRegion(rowRect, def.description);

            if (Widgets.ButtonText(modeRect, ModeLabel(has, rule)))
                OpenModeMenu(def);

            if (keepAtMost)
            {
                int amt = rule.amount;
                string buf = amountBuffers.TryGetValue(def.defName, out var b) ? b : amt.ToString();
                Widgets.TextFieldNumeric(amtRect, ref amt, ref buf, 0f, 999999f);
                amountBuffers[def.defName] = buf;
                if (amt != rule.amount)
                {
                    rule.amount = amt;
                    working[def.defName] = rule;
                }
            }

            curY += RowHeight;
        }

        private static string ModeLabel(bool has, ItemUnloadRule rule)
        {
            if (!has)
                return "HaulersDream.ItemUnload.ModeDefault".Translate();
            switch (rule.mode)
            {
                case ItemUnloadMode.KeepAll: return "HaulersDream.ItemUnload.ModeKeepAll".Translate();
                case ItemUnloadMode.KeepAtMost: return "HaulersDream.ItemUnload.ModeKeepAtMost".Translate();
                case ItemUnloadMode.UnloadAlways: return "HaulersDream.ItemUnload.ModeUnloadAlways".Translate();
                default: return "HaulersDream.ItemUnload.ModeDefault".Translate();
            }
        }

        private void OpenModeMenu(ThingDef def)
        {
            string name = def.defName;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("HaulersDream.ItemUnload.ModeDefault".Translate(), () =>
                {
                    working.Remove(name);
                    amountBuffers.Remove(name);
                }),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeKeepAll".Translate(), () =>
                    working[name] = new ItemUnloadRule(ItemUnloadMode.KeepAll)),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeKeepAtMost".Translate(), () =>
                {
                    int dflt = working.TryGetValue(name, out var r) && r.amount > 0 ? r.amount : 25;
                    working[name] = new ItemUnloadRule(ItemUnloadMode.KeepAtMost, dflt);
                    amountBuffers.Remove(name); // refresh the edit buffer from the new value
                }),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeUnloadAlways".Translate(), () =>
                    working[name] = new ItemUnloadRule(ItemUnloadMode.UnloadAlways)),
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // True if this category (or any descendant) has an ever-storable item whose label matches the search.
        private bool SubtreeMatches(ThingCategoryDef cat)
        {
            if (cat == null || hasStorableDescendant == null || !hasStorableDescendant.Contains(cat))
                return false;
            if (storableDirect.TryGetValue(cat, out var defs))
                foreach (var def in defs)
                    if (search.filter.Matches(def.label))
                        return true;
            if (cat.childCategories != null)
                foreach (var child in cat.childCategories)
                    if (SubtreeMatches(child))
                        return true;
            return false;
        }

        public override void PreClose()
        {
            base.PreClose();
            settings?.SetItemRules(working);
            HaulersDreamMod.Instance?.WriteSettings(); // persist immediately
        }
    }
}
