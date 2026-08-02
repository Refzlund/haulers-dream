using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
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
            // Baseline the work-override snapshot from the loaded settings so the FIRST WriteSettings of the
            // session (e.g. closing the settings window without changing anything) does not spuriously re-sync
            // work types. Pawns already reflect the loaded settings — vanilla re-syncs disabled work types on
            // save load. See WriteSettings (issue #59).
            syncedAllPawnsCanHaul = Settings.allPawnsCanHaul;
            syncedAllPawnsCanClean = Settings.allPawnsCanClean;
            syncedAllPawnsCanCutPlants = Settings.allPawnsCanCutPlants;

            // Start the always-on disk debug trail next to Player.log. Resolved here (main thread) where the Unity
            // path API is safe to read; the writer then runs on its own background thread.
            HDDebugLog.ConfigureDirectory(UnityEngine.Application.consoleLogPath);
            // Flush the trail cleanly on game exit so its TAIL is never lost. Application.quitting fires on the main
            // thread BEFORE the runtime aborts the writer's background thread, so this drains the final lines
            // (frequently the exact moment being diagnosed) that the abort would otherwise drop. Subscribed once,
            // here at construction, on the main thread as Unity requires.
            UnityEngine.Application.quitting += () => HDDebugLog.FlushAndClose();

            var harmony = new Harmony(HarmonyId);
            ApplyPatchesResilient(harmony, Assembly.GetExecutingAssembly());
            VerifyDropProtection(harmony);
            HaulersDreamSettings.VerifyProfileIntegrity();
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

        // The protection-critical vanilla seams behind HD's single most-reported, most-recurring bug — pawns
        // dropping the crops/milk/drugs HD scooped into their inventory (issues #62, #81, #87). Each entry is a
        // (type, method) that one of HD's guard patches MUST bind to; if any silently stops binding (a private
        // vanilla method renamed in a point-release, a guard class deleted in a refactor), the bug returns with no
        // other symptom. The set is duplicated, by design, in scripts/check-drop-protection.ts so the build fails
        // too — runtime tripwire + build tripwire + the Core oracle tests are the "won't recur unnoticed" net.
        private static readonly (Type type, string method)[] DropProtectionTargets =
        {
            (typeof(JobGiver_DropUnusedInventory), "TryGiveJob"),                 // Layer 1: re-arm the food clock
            (typeof(JobGiver_DropUnusedInventory), "Drop"),                      // Layer 2: per-drop veto
            (typeof(JobGiver_DropUnusedInventory), "ShouldKeepDrugInInventory"), // #81: keep tagged drugs
        };

        /// <summary>
        /// Startup tripwire for the recurring "pawns drop scooped inventory cargo" bug. Verifies that every
        /// protection-critical vanilla seam (a) still EXISTS on this RimWorld build and (b) actually carries an
        /// HD guard patch, and logs a LOUD error (not the quiet Warn the resilient apply path uses for ordinary
        /// optional features) the moment one doesn't — so a future RimWorld rename or a regressed patch shows up
        /// in the player's log and the in-game HD report instead of as silently dropped crops. This never throws
        /// and never disables anything; it only makes a silent breakage loud.
        /// </summary>
        private static void VerifyDropProtection(Harmony harmony)
        {
            foreach (var (type, methodName) in DropProtectionTargets)
            {
                var method = AccessTools.Method(type, methodName);
                if (method == null)
                {
                    HDLog.Err($"DROP-PROTECTION TRIPWIRE: vanilla {type.Name}.{methodName} was not found on this "
                        + "RimWorld build (renamed or removed). Hauler's Dream can no longer stop pawns from "
                        + "dropping the crops / milk / drugs it scooped into their inventory — this needs a code "
                        + "update. Please report it with this log attached.");
                    continue;
                }
                if (!HasHdPatch(Harmony.GetPatchInfo(method)))
                    HDLog.Err($"DROP-PROTECTION TRIPWIRE: vanilla {type.Name}.{methodName} exists but Hauler's "
                        + "Dream's guard did not attach to it, so pawns may drop scooped inventory cargo. Another "
                        + "mod may have replaced the method, or HD's guard patch is missing. Please report it with "
                        + "this log attached.");
            }
        }

        // True if HD owns a prefix / postfix / transpiler on this method (the actual guards; the universal
        // exception tagger's finalizer does NOT count — we want to confirm the GUARD bound, not just any HD patch).
        private static bool HasHdPatch(Patches info)
            => info != null && (OwnedByHd(info.Prefixes) || OwnedByHd(info.Postfixes) || OwnedByHd(info.Transpilers));

        private static bool OwnedByHd(IEnumerable<Patch> patches)
        {
            if (patches == null)
                return false;
            foreach (var p in patches)
                if (p != null && p.owner == HarmonyId)
                    return true;
            return false;
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

        // #197: the vanilla work-capability getters HD only POSTFIXES. A postfix is SKIPPED by Harmony when the
        // original throws, so the universal tagger's finalizer on one of these can never be observing an HD fault
        // — it can only convert a cleanly-propagating FOREIGN throw (a malformed modded pawn: vanilla's
        // GetDisabledWorkTypes reads ageTracker / lifeStageWorkSettings with no null guard, e.g. a Dead Man's
        // Switch humanoid mech) into an HD-RESTAMPED one, misreporting it as Hauler's Dream (the #197 report; same
        // false-blame class as #97/#126/#190). Excluding these from the tagger lets such a fault propagate with its
        // real stack so the game's own log names the true source. Property getters carry their get_ prefix.
        private static readonly HashSet<(Type type, string name)> TaggerExcludedSafeGetters =
            new HashSet<(Type type, string name)>
            {
                (typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes)),
                (typeof(Pawn), "get_" + nameof(Pawn.CombinedDisabledWorkTags)),
            };

        /// <summary>
        /// True when <paramref name="method"/> is one of the work-capability getters HD only POSTFIXES
        /// (<see cref="TaggerExcludedSafeGetters"/>) AND HD's patch there is STILL postfix-only, so the universal
        /// tagger must be skipped for it (see that set's remark, issue #197). The postfix-only gate is
        /// belt-and-braces: if HD ever adds a prefix/transpiler here, its own code really does enter the throw
        /// path, the breadcrumb becomes meaningful again, and the tagger re-attaches automatically.
        /// </summary>
        private static bool IsSafePostfixOnlyBlameMagnet(MethodBase method, Patches info)
        {
            if (method?.DeclaringType == null || info == null)
                return false;
            if (!TaggerExcludedSafeGetters.Contains((method.DeclaringType, method.Name)))
                return false;
            return OwnedByHd(info.Postfixes) && !OwnedByHd(info.Prefixes) && !OwnedByHd(info.Transpilers);
        }

        /// <summary>
        /// Once every HD patch is applied, attach a tagging FINALIZER to each method HD patched, so any exception
        /// that escapes the original (or another patch on it) is LOGGED with the <see cref="HDLog.Tag"/> breadcrumb
        /// AND left to propagate unchanged. The tagger is deliberately VOID: a finalizer that neither returns nor
        /// replaces the exception cannot swallow OR restamp it, so Harmony emits a plain <c>rethrow</c> and the
        /// game still surfaces the error with its original frames (see
        /// <see cref="HDLog.UniversalExceptionFinalizer"/> for why that return type is load-bearing, issue #236);
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
            var seen = new HashSet<MethodBase>();
            int tagged = 0;
            foreach (var raw in methods)
            {
                if (raw == null)
                    continue;
                // Normalise to the DECLARING method. GetPatchedMethods() can hand back a method whose ReflectedType is
                // a SUBCLASS that merely inherits it (e.g. Vehicle Framework's WorkGiver_PackVehicle inheriting
                // JobOnThing from the closed generic base WorkGiver_CarryToVehicle<TransferableOneWay>). Harmony refuses
                // to patch such a method ("You can only patch implemented methods — patch the declared method ...
                // instead"), which is the ArgumentException a VF user reported. The declaring method is the one that
                // actually runs, so tagging it is equivalent (Steam VF report).
                var method = NormalizeToDeclared(raw);
                // Skip methods Harmony can't patch anyway: an abstract method has no body, and an OPEN generic
                // definition has no concrete code. (A CLOSED generic like WorkGiver_CarryToVehicle<TransferableOneWay>
                // is fine — ContainsGenericParameters is false there — so it is NOT skipped.)
                if (method is MethodInfo nmi && (nmi.IsAbstract || nmi.ContainsGenericParameters))
                    continue;
                // Several patched entries can collapse onto the SAME declaring method after normalisation (generic
                // code-sharing across reference-type instantiations) — tag it once.
                if (!seen.Add(method))
                    continue;
                var info = Harmony.GetPatchInfo(method);
                if (IsSafePostfixOnlyBlameMagnet(method, info))
                    continue; // #197: excluded so a foreign fault through these getters keeps its real stack (see method)
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

        /// <summary>
        /// Re-resolve a possibly-INHERITED method to the form Harmony can patch: one whose <c>ReflectedType</c> equals
        /// its <c>DeclaringType</c>. <see cref="HarmonyLib.AccessTools.Method(Type, string, Type[], Type[])"/> called on
        /// a subclass returns the base's <see cref="MethodInfo"/> with <c>ReflectedType</c> set to that subclass, which
        /// Harmony rejects for patching. Re-resolving on the declaring type (e.g. Vehicle Framework's
        /// <c>WorkGiver_CarryToVehicle&lt;TransferableOneWay&gt;.JobOnThing</c>) yields the shared method that actually
        /// runs. A constructor, an already-declared method, or a member that can't be re-resolved passes through
        /// unchanged — this is a no-op whenever normalisation is unnecessary, so it never alters a method that was
        /// already fine.
        /// </summary>
        internal static MethodBase NormalizeToDeclared(MethodBase m)
        {
            if (!(m is MethodInfo mi) || mi.DeclaringType == null || mi.ReflectedType == mi.DeclaringType)
                return m;
            var paramTypes = Array.ConvertAll(mi.GetParameters(), p => p.ParameterType);
            return AccessTools.DeclaredMethod(mi.DeclaringType, mi.Name, paramTypes) ?? m;
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
            if (HasDirectHarmonyAttribute(type))
                return true;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var m in type.GetMethods(all))
                if (HasDirectHarmonyAttribute(m))
                    return true;
            return false;
        }

        /// <summary>
        /// True if <paramref name="member"/> carries a DIRECT (inherit:false) Harmony injection/patch attribute.
        ///
        /// DEFENSE-IN-DEPTH (issue #6): reading a member's attributes can THROW. Mono materializes ALL of a member's
        /// attributes to filter by type, so a SINGLE attribute whose type can't be resolved — e.g. a soft-dep
        /// optional-mod attribute (a <c>Multiplayer.API</c> <c>[SyncMethod]</c> when the Multiplayer mod isn't loaded)
        /// whose assembly is absent — throws <c>TypeLoadException</c> for the WHOLE probe. Letting that escape here
        /// would brick the entire mod's startup (the loop in <see cref="ApplyPatchesResilient"/> dies and NO patch is
        /// applied). A member whose attributes can't be resolved is never an HD Harmony patch target (HD patch methods
        /// carry only Harmony attributes, all resolvable), so on a resolution failure we log once and treat it as
        /// "no Harmony attribute," then keep scanning — the same resilient-degrade stance as
        /// <see cref="ApplyPatchesResilient"/>. HD itself no longer bakes any such attribute (synced methods are now
        /// registered programmatically — see <c>MultiplayerCompat</c>), so this is a backstop against a future or
        /// foreign one, not the primary fix for #6.
        /// </summary>
        private static bool HasDirectHarmonyAttribute(MemberInfo member)
        {
            try
            {
                return member.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0
                    || member.GetCustomAttributes(typeof(HarmonyPrefix), inherit: false).Length > 0
                    || member.GetCustomAttributes(typeof(HarmonyPostfix), inherit: false).Length > 0
                    || member.GetCustomAttributes(typeof(HarmonyTranspiler), inherit: false).Length > 0
                    || member.GetCustomAttributes(typeof(HarmonyFinalizer), inherit: false).Length > 0
                    || member.GetCustomAttributes(typeof(HarmonyReversePatch), inherit: false).Length > 0;
            }
            catch (Exception e)
            {
                string where = (member.DeclaringType?.FullName ?? "?") + "." + member.Name;
                HDLog.WarnOnce("could not read attributes on " + where + " (likely an optional-mod attribute whose "
                    + "assembly isn't loaded) — treating it as a non-patch member so startup isn't bricked. "
                    + e.GetType().Name, ("HD.attrProbe." + where).GetHashCode());
                return false;
            }
        }

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        public override string SettingsCategory() => "HaulersDream.SettingsCategory".Translate();

        // The work-override toggle values last synced to pawns via Notify_DisabledWorkTypesChanged. HD's
        // WorkOverride patches key disabled work types off EXACTLY these three settings, so a pawn's cached
        // disabled work types only go stale when one of them changes. WriteSettings compares against these to
        // re-sync ONLY on a real change (issue #59). Baselined from the loaded settings in the constructor.
        private static bool syncedAllPawnsCanHaul, syncedAllPawnsCanClean, syncedAllPawnsCanCutPlants;

        public override void WriteSettings()
        {
            base.WriteSettings();
            var s = Settings;
            if (Current.Game == null || s == null)
                return;
            // Re-sync work priorities ONLY when a work-override toggle actually changed. Toggling allPawnsCanHaul/
            // Clean/CutPlants changes what Pawn.CombinedDisabledWorkTags / GetDisabledWorkTypes report (HD's
            // WorkOverride patches), so a stale cache otherwise leaves a pawn doing now-forbidden work while the
            // work tab draws the box locked (vanilla only re-syncs disabled work types on save load). NOTHING
            // else HD persists affects disabled work types.
            //
            // The old code notified on EVERY settings write — including no-op window closes and HD's own dialogs
            // that call WriteSettings to persist routing/storage settings — which fired
            // Notify_DisabledWorkTypesChanged on every player pawn each time, needlessly running every OTHER mod's
            // patches on that method. With one such mod throwing inside its patch (issue #59: a work-defaults mod
            // whose postfix did a LINQ .First() that matched nothing), the exception propagated out of
            // WriteSettings -> Dialog_ModSettings.PreClose and BROKE the settings-window close. Gating the re-sync
            // on a real change keeps the necessary refresh while no longer poking unrelated work-type listeners on
            // every write. (When a toggle DID change, HD still notifies — that is the legitimate re-sync; any throw
            // from another mod's patch there is that mod's bug, on a notify HD genuinely needs to make.)
            if (s.allPawnsCanHaul == syncedAllPawnsCanHaul
                && s.allPawnsCanClean == syncedAllPawnsCanClean
                && s.allPawnsCanCutPlants == syncedAllPawnsCanCutPlants)
                return;
            syncedAllPawnsCanHaul = s.allPawnsCanHaul;
            syncedAllPawnsCanClean = s.allPawnsCanClean;
            syncedAllPawnsCanCutPlants = s.allPawnsCanCutPlants;

            // No try/catch: an HD re-sync failure is a real bug to surface as a red error, not a silent warning.
            var pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p?.Faction != null && p.Faction.IsPlayer)
                    p.Notify_DisabledWorkTypesChanged();
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

        // Per-(method, exception type) occurrence COUNT this session. ConcurrentDictionary + AddOrUpdate is the
        // atomic increment (the finalizer can run off the main thread — a threading mod's work scan). Drives the
        // first-occurrence-full-then-suppress-repeats rate limit below: a fault that merely PASSES THROUGH an
        // HD-patched method can recur every tick (a persistently broken pawn in a heavily-modded save — issue #190:
        // another mod's mechanoid-DPS NRE and the broken pawn's think-tree failing through EndCurrentJob every
        // tick), and the report trail is size capped, so a breadcrumb per occurrence would evict the rest of HD's
        // own history from the trail a bug report ships.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> stackLoggedCounts =
            new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

        // After the first full breadcrumb, note a STILL-recurring fault only at these occurrence counts, so the
        // recurrence stays visible in the trail without a per-tick flood (a handful of lines instead of hundreds).
        private static bool IsRepeatCheckpoint(int count) => count == 10 || count == 100 || count == 1000 || count % 10000 == 0;

        /// <summary>
        /// True if Hauler's Dream owns a TRANSPILER on <paramref name="method"/>. A transpiler rewrites the
        /// original method's IL in place, so a fault its edits introduced throws with only the original method's
        /// frames and NO <c>HaulersDream.</c> frame — the finalizer's stack-trace check can't detect HD's
        /// involvement there. The finalizer uses this so it never describes HD as a pure "bystander" on a method
        /// HD transpiles (QA #197: a report of HD's OWN transpiler bug would otherwise be waved away).
        /// Inlined here (not the private HaulersDreamMod.OwnedByHd) because that helper isn't
        /// visible from HDLog; keyed on the public <see cref="HaulersDreamMod.HarmonyId"/>.
        /// </summary>
        private static bool HasHdTranspiler(MethodBase method)
        {
            if (method == null)
                return false;
            var transpilers = Harmony.GetPatchInfo(method)?.Transpilers;
            if (transpilers == null)
                return false;
            foreach (var p in transpilers)
                if (p != null && p.owner == HaulersDreamMod.HarmonyId)
                    return true;
            return false;
        }

        /// <summary>
        /// The universal exception breadcrumb (Issue #3) attached to EVERY method Hauler's Dream patches (see
        /// <see cref="HaulersDreamMod.AttachUniversalExceptionTagger"/>). It runs as a Harmony FINALIZER, so it
        /// observes any exception thrown by the original method or by any patch on it, logs a tagged breadcrumb
        /// (with the full origin stack once per method and exception type), and lets the exception continue.
        ///
        /// <para><b>VOID is load-bearing (issue #236), not a style choice.</b> Harmony emits a plain
        /// <c>rethrow</c> — which PRESERVES the exception's original frames — only when EVERY finalizer on the
        /// patched method returns void; a finalizer whose return type is <see cref="Exception"/> flips it to
        /// <c>throw ex</c>, which RESTAMPS the trace at the patched method and erases every frame above it
        /// (verified by decompiling both the pinned reference, Lib.Harmony 2.3.6 <c>MethodPatcher.AddFinalizers</c>,
        /// and the runtime the game actually loads, brrainz.harmony 2.4.1 <c>MethodCreator.AddFinalizers</c>: both
        /// set <c>rethrowPossible = false</c> when <c>fix.ReturnType != typeof(void)</c>). Void also makes "never
        /// swallow the exception" STRUCTURAL rather than a promise in a comment — there is no return value with
        /// which to drop it.</para>
        ///
        /// <para><b>Where the restamp still happens, and why that is fine.</b> <c>rethrowPossible</c> is computed
        /// over ALL finalizers on the method from ALL mods, so it is NOT enough for this one to be void.
        /// (a) A FOREIGN non-void finalizer on the same method restores the restamp, and that is not HD's to
        /// prevent. (b) HD's OWN <c>HDGuard.SeamThrew</c> finalizers return <see cref="Exception"/>, and on their
        /// ~15 seams <see cref="HaulersDreamMod.AlreadyHasHandlingFinalizer"/> deliberately skips this tagger, so
        /// HD's is the only finalizer and Harmony emits <c>throw ex</c> there. That restamp is ACCEPTED, not
        /// overlooked: <c>SeamThrew</c> logs the fully-rendered exception (<c>HDFault.Render</c>, frame-rebuilding
        /// and placeholder-proof) BEFORE it returns, so the origin frames are already in the log by the time the
        /// trace is rewritten — the same first-occurrence-in-full contract this method uses.</para>
        ///
        /// <para><b>Do NOT "fix" that by converting the seams to void.</b> The rethrow-only seams could be, but
        /// the two that CONTAIN — <c>HarmonyPatches.TryIssueJobPackage</c> and
        /// <c>Patch_JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory</c>, which return
        /// <c>HDGuard.SeamContained</c> — CANNOT: suppressing an exception in a Harmony finalizer means returning
        /// <c>null</c>, and a void finalizer has no return value with which to express that. Containment is what
        /// keeps a colony working when a foreign work giver (#235) or a modded drug (#232) throws, so voiding
        /// those two would silently re-break both.</para>
        ///
        /// <para>The wording is deliberately HONEST and never categorical. HD's code being in the stack does not
        /// mean HD caused the fault, and — the #236 lesson — HD's code being ABSENT from the stack does not mean
        /// HD is uninvolved. Both the classification and the four sentences live in
        /// <see cref="BlamePolicy"/>, in the Verse-free half, so the wording contract is where the headless
        /// tests can enforce it; this method only gathers the three facts and prints the verdict.</para>
        /// </summary>
        public static void UniversalExceptionFinalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null)
                return;

            string where = __originalMethod != null
                ? (__originalMethod.DeclaringType?.FullName + "." + __originalMethod.Name)
                : "a patched method";

            // Dedupe key per (patched method, exception type): each DISTINCT fault at a method gets logged once,
            // so a second, different exception later at the same method is not hidden by the first, while a fault
            // that repeats every tick is still logged only once. (net48 has no System.HashCode, so combine by hand.)
            int methodKey = __originalMethod != null ? __originalMethod.GetHashCode() : where.GetHashCode();
            int key = unchecked((methodKey * 397) ^ __exception.GetType().GetHashCode());
            // RATE LIMIT (issue #190): a fault that merely PASSES THROUGH an HD-patched method can recur every
            // tick, and the report trail is size capped — a breadcrumb per occurrence would evict the rest of
            // HD's history. So log the FIRST occurrence of each (method, exception type) in FULL — with the origin
            // stack that names the real fault — then SUPPRESS the repeats (a few terse checkpoints aside).
            //
            // The counter is taken FIRST, and rendering + classifying happen INSIDE the first-occurrence branch:
            // both walk the exception's stack frames (and the classifier asks Harmony for patch info), which for
            // the per-tick recurring fault this limit exists for would otherwise be paid every tick and thrown
            // away. Occurrence 2+ never used either value.
            int count = stackLoggedCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
            if (count == 1)
            {
                // Render EXACTLY ONCE, and never from a string another mod controls (issues #235/#236).
                // brrainz.harmony — which supplies the 0Harmony runtime HD runs against — installs a
                // DEDUPLICATING trace renderer: the SECOND render of one exception returns a
                // "duplicate stacktrace, see ref for original" placeholder instead of the frames. The old code
                // rendered once to probe and again to log, so the log got the placeholder and the lines naming
                // the real source were gone — the #236 report itself. HDFault.Render is placeholder-proof (it
                // rebuilds from StackFrame objects when ToString() produced no frame lines) and total (a foreign
                // ToString()/Message override that throws degrades instead of costing the whole breadcrumb,
                // which Harmony would swallow silently).
                string rendered = HDFault.Render(__exception);

                // The HD-frame test reads FRAME OBJECTS, never the rendered text. Rendering once is necessary
                // but not sufficient: HD's tagger runs at Priority.Last, so a foreign finalizer on the same
                // method renders first and takes the first-render slot, leaving HD the placeholder — and
                // Harmony's enhanced trace also ANNOTATES each frame with the patches on it, which for a method
                // HD patched prints HD's own type names purely because HD patched it. Text would therefore give
                // both a false negative and a false positive, the second asserting a fact that is untrue —
                // exactly the false-blame class #236 exists to end. new StackTrace(ex, false).GetFrames() is not
                // interceptable that way.
                var involvement = BlamePolicy.Classify(HDFault.InvolvesHaulersDream(__exception),
                    HasHdTranspiler(__originalMethod), HaulChurnGuard.PlacementWrapperIsAncestor);

                // First time: the full breadcrumb, once to the disk trail AND the console (Log.ErrorOnce dedupes
                // the console by key).
                ErrOnce(
                    $"an exception passed through {where}, a method Hauler's Dream patches. "
                    + BlamePolicy.Describe(involvement)
                    + " Hauler's Dream neither alters nor swallows it. The stack below is the one captured when "
                    + "the exception reached this method; the game's own report continues from here.\n"
                    + rendered,
                    key);
            }
            else if (IsRepeatCheckpoint(count))
            {
                // Still recurring: a terse DISK-ONLY note (Dbg — never re-hits the console, stays out of a normal
                // player's log) so the recurrence stays visible at a few thresholds without the per-tick flood.
                // Type name and message go through total readers for the same reason the full render does: a
                // foreign override that throws here would silently cost the note (Harmony swallows it).
                Dbg($"the exception through {where} [{HDFault.ExceptionTypeName(__exception)}: "
                    + $"{HDFault.SafeMessage(__exception)}] has now recurred {count}x this session — "
                    + "per-occurrence logging suppressed to protect the report trail.");
            }
        }
    }
}
