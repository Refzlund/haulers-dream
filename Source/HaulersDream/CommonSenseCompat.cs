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

        // Self-register the per-tick owns-flow memo clear with the game-load hygiene sweep (see CacheRegistry). This
        // closes a gap: the memo was previously NEVER cleared on load, so a cross-session quickload landing on the
        // same TicksGame could briefly serve the previous game's owns-flow value on the main thread until the tick
        // advanced. The static ctor runs once on first use (the only way the memo can hold cross-session data);
        // ClearOwnsCache resets the FinalizeInit (main) thread's slot — other threads' memos are per-tick
        // self-clearing, and a -1 tick forces a recompute regardless.
        static CommonSenseCompat() => CacheRegistry.Register(ClearOwnsCache);

        /// <summary>Drop the main thread's per-tick owns-flow memo so an equal TicksGame across a quickload cannot
        /// serve a previous session's value. Hygiene only — the next read recomputes from the live CS toggle fields
        /// (cheap reflection); the values are loop-invariant within a tick. Mirrors <see cref="PawnMassCache.Clear"/>.</summary>
        private static void ClearOwnsCache()
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
                // probe that tick. (Find.TickManager is non-null on every work-scan path; -1 fallback keeps a
                // null-TickManager edge — e.g. a menu-time probe — correct by forcing a recompute.)
                int tick = Find.TickManager?.TicksGame ?? -1;
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
