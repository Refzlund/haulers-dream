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

        // Per-category aggregate shown on each category row's button: the single rule shared by EVERY eligible def in
        // that category's subtree, or "Mixed" when they differ. Instance state (depends on `working`), computed lazily
        // for the categories actually drawn and reused across frames; the WHOLE cache is dropped (catAggDirty) on any
        // rule edit, so a draw walks a subtree only on the first frame after a change — never per frame (CE's ammo
        // tree is huge). Every `working` write goes through PutRule/DropRule (or sets the flag) so it can't go stale.
        private readonly Dictionary<ThingCategoryDef, CatAgg> catAggCache = new Dictionary<ThingCategoryDef, CatAgg>();
        private bool catAggDirty = true;

        // Built once — def data never changes at runtime. Which categories contain any eligible item, the eligible
        // defs directly under each category (sorted by label), and the full recursive set of eligible defs under
        // each category's subtree (used by the per-category bulk-set button — precomputed so a click is O(k) and we
        // never re-walk a potentially huge subtree, e.g. CE's ammo tree, on the GUI thread). Lets us skip empty
        // categories and avoids re-walking + re-filtering the whole def tree every frame.
        //
        // "Eligible" = anything a pawn can hold/haul in its inventory, since this dialog governs INVENTORY unload
        // rules (not stockpile storage). That is broader than EverStorable(false), which omits items a pawn can
        // carry but that aren't stockpile-storable. See BuildCaches for the exact predicate + rationale.
        private static Dictionary<ThingCategoryDef, List<ThingDef>> eligibleDirect;
        private static HashSet<ThingCategoryDef> hasEligibleDescendant;
        private static Dictionary<ThingCategoryDef, List<ThingDef>> subtreeDefs;

        private const float RowHeight = 28f;
        private const float CatHeight = 26f;
        private const float Indent = 14f;
        private const float ModeBtnWidth = 132f;
        private const float AmountWidth = 58f;
        // Default "keep at most" amount applied by the per-category bulk-set (and a sensible per-item starting point).
        private const int DefaultKeepAtMost = 25;

        // The shared state of a category's whole eligible subtree, shown on its row button.
        private enum CatAgg { Default, KeepAll, KeepAtMost, UnloadAlways, Mixed }

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
            if (eligibleDirect != null)
                return;
            eligibleDirect = new Dictionary<ThingCategoryDef, List<ThingDef>>();
            hasEligibleDescendant = new HashSet<ThingCategoryDef>();
            subtreeDefs = new Dictionary<ThingCategoryDef, List<ThingDef>>();
            var root = ThingCategoryNodeDatabase.RootNode;
            if (root?.catDef != null)
                BuildCaches(root.catDef);
        }

        // True if a def should appear in this picker. The universe is every item in the game and installed mods
        // (the same set a workbench bill's category filter covers), so the predicate is simply ThingCategory.Item.
        // This never drops anything the dialog showed before: the old filter was def.EverStorable(false), which
        // returns true for every Item already in the category tree (its `category == ThingCategory.Item` branch always
        // hits, because a def only reaches a category's `childThingDefs` when its thingCategories is non-empty —
        // decompile-verified), so "all Items" is a superset. Expressing it directly as ThingCategory.Item is clearer
        // than the storable test and keeps the picker comprehensive. A rule on an item a pawn can't actually carry is
        // simply inert — the unload check only reads a rule when that item is in inventory — and fully fallback-safe.
        // Corpse defs are ThingCategory.Item (ThingDefGenerator_Corpses) but are never inventory loot — a pawn can't
        // pocket a corpse — so we skip them explicitly, mirroring YieldRouter (corpses keep their own hauling/rot flow).
        private static bool IsEligible(ThingDef def)
        {
            return def != null && def.category == ThingCategory.Item && !def.IsCorpse;
        }

        // Records each category's direct eligible defs and the recursive eligible-defs-under-subtree list; returns
        // true if this category or any descendant holds an eligible def.
        private static bool BuildCaches(ThingCategoryDef cat)
        {
            if (cat == null)
                return false;
            var direct = new List<ThingDef>();
            if (cat.childThingDefs != null)
                foreach (var def in cat.childThingDefs)
                    if (IsEligible(def))
                        direct.Add(def);
            direct.Sort((a, b) => string.Compare(a.label ?? a.defName, b.label ?? b.defName,
                System.StringComparison.OrdinalIgnoreCase));
            eligibleDirect[cat] = direct;

            // Subtree accumulator = this category's direct eligible defs + every descendant's. Built bottom-up from
            // the recursive calls below so each node is walked exactly once (no per-frame, no re-recursion on click).
            var subtree = new List<ThingDef>(direct);
            bool any = direct.Count > 0;
            if (cat.childCategories != null)
                foreach (var child in cat.childCategories)
                {
                    any |= BuildCaches(child); // recurse ALL children — never short-circuit (must record every node)
                    if (subtreeDefs.TryGetValue(child, out var childSubtree))
                        subtree.AddRange(childSubtree);
                }
            subtreeDefs[cat] = subtree;
            if (any)
                hasEligibleDescendant.Add(cat);
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
                catAggDirty = true; // rules changed -> category aggregates recompute on the next draw
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
                if (cat == null || hasEligibleDescendant == null || !hasEligibleDescendant.Contains(cat))
                    continue;
                if (search.filter.Active && !SubtreeMatches(cat))
                    continue;
                DrawCategory(child, depth, width, ref curY);
            }
            if (eligibleDirect != null && node.catDef != null && eligibleDirect.TryGetValue(node.catDef, out var defs))
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

            // Right-aligned category button, sized like the per-item mode button for visual consistency. Its LABEL is
            // the category's aggregate state — the single rule shared by every item in the subtree (Default / Never
            // unload / Keep at most / Always unload), or "Mixed" when they differ — so a category row reads at a glance
            // like an item row does, and editing one item flips its categories to "Mixed". Clicking it opens the
            // bulk-set menu (the tooltip explains that). Only shown when NOT searching — same as the expand arrow
            // above: during a search the tree is force-opened and the rows are a FILTERED subset, so a category-level
            // bulk action there would be ambiguous (apply to the visible matches, or the whole subtree?). Hiding it
            // sidesteps that and keeps search purely about finding individual items; the button is always available
            // with search cleared, where it unambiguously applies to the full subtree.
            float labelWidth = width - x - 22f;
            if (!search.filter.Active)
            {
                var catBtnRect = new Rect(width - ModeBtnWidth - 2f, curY + (CatHeight - (RowHeight - 4f)) / 2f,
                    ModeBtnWidth, RowHeight - 4f);
                TooltipHandler.TipRegion(catBtnRect, "HaulersDream.ItemUnload.SetCategoryTip".Translate());
                if (Widgets.ButtonText(catBtnRect, CatAggLabel(GetCatAgg(cat))))
                    OpenCategoryMenu(cat);
                // Shrink the label/invisible-toggle rect so it no longer overlaps the bulk-set button (a click on
                // the button must not also toggle the fold).
                labelWidth = catBtnRect.x - 6f - (x + 22f);
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(x + 22f, curY, System.Math.Max(40f, labelWidth), CatHeight);
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
                    PutRule(def.defName, rule);
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
                    DropRule(name);
                    amountBuffers.Remove(name);
                }),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeKeepAll".Translate(), () =>
                    PutRule(name, new ItemUnloadRule(ItemUnloadMode.KeepAll))),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeKeepAtMost".Translate(), () =>
                {
                    int dflt = working.TryGetValue(name, out var r) && r.amount > 0 ? r.amount : DefaultKeepAtMost;
                    PutRule(name, new ItemUnloadRule(ItemUnloadMode.KeepAtMost, dflt));
                    amountBuffers.Remove(name); // refresh the edit buffer from the new value
                }),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeUnloadAlways".Translate(), () =>
                    PutRule(name, new ItemUnloadRule(ItemUnloadMode.UnloadAlways))),
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Bulk-set menu for a whole category subtree — the same four modes as the per-item menu, applied to every
        // eligible def under this category at once (its own direct defs + all descendant categories'). Like a
        // workbench bill's category filter: set big groups (e.g. CE's dozens of ammo types) in one action instead
        // of item-by-item. Operates ONLY on the precomputed live-def subtree list, so working-dict entries for
        // items from absent mods are never touched — same fallback-safety invariant as "Clear all" and the
        // per-item path.
        private void OpenCategoryMenu(ThingCategoryDef cat)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("HaulersDream.ItemUnload.ModeDefault".Translate(), () =>
                    ApplyToCategory(cat, isDefault: true, default)),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeKeepAll".Translate(), () =>
                    ApplyToCategory(cat, isDefault: false, new ItemUnloadRule(ItemUnloadMode.KeepAll))),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeKeepAtMost".Translate(), () =>
                    // Shared sensible default; the player tweaks individual amounts afterwards.
                    ApplyToCategory(cat, isDefault: false, new ItemUnloadRule(ItemUnloadMode.KeepAtMost, DefaultKeepAtMost))),
                new FloatMenuOption("HaulersDream.ItemUnload.ModeUnloadAlways".Translate(), () =>
                    ApplyToCategory(cat, isDefault: false, new ItemUnloadRule(ItemUnloadMode.UnloadAlways))),
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Applies one outcome to every eligible def in the category's subtree. isDefault removes the working-dict
        // entry (back to "Default"); otherwise it writes a copy of the given rule. Amount buffers are always cleared
        // for the affected defs so any "keep at most" field re-reads from the freshly written value. O(k) over the
        // precomputed subtree — no tree walk here.
        private void ApplyToCategory(ThingCategoryDef cat, bool isDefault, ItemUnloadRule rule)
        {
            if (cat == null || subtreeDefs == null || !subtreeDefs.TryGetValue(cat, out var defs))
                return;
            foreach (var def in defs)
            {
                string name = def.defName;
                if (isDefault)
                    working.Remove(name);
                else
                    working[name] = new ItemUnloadRule(rule.mode, rule.amount);
                amountBuffers.Remove(name);
            }
            catAggDirty = true; // the whole subtree changed -> category aggregates recompute on the next draw
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // Single funnel for per-item rule writes so the category-aggregate cache is invalidated with every edit.
        private void PutRule(string defName, ItemUnloadRule rule)
        {
            working[defName] = rule;
            catAggDirty = true;
        }

        private void DropRule(string defName)
        {
            working.Remove(defName);
            catAggDirty = true;
        }

        // The aggregate label shown on a category row button. Lazily computed from the precomputed subtree + the live
        // `working` rules and cached; the cache is dropped whole whenever a rule changes (catAggDirty), so a draw
        // recomputes a category at most once per edit, then reads O(1) every frame after.
        private CatAgg GetCatAgg(ThingCategoryDef cat)
        {
            if (catAggDirty)
            {
                catAggCache.Clear();
                catAggDirty = false;
            }
            if (catAggCache.TryGetValue(cat, out var agg))
                return agg;
            agg = ComputeCatAgg(cat);
            catAggCache[cat] = agg;
            return agg;
        }

        // Default when every eligible def in the subtree has no rule; one of the three modes when they ALL carry the
        // identical rule (same mode, and same amount too for KeepAtMost); Mixed otherwise. A def can appear more than
        // once in the subtree list (multi-category membership) — harmless, since the same def carries the same rule.
        private CatAgg ComputeCatAgg(ThingCategoryDef cat)
        {
            if (subtreeDefs == null || !subtreeDefs.TryGetValue(cat, out var defs) || defs.Count == 0)
                return CatAgg.Default;
            bool first = true;
            bool firstHasRule = false;
            ItemUnloadRule firstRule = default;
            foreach (var def in defs)
            {
                bool hasRule = working.TryGetValue(def.defName, out var rule);
                if (first)
                {
                    first = false;
                    firstHasRule = hasRule;
                    firstRule = rule;
                    continue;
                }
                if (hasRule != firstHasRule)
                    return CatAgg.Mixed;
                if (hasRule && (rule.mode != firstRule.mode
                                || (rule.mode == ItemUnloadMode.KeepAtMost && rule.amount != firstRule.amount)))
                    return CatAgg.Mixed;
            }
            if (!firstHasRule)
                return CatAgg.Default;
            switch (firstRule.mode)
            {
                case ItemUnloadMode.KeepAll: return CatAgg.KeepAll;
                case ItemUnloadMode.KeepAtMost: return CatAgg.KeepAtMost;
                case ItemUnloadMode.UnloadAlways: return CatAgg.UnloadAlways;
                default: return CatAgg.Default;
            }
        }

        private static string CatAggLabel(CatAgg agg)
        {
            switch (agg)
            {
                case CatAgg.KeepAll: return "HaulersDream.ItemUnload.ModeKeepAll".Translate();
                case CatAgg.KeepAtMost: return "HaulersDream.ItemUnload.ModeKeepAtMost".Translate();
                case CatAgg.UnloadAlways: return "HaulersDream.ItemUnload.ModeUnloadAlways".Translate();
                case CatAgg.Mixed: return "HaulersDream.ItemUnload.ModeMixed".Translate();
                default: return "HaulersDream.ItemUnload.ModeDefault".Translate();
            }
        }

        // True if this category (or any descendant) has an eligible item whose label matches the search.
        private bool SubtreeMatches(ThingCategoryDef cat)
        {
            if (cat == null || hasEligibleDescendant == null || !hasEligibleDescendant.Contains(cat))
                return false;
            if (eligibleDirect.TryGetValue(cat, out var defs))
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
