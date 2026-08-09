using System;
using HarmonyLib;
using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Survival Tools compatibility bridge — REFLECTION ONLY (two cached <c>TypeByName</c> lookups, no per-call
    /// invoke), no hard assembly reference, so HD behaves identically with or without the mod. Verified against the
    /// shipped <c>SurvivalTools.dll</c> of "Survival Tools Reborn" (Jellypowered, Steam 3554664966, packageId
    /// <c>jellypowered.survivaltools</c>, source github.com/Jellypowered/SurvivalTools) — the continuation of
    /// XeoNovaDan's original Survival Tools, which shares the same namespace and identity types, so one shim covers
    /// the whole family exactly the way <see cref="GrabYourToolCompat"/> covers Tools O' Plenty through GYT.
    ///
    /// <para>What the mod does: a colonist carries work TOOLS (pickaxes, axes, sickles — <c>SurvivalTools.SurvivalTool
    /// : ThingWithComps</c>, whose <c>SurvivalToolProperties</c> mod extension holds the work-stat factors) in
    /// <c>inventory.innerContainer</c> (<c>SurvivalToolUtility.cs:429-430</c>, <c>GetHeldSurvivalTools</c> is literally
    /// a filter over <c>innerContainer</c>) and fetches them back on its own: an auto-pickup postfix on
    /// <c>JobGiver_Work.TryIssueJobPackage</c> queues a <c>TakeInventory</c> for the best map tool before a gated job
    /// (<c>AI/AutoToolPickup_UtilityIntegrated.cs:11</c>), and <c>JobGiver_OptimizeSurvivalTools</c> rescans every
    /// 3600-14400 ticks (<c>AI/JobGiver_OptimizeSurvivalTools.cs:12-13</c>). Its acquisition policy treats a STORED
    /// tool as a valid source — <c>ToolIsAcquirableByPolicy</c> accepts any <c>tool.IsInAnyStorage()</c>
    /// (<c>:275</c>) — so a tool HD ships to a shelf is a re-fetch candidate by design.</para>
    ///
    /// <para>Why this exists: no existing HD keep covered these tools. The GYT branch requires
    /// <c>def.equippedStatOffsets</c>, which every survival-tool def leaves empty (the factors flow through the mod's
    /// own <c>StatPart_SurvivalTool</c>, not vanilla equipped offsets), and the defs carry melee <c>&lt;tools&gt;</c>
    /// so they are <c>IsMeleeWeapon</c> and fall into the Simple Sidearms branch, which reports a remembered count of
    /// 0 and therefore FULL surplus. At HD's defaults nothing happens — the unload pass is tag-scoped and a tool the
    /// mod fetched is untagged — but with "unload all surplus" ON, adoption tags the pawn's whole toolkit, HD ships it
    /// to storage, and the mod's auto-pickup fetches it straight back: the unload<->re-fetch LOOP the Simple Sidearms
    /// / Grab Your Tool / Combat Extended keeps already sever. The mod's own protection does not reach HD — it guards
    /// only vanilla's hook, a postfix on <c>Pawn_InventoryTracker.FirstUnloadableThing</c>
    /// (<c>Harmony/Patch_Pawn_InventoryTracker.cs:13</c>), which HD's unload driver never consults.
    /// <see cref="IsCarriedTool"/> reports a carried tool as keep-stock so <see cref="InventorySurplus.SurplusOf"/>
    /// returns 0 and it is never adopted/unloaded, and the tag guards never auto-tag it. Auto-active on detection (no
    /// setting), matching the other keep-shims; a player who wants a tool def shipped to storage anyway can still set
    /// an explicit per-def "Unload always" rule, which wins in <c>SurplusOf</c> before this keep is consulted.</para>
    ///
    /// <para>READ-ONLY, and that is a rule not an accident: this shim touches NO Survival Tools state — not the
    /// per-pawn assignment tracker, not the forced-handler, not the tool's wear counter — so it cannot perturb the
    /// mod's own bookkeeping and is deterministic across multiplayer clients. It also does NOT fix anything on the
    /// mod's side; its unread <c>ModCompatibilityCheck.OtherInventoryModsActive</c> flag (defined, never consulted)
    /// is deliberately left alone.</para>
    /// </summary>
    public static class SurvivalToolsCompat
    {
        private static bool initialized;
        // SurvivalTools.SurvivalTool : ThingWithComps — the tool OBJECT. Doubles as the mod-presence signal: absent
        // means TypeByName found nothing, which is the ordinary case and never logged.
        private static Type toolType;
        // SurvivalTools.SurvivalToolProperties : DefModExtension — the work-stat factors. Load-bearing, and resolved
        // separately so a rename of it (rather than of the tool class) is reported instead of silently widening the
        // keep to any def that merely uses the tool thing-class.
        private static Type propsType;

        /// <summary>Whether Survival Tools is loaded AND both identity types resolved. Cached; detected by type, no
        /// hard ref. False when the mod is absent (every call site is then inert) and also when it is present but a
        /// type did not bind — the fail-closed direction, warned about once at detection.</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return toolType != null && propsType != null;
            }
        }

        private static void Init()
        {
            initialized = true;
            // No try/catch: MOD-ABSENT is the TypeByName == null precondition (it returns null, never throws).
            // Lazy, once (on first IsActive).
            toolType = AccessTools.TypeByName("SurvivalTools.SurvivalTool");
            if (toolType == null)
                return; // Mod not loaded — HD's keeps are exactly what they were, no line logged (absent is normal).
            propsType = AccessTools.TypeByName("SurvivalTools.SurvivalToolProperties");
            // The detection line goes through HDLog, not raw Log.Message: HDLog also writes to HD's disk-backed trail,
            // which is what an in-game issue report ships — so "was this shield live in that session?" stays
            // answerable from the report alone (the exact question issue #233 turned on). Init runs once, so the
            // warning is a once-per-session line without a latch.
            if (propsType != null)
                HDLog.Msg("Survival Tools detected — a pawn's carried survival tools are excluded from surplus "
                          + "unloading.");
            else
                HDLog.Warn("Survival Tools present but SurvivalToolProperties did not resolve (a version/rename?). "
                           + "HD cannot recognise carried survival tools, so with 'unload all surplus' ON an "
                           + "unload↔re-fetch loop may occur; turning that option off avoids it. HD continues.");
        }

        /// <summary>
        /// True if HD should treat this inventory Thing as a Survival Tools tool the pawn keeps for its work — the
        /// mod resolved, the carrier is a HUMANLIKE, and the stack is one of the mod's tool objects. Keep-ALL, not
        /// count-precise, and every fact fails closed to "not a tool": the reasoning for both, and for why an
        /// over-keep here cannot strand HD's own cargo, is <see cref="SurvivalToolKeepPolicy"/>'s.
        ///
        /// <para>Purely READ-ONLY: it inspects the carrier's race, the Thing's runtime class and the def's vanilla
        /// <c>modExtensions</c> list, and touches no Survival Tools state. Safe on the hot surplus/alert/render paths
        /// and deterministic across multiplayer clients. Returns false when the mod is absent, so every call site is
        /// inert without it.</para>
        ///
        /// <para>NOTE: like <see cref="GrabYourToolCompat.IsCarriedTool"/> this does not verify the Thing is in the
        /// pawn's inventory — every caller passes an inventory stack, where that already holds — and it is def-level,
        /// not (def, stuff): the mod tracks tools by def and quality, never by stuff.</para>
        /// </summary>
        /// <param name="pawn">The carrying pawn. Null, or a non-humanlike (a pack animal, a mech), keeps nothing.</param>
        /// <param name="thing">The inventory stack being assessed.</param>
        /// <returns>True when the stack is the pawn's own survival-tool kit and must not be unloaded.</returns>
        public static bool IsCarriedTool(Pawn pawn, Thing thing)
        {
            // Short-circuit for the ordinary game where the mod is absent: C# evaluates every argument of the rule
            // call below eagerly, and SurplusOf asks this for every inventory stack, so the mod-absent answer must
            // not cost a race read and a type test. The rule below is still the whole decision.
            bool resolved = IsActive;
            if (!resolved || pawn?.def == null || thing?.def == null)
                return false;
            return SurvivalToolKeepPolicy.KeepsCarriedTool(resolved,
                pawn.RaceProps != null && pawn.RaceProps.Humanlike,
                IsSurvivalToolThing(thing));
        }

        // The mod's OWN identity predicate, reproduced without a reflective invoke:
        // `def.thingClass == typeof(SurvivalTool) && def.HasModExtension<SurvivalToolProperties>()`
        // (SurvivalToolUtility.cs:411-412). Testing the Thing's runtime class instead of `def.thingClass` is
        // deliberate — it costs one type check instead of a field read plus a comparison, and it also covers a fork
        // that SUBCLASSES the tool class. The mod-extension half is kept because it is what makes a def a tool the
        // mod actually manages: without it the mod would neither auto-pick it up nor re-fetch it, so HD keeping it
        // would pin a stack for no one's benefit. `Def.modExtensions` is a public vanilla list (same walk as
        // DbhCompat.IsKeptDrink). Callers reach this only when IsActive, so both types are non-null here.
        private static bool IsSurvivalToolThing(Thing thing)
        {
            if (!toolType.IsInstanceOfType(thing))
                return false;
            var exts = thing.def.modExtensions;
            if (exts == null)
                return false;
            for (int i = 0; i < exts.Count; i++)
                if (exts[i] != null && propsType.IsInstanceOfType(exts[i]))
                    return true;
            return false;
        }
    }
}
