using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Marks a serialized field on <see cref="HaulersDreamSettings"/> as profile-management METADATA (the saved
    /// profile list, the active-profile name) rather than a tunable SETTING value. Profile capture / apply /
    /// dirty-comparison skip these fields, and the settings-drift checker excludes them from the
    /// field==Scribe==Reset triple (profiles are user data — never wiped by ResetToDefaults).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ProfileMetaAttribute : Attribute { }

    /// <summary>
    /// A named preset: a full snapshot of every Hauler's Dream setting value. The snapshot is itself a
    /// <see cref="HaulersDreamSettings"/> instance used purely as a data container — its ExposeData serializes the
    /// setting fields, and the static <see cref="HaulersDreamSettings.SerializingSnapshot"/> guard stops the
    /// nested profile list from recursing while a snapshot is (de)serialized.
    /// </summary>
    public class SettingsProfile : IExposable
    {
        public string name;
        public HaulersDreamSettings snapshot;

        public SettingsProfile() { }

        public SettingsProfile(string name, HaulersDreamSettings snapshot)
        {
            this.name = name;
            this.snapshot = snapshot;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            bool prev = HaulersDreamSettings.SerializingSnapshot;
            HaulersDreamSettings.SerializingSnapshot = true;
            try { Scribe_Deep.Look(ref snapshot, "snapshot"); }
            finally { HaulersDreamSettings.SerializingSnapshot = prev; }
            if (Scribe.mode == LoadSaveMode.LoadingVars && snapshot == null)
                snapshot = new HaulersDreamSettings();
        }
    }

    public partial class HaulersDreamSettings
    {
        // The setting fields to capture/compare/apply: every instance field EXCEPT [System.NonSerialized] caches
        // and [ProfileMeta] fields. Reflected once (the field set is fixed at compile time).
        private static FieldInfo[] settingFieldsCache;

        private static FieldInfo[] SettingFields
        {
            get
            {
                if (settingFieldsCache == null)
                {
                    var list = new List<FieldInfo>();
                    foreach (var fi in typeof(HaulersDreamSettings).GetFields(
                                 BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (fi.IsNotSerialized) continue;                              // ruleMap/keepDefNames/drawDetourLines/caches
                        if (fi.IsDefined(typeof(ProfileMetaAttribute), false)) continue; // savedProfiles/activeProfileName
                        list.Add(fi);
                    }
                    settingFieldsCache = list.ToArray();
                }
                return settingFieldsCache;
            }
        }

        // ---- deep copy of a single setting value (reference fields are cloned so a profile never shares mutable
        // state with the live settings; value types / enums / strings are immutable and shared as-is) ----
        private static object CloneValue(object v)
        {
            switch (v)
            {
                case null:
                    return null;
                case List<string> ls:
                    return new List<string>(ls);
                case StorageBuildingFilter sf:
                    return new StorageBuildingFilter
                    {
                        denied = new HashSet<string>(sf.denied ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                        allowed = new HashSet<string>(sf.allowed ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                    };
                case Dictionary<string, RouteDialogPrefs> rp:
                    var d = new Dictionary<string, RouteDialogPrefs>(rp.Count);
                    foreach (var kv in rp)
                        d[kv.Key] = CloneRoutePrefs(kv.Value);
                    return d;
                case Dictionary<string, SowRouteTemplate> sr:
                    var sd = new Dictionary<string, SowRouteTemplate>(sr.Count);
                    foreach (var kv in sr)
                        sd[kv.Key] = CloneSowTemplate(kv.Value);
                    return sd;
                case Dictionary<string, RemoveFloorRouteTemplate> rf:
                    var rfd = new Dictionary<string, RemoveFloorRouteTemplate>(rf.Count);
                    foreach (var kv in rf)
                        rfd[kv.Key] = CloneRemoveFloorTemplate(kv.Value);
                    return rfd;
                case string _:
                    return v; // immutable
                default:
                    // Value types (bool/int/float/enum) are immutable copies-by-value — safe to share. A NEW
                    // reference-type setting added without a clone case here would silently share mutable state
                    // between a profile and the live settings — fail loud so it's caught the moment it's added.
                    if (!v.GetType().IsValueType)
                        throw new InvalidOperationException(
                            $"HaulersDream profile capture: reference-type setting field of type {v.GetType()} has no "
                            + "deep-clone case in CloneValue — add one (and a matching ValuesEqual case).");
                    return v;
            }
        }

        private static RouteDialogPrefs CloneRoutePrefs(RouteDialogPrefs s)
        {
            if (s == null) return null;
            return new RouteDialogPrefs
            {
                mode = s.mode, maxTravel = s.maxTravel, radius = s.radius, amount = s.amount, smart = s.smart,
                allowHarvest = s.allowHarvest, growthThreshold = s.growthThreshold,
                selectionMethod = s.selectionMethod, distanceBasis = s.distanceBasis,
            };
        }

        private static SowRouteTemplate CloneSowTemplate(SowRouteTemplate s)
        {
            if (s == null) return null;
            return new SowRouteTemplate
            {
                mode = s.mode, maxTravel = s.maxTravel, radius = s.radius, amount = s.amount,
                smart = s.smart, selectionMethod = s.selectionMethod,
            };
        }

        private static RemoveFloorRouteTemplate CloneRemoveFloorTemplate(RemoveFloorRouteTemplate s)
        {
            if (s == null) return null;
            return new RemoveFloorRouteTemplate
            {
                mode = s.mode, maxTravel = s.maxTravel, radius = s.radius, amount = s.amount,
                selectionMethod = s.selectionMethod,
            };
        }

        /// <summary>Copy every setting value FROM <paramref name="from"/> INTO <paramref name="to"/> (deep-cloning
        /// the reference fields). Both are <see cref="HaulersDreamSettings"/> instances.</summary>
        public static void CopySettings(HaulersDreamSettings from, HaulersDreamSettings to)
        {
            foreach (var fi in SettingFields)
                fi.SetValue(to, CloneValue(fi.GetValue(from)));
        }

        // ---- value equality for dirty-detection (collections compared by content) ----
        private static bool SetEq(HashSet<string> a, HashSet<string> b)
        {
            int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
            if (ca != cb) return false;
            return ca == 0 || a.SetEquals(b);
        }

        private static bool RoutePrefsEqual(RouteDialogPrefs a, RouteDialogPrefs b)
        {
            if (a == null || b == null) return a == null && b == null;
            return a.mode == b.mode && a.maxTravel == b.maxTravel && a.radius == b.radius && a.amount == b.amount
                && a.smart == b.smart && a.allowHarvest == b.allowHarvest && a.growthThreshold == b.growthThreshold
                && a.selectionMethod == b.selectionMethod && a.distanceBasis == b.distanceBasis;
        }

        // The two remembered-route templates already carry a field-by-field ValueEquals; reuse it (single source of
        // truth for their field list) with a null-safe wrapper so two missing entries compare equal.
        private static bool SowTemplateEqual(SowRouteTemplate a, SowRouteTemplate b)
        {
            if (a == null || b == null) return a == null && b == null;
            return a.ValueEquals(b);
        }

        private static bool RemoveFloorTemplateEqual(RemoveFloorRouteTemplate a, RemoveFloorRouteTemplate b)
        {
            if (a == null || b == null) return a == null && b == null;
            return a.ValueEquals(b);
        }

        private static bool ValuesEqual(object x, object y)
        {
            if (x == null || y == null) return x == null && y == null;
            switch (x)
            {
                case List<string> lx:
                    var ly = y as List<string>;
                    return ly != null && lx.Count == ly.Count && new HashSet<string>(lx).SetEquals(ly);
                case StorageBuildingFilter fx:
                    var fy = y as StorageBuildingFilter;
                    return fy != null && SetEq(fx.denied, fy.denied) && SetEq(fx.allowed, fy.allowed);
                case Dictionary<string, RouteDialogPrefs> dx:
                    var dy = y as Dictionary<string, RouteDialogPrefs>;
                    if (dy == null || dx.Count != dy.Count) return false;
                    foreach (var kv in dx)
                        if (!dy.TryGetValue(kv.Key, out var p2) || !RoutePrefsEqual(kv.Value, p2)) return false;
                    return true;
                case Dictionary<string, SowRouteTemplate> sx:
                    var sy = y as Dictionary<string, SowRouteTemplate>;
                    if (sy == null || sx.Count != sy.Count) return false;
                    foreach (var kv in sx)
                        if (!sy.TryGetValue(kv.Key, out var s2) || !SowTemplateEqual(kv.Value, s2)) return false;
                    return true;
                case Dictionary<string, RemoveFloorRouteTemplate> rx:
                    var ry = y as Dictionary<string, RemoveFloorRouteTemplate>;
                    if (ry == null || rx.Count != ry.Count) return false;
                    foreach (var kv in rx)
                        if (!ry.TryGetValue(kv.Key, out var r2) || !RemoveFloorTemplateEqual(kv.Value, r2)) return false;
                    return true;
                default:
                    // Compare floats with a small tolerance rather than exact bit-equality: a float persisted to the
                    // config and reloaded can differ from the in-memory ResetToDefaults() default by a round-trip /
                    // formatting artifact. The tolerance (1e-4) is far below any HD setting's observable granularity
                    // (percentage sliders read at 1% / 0.01; the smallest float default is bleedThresholdPerDay =
                    // 0.001), so no user-visible change is ever masked; ints, bools, enums and strings compare
                    // exactly. NOTE: the reported "Custom (unsaved)" on a pristine config was NOT a float artifact —
                    // it was the two Dictionary cases above (remembered sow / remove-floor routes) falling through to
                    // this reference-equality Equals; this tolerance is defensive hardening, not that fix.
                    if (x is float fa && y is float fb)
                        return fa == fb || System.Math.Abs(fa - fb) <= 1e-4f;
                    return x.Equals(y);
            }
        }

        /// <summary>True if the two settings instances hold identical values for every tunable setting field.</summary>
        public static bool StateEquals(HaulersDreamSettings a, HaulersDreamSettings b)
        {
            foreach (var fi in SettingFields)
                if (!ValuesEqual(fi.GetValue(a), fi.GetValue(b)))
                    return false;
            return true;
        }

        // ---- profile state ----------------------------------------------------------------------------------
        // The built-in defaults, cached as a snapshot for dirty-comparison against the "Default" baseline.
        private HaulersDreamSettings DefaultsSnapshot
        {
            get
            {
                if (defaultsSnapshotCache == null)
                {
                    var d = new HaulersDreamSettings();
                    d.ResetToDefaults();
                    defaultsSnapshotCache = d;
                }
                return defaultsSnapshotCache;
            }
        }

        /// <summary>The currently-active named profile, or null when on Default / Custom (no named profile, or the
        /// named profile was deleted out from under us).</summary>
        public SettingsProfile ActiveProfile =>
            string.IsNullOrEmpty(activeProfileName)
                ? null
                : savedProfiles?.FirstOrDefault(p => p.name == activeProfileName);

        private HaulersDreamSettings BaselineState => ActiveProfile?.snapshot ?? DefaultsSnapshot;

        /// <summary>True when the live settings differ from the active baseline (the named profile's snapshot, or
        /// the built-in defaults when none is active) — i.e. there are unsaved changes.</summary>
        public bool IsDirty => !StateEquals(this, BaselineState);

        /// <summary>The selector label: "Default (profile, built-in)" / "Custom (unsaved)" /
        /// "&lt;name&gt; (profile)" / "&lt;name&gt; (profile, unsaved changes)".</summary>
        public string CurrentProfileLabel
        {
            get
            {
                var p = ActiveProfile;
                if (p == null)
                    return (IsDirty ? "HaulersDream.Profile.Custom" : "HaulersDream.Profile.Default").Translate();
                return (IsDirty ? "HaulersDream.Profile.NamedDirty" : "HaulersDream.Profile.Named").Translate(p.name);
            }
        }

        private HaulersDreamSettings CaptureSnapshot()
        {
            var snap = new HaulersDreamSettings();
            CopySettings(this, snap);
            return snap;
        }

        private void ApplyState(HaulersDreamSettings snap)
        {
            if (snap == null) return;
            CopySettings(snap, this);
            ResetTransientCaches();
        }

        /// <summary>Apply a named profile's values to the live settings and make it the active profile.</summary>
        public void ApplyProfile(SettingsProfile p)
        {
            if (p?.snapshot == null) return;
            ApplyState(p.snapshot);
            activeProfileName = p.name;
        }

        /// <summary>The "Default (built-in)" entry — reset every value to its default and leave no active profile.
        /// This is the "reset changes" action; the Default profile itself can never be modified.</summary>
        public void ApplyDefaultProfile()
        {
            ResetToDefaults();
            activeProfileName = "";
        }

        /// <summary>Startup tripwire for the profile system's value-equality / clone / codec coverage. Every serialized
        /// REFERENCE-type setting needs a matching case in <see cref="ValuesEqual"/>, <see cref="CloneValue"/>, and
        /// the copy/paste codec; a field that is missing one makes a pristine (or freshly reset) config compare
        /// UNEQUAL to the defaults, so the selector wrongly reads "Custom (unsaved)" and "Create/Copy profile" can
        /// throw (this was the reported bug: the two remembered-route dictionaries fell through to reference equality).
        /// This exercises the real capture + compare + export/import paths on a fresh default — plus a synthesized
        /// remembered-route entry so the two template dictionaries are actually encoded — and logs a LOUD error the
        /// moment the invariant breaks, turning a silent "always Custom" regression into something visible in the log
        /// (mirroring the drop-protection tripwire). Never throws and never disables anything.
        /// Also SCHEDULES <see cref="VerifySerializationIntegrity"/>, the half of the tripwire that runs settings
        /// through the real Scribe serializer — deferred off the mod constructor, see the call at the end.</summary>
        public static void VerifyProfileIntegrity()
        {
            try
            {
                var a = new HaulersDreamSettings();
                a.ResetToDefaults();
                var b = new HaulersDreamSettings();
                b.ResetToDefaults();

                // A fresh default must equal another fresh default. If a serialized reference field lacks a
                // ValuesEqual case (so it compares by reference), this is false and the selector shows "Custom".
                if (!StateEquals(a, b))
                    HDLog.Err("PROFILE TRIPWIRE: two freshly-reset default configurations do not compare equal — a "
                        + "serialized reference-type setting is missing a ValuesEqual case in SettingsProfiles, so the "
                        + "profile selector shows \"Custom (unsaved)\" on a pristine or just-reset config. Please "
                        + "report this with the log attached.");

                // Capturing a snapshot must not throw (a missing CloneValue case throws) and must round-trip equal.
                var snap = a.CaptureSnapshot();
                if (!StateEquals(a, snap))
                    HDLog.Err("PROFILE TRIPWIRE: a captured profile snapshot does not equal its source — a serialized "
                        + "reference-type setting is cloned or compared incorrectly in SettingsProfiles. Please report "
                        + "this with the log attached.");

                // #238: a snapshot must carry the CURRENT schema stamp, or its ExposeData re-runs the legacy yield
                // migration on every load and wipes the nine "Collect work results" settings. CopySettings skips
                // [ProfileMeta] fields, so this depends on the field INITIALIZER being CurrentSettingsSchema.
                if (snap.settingsSchemaVersion != CurrentSettingsSchema)
                    HDLog.Err("PROFILE TRIPWIRE: a captured profile snapshot carries settings schema "
                        + $"{snap.settingsSchemaVersion}, not {CurrentSettingsSchema} — its saved copy will re-run the "
                        + "one-time yield migration on every launch and reset the \"Collect work results\" settings. "
                        + "Please report this with the log attached.");

                // Export -> import must round-trip a profile that actually USES the reference-type template
                // dictionaries, so the copy/paste codec's encode + parse are exercised (not just the empty no-op).
                var withData = a.CaptureSnapshot();
                withData.rememberedSowRoutesByDef["HaulersDreamProfileSelfTest"] = new SowRouteTemplate { maxTravel = 42 };
                withData.rememberedRemoveFloorRoutesByDef["HaulersDreamProfileSelfTest"] = new RemoveFloorRouteTemplate { radius = 3 };
                var token = a.ExportProfileToString(withData, "self-test");
                if (!a.TryImportProfileFromString(token, out _, out var back) || !StateEquals(withData, back))
                    HDLog.Err("PROFILE TRIPWIRE: a profile that uses remembered sow / remove-floor routes does not "
                        + "survive copy -> paste — the copy/paste codec is missing an encode/parse case in "
                        + "SettingsProfiles. Please report this with the log attached.");
            }
            catch (System.Exception e)
            {
                HDLog.Err($"PROFILE TRIPWIRE: capturing or comparing a default profile threw {e.GetType().Name}: "
                    + $"{e.Message} — a serialized reference-type setting is missing a CloneValue / codec case in "
                    + "SettingsProfiles, so \"Create new profile\" / \"Copy profile\" will fail. Please report this "
                    + "with the log attached.");
            }

            // The SERIALIZER self-checks below are deliberately deferred off the mod constructor. Scribe_Deep.Look
            // -> ScribeExtractor.SaveableFromNode -> BackCompatibility.GetBackCompatibleType is the first thing HD
            // does that materialises GenTypes.AllTypes, and that walk Log.Errors once per BROKEN FOREIGN assembly
            // in the load order. Run inline, someone else's red line would land in the middle of HD's own startup
            // lines — a mod this project has already been misblamed for breaking. ExecuteWhenFinished queues the
            // action until the startup long event completes: a mod ctor runs INSIDE that event, so currentEvent is
            // non-null and its ShouldWaitUntilDisplayed is already false, and the "run it inline" branch of
            // LongEventHandler.ExecuteWhenFinished is not taken. Scribe.mode is Inactive there (nothing is being
            // loaded at the main menu); each check re-verifies that anyway and bails if not.
            LongEventHandler.ExecuteWhenFinished(VerifySerializationIntegrity);
        }

        /// <summary>
        /// The startup self-checks that run settings through the game's REAL serializer, in memory. Split from
        /// <see cref="VerifyProfileIntegrity"/> so they can be deferred past the mod constructor (see the
        /// LongEventHandler call there). Never throws; reports only through <see cref="HDLog.Err"/>.
        /// </summary>
        private static void VerifySerializationIntegrity()
        {
            try
            {
                var a = new HaulersDreamSettings();
                a.ResetToDefaults();

                // A PRISTINE snapshot: every value at its default, so Scribe writes almost no nodes. This is the
                // shape that catches a wrong Scribe default — a field whose absent node reloads as something other
                // than what the object held.
                VerifyScribeRoundTrip(a.CaptureSnapshot());

                // ...and a snapshot deliberately pushed OFF its defaults, because the pristine one is BLIND to the
                // exact fault #238 was: a load-time side effect that rewrites a field TO its default. On a pristine
                // snapshot such a rewrite is invisible (the value was already the default, so before == after);
                // only a non-default value makes the overwrite show up as a difference. Every value here is written
                // as a real node by Scribe and read straight back, and none of them is a legacy node, so no
                // migration should fire. routeOrderExactMax is deliberately left inside [1,16] so the one
                // sanctioned ExposeData clamp stays a no-op and cannot be mistaken for a fault.
                var drifted = a.CaptureSnapshot();
                drifted.yieldMining = YieldBehavior.Disabled;
                drifted.yieldStrip = YieldBehavior.Disabled;
                drifted.yieldHarvest = YieldBehavior.DirectToInventory;
                drifted.unloadGraceTicks = 1234;
                VerifyScribeRoundTrip(drifted);

                VerifyLegacyYieldMigration();
            }
            catch (System.Exception e)
            {
                HDLog.Err("PROFILE TRIPWIRE: the settings serializer self-check could not run: " + e);
            }
        }

        /// <summary>
        /// Save -> load a settings snapshot through the game's REAL serializer (in memory) and assert nothing
        /// changed. This is the hole issue #238 fell through: VerifyProfileIntegrity exercised capture, compare and
        /// the copy/paste codec, but NOTHING exercised ExposeData — where a load-time migration was silently
        /// rewriting nine settings on every launch, with both build guards green. Give it a source that is OFF its
        /// defaults (see the caller): a rewrite-to-default cannot show up as a difference on a pristine one.
        /// </summary>
        private static void VerifyScribeRoundTrip(HaulersDreamSettings source)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
                return; // never fight a (de)serialisation already in progress
            bool prevSnap = SerializingSnapshot;
            SerializingSnapshot = true; // exercise the PROFILE-SNAPSHOT path (no nested profile list)
            try
            {
                string xml = Scribe.saver.DebugOutputFor(source);
                if (string.IsNullOrEmpty(xml))
                    return; // the saver refused — not a settings fault
                var back = LoadFromSaveableXml(xml);

                if (back == null || !StateEquals(source, back))
                    HDLog.Err("PROFILE TRIPWIRE: a settings snapshot does not survive a save -> load round-trip "
                        + "through the game's own settings serializer — HaulersDreamSettings.ExposeData is CHANGING "
                        + "a value while loading it (a migration or a clamp firing when it should not). Saved "
                        + "profiles will silently lose those settings on every launch. Please report this with the "
                        + "log attached.");
            }
            catch (System.Exception ex)
            {
                HDLog.Err("PROFILE TRIPWIRE round-trip self-check could not run: " + HDFault.Render(ex));
            }
            finally
            {
                SerializingSnapshot = prevSnap;
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
            }
        }

        // A pre-#80 config node exactly as an old file carries it: only the two values that player had CHANGED
        // (Scribe omits anything equal to its default, so the other six legacy bools are simply absent), and no
        // <settingsSchemaVersion> at all — the stamp did not exist yet. This is the input the #238 fix has to keep
        // migrating: the probe must SEE the legacy nodes, the migration must map them, and the stamp must land.
        private const string LegacyConfigProbeXml =
            "<saveable><pickupMode>DirectToInventory</pickupMode><haulMining>False</haulMining></saveable>";

        /// <summary>
        /// Load the hard-coded pre-#80 fragment above through the real Scribe loader and assert the one-time yield
        /// migration still produces the right ten values and stamps the schema. Nothing else covers this end to
        /// end: the pure policy is a two-clause AND, while everything that can actually break — HasLegacyYieldNodes,
        /// the field initializer, the ordering inside ExposeData — is Verse-coupled, and the round-trip check above
        /// can never enter the migration branch (a snapshot it writes carries no legacy nodes by construction).
        /// </summary>
        private static void VerifyLegacyYieldMigration()
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
                return;
            bool prevSnap = SerializingSnapshot;
            // FALSE, unlike the round-trip check: a legacy configuration is a LIVE settings file, not a profile
            // snapshot, so this exercises the full-file path (the migration branch itself runs before the
            // profile-list block either way, so the assertions below do not depend on this).
            SerializingSnapshot = false;
            try
            {
                var migrated = LoadFromSaveableXml(LegacyConfigProbeXml);
                if (migrated == null)
                {
                    HDLog.Err("PROFILE TRIPWIRE: a pre-#80 legacy settings node could not be loaded at all. "
                        + "Upgrading from an old Hauler's Dream may lose the \"Collect work results\" options. "
                        + "Please report this with the log attached.");
                    return;
                }

                // pickupMode = DirectToInventory + haulMining = False, every other legacy toggle absent (old
                // default true). Mining and its Chunks split go Disabled; Strip was always drop-then-haul so it
                // ignores the global mode; everything else follows the global mode. Fishing is a post-#80 category
                // with no legacy toggle and must stay untouched at its field default.
                var problems = new List<string>();
                ExpectYield(problems, "yieldHarvest", migrated.yieldHarvest, YieldBehavior.DirectToInventory);
                ExpectYield(problems, "yieldLogging", migrated.yieldLogging, YieldBehavior.DirectToInventory);
                ExpectYield(problems, "yieldMining", migrated.yieldMining, YieldBehavior.Disabled);
                ExpectYield(problems, "yieldChunks", migrated.yieldChunks, YieldBehavior.Disabled);
                ExpectYield(problems, "yieldDeepDrill", migrated.yieldDeepDrill, YieldBehavior.DirectToInventory);
                ExpectYield(problems, "yieldDeconstruct", migrated.yieldDeconstruct, YieldBehavior.DirectToInventory);
                ExpectYield(problems, "yieldAnimals", migrated.yieldAnimals, YieldBehavior.DirectToInventory);
                ExpectYield(problems, "yieldStrip", migrated.yieldStrip, YieldBehavior.DropThenHaul);
                ExpectYield(problems, "yieldUninstall", migrated.yieldUninstall, YieldBehavior.DirectToInventory);
                ExpectYield(problems, "yieldFishing", migrated.yieldFishing, YieldBehavior.DropThenHaul);
                if (migrated.settingsSchemaVersion != CurrentSettingsSchema)
                    problems.Add($"settingsSchemaVersion={migrated.settingsSchemaVersion} "
                        + $"(expected {CurrentSettingsSchema})");

                if (problems.Count > 0)
                    HDLog.Err("PROFILE TRIPWIRE: the one-time upgrade of a pre-#80 configuration no longer produces "
                        + "the right \"Collect work results\" values — " + string.Join(", ", problems) + ". Anyone "
                        + "upgrading from an old Hauler's Dream will silently get the wrong settings. Please report "
                        + "this with the log attached.");
            }
            catch (System.Exception ex)
            {
                HDLog.Err("PROFILE TRIPWIRE legacy-migration self-check could not run: " + HDFault.Render(ex));
            }
            finally
            {
                SerializingSnapshot = prevSnap;
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
            }
        }

        /// <summary>Record a "&lt;name&gt;=&lt;actual&gt; (expected &lt;want&gt;)" line when a migrated yield value
        /// is not what the legacy mapping should have produced.</summary>
        private static void ExpectYield(List<string> problems, string name, YieldBehavior actual, YieldBehavior want)
        {
            if (actual != want)
                problems.Add($"{name}={actual} (expected {want})");
        }

        /// <summary>
        /// Run the real Scribe loader over an in-memory <c>&lt;saveable&gt;…&lt;/saveable&gt;</c> fragment and
        /// return the settings it produces. Mode + curXmlParent are exactly what ScribeLoader.InitLoading sets up
        /// for a file; Scribe.ForceStop() in the finally clears curXmlParent AND the cross-ref / post-load-init
        /// queues, so a later savegame load is untouched. The Class="…" attribute DebugOutputFor stamps (it
        /// deep-saves through an IExposable-typed ref) is left alone deliberately: SaveableFromNode feeds it to
        /// BackCompatibility.GetBackCompatibleType, which it also calls with typeof(T).FullName when the attribute
        /// is absent — and for this type those two strings are identical (HaulersDream is not one of
        /// GenTypes.IgnoredNamespaceNames), so stripping it would change nothing.
        /// Callers own the Scribe.mode == Inactive check and the SerializingSnapshot scope.
        /// </summary>
        private static HaulersDreamSettings LoadFromSaveableXml(string saveableXml)
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml("<HaulersDreamSelfTest>" + saveableXml + "</HaulersDreamSelfTest>");
            HaulersDreamSettings loaded = null;
            try
            {
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe.loader.curXmlParent = doc.DocumentElement;
                Scribe_Deep.Look(ref loaded, "saveable");
            }
            finally
            {
                Scribe.ForceStop();
            }
            return loaded;
        }

        /// <summary>Overwrite the active named profile with the current live values.</summary>
        public void SaveActiveProfile()
        {
            var p = ActiveProfile;
            if (p == null) return;
            p.snapshot = CaptureSnapshot();
        }

        /// <summary>Save the current live values as a new profile (or overwrite an existing same-named one) and make
        /// it active. Returns the profile, or null if the name was blank.</summary>
        public SettingsProfile SaveAsNewProfile(string profileName)
        {
            profileName = profileName?.Trim();
            if (string.IsNullOrEmpty(profileName)) return null;
            if (savedProfiles == null) savedProfiles = new List<SettingsProfile>();
            var existing = savedProfiles.FirstOrDefault(p => string.Equals(p.name, profileName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.snapshot = CaptureSnapshot();
                activeProfileName = existing.name;
                return existing;
            }
            var np = new SettingsProfile(profileName, CaptureSnapshot());
            savedProfiles.Add(np);
            activeProfileName = profileName;
            return np;
        }

        /// <summary>Delete a saved profile; if it was active, fall back to Custom/Default (the live values stay).</summary>
        public void DeleteProfile(SettingsProfile p)
        {
            if (p == null || savedProfiles == null) return;
            savedProfiles.Remove(p);
            if (activeProfileName == p.name) activeProfileName = "";
        }

        // Invalidate the decode/derived caches after a bulk state swap so changed lists take effect immediately.
        // (ruleMap is the [NonSerialized] decode cache of itemUnloadRules — same partial class, so accessible.)
        private void ResetTransientCaches()
        {
            ruleMap = null;
        }

        // ===================== portable profile strings (copy / paste) ======================================
        // A profile is exported as a compact token (see ProfileCodec): the mod version + the profile name + ONLY
        // the settings that differ from THAT version's defaults. The importer reconstructs the rest from the named
        // version's defaults, so the token stays short and survives future default changes.

        // Field separators used INSIDE a single encoded collection value (distinct from the codec's \n/\t framing).
        private const char US = '\u001f'; // unit separator — between elements of a list / fields of a struct
        private const char RS = '\u001e'; // record separator — between records (dict entries; the filter's two sets)

        private static string currentModVersionCache;
        /// <summary>The running mod version as "major.minor.patch" (from the assembly version, synced from
        /// package.json by scripts/sync-version.ts). Stamped into every exported profile token.</summary>
        public static string CurrentModVersion
        {
            get
            {
                if (currentModVersionCache == null)
                {
                    var v = typeof(HaulersDreamSettings).Assembly.GetName().Version;
                    currentModVersionCache = v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
                }
                return currentModVersionCache;
            }
        }

        private static Dictionary<string, FieldInfo> settingFieldsByNameCache;
        private static Dictionary<string, FieldInfo> SettingFieldsByName
        {
            get
            {
                if (settingFieldsByNameCache == null)
                {
                    settingFieldsByNameCache = new Dictionary<string, FieldInfo>();
                    foreach (var fi in SettingFields)
                        settingFieldsByNameCache[fi.Name] = fi;
                }
                return settingFieldsByNameCache;
            }
        }

        // Per-version default CHANGES, newest entries appended. Each entry records, for a version where one or more
        // defaults CHANGED, the PRE-CHANGE value of each affected key (the value every EARLIER version used). To
        // reconstruct version V's defaults, take the current live defaults and — for every entry NEWER than V —
        // restore those keys to their pre-change values. Adding a default change is ONE entry: no full baseline and
        // no per-version duplication. EMPTY today (this is the first version with copy/paste), so DefaultsForVersion
        // returns the current defaults for every version.
        //   Example, if 1.5.0 flips carryLimitFraction 0.75 -> 1.0:
        //     new DefaultChange("1.5.0", new Dictionary<string,string> { { "carryLimitFraction", "0.75" } })
        private sealed class DefaultChange
        {
            public readonly string version;
            public readonly Dictionary<string, string> preChange;
            public DefaultChange(string version, Dictionary<string, string> preChange)
            { this.version = version; this.preChange = preChange; }
        }

        private static readonly List<DefaultChange> DefaultChanges = new List<DefaultChange>();

        private static bool TryParseModVersion(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(s)) return false;
            if (!s.Contains(".")) s += ".0"; // System.Version needs at least major.minor
            return Version.TryParse(s, out v);
        }

        /// <summary>A fresh defaults snapshot for the given mod version: the current live defaults, with any keys
        /// whose default has CHANGED since that version restored to their value at that version.</summary>
        public HaulersDreamSettings DefaultsForVersion(string version)
        {
            var snap = new HaulersDreamSettings();
            snap.ResetToDefaults();
            if (DefaultChanges.Count > 0 && TryParseModVersion(version, out var asked))
            {
                foreach (var change in DefaultChanges
                             .Where(c => TryParseModVersion(c.version, out var cv) && cv > asked)
                             .OrderByDescending(c => TryParseModVersion(c.version, out var cv) ? cv : new Version(0, 0)))
                    foreach (var kv in change.preChange)
                        if (SettingFieldsByName.TryGetValue(kv.Key, out var fi))
                            DecodeFieldValue(snap, fi, kv.Value);
            }
            return snap;
        }

        /// <summary>Encode a settings snapshot (typically the live settings) to a portable token: version + name +
        /// only the values that differ from the current defaults.</summary>
        public string ExportProfileToString(HaulersDreamSettings snapshot, string name)
        {
            var def = DefaultsForVersion(CurrentModVersion);
            var changed = new Dictionary<string, string>();
            foreach (var fi in SettingFields)
            {
                var value = fi.GetValue(snapshot);
                if (!ValuesEqual(value, fi.GetValue(def)))
                    changed[fi.Name] = EncodeFieldValue(value);
            }
            return ProfileCodec.Encode(CurrentModVersion, name ?? "", changed);
        }

        /// <summary>Decode a portable token back into a full settings snapshot (the token's version's defaults +
        /// its changes). Unknown keys (from a newer version) are skipped. Returns false on a malformed token.</summary>
        public bool TryImportProfileFromString(string token, out string name, out HaulersDreamSettings snapshot)
        {
            name = null;
            snapshot = null;
            if (!ProfileCodec.TryDecode(token, out var data))
                return false;
            var snap = DefaultsForVersion(data.Version);
            foreach (var kv in data.Changed)
                if (SettingFieldsByName.TryGetValue(kv.Key, out var fi))
                    DecodeFieldValue(snap, fi, kv.Value);
            name = data.Name ?? "";
            snapshot = snap;
            return true;
        }

        /// <summary>Add (or overwrite a same-named) profile from an imported snapshot, then make it active + apply it.</summary>
        public SettingsProfile ImportAsProfile(string name, HaulersDreamSettings snapshot)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || snapshot == null) return null;
            if (savedProfiles == null) savedProfiles = new List<SettingsProfile>();
            var existing = savedProfiles.FirstOrDefault(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.snapshot = snapshot;
            else { existing = new SettingsProfile(name, snapshot); savedProfiles.Add(existing); }
            ApplyProfile(existing);
            return existing;
        }

        // ---- per-field value encode / decode (string form used in both the diff and the defaults registry) ----
        private static string EncodeFieldValue(object v)
        {
            switch (v)
            {
                case null:
                    return "";
                case bool b:
                    return b ? "1" : "0";
                case List<string> ls:
                    return string.Join(US.ToString(), ls);
                case StorageBuildingFilter sf:
                    return string.Join(US.ToString(), sf.denied ?? Enumerable.Empty<string>())
                        + RS + string.Join(US.ToString(), sf.allowed ?? Enumerable.Empty<string>());
                case Dictionary<string, RouteDialogPrefs> rp:
                    return string.Join(RS.ToString(), rp.Select(kv => kv.Key + US + EncodeRoutePrefs(kv.Value)));
                case Dictionary<string, SowRouteTemplate> sr:
                    return string.Join(RS.ToString(), sr.Select(kv => kv.Key + US + EncodeSowTemplate(kv.Value)));
                case Dictionary<string, RemoveFloorRouteTemplate> rf:
                    return string.Join(RS.ToString(), rf.Select(kv => kv.Key + US + EncodeRemoveFloorTemplate(kv.Value)));
                case Enum e:
                    return e.ToString();
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        private static string EncodeRoutePrefs(RouteDialogPrefs p)
        {
            if (p == null) p = new RouteDialogPrefs();
            return string.Join(US.ToString(), new[]
            {
                p.mode.ToString(),
                p.maxTravel.ToString(CultureInfo.InvariantCulture),
                p.radius.ToString(CultureInfo.InvariantCulture),
                p.amount.ToString(CultureInfo.InvariantCulture),
                p.smart ? "1" : "0",
                p.allowHarvest ? "1" : "0",
                p.growthThreshold.ToString(CultureInfo.InvariantCulture),
                p.selectionMethod.ToString(),
                p.distanceBasis.ToString(),
            });
        }

        private static string EncodeSowTemplate(SowRouteTemplate p)
        {
            if (p == null) p = new SowRouteTemplate();
            return string.Join(US.ToString(), new[]
            {
                p.mode.ToString(),
                p.maxTravel.ToString(CultureInfo.InvariantCulture),
                p.radius.ToString(CultureInfo.InvariantCulture),
                p.amount.ToString(CultureInfo.InvariantCulture),
                p.smart ? "1" : "0",
                p.selectionMethod.ToString(),
            });
        }

        private static string EncodeRemoveFloorTemplate(RemoveFloorRouteTemplate p)
        {
            if (p == null) p = new RemoveFloorRouteTemplate();
            return string.Join(US.ToString(), new[]
            {
                p.mode.ToString(),
                p.maxTravel.ToString(CultureInfo.InvariantCulture),
                p.radius.ToString(CultureInfo.InvariantCulture),
                p.amount.ToString(CultureInfo.InvariantCulture),
                p.selectionMethod.ToString(),
            });
        }

        // Decode a string value into the field on the target, dispatched by the field's declared type. A value that
        // fails to parse (corrupt paste, an enum name from a newer version) is skipped with a visible warning so the
        // rest of the profile still imports — malformed external input is not a fault to crash on.
        private void DecodeFieldValue(HaulersDreamSettings target, FieldInfo fi, string str)
        {
            try
            {
                fi.SetValue(target, ParseFieldValue(fi.FieldType, str));
            }
            catch (Exception e)
            {
                HDLog.Warn($"profile import: could not decode setting '{fi.Name}' from \"{str}\" — keeping default. {e.GetType().Name}: {e.Message}");
            }
        }

        private static object ParseFieldValue(Type t, string s)
        {
            if (t == typeof(bool)) return s == "1" || s == "true" || s == "True";
            if (t == typeof(int)) return int.Parse(s, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return float.Parse(s, CultureInfo.InvariantCulture);
            if (t.IsEnum) return Enum.Parse(t, s);
            if (t == typeof(List<string>))
                return s.Length == 0 ? new List<string>() : new List<string>(s.Split(US).Where(x => x.Length > 0));
            if (t == typeof(StorageBuildingFilter)) return ParseFilter(s);
            if (t == typeof(Dictionary<string, RouteDialogPrefs>)) return ParseRoutePrefsDict(s);
            if (t == typeof(Dictionary<string, SowRouteTemplate>)) return ParseSowTemplateDict(s);
            if (t == typeof(Dictionary<string, RemoveFloorRouteTemplate>)) return ParseRemoveFloorTemplateDict(s);
            return Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
        }

        private static HashSet<string> ParseIdSet(string s) =>
            new HashSet<string>(
                string.IsNullOrEmpty(s) ? Enumerable.Empty<string>() : s.Split(US).Where(x => x.Length > 0),
                StringComparer.OrdinalIgnoreCase);

        private static StorageBuildingFilter ParseFilter(string s)
        {
            var parts = s.Split(RS);
            return new StorageBuildingFilter
            {
                denied = ParseIdSet(parts.Length > 0 ? parts[0] : ""),
                allowed = ParseIdSet(parts.Length > 1 ? parts[1] : ""),
            };
        }

        private static Dictionary<string, RouteDialogPrefs> ParseRoutePrefsDict(string s)
        {
            var d = new Dictionary<string, RouteDialogPrefs>();
            if (s.Length == 0) return d;
            foreach (var entry in s.Split(RS))
            {
                var f = entry.Split(US);
                if (f.Length < 10) continue; // key + 9 fields
                d[f[0]] = new RouteDialogPrefs
                {
                    mode = (RouteMode)Enum.Parse(typeof(RouteMode), f[1]),
                    maxTravel = int.Parse(f[2], CultureInfo.InvariantCulture),
                    radius = int.Parse(f[3], CultureInfo.InvariantCulture),
                    amount = int.Parse(f[4], CultureInfo.InvariantCulture),
                    smart = f[5] == "1",
                    allowHarvest = f[6] == "1",
                    growthThreshold = int.Parse(f[7], CultureInfo.InvariantCulture),
                    selectionMethod = (RouteSelectionMethod)Enum.Parse(typeof(RouteSelectionMethod), f[8]),
                    distanceBasis = (RouteDistanceBasis)Enum.Parse(typeof(RouteDistanceBasis), f[9]),
                };
            }
            return d;
        }

        private static Dictionary<string, SowRouteTemplate> ParseSowTemplateDict(string s)
        {
            var d = new Dictionary<string, SowRouteTemplate>();
            if (s.Length == 0) return d;
            foreach (var entry in s.Split(RS))
            {
                var f = entry.Split(US);
                if (f.Length < 7) continue; // key + 6 fields
                d[f[0]] = new SowRouteTemplate
                {
                    mode = (SowRouteMode)Enum.Parse(typeof(SowRouteMode), f[1]),
                    maxTravel = int.Parse(f[2], CultureInfo.InvariantCulture),
                    radius = int.Parse(f[3], CultureInfo.InvariantCulture),
                    amount = int.Parse(f[4], CultureInfo.InvariantCulture),
                    smart = f[5] == "1",
                    selectionMethod = (RouteSelectionMethod)Enum.Parse(typeof(RouteSelectionMethod), f[6]),
                };
            }
            return d;
        }

        private static Dictionary<string, RemoveFloorRouteTemplate> ParseRemoveFloorTemplateDict(string s)
        {
            var d = new Dictionary<string, RemoveFloorRouteTemplate>();
            if (s.Length == 0) return d;
            foreach (var entry in s.Split(RS))
            {
                var f = entry.Split(US);
                if (f.Length < 6) continue; // key + 5 fields
                d[f[0]] = new RemoveFloorRouteTemplate
                {
                    mode = (RemoveFloorRouteMode)Enum.Parse(typeof(RemoveFloorRouteMode), f[1]),
                    maxTravel = int.Parse(f[2], CultureInfo.InvariantCulture),
                    radius = int.Parse(f[3], CultureInfo.InvariantCulture),
                    amount = int.Parse(f[4], CultureInfo.InvariantCulture),
                    selectionMethod = (RouteSelectionMethod)Enum.Parse(typeof(RouteSelectionMethod), f[5]),
                };
            }
            return d;
        }
    }

    /// <summary>A tiny modal text-input dialog: prompts for a profile name and invokes the callback on OK/Enter.</summary>
    public class Dialog_NameProfile : Window
    {
        private string curName = "";
        private readonly Action<string> onAccept;
        private bool focusedOnce;

        public Dialog_NameProfile(Action<string> onAccept, string initial = "")
        {
            this.onAccept = onAccept;
            curName = initial ?? "";
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
            // Vanilla's Window.InnerWindowOnGUI runs Notify_PressedAccept (Return) BEFORE DoWindowContents and, with
            // the default closeOnAccept=true, would Close() us and Use() the event so our own Enter handler never
            // fires (the dialog vanishes WITHOUT saving). Handle Enter ourselves, exactly like vanilla Dialog_Rename.
            closeOnAccept = false;
        }

        public override Vector2 InitialSize => new Vector2(380f, 168f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 28f), "HaulersDream.Profile.NameDialogTitle".Translate());

            GUI.SetNextControlName("HD_ProfileNameField");
            curName = Widgets.TextField(new Rect(0f, 36f, inRect.width, 32f), curName);
            if (!focusedOnce)
            {
                UI.FocusControl("HD_ProfileNameField", this);
                focusedOnce = true;
            }

            bool enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            var okRect = new Rect(inRect.width - 120f, inRect.height - 36f, 120f, 32f);
            var cancelRect = new Rect(inRect.width - 248f, inRect.height - 36f, 120f, 32f);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
                Close();
            if ((Widgets.ButtonText(okRect, "OK".Translate()) || enterPressed) && !curName.Trim().NullOrEmpty())
            {
                if (enterPressed) Event.current.Use();
                onAccept?.Invoke(curName.Trim());
                Close();
            }
        }
    }

    /// <summary>Shows the portable profile string for the current settings, read-only, with a "copy to clipboard"
    /// button. The string holds only the mod version + the settings that differ from that version's defaults.</summary>
    public class Dialog_CopyProfile : Window
    {
        private readonly string token;

        public Dialog_CopyProfile(string token)
        {
            this.token = token ?? "";
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(520f, 320f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 28f), "HaulersDream.Profile.CopyTitle".Translate());

            var pf = Text.Font;
            var pc = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.72f, 0.76f);
            Widgets.Label(new Rect(0f, 30f, inRect.width, 44f), "HaulersDream.Profile.CopyInfo".Translate());
            GUI.color = pc;
            Text.Font = pf;

            var taRect = new Rect(0f, 78f, inRect.width, inRect.height - 78f - 44f);
            Widgets.TextArea(taRect, token, readOnly: true);

            var copyRect = new Rect(0f, inRect.height - 36f, 220f, 32f);
            var closeRect = new Rect(inRect.width - 120f, inRect.height - 36f, 120f, 32f);
            if (Widgets.ButtonText(copyRect, "HaulersDream.Profile.CopyToClipboard".Translate()))
            {
                GUIUtility.systemCopyBuffer = token;
                Messages.Message("HaulersDream.Profile.CopiedMsg".Translate(), MessageTypeDefOf.TaskCompletion, historical: false);
            }
            if (Widgets.ButtonText(closeRect, "CloseButton".Translate()))
                Close();
        }
    }

    /// <summary>Accepts a pasted profile string, previews its name (auto-filled, editable), and creates a new profile
    /// from it. Reconstructs the full settings from the string's version defaults + its changes.</summary>
    public class Dialog_PasteProfile : Window
    {
        private readonly HaulersDreamSettings settings;
        private string token = "";
        private string profileName = "";
        private bool nameEdited;
        private string lastParsed;
        private bool tokenValid;
        private Vector2 scroll;

        public Dialog_PasteProfile(HaulersDreamSettings settings)
        {
            this.settings = settings;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
            // Pre-fill from the clipboard if it already looks like one of our tokens.
            var clip = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip) && clip.TrimStart().StartsWith(ProfileCodec.Prefix))
                token = clip.Trim();
        }

        public override Vector2 InitialSize => new Vector2(520f, 380f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            float y = 0f;
            Widgets.Label(new Rect(0f, y, inRect.width, 28f), "HaulersDream.Profile.PasteTitle".Translate());
            y += 32f;

            Widgets.Label(new Rect(0f, y, inRect.width, 22f), "HaulersDream.Profile.PasteTokenLabel".Translate());
            y += 24f;
            token = WidgetsCompat.TextAreaScrollable(new Rect(0f, y, inRect.width, 96f), token, ref scroll);
            y += 100f;

            if (Widgets.ButtonText(new Rect(0f, y, 220f, 28f), "HaulersDream.Profile.PasteFromClipboard".Translate()))
            {
                token = (GUIUtility.systemCopyBuffer ?? "").Trim();
                nameEdited = false; // re-prefill the name from the freshly pasted token
            }
            y += 34f;

            // Re-parse only when the token text changes; auto-fill the name unless the user has edited it.
            if (token != lastParsed)
            {
                lastParsed = token;
                tokenValid = settings.TryImportProfileFromString(token, out var parsedName, out _);
                if (tokenValid && !nameEdited)
                    profileName = parsedName ?? "";
            }

            Widgets.Label(new Rect(0f, y, inRect.width, 22f), "HaulersDream.Profile.NameDialogTitle".Translate());
            y += 24f;
            string newName = Widgets.TextField(new Rect(0f, y, inRect.width, 30f), profileName);
            if (newName != profileName)
            {
                profileName = newName;
                nameEdited = true;
            }
            y += 34f;

            if (!token.NullOrEmpty() && !tokenValid)
            {
                var pf = Text.Font;
                var pc = GUI.color;
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.9f, 0.5f, 0.5f);
                Widgets.Label(new Rect(0f, y, inRect.width, 20f), "HaulersDream.Profile.PasteInvalid".Translate());
                GUI.color = pc;
                Text.Font = pf;
            }

            var createRect = new Rect(inRect.width - 150f, inRect.height - 36f, 150f, 32f);
            var cancelRect = new Rect(inRect.width - 280f, inRect.height - 36f, 120f, 32f);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
                Close();

            bool canCreate = tokenValid && !profileName.Trim().NullOrEmpty();
            if (Widgets.ButtonText(createRect, "HaulersDream.Profile.PasteCreate".Translate(), active: canCreate) && canCreate)
            {
                if (settings.TryImportProfileFromString(token, out _, out var snap))
                {
                    settings.ImportAsProfile(profileName.Trim(), snap);
                    Close();
                }
            }
        }
    }

    internal static class WidgetsCompat
    {
        // RimWorld 1.6 has no Widgets.TextAreaScrollable; emulate a scrollable editable text box via a scroll view.
        public static string TextAreaScrollable(Rect rect, string text, ref Vector2 scrollbarPosition)
        {
            float innerWidth = rect.width - 16f;
            float innerHeight = Mathf.Max(rect.height, Text.CalcHeight(text + "\n", innerWidth));
            var view = new Rect(0f, 0f, innerWidth, innerHeight);
            Widgets.BeginScrollView(rect, ref scrollbarPosition, view);
            string result = Widgets.TextArea(view, text);
            Widgets.EndScrollView();
            return result;
        }
    }
}
