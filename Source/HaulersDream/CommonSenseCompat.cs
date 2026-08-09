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
    /// takes over whenever adv_cleaning || adv_haul_all_ings (both default true).
    ///
    /// <para>THE TWO FACTS, kept apart on purpose (issue #243). Taking the driver is not the same as doing the
    /// gathering, and this bridge exposes one property for each:</para>
    /// <list type="bullet">
    /// <item><see cref="GathersIngredients"/> — adv_haul_all_ings. CS pockets the ingredients itself and then
    /// re-deposits them on the bench floor, which against HD's own gather is the infinite gather → bench → unload
    /// loop. HD CEDES its gather conversions here, and the per-bench notice says so.</item>
    /// <item><see cref="OwnsDoBillDriver"/> — adv_cleaning || adv_haul_all_ings. CS holds the driver, but with the
    /// cleaning option alone its replacement chain hands the collecting straight back to vanilla's own
    /// CollectIngredientsToils. Nothing is gathered into an inventory, so HD does NOT cede — it did until v1.23.0,
    /// which is why "nothing gathers when the button is turned on either" (Lensrub, 2026-08-03). The one thing
    /// still keyed to this is the allowBatchUnderCommonSense opt-in.</item>
    /// </list>
    ///
    /// <para>Fail-open when CS absent (HD = vanilla-HD). Deliberately fail-CLOSED (cede / present-as-owning) when CS
    /// is present but its toggle fields can't be read (fork/rename) — see CommonSenseCedePolicy, which holds the
    /// reasoning and the decompiled evidence for both facts. The bool VALUES are read LIVE (cache only the type /
    /// FieldInfos), because CS toggles change at runtime.</para>
    /// </summary>
    public static class CommonSenseCompat
    {
        private static bool initialized;
        private static bool active;                 // CommonSense.Settings resolves
        private static FieldInfo advCleaningField;  // CommonSense.Settings.adv_cleaning  (static bool)
        private static FieldInfo advHaulAllField;    // CommonSense.Settings.adv_haul_all_ings (static bool)

        // Per-tick memo of the computed OwnsDoBillDriver result. The CS toggle bools only change on the settings
        // window closing (a between-ticks UI event), so the two reflective FieldInfo.GetValue(null) reads + the
        // two `is bool` box-tests are loop-invariant within a tick. It is read through
        // BatchSuppressedByCommonSense by the batch-route postfix (a per-pawn work scan) and by the bill menu
        // (every frame a bill's dropdown is open), so caching the result per tick removes 2 reflective reads +
        // 2 boxes from each of those. A 1-tick lag on a settings flip is invisible (the toggle changes between
        // ticks anyway). [ThreadStatic] per the assembly's hook-reachable-scratch convention (a worker-thread work
        // scan gets its own slot).
        [System.ThreadStatic] private static int ownsCacheTick;
        [System.ThreadStatic] private static bool ownsCacheValue;
        [System.ThreadStatic] private static bool ownsCacheValid;

        // GathersIngredients has NO memo on purpose — see the note on that property.

        // Self-register the per-tick owns-driver memo clear with the game-load hygiene sweep (see CacheRegistry).
        // This closes a gap: the memo was previously NEVER cleared on load, so a cross-session quickload landing on
        // the same TicksGame could briefly serve the previous game's value on the main thread until the tick
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
        /// True when Common Sense's Prefix replaces the vanilla DoBill toil chain — adv_cleaning OR
        /// adv_haul_all_ings. Live-read (CS toggles are runtime-mutable), memoized per tick.
        ///
        /// <para>→ GOTCHA: this is NOT the cede test. Use <see cref="GathersIngredients"/> for that. Ceding on
        /// driver ownership is issue #243: with cleaning on and haul-all off, CS owns the driver and gathers
        /// nothing, so a cede here left NOBODY gathering. The one legitimate reader is
        /// <see cref="BatchSuppressedByCommonSense"/>, whose opt-in promises the player "hand all cooking and
        /// crafting over to Common Sense" — a promise about who is in charge of the bill, which is exactly driver
        /// ownership, so it keeps meaning what it always meant.</para>
        /// </summary>
        public static bool OwnsDoBillDriver
        {
            get
            {
                if (!initialized)
                    Init();
                if (!active)
                    return false; // CS absent: fail-open, no reflection (the cheapest path — never touches the memo)
                // Per-tick memo: the CS toggles are runtime-mutable only on settings-window close, so within one
                // tick the two reflective reads are invariant. Recompute once per tick, reuse across every probe
                // that tick.
                //
                // Read the tick through Current.Game, NOT Find.TickManager: Find.TickManager is a plain
                // `Current.Game.tickManager` property, so `Find.TickManager?.X` null-checks the RESULT and still
                // throws when there is no game at all (main menu, GenScene.GoToMainMenu nulls Current.Game).
                // The -1 fallback then forces a recompute, which is correct outside a game.
                int tick = Current.Game?.tickManager?.TicksGame ?? -1;
                if (ownsCacheValid && ownsCacheTick == tick)
                    return ownsCacheValue;
                // Both options, and both must read as real bools — this fact is the OR of the two, so a fork that
                // moved either one leaves HD unable to prove the driver is free.
                bool cleaningRead = TryReadOption(advCleaningField, out bool ac);
                bool haulAllRead = TryReadOption(advHaulAllField, out bool ah);
                bool readable = cleaningRead && haulAllRead;
                bool owns = CommonSenseCedePolicy.CommonSenseOwnsDoBillDriver(active, readable, ac, ah);
                ownsCacheTick = tick;
                ownsCacheValue = owns;
                ownsCacheValid = true;
                return owns;
            }
        }

        /// <summary>
        /// Is Common Sense actually POCKETING bill ingredients right now — its haul-all-ingredients option on?
        ///
        /// <para>This is the ONE fact both halves of issue #243 turn on. HD's gather conversions stand down exactly
        /// here (the inventory-route conversion and the carried-stock injection that completes it), and the
        /// per-bench notice reports exactly here. Deliberately one property and not two: the shipped bug was the
        /// cede reading driver ownership while the notice read gathering, so a player who had turned Common Sense's
        /// gathering off got neither mod's gather and no explanation.</para>
        ///
        /// <para>Unreadable field (a CS fork/rename) reads as ON, matching the fail-CLOSED stance on the same
        /// drift: CS ships the option ON, and a notice that names the option to look at is still the most useful
        /// thing to say.</para>
        /// </summary>
        public static bool GathersIngredients
        {
            get
            {
                if (!initialized)
                    Init();
                if (!active)
                    return false; // CS absent: nothing foreign is gathering (cheapest path — never touches the memo)
                // DELIBERATELY NOT MEMOIZED, unlike OwnsDoBillDriver, even though this is now read on the work-scan
                // path too. It would be actively WRONG here on two counts, and both bite exactly the player this
                // fix is for: a tick-keyed memo never expires while the game is PAUSED, which is precisely when
                // someone alt-tabs to Common Sense's options and turns this very setting off (they would come back
                // to a bench button still telling them to turn off something they just turned off); and reading the
                // tick at all drags in Current.Game, which does not exist when mod options are opened from the main
                // menu. The cost it saves is one FieldInfo.GetValue + one box, paid only by players who actually
                // run Common Sense and only on a bill probe that got past every cheap gate ahead of it.
                // Only adv_haul_all_ings is consulted, so only IT has to be readable: a fork that moved the
                // unrelated cleaning field must not push HD onto the fail-closed path for a fact it can still read
                // exactly.
                bool readable = TryReadOption(advHaulAllField, out bool ah);
                return CommonSenseCedePolicy.CommonSenseGathersIngredients(active, readable, ah);
            }
        }

        /// <summary>
        /// Read one of Common Sense's static bool option fields by reflection.
        /// </summary>
        /// <param name="field">The bound field, or null when <see cref="Init"/> could not resolve it.</param>
        /// <param name="value">The option's live value, or false when it could not be read — never trust this
        /// without the return value, since "off" and "unknown" mean opposite things to the cede.</param>
        /// <returns>True only when the field bound AND still holds a bool.</returns>
        /// <remarks>
        /// The <c>is bool</c> test is not defensive noise: a fork is free to change an option's TYPE as well as
        /// its name, and a field that binds but yields a non-bool has to count as unreadable so the caller lands
        /// on the fail-CLOSED branch rather than silently reading it as "off".
        /// </remarks>
        private static bool TryReadOption(FieldInfo field, out bool value)
        {
            object raw = field?.GetValue(null);
            if (raw is bool b)
            {
                value = b;
                return true;
            }
            value = false;
            return false;
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
        /// actually batch (it falls back to CS's one-at-a-time cook flow). Exactly <see cref="OwnsDoBillDriver"/>
        /// AND the <c>allowBatchUnderCommonSense</c> opt-in being OFF (the opt-in defaults ON, so this is normally
        /// false even under CS). Single source of truth for both (a) the batch-route conversion gate
        /// (Patch_WorkGiver_DoBill_BatchRoute) and (b) hiding the "Batch: …" dropdown options + row marker
        /// (Patch_BillRepeatMode_Batch), so the player is never offered or shown a batch mode that won't run.
        /// False whenever CS doesn't hold the driver (CS absent, or its cleaning/haul-all both off) or the opt-in
        /// is on.
        /// </summary>
        public static bool BatchSuppressedByCommonSense
            => OwnsDoBillDriver && !(HaulersDreamMod.Settings?.allowBatchUnderCommonSense ?? true);

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
            HDLog.Msg("Common Sense detected — HD cedes the crafting-ingredient gather to it whenever its "
                        + "haul-all-ingredients option is on"
                        + (readable ? "." : " (toggle fields unresolved — treating CS as gathering as a safe fallback)."));
            if (!readable)
                // CS is present (Settings resolved) but its toggle fields did not bind (a CS fork/version renamed
                // them) — HD fail-CLOSED here (always cedes the gather to CS, so the gather->bench->unload loop
                // can't reopen), but surface the drift: HD's own ingredient-gather conversions stay OFF whenever CS
                // is installed, even if the player has CS's adv_cleaning/adv_haul_all_ings turned off.
                HDLog.Warn("Common Sense present but Settings.adv_cleaning"
                           + (advCleaningField == null ? " (UNRESOLVED)" : "")
                           + " / adv_haul_all_ings" + (advHaulAllField == null ? " (UNRESOLVED)" : "")
                           + " did not resolve; HD ceding the crafting-ingredient gather to CS unconditionally "
                           + "(its own gather conversions stay off while CS is installed).");
        }
    }
}
