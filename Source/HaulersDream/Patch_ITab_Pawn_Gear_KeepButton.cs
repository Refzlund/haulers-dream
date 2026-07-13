using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// #197 — a per-item "keep N in inventory" control on the pawn Gear tab. A POSTFIX on the private
    /// <c>ITab_Pawn_Gear.DrawThingRow(ref float y, float width, Thing thing, bool inventory)</c> draws a compact chip
    /// on the RIGHT of each INVENTORY row: always visible (with the kept amount) for a def this pawn is keeping, and
    /// appearing on hover for any other inventory item so the player can start keeping it. Clicking the chip opens a
    /// vanilla <see cref="Dialog_Slider"/> (0..held) to set the exact amount; 0 stops keeping. The write is routed
    /// through the MP-synced <see cref="MultiplayerCompat.SetKeptCount"/> so it replicates in multiplayer.
    ///
    /// A POSTFIX is deliberately non-invasive: it never alters vanilla's row layout, so it coexists with the many
    /// other mods that prefix/transpile <c>DrawThingRow</c> (Common Sense, quality-colour mods, inventory tweaks).
    /// The postfix cannot see vanilla's internal <c>rect</c>, so it re-derives the row rect from the (post-increment)
    /// <c>y</c> and reserves the same right-edge band vanilla uses for its mass readout + info/drop/ingest buttons, so
    /// the chip clears vanilla's own controls. (Against a mod that ADDS yet more right-edge buttons the chip can
    /// overlap — the inherent limit of layering onto a fixed row; it never overlaps vanilla itself.)
    ///
    /// Inert unless the Gear-tab keep control is enabled in mod options and the pawn is a player-controlled colonist
    /// (the same audience that gets vanilla's Drop button on inventory rows). Gated by <see cref="Prepare"/> so the
    /// whole patch is skipped when the target method can't be resolved (a RimWorld rename), leaving the tab untouched.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
    public static class Patch_ITab_Pawn_Gear_KeepButton
    {
        private const float RowHeight = 28f;   // ITab_Pawn_Gear.ThingRowHeight (decompile-verified)
        private const float ChipW = 40f;
        private const float ChipH = 20f;

        // Reflected once: the Gear tab's currently-shown pawn (protected getter). Null if a RimWorld rename dropped it
        // → Prepare disables the whole patch, so the tab draws exactly as vanilla.
        private static readonly MethodInfo SelPawnForGearGetter =
            AccessTools.PropertyGetter(typeof(ITab_Pawn_Gear), "SelPawnForGear");

        // Per-frame memo of the reflected pawn + its comp, so the reflection Invoke + GetComp run ONCE per frame
        // instead of once per inventory row (the shown pawn is stable within a frame). Keyed on (instance, frame);
        // OnGUI's multiple events per frame share Time.frameCount, so the whole tab reuses one lookup.
        private static ITab_Pawn_Gear cachedInstance;
        private static int cachedFrame = -1;
        private static Pawn cachedPawn;
        private static CompHauledToInventory cachedComp;

        static bool Prepare() => SelPawnForGearGetter != null;

        /// <summary>The shown pawn + its <see cref="CompHauledToInventory"/>, memoized per (instance, frame). Returns
        /// false when there is no controllable colonist comp to keep for.</summary>
        private static bool TryGetPawnComp(ITab_Pawn_Gear inst, out Pawn pawn, out CompHauledToInventory comp)
        {
            int frame = Time.frameCount;
            if (inst != cachedInstance || frame != cachedFrame)
            {
                cachedInstance = inst;
                cachedFrame = frame;
                cachedPawn = SelPawnForGearGetter.Invoke(inst, null) as Pawn;
                cachedComp = cachedPawn != null && cachedPawn.IsColonistPlayerControlled
                    ? cachedPawn.GetComp<CompHauledToInventory>()
                    : null;
            }
            pawn = cachedPawn;
            comp = cachedComp;
            return pawn != null && comp != null;
        }

        static void Postfix(ITab_Pawn_Gear __instance, float y, float width, Thing thing, bool inventory)
        {
            // Only inventory rows (equipment/apparel are not "kept" in the pack sense), only when the feature is on,
            // and only for a STACKABLE item: a per-def keep-count is a quantity, meaningless for a single-unique thing
            // (a weapon/apparel piece a mod stashes in the pack). Excluding stackLimit <= 1 also keeps HD's keep-count
            // out of the way of Simple Sidearms' per-(def,stuff) sidearm bookkeeping (whose stacks are stackLimit 1).
            var s = HaulersDreamMod.Settings;
            if (!inventory || thing?.def == null || s == null || !s.keepInventoryGearButton)
                return;
            if (thing.def.category != ThingCategory.Item || !thing.def.EverHaulable || thing.def.stackLimit <= 1)
                return;

            if (!TryGetPawnComp(__instance, out var pawn, out var comp))
                return;

            int keptN = comp.KeptCountOf(thing.def);
            // The postfix runs AFTER vanilla incremented y by the row height, so the row it drew sits just above.
            var row = new Rect(0f, y - RowHeight, width, RowHeight);
            bool rowHovered = Mouse.IsOver(row);
            // Draw only when there's something to show/do: an active pin (always), or the player is hovering the row
            // (offer to start keeping). A non-kept, un-hovered row is left byte-for-byte as vanilla drew it.
            if (keptN <= 0 && !rowHovered)
                return;

            // Reserve the same right-edge band vanilla carved (mass 60 + info 24 + drop 24 + ingest 24) so the chip
            // sits just left of vanilla's controls. The ingest slot is only present for food, but we reserve it
            // unconditionally so the chip can never overlap it — a small gap for non-food is purely cosmetic.
            const float VanillaRightBand = 60f + 24f + 24f + 24f;
            float chipX = width - VanillaRightBand - 4f - ChipW;
            if (chipX < 34f) // never crowd the item icon/label start on a very narrow tab
                chipX = 34f;
            var chip = new Rect(chipX, row.y + (RowHeight - ChipH) / 2f, ChipW, ChipH);

            DrawChip(chip, keptN, rowHovered);

            if (Widgets.ButtonInvisible(chip))
                OpenAmountDialog(pawn, comp, thing.def);
            if (Mouse.IsOver(chip))
            {
                string tip = keptN > 0
                    ? "HaulersDream.Keep.GearTooltipKept".Translate(keptN, thing.def.label).ToString()
                    : "HaulersDream.Keep.GearTooltipOff".Translate(thing.def.label).ToString();
                TooltipHandler.TipRegion(chip, tip);
            }
        }

        /// <summary>Draw the compact keep chip: a filled amber box with the kept count when pinned, or a faint
        /// outlined "keep" affordance when merely hovered. A hover wash lifts either state.</summary>
        private static void DrawChip(Rect chip, int keptN, bool rowHovered)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            bool chipHovered = Mouse.IsOver(chip);
            if (keptN > 0)
            {
                // Active pin: amber fill + count, so it reads as "held: N" at a glance and stays visible without hover.
                Widgets.DrawBoxSolid(chip, new Color(0.85f, 0.66f, 0.20f, chipHovered ? 0.55f : 0.38f));
                // Count in a warm off-white (white with a yellow tint). The amber fill is semi-transparent over the
                // dark Gear tab, so it reads dark; a dark count on it had poor contrast — bright warm text keeps the
                // number legible without hover while staying in the amber theme.
                GUI.color = new Color(1f, 0.95f, 0.75f);
                Widgets.Label(chip, keptN.ToString());
                GUI.color = new Color(0.85f, 0.66f, 0.20f, 0.9f);
                Widgets.DrawBox(chip);
            }
            else
            {
                // Hover-only affordance to START keeping: a faint outlined "keep".
                Widgets.DrawBoxSolid(chip, new Color(1f, 1f, 1f, chipHovered ? 0.16f : 0.07f));
                GUI.color = new Color(0.85f, 0.86f, 0.9f, chipHovered ? 0.95f : 0.6f);
                Widgets.Label(chip, "HaulersDream.Keep.GearChip".Translate());
                Widgets.DrawBox(chip);
            }
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>Open the vanilla amount slider (0..what the pawn holds) to set the keep pin for this def, then
        /// apply it through the MP-synced setter. Default = the current pin, or the held amount when starting fresh.</summary>
        private static void OpenAmountDialog(Pawn pawn, CompHauledToInventory comp, ThingDef def)
        {
            int held = HeldOfDef(pawn, def);
            int cur = comp.KeptCountOf(def);
            int max = Mathf.Max(held, cur, 1);
            var pawnLocal = pawn;
            var defLocal = def;
            Find.WindowStack.Add(new Dialog_Slider(
                n => "HaulersDream.Keep.GearSliderLabel".Translate(n, defLocal.label),
                0, max,
                n => MultiplayerCompat.SetKeptCount(pawnLocal, defLocal, n),
                startingValue: cur > 0 ? cur : held));
        }

        /// <summary>Total units of <paramref name="def"/> across the pawn's inventory stacks (UI-path read only).</summary>
        private static int HeldOfDef(Pawn pawn, ThingDef def)
        {
            var owner = pawn?.inventory?.innerContainer;
            if (owner == null || def == null)
                return 0;
            int n = 0;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t != null && t.def == def)
                    n += t.stackCount;
            }
            return n;
        }
    }
}
