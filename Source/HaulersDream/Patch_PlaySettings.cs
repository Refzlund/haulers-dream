using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Adds the "Remember plan" INTERFACE TOGGLE to the bottom-right play-settings row (the same row that holds
    /// vanilla's Show Zones / Beauty / Roof-overlay toggles). It mirrors <see cref="HaulersDreamSettings.rememberPlan"/>:
    /// when ON, choosing a right-click "Plan prioritized …" option reuses the settings last used on that target type
    /// in one click (see <see cref="FloatMenuOptionProvider_PlanRoute"/> / <see cref="Dialog_PlanRoute.ExecuteRemembered"/>);
    /// when OFF, the planner dialog opens as before.
    ///
    /// <para>The toggle registers a <see cref="UIHighlighter"/> opportunity under <see cref="RememberPlanHighlightTag"/>,
    /// so hovering the "Plan prioritized …" float-menu option (which fires <c>UIHighlighter.HighlightTag</c> with the
    /// same tag) makes this button flash — teaching the player where the one-click behaviour is controlled. Only shown
    /// on the MAP controls (not the world view) and only while the route planner feature is enabled.</para>
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
    }
}
