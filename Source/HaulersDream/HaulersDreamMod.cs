using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Mod entry point: loads settings and applies all Harmony patches in this assembly.
    /// </summary>
    public class HaulersDreamMod : Mod
    {
        public const string HarmonyId = "giwaffed.HaulersDream";
        public const string PackageId = "giwaffed.HaulersDream"; // matches About.xml <packageId>; used to skip self in mod scans

        public static HaulersDreamMod Instance { get; private set; }
        public static HaulersDreamSettings Settings { get; private set; }

        public HaulersDreamMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<HaulersDreamSettings>();

            // Start the always-on disk debug trail next to Player.log. Resolved here (main thread) where the Unity
            // path API is safe to read; the writer then runs on its own background thread.
            HDDebugLog.ConfigureDirectory(UnityEngine.Application.consoleLogPath);

            var harmony = new Harmony(HarmonyId);
            ApplyPatchesResilient(harmony, Assembly.GetExecutingAssembly());
            HDLog.Msg("initialised — carry limit defaults to each pawn's max carrying capacity.");
        }

        /// <summary>
        /// Like <c>harmony.PatchAll(assembly)</c>, but applies each annotated patch class in its OWN try/catch so a
        /// single unresolvable target — e.g. a private vanilla method renamed in a future RimWorld point-release —
        /// degrades that ONE feature with a logged warning instead of throwing inside <c>PatchAll</c> and taking
        /// down ALL of the mod's patches (the catastrophic-failure mode: one rename = total mod death in a large
        /// load order). <c>[HarmonyPriority]</c> still governs per-target injection order, so behavior is unchanged
        /// when every target resolves.
        ///
        /// We ONLY process types that are genuine patch containers — i.e. carry a DIRECT (non-inherited) Harmony
        /// attribute on the class or on a method. Harmony's own <c>PatchAll</c> calls <c>CreateClassProcessor(t).Patch()</c>
        /// on EVERY type and relies on a null container-attribute set to skip non-patches; but its attribute lookup
        /// uses <c>GetCustomAttributes(inherit: true)</c>, which mis-classifies this mod's OWN <c>JobDriver</c>
        /// subclasses (they inherit attributes through the vanilla <c>JobDriver</c> chain) and makes Harmony try to
        /// patch <c>JobDriver.Cleanup</c> for each — exactly the spurious failures the inherit:false filter below
        /// excludes. The filter captures every real HD patch (all use a direct class- or method-level attribute).
        /// </summary>
        private static void ApplyPatchesResilient(Harmony harmony, Assembly assembly)
        {
            int applied = 0, failed = 0;
            // GetTypesFromAssembly tolerates a ReflectionTypeLoadException (returns the loadable types).
            foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
            {
                if (!IsHarmonyPatchContainer(type))
                    continue; // the mod's own JobDrivers/Comps/etc. — not patches
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    HDLog.Err($"patch class '{type.Name}' could not be applied on this RimWorld build "
                        + "(a hooked vanilla target is likely missing or renamed) — that feature is disabled; the "
                        + $"rest of the mod continues. {e.GetType().Name}: {e.Message}");
                }
            }
            if (failed > 0)
                HDLog.Warn($"{applied} patch class(es) applied, {failed} skipped due to missing targets (see errors above).");

            AttachUniversalExceptionTagger(harmony);
        }

        // The universal tagger (Issue #3): the SINGLE mechanism that fulfils "tell the user/developer an exception
        // passed through Hauler's Dream's code, but never swallow it." Reused as one cached HarmonyMethod.
        private static readonly HarmonyMethod UniversalTagger =
            new HarmonyMethod(AccessTools.Method(typeof(HDLog), nameof(HDLog.UniversalExceptionFinalizer)))
            {
                // Run LAST among finalizers so any finalizer that legitimately TRANSFORMS the exception (HD's own
                // HDGuard.SeamThrew, or a foreign finalizer) has already run before we observe + tag whatever
                // actually escapes.
                priority = Priority.Last,
            };

        /// <summary>
        /// Once every HD patch is applied, attach a tagging FINALIZER to each method HD patched, so any exception
        /// that escapes the original (or another patch on it) is LOGGED with the <see cref="HDLog.Tag"/> breadcrumb
        /// AND re-thrown unchanged. A Harmony finalizer that returns its <c>__exception</c> re-throws it (returning
        /// null would SWALLOW it — which this never does), so the game still surfaces the error exactly as before;
        /// HD only ADDS a breadcrumb identifying that its code was in the call stack. This is the project-wide
        /// answer to "let errors pass through, but tag them," applied automatically to every seam HD hooks rather
        /// than relying on a hand-written finalizer per patch.
        ///
        /// Methods that ALREADY carry an HD finalizer which OBSERVES the exception (an <see cref="HDGuard"/>
        /// log-and-rethrow seam — it takes <c>__exception</c> / returns <see cref="Exception"/>) are skipped to
        /// avoid a double log. The project's VOID cleanup finalizers (which reset thread-statics and neither read
        /// nor return the exception) are NOT skipped, so those seams still get tagged.
        /// </summary>
        private static void AttachUniversalExceptionTagger(Harmony harmony)
        {
            // Materialise first: harmony.Patch(...) below mutates the patch registry, so we must not iterate a live
            // view of it.
            var methods = new List<MethodBase>(harmony.GetPatchedMethods());
            int tagged = 0;
            foreach (var method in methods)
            {
                if (method == null)
                    continue;
                var info = Harmony.GetPatchInfo(method);
                if (info?.Finalizers != null && AlreadyHasHandlingFinalizer(info.Finalizers))
                    continue; // an HDGuard.SeamThrew finalizer already tags + rethrows here — don't double-log
                try
                {
                    harmony.Patch(method, finalizer: UniversalTagger);
                    tagged++;
                }
                catch (Exception e)
                {
                    // Never fatal: a method we can't attach the tagger to simply won't carry the breadcrumb.
                    HDLog.Warn($"could not attach the exception tagger to {method.DeclaringType?.FullName}.{method.Name} "
                        + $"— exceptions through it won't carry the Hauler's Dream breadcrumb. {e.GetType().Name}: {e.Message}");
                }
            }
            HDLog.Dbg($"exception tagger attached to {tagged} of {methods.Count} patched method(s).");
        }

        /// <summary>True if any of <paramref name="finalizers"/> is an HD-owned finalizer that OBSERVES the
        /// exception (takes a <c>__exception</c> parameter or returns an <see cref="Exception"/>) — i.e. already
        /// tags + rethrows. A void HD cleanup finalizer (no <c>__exception</c> param, void return) returns FALSE
        /// here, so its method still receives the universal tagger.</summary>
        private static bool AlreadyHasHandlingFinalizer(IEnumerable<Patch> finalizers)
        {
            foreach (var p in finalizers)
            {
                if (p?.owner != HarmonyId || p.PatchMethod == null)
                    continue;
                if (p.PatchMethod.ReturnType == typeof(Exception))
                    return true;
                foreach (var pi in p.PatchMethod.GetParameters())
                    if (pi.Name == "__exception" || pi.ParameterType == typeof(Exception))
                        return true;
            }
            return false;
        }

        // A genuine patch container has a DIRECT (inherit:false) Harmony attribute on the class, or a method carrying
        // a Harmony injection/patch attribute. Deliberately inherit:false so the mod's own JobDriver subclasses
        // (which inherit attributes via the vanilla JobDriver chain) are NOT treated as patches.
        private static bool IsHarmonyPatchContainer(Type type)
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0)
                return true;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var m in type.GetMethods(all))
            {
                if (m.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyPrefix), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyPostfix), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyTranspiler), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyFinalizer), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyReversePatch), inherit: false).Length > 0)
                    return true;
            }
            return false;
        }

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        public override string SettingsCategory() => "HaulersDream.SettingsCategory".Translate();

        public override void WriteSettings()
        {
            base.WriteSettings();
            // Re-sync work priorities when the settings window closes: toggling a work override
            // (allPawnsCanHaul/Clean/CutPlants) OFF otherwise leaves STALE priorities — the work scan runs off
            // priorities, so a pawn keeps doing the now-forbidden work while the work tab draws the box locked
            // (vanilla only re-syncs disabled work types on save load). Notifying unconditionally on every
            // settings close is cheap (it just re-reads GetDisabledWorkTypes and zeroes disabled priorities).
            // No try/catch: a re-sync failure is a real bug to surface as a red error, not a silent warning.
            if (Current.Game != null)
            {
                var pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p?.Faction != null && p.Faction.IsPlayer)
                        p.Notify_DisabledWorkTypesChanged();
                }
            }
        }
    }

    /// <summary>
    /// The SINGLE SOURCE OF TRUTH for everything Hauler's Dream writes to the log (Issue #3). Every message,
    /// warning, and error the mod emits flows through here and carries <see cref="Tag"/>, so the player/developer
    /// can always see at a glance that a line came from (or passed through) this mod — and the tag itself can be
    /// changed in exactly ONE place. Nothing here ever SWALLOWS an exception: the tagging machinery only ADDS a
    /// breadcrumb and re-throws (see <see cref="UniversalExceptionFinalizer"/>).
    ///
    /// Verbose <see cref="Dbg"/> is additionally gated behind the mod setting AND Dev Mode (parity with BLFT —
    /// debug spam never reaches a normal player even if the (now Dev-only) toggle was left on in an old config).
    /// See .docs/02. The other channels are ALWAYS-emitted.
    /// </summary>
    public static class HDLog
    {
        /// <summary>The one place the log prefix is defined. Change it here and every HD log line updates.</summary>
        public const string Tag = "[Hauler's Dream] ";

        // Every channel — including verbose DBG, which a normal player never sees in the console — is written to an
        // always-on, disk-backed trail (HDDebugLog) so an in-game issue report carries Hauler's Dream's own recent
        // history WITHOUT the player having to turn verbose logging on first. Disk-backed (size-capped + rotated)
        // rather than RAM so a long session can't grow an unbounded in-memory buffer. Thread-safe: HDDebugLog's
        // queue is lock-free, so the off-main-thread universal exception finalizer (which logs via ErrOnce) is safe.
        // The disk write is the only added cost on a DBG call; the interpolated string is built by the caller either
        // way, so always-capturing it just enqueues an already-built line.
        private static void Emit(string level, string message)
        {
            HDDebugLog.Enqueue(System.DateTime.Now.ToString("MM-dd HH:mm:ss") + " " + level + " " + message);
        }

        /// <summary>The captured HD trail (newest lines) for the in-game issue reporter. Null when empty.</summary>
        public static string GetReportLog() => HDDebugLog.GetReportTail(HDDebugLog.ReportTailBytes);

        /// <summary>Verbose debug line — ALWAYS written to the disk trail (so it appears in a report without the
        /// player enabling verbose logging); printed to the console only with Dev Mode AND the verbose-logging
        /// setting on (parity with the old console behaviour — debug spam never reaches a normal player).</summary>
        public static void Dbg(string message)
        {
            Emit("DBG", message);
            if (Prefs.DevMode && HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.verboseLogging)
                Log.Message(Tag + message);
        }

        /// <summary>An ALWAYS-emitted plain message carrying the tag — mod init, optional-mod-detected notices, etc.</summary>
        public static void Msg(string message)
        {
            Emit("MSG", message);
            Log.Message(Tag + message);
        }

        /// <summary>An ALWAYS-emitted warning (not Dev/verbose-gated) carrying the tag — for genuine
        /// degrade-but-keep-going conditions (e.g. an optional mod is present but a load-bearing reflected member
        /// did not bind, so a feature is silently disabled). No dedup: each call logs (callers self-gate with a
        /// `warned` latch where one-shot is wanted).</summary>
        public static void Warn(string message)
        {
            Emit("WARN", message);
            Log.Warning(Tag + message);
        }

        /// <summary>A tag-carrying warning logged at most ONCE per <paramref name="key"/>. Mirrors <c>Log.WarningOnce</c>.</summary>
        public static void WarnOnce(string message, int key)
        {
            Emit("WARN", message);
            Log.WarningOnce(Tag + message, key);
        }

        /// <summary>An ALWAYS-emitted error (not Dev/verbose-gated) carrying the tag — for fail-loud
        /// faults (a transpiler IL match broke, a foreign WorkGiver threw). No dedup.</summary>
        public static void Err(string message)
        {
            Emit("ERR", message);
            Log.Error(Tag + message);
        }

        /// <summary>A tag-carrying error logged at most ONCE per <paramref name="key"/> — for a fault that recurs
        /// every tick/scan and must not flood the log. Mirrors <c>Log.ErrorOnce</c>.</summary>
        public static void ErrOnce(string message, int key)
        {
            Emit("ERR", message);
            Log.ErrorOnce(Tag + message, key);
        }

        /// <summary>
        /// The universal exception breadcrumb (Issue #3) attached to EVERY method Hauler's Dream patches (see
        /// <see cref="HaulersDreamMod.AttachUniversalExceptionTagger"/>). It runs as a Harmony FINALIZER, so it
        /// observes any exception thrown by the original method or by any patch on it. It logs a tagged,
        /// once-per-method breadcrumb and then RE-THROWS the exception unchanged by returning it — returning null
        /// would swallow it, which we never do. The wording is deliberately HONEST: HD's code being in the stack
        /// does not mean HD caused the fault (the original method or another mod's patch may have), so we never
        /// claim blame — we only make HD's involvement visible.
        /// </summary>
        public static Exception UniversalExceptionFinalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null)
            {
                string where = __originalMethod != null
                    ? (__originalMethod.DeclaringType?.FullName + "." + __originalMethod.Name)
                    : "a patched method";
                ErrOnce(
                    $"an exception passed through {where} — a method Hauler's Dream patches. Hauler's Dream is in "
                    + "the call stack but did not necessarily cause this (the original method or another mod's "
                    + "patch may have). Re-throwing it unchanged so the game still reports it below — "
                    + $"[{__exception.GetType().Name}: {__exception.Message}]",
                    __originalMethod != null ? __originalMethod.GetHashCode() : where.GetHashCode());
            }
            return __exception; // NEVER null — that would swallow the exception. Returning it re-throws it.
        }
    }
}
