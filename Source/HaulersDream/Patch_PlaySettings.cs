using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Adds the "Remember plan" INTERFACE TOGGLE to the bottom-right play-settings row (the same row that holds
    /// vanilla's Show Zones / Beauty / Roof-overlay toggles). It is the MASTER SWITCH for one-click remembered routes
    /// (<see cref="HaulersDreamSettings.rememberPlan"/>): when ON, a target type that has an explicit saved template
    /// (created with the "Remember" button in a plan dialog — see <see cref="DrawRememberButton"/>) shows "Plan
    /// prioritized … (remembered)" and runs it in one click (see <see cref="FloatMenuOptionProvider_PlanRoute"/> /
    /// <see cref="Dialog_PlanRoute.ExecuteRemembered"/>); when OFF, the planner dialog always opens. The toggle alone
    /// never fabricates a "(remembered)" option — a type with no saved template keeps the plain "Plan prioritized …".
    ///
    /// <para>The toggle registers a <see cref="UIHighlighter"/> opportunity under <see cref="RememberPlanHighlightTag"/>,
    /// so hovering the "Plan prioritized …" float-menu option or the in-dialog "Remember" button (both fire
    /// <c>UIHighlighter.HighlightTag</c> with the same tag) makes this button flash — teaching the player where the
    /// one-click behaviour is switched on. Only shown on the MAP controls (not the world view) and only while the
    /// route planner feature is enabled.</para>
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettings
    {
        /// <summary>UIHighlighter tag shared by this toggle (registers the opportunity) and the plan float-menu
        /// option (triggers the flash on hover).</summary>
        public const string RememberPlanHighlightTag = "HaulersDream.RememberPlan";

        // The bookmark-check glyph (white, tinted by the game like every interface toggle). BadTex fallback keeps the
        // toggle usable — as a plain button — if the texture is ever missing, rather than throwing at draw time.
        private static readonly Texture2D RememberPlanIcon =
            ContentFinder<Texture2D>.Get("HaulersDream/Interface/RememberPlan", false) ?? BaseContent.BadTex;

        static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView)
                return; // an in-game map control, not a world-view one
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.planRoutes)
                return; // the route planner feature is off → the toggle would control nothing

            bool val = s.rememberPlan;
            row.ToggleableIcon(ref val, RememberPlanIcon, "HaulersDream.PlanRoute.RememberToggleTip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle, RememberPlanHighlightTag);
            if (val != s.rememberPlan)
            {
                s.rememberPlan = val;
                HaulersDreamMod.Instance?.WriteSettings(); // persist the flip immediately (global mod setting)
            }
        }

        /// <summary>
        /// Draw the "Remember" BUTTON inside a plan dialog. Pressing it saves the dialog's CURRENT settings as the
        /// explicit remembered template for this specific target type (overwriting any previous one) — the
        /// <paramref name="onRemember"/> callback does the actual save. That saved template is what makes the type's
        /// right-click menu read "(remembered)" and run in one click (while the interface toggle is on), and is a
        /// SEPARATE layer from the per-instance settings the dialog auto-restores on close.
        ///
        /// <para>While the button is hovered it flashes the bottom-right interface toggle (via
        /// <see cref="RememberPlanHighlightTag"/>, the yellow blink), so the player sees the button and the master
        /// switch are one feature. Shared by the thing / sow / remove-floor plan dialogs.</para>
        /// </summary>
        public static void DrawRememberButton(Listing_Standard l, System.Action onRemember)
        {
            var row = l.GetRect(32f);
            if (Mouse.IsOver(row))
                UIHighlighter.HighlightTag(RememberPlanHighlightTag); // blink the interface toggle to show the link
            if (Widgets.ButtonText(row, "HaulersDream.PlanRoute.RememberButton".Translate()))
                onRemember?.Invoke();
            TooltipHandler.TipRegion(row, "HaulersDream.PlanRoute.RememberButtonDesc".Translate());
        }
    }
}
