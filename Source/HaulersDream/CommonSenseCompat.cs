using System.Reflection;
using HaulersDream.Core;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Common Sense compatibility bridge — REFLECTION ONLY, no hard assembly reference. CS
    /// (avilmask.CommonSense) replaces the vanilla JobDriver_DoBill driver via a MakeNewToils Prefix that is
    /// installed EVERY session (its Prepare() returns true with the default optimal_patching_in_use==false) and
    /// runs CS's own gather toils whenever adv_cleaning || adv_haul_all_ings (both default true). That flow
    /// re-deposits HD's in-inventory ingredients onto the bench floor, which HD then unloads and re-gathers — an
    /// infinite gather->bench->unload loop. When CS OWNS the DoBill flow, HD cedes: it does not convert automatic
    /// bills to BillPrepGather / BatchCraft, leaving CS as the single source of truth.
    ///
    /// Fail-open when CS absent (HD = vanilla-HD). Deliberately fail-CLOSED (cede) when CS is present but its
    /// toggle fields can't be read (fork/rename) — see CommonSenseCedePolicy. The two bool VALUES are read LIVE
    /// on every query (cache only the type / FieldInfos), because CS toggles change at runtime.
    /// </summary>
    public static class CommonSenseCompat
    {
        private static bool initialized;
        private static bool active;                 // CommonSense.Settings resolves
        private static FieldInfo advCleaningField;  // CommonSense.Settings.adv_cleaning  (static bool)
        private static FieldInfo advHaulAllField;    // CommonSense.Settings.adv_haul_all_ings (static bool)

        // Per-tick memo of the computed OwnsDoBillFlow result. The CS toggle bools only change on the settings
        // window closing (a between-ticks UI event), so the two reflective FieldInfo.GetValue(null) reads + the
        // two `is bool` box-tests are loop-invariant within a tick. OwnsDoBillFlow is the FIRST statement of BOTH
        // DoBill postfixes (per-pawn-scan even when HD features are off), so caching the result per tick removes
        // 2 reflective reads + 2 boxes from every crafter/cook ingredient probe. A 1-tick lag on a settings flip
        // is invisible (the toggle changes between ticks anyway). [ThreadStatic] per the assembly's
        // hook-reachable-scratch convention (a worker-thread work scan gets its own slot).
        [System.ThreadStatic] private static int ownsCacheTick;
        [System.ThreadStatic] private static bool ownsCacheValue;
        [System.ThreadStatic] private static bool ownsCacheValid;

        // GathersIngredients has NO memo on purpose — see the note on that property.

        // Self-register the per-tick owns-flow memo clear with the game-load hygiene sweep (see CacheRegistry). This
        // closes a gap: the memo was previously NEVER cleared on load, so a cross-session quickload landing on the
        // same TicksGame could briefly serve the previous game's owns-flow value on the main thread until the tick
        // advanced. The static ctor runs once on first use (the only way a memo can hold cross-session data);
        // ClearTickCaches resets the FinalizeInit (main) thread's slots — other threads' memos are per-tick
        // self-clearing, and a -1 tick forces a recompute regardless.
        static CommonSenseCompat() => CacheRegistry.Register(ClearTickCaches);

        /// <summary>Drop the main thread's per-tick memos so an equal TicksGame across a quickload cannot serve a
        /// previous session's value. Hygiene only — the next read recomputes from the live CS toggle fields
        /// (cheap reflection); the values are loop-invariant within a tick. Mirrors <see cref="PawnMassCache.Clear"/>.</summary>
        private static void ClearTickCaches()
        {
            ownsCacheValid = false;
            ownsCacheTick = -1;
            ownsCacheValue = false;
        }

        /// <summary>Whether Common Sense is loaded (its Settings type resolves). Cached.</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return active;
            }
        }

        /// <summary>
        /// True when CS owns the vanilla DoBill driver and HD must cede its own gather conversions. Live-reads
        /// adv_cleaning / adv_haul_all_ings each call (CS toggles are runtime-mutable). Present-as-owning when a
        /// field is unreadable; false (fail-open) when CS is absent.
        /// </summary>
        public static bool OwnsDoBillFlow
        {
            get
            {
                if (!initialized)
                    Init();
                if (!active)
                    return false; // CS absent: fail-open, no reflection (the cheapest path — never touches the memo)
                // Per-tick memo: the CS toggles are runtime-mutable only on settings-window close, so within one
                // tick the two reflective reads are invariant. Recompute once per tick, reuse across every DoBill
                // probe that tick.
                //
                // Read the tick through Current.Game, NOT Find.TickManager: Find.TickManager is a plain
                // `Current.Game.tickManager` property, so `Find.TickManager?.X` null-checks the RESULT and still
                // throws when there is no game at all (main menu, GenScene.GoToMainMenu nulls Current.Game).
                // The -1 fallback then forces a recompute, which is correct outside a game.
                int tick = Current.Game?.tickManager?.TicksGame ?? -1;
                if (ownsCacheValid && ownsCacheTick == tick)
                    return ownsCacheValue;
                bool readable = advCleaningField != null && advHaulAllField != null;
                bool ac = readable && advCleaningField.GetValue(null) is bool a && a;
                bool ah = readable && advHaulAllField.GetValue(null) is bool h && h;
                bool owns = CommonSenseCedePolicy.ShouldCedeDoBillFlow(active, readable, ac, ah);
                ownsCacheTick = tick;
                ownsCacheValue = owns;
                ownsCacheValid = true;
                return owns;
            }
        }

        /// <summary>
        /// The NARROW question the UI needs (issue #243): is Common Sense actually POCKETING bill ingredients right
        /// now? CS is active AND its haul-all-ingredients option is on.
        ///
        /// <para>Deliberately narrower than <see cref="OwnsDoBillFlow"/>, which also trips on CS's cleaning option
        /// alone. With cleaning alone CS still owns the driver — so HD must still cede — but its replacement toils
        /// run vanilla's carry-in-hands collect and nothing goes into an inventory. A "another mod is gathering
        /// ingredients" notice driven off <see cref="OwnsDoBillFlow"/> would therefore be FALSE in exactly that
        /// configuration, which is why this is a separate read rather than a reuse.</para>
        ///
        /// <para>Unreadable field (a CS fork/rename) reads as ON, matching the fail-CLOSED stance
        /// <see cref="OwnsDoBillFlow"/> takes on the same drift: CS ships the option ON, HD is ceding anyway, and a
        /// notice that names the option to look at is still the most useful thing to say.</para>
        /// </summary>
        public static bool GathersIngredients
        {
            get
            {
                if (!initialized)
                    Init();
                if (!active)
                    return false; // CS absent: nothing foreign is gathering (cheapest path — never touches the memo)
                // DELIBERATELY NOT MEMOIZED, unlike OwnsDoBillFlow. This is read only from render paths (a bench
                // gizmo's description, the settings tab) — one reflective field read, not a per-pawn scan — so a
                // memo saves nothing measurable. It would also be actively WRONG here on two counts: a tick-keyed
                // memo never expires while the game is PAUSED, which is exactly when a player alt-tabs to Common
                // Sense's options and turns this very setting off (they would still be told to turn off something
                // they just turned off); and reading the tick at all drags in Current.Game, which does not exist
                // when mod options are opened from the main menu.
                return advHaulAllField == null
                       || !(advHaulAllField.GetValue(null) is bool on)
                       || on;
            }
        }

        /// <summary>
        /// Common Sense's own label for the haul-all-ingredients option, read from CS's keyed translations at
        /// runtime so the notice points at a control the player can actually find in THEIR language. Falls back to
        /// the English wording CS ships when the key is absent (CS not loaded, or a fork that renamed it).
        ///
        /// <para>The key is CS's, not HD's, so it must stay out of HD's own Languages/ files — HD's translation
        /// parity guard would otherwise demand 16 copies of a string HD does not own and cannot keep in step with
        /// CS.</para>
        /// </summary>
        public static string HaulAllIngredientsOptionLabel
        {
            get
            {
                if (!HaulAllIngredientsLabelKey.CanTranslate())
                    return "Pawns are encouraged to pick up all ingredients before hauling them to the crafting place";
                string label = HaulAllIngredientsLabelKey.Translate();
                return label;
            }
        }

        /// <summary>CS's keyed id for the haul-all-ingredients option label (verified against the shipped
        /// CommonSense.dll and its Languages/*/Keyed/strings.xml, which every CS translation carries).</summary>
        private const string HaulAllIngredientsLabelKey = "advanced_haul_all_ings_label";

        /// <summary>The mod's display name, for a notice that has to say whose behaviour the player is seeing.
        /// A proper noun, so it is not translated.</summary>
        public const string ModName = "Common Sense";

        /// <summary>
        /// True when HD is ceding the BATCH-CRAFT path to Common Sense right now, so a batch-flagged bill will NOT
        /// actually batch (it falls back to CS's one-at-a-time cook flow). Exactly <see cref="OwnsDoBillFlow"/> AND
        /// the <c>allowBatchUnderCommonSense</c> opt-in being OFF (the opt-in defaults ON, so this is normally false
        /// even under CS). Single source of truth for both (a) the batch-route conversion gate
        /// (Patch_WorkGiver_DoBill_BatchRoute) and (b) hiding the "Batch: …" dropdown options + row marker
        /// (Patch_BillRepeatMode_Batch), so the player is never offered or shown a batch mode that won't run.
        /// False whenever CS doesn't own the flow (CS absent, or its cleaning/haul-all both off) or the opt-in is on.
        /// </summary>
        public static bool BatchSuppressedByCommonSense
            => OwnsDoBillFlow && !(HaulersDreamMod.Settings?.allowBatchUnderCommonSense ?? true);

        private static void Init()
        {
            initialized = true;
            // No try/catch: CS-ABSENT is the TypeByName == null precondition (it returns null, never throws).
            var settingsType = AccessTools.TypeByName("CommonSense.Settings");
            if (settingsType == null)
                return; // Common Sense not loaded — the real precondition; HD operates as vanilla-HD.
            active = true;
            advCleaningField = AccessTools.Field(settingsType, "adv_cleaning");
            advHaulAllField = AccessTools.Field(settingsType, "adv_haul_all_ings");
            bool readable = advCleaningField != null && advHaulAllField != null;
            HDLog.Msg("Common Sense detected — HD cedes the DoBill ingredient-gather flow to it"
                        + (readable ? "." : " (toggle fields unresolved — treating CS as owning the flow as a safe fallback)."));
            if (!readable)
                // CS is present (Settings resolved) but its toggle fields did not bind (a CS fork/version renamed
                // them) — HD fail-CLOSED here (always cedes the DoBill flow to CS, so the gather->bench->unload
                // loop can't reopen), but surface the drift: HD's own ingredient-gather conversions stay OFF
                // whenever CS is installed, even if the player has CS's adv_cleaning/adv_haul_all_ings turned off.
                HDLog.Warn("Common Sense present but Settings.adv_cleaning"
                           + (advCleaningField == null ? " (UNRESOLVED)" : "")
                           + " / adv_haul_all_ings" + (advHaulAllField == null ? " (UNRESOLVED)" : "")
                           + " did not resolve; HD ceding the DoBill ingredient-gather flow to CS unconditionally "
                           + "(its own gather conversions stay off while CS is installed).");
        }
    }
}
