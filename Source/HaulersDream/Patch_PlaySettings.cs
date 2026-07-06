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
    // StaticConstructorOnStartup: RememberPlanIcon loads via ContentFinder in a static field initializer.
    // Without this, the type can be initialized before game content is loaded (or off the main thread),
    // the icon silently falls back to BadTex (magenta), and RimWorld's startup scan logs a
    // "probably needs a StaticConstructorOnStartup attribute" warning. Coexists fine with HarmonyPatch.
    [StaticConstructorOnStartup]
    public static class Patch_PlaySettings
    {
        /// <summary>UIHighlighter tag shared by this toggle (registers the opportunity) and the plan float-menu
        /// option (triggers the flash on hover).</summary>
        public const string RememberPlanHighlightTag = "HaulersDream.RememberPlan";

        // The bookmark-check glyph (white, tinted by the game like every interface toggle). BadTex fallback keeps the
        // toggle usable — as a plain button — if the texture is ever missing, rather than throwing at draw time.
        private static readonly Texture2D RememberPlanIcon =
            ContentFinder<Texture2D>.Get("HaulersDream/Interface/RememberPlan", false) ?? BaseContent.BadTex;

        /// <summary>Screen-space rect of the interface toggle icon, captured each frame while it is drawn. Read by
        /// <see cref="DrawRememberArrow"/> so a hovered in-dialog "Remember" button can draw a pointer arrow to it.
        /// null until the toggle has been drawn at least once (e.g. before any map controls this session).</summary>
        public static Rect? RememberToggleScreenRect;

        static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView)
                return; // an in-game map control, not a world-view one
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.planRoutes)
                return; // the route planner feature is off → the toggle would control nothing

            // Capture the toggle icon's screen rect for the pointer arrow. WidgetRow grows either left or right;
            // ToggleableIcon draws a 24px icon at the current X then advances, so the sign of the X delta tells us
            // which side the icon sits on. FinalX/FinalY are the row's live cursor (screen space — the play-settings
            // row has no window group).
            float xBefore = row.FinalX, yBefore = row.FinalY;
            bool val = s.rememberPlan;
            row.ToggleableIcon(ref val, RememberPlanIcon, "HaulersDream.PlanRoute.RememberToggleTip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle, RememberPlanHighlightTag);
            float iconX = row.FinalX > xBefore ? xBefore : xBefore - 24f;
            RememberToggleScreenRect = new Rect(iconX, yBefore, 24f, 24f);
            if (val != s.rememberPlan)
            {
                s.rememberPlan = val;
                HaulersDreamMod.Instance?.WriteSettings(); // persist the flip immediately (global mod setting)
            }
        }

        /// <summary>
        /// Draw the "Remember" BUTTON at the bottom of a plan dialog. Pressing it saves the dialog's CURRENT settings
        /// as the explicit remembered template for this specific target type (overwriting any previous one) — the
        /// <paramref name="onRemember"/> callback does the actual save. That saved template is what makes the type's
        /// right-click menu read "(remembered)" and run in one click (while the interface toggle is on), and is a
        /// SEPARATE layer from the per-instance settings the dialog auto-restores on close.
        ///
        /// <para>When <paramref name="alreadyRemembered"/> (the current settings already ARE this type's saved
        /// template) the button is drawn as a faint, disabled "Already remembered" outline — nothing to save until a
        /// setting changes. While the button is hovered (either state) it flashes the bottom-right interface toggle
        /// (via <see cref="RememberPlanHighlightTag"/>); the dialog additionally draws a pointer arrow to it from its
        /// <c>ExtraOnGUI</c>. Shared by the thing / sow / remove-floor plan dialogs.</para>
        /// </summary>
        public static void DrawRememberButton(Rect row, bool alreadyRemembered, System.Action onRemember)
        {
            if (Mouse.IsOver(row))
                UIHighlighter.HighlightTag(RememberPlanHighlightTag); // blink the interface toggle to show the link
            if (alreadyRemembered)
            {
                DrawFaintDisabledButton(row, "HaulersDream.PlanRoute.RememberButtonSaved".Translate());
                TooltipHandler.TipRegion(row, "HaulersDream.PlanRoute.RememberButtonSavedDesc".Translate());
            }
            else
            {
                if (Widgets.ButtonText(row, "HaulersDream.PlanRoute.RememberButton".Translate()))
                    onRemember?.Invoke();
                TooltipHandler.TipRegion(row, "HaulersDream.PlanRoute.RememberButtonDesc".Translate());
            }
        }

        // A disabled-looking button: no wood texture, transparent interior, a faint 1px outline and faint centered
        // text — reads as "present but inert" without competing with the live Cancel/Append/Replace buttons.
        private static void DrawFaintDisabledButton(Rect rect, string label)
        {
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.30f);
            Widgets.DrawBox(rect, 1);
            var prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label); // inherits the faint GUI.color → faint text
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }

        /// <summary>
        /// Draw a pointer arrow from the in-dialog "Remember" button to the bottom-right interface toggle, called from
        /// a plan dialog's <c>ExtraOnGUI</c> (screen space, outside the window's clip group) while the button is
        /// hovered. Strengthens the "these are the same feature" cue that the yellow toggle-blink starts.
        /// </summary>
        /// <param name="windowRect">The dialog's window rect; the button rect is window-local (the content is drawn
        /// inside a GUI group at this rect), so we add its origin to get screen space.</param>
        /// <param name="localButtonRect">The "Remember" button rect in the dialog's local coordinates.</param>
        public static void DrawRememberArrow(Rect windowRect, Rect localButtonRect)
        {
            if (RememberToggleScreenRect == null)
                return;
            var toggle = RememberToggleScreenRect.Value;
            // Window content is drawn inside a group inset by the window Margin (Window.InnerWindowOnGUI does
            // BeginGroup(windowRect.AtZero().ContractedBy(Margin)) then DoWindowContents at zero), so the local→screen
            // map is windowRect origin + the margin + the local position. The 3 plan dialogs use the default margin.
            const float margin = Window.StandardMargin;
            var btn = new Rect(localButtonRect.x + windowRect.x + margin, localButtonRect.y + windowRect.y + margin,
                localButtonRect.width, localButtonRect.height);
            Vector2 start = new Vector2(btn.xMax, btn.center.y);
            Vector2 dir = (toggle.center - start).normalized;
            if (dir == Vector2.zero)
                return;
            // Stop the tip just short of the icon so the arrowhead points AT it rather than covering it.
            Vector2 end = toggle.center - dir * (toggle.width * 0.6f);
            var color = new Color(1f, 0.85f, 0.2f, 0.95f);
            const float width = 2.5f;
            Widgets.DrawLine(start, end, color, width);
            // Arrowhead: two short strokes from the tip, swept back off the incoming direction.
            const float headLen = 13f;
            float ang = 26f * Mathf.Deg2Rad;
            Vector2 back = -dir;
            Vector2 h1 = new Vector2(back.x * Mathf.Cos(ang) - back.y * Mathf.Sin(ang), back.x * Mathf.Sin(ang) + back.y * Mathf.Cos(ang));
            Vector2 h2 = new Vector2(back.x * Mathf.Cos(-ang) - back.y * Mathf.Sin(-ang), back.x * Mathf.Sin(-ang) + back.y * Mathf.Cos(-ang));
            Widgets.DrawLine(end, end + h1 * headLen, color, width);
            Widgets.DrawLine(end, end + h2 * headLen, color, width);
        }
    }
}
