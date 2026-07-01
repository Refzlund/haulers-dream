using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Grab Your Tool! (Continued) compatibility bridge — REFLECTION-FREE presence check, no hard assembly
    /// reference, so the mod runs identically with or without Grab Your Tool installed. Verified against the GYT
    /// source (emipa606/GrabYourTool, packageId <c>Mlie.GrabYourTool</c>, assembly/namespace
    /// <c>CM_Grab_Your_Tool</c>).
    ///
    /// <para>What GYT does: a colonist carries work TOOLS (equippable weapon-defs with
    /// <c>equippedStatOffsets</c> — pickaxes, hammers, sickles, e.g. the "Tools O' Plenty" mod, whose tools are
    /// weapons) in <c>inventory.innerContainer</c>, and GYT auto-swaps the best one into the equipment slot for
    /// the current job (<c>ToolMemoryTracker.EquipAppropriateWeapon</c>). It never drops a tool to the ground —
    /// it moves it between inventory and equipment and keeps it on the pawn indefinitely.</para>
    ///
    /// <para>Why this exists: without it, HD's "unload all surplus" ships a GYT-carried tool to storage (HD's
    /// weapon-keep is otherwise Simple-Sidearms-gated, so with SS absent — or for a tool SS never remembered — a
    /// tool reads as full surplus). GYT then re-fetches it, and HD re-adopts it — an unload↔pickup LOOP, the same
    /// failure class the SS / DBH / CE keep-shims exist to prevent. <see cref="IsCarriedTool"/> reports a carried
    /// tool as keep-stock so <see cref="InventorySurplus.SurplusOf"/> returns 0 and it is never adopted/unloaded,
    /// and the tag guards never auto-tag it. Auto-active on GYT presence (no setting), matching the other
    /// keep-shims; a user who wants a specific tool def shipped to storage anyway can still set an explicit
    /// per-def "Unload always" rule, which wins in <c>SurplusOf</c> before this keep is consulted.</para>
    /// </summary>
    public static class GrabYourToolCompat
    {
        private static bool initialized;
        private static bool active;

        /// <summary>Whether Grab Your Tool is loaded (its mod type resolves). Cached; detected by type, no hard ref.</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return active;
            }
        }

        private static void Init()
        {
            initialized = true;
            // No try/catch: GYT-ABSENT is the TypeByName == null precondition (it returns null, never throws).
            // Lazy, once (on first IsActive).
            active = AccessTools.TypeByName("CM_Grab_Your_Tool.GrabYourToolMod") != null;
            if (active)
                HDLog.Msg("Grab Your Tool detected — carried tools are excluded from surplus unloading.");
        }

        /// <summary>
        /// True if HD should treat this inventory Thing as a Grab Your Tool carried TOOL the pawn keeps for its
        /// work — GYT present, the pawn is a HUMANLIKE (a colonist or slave — anyone who does work with tools; NOT
        /// a pack animal, so a weapon loaded onto a carrier still unloads normally), and the def is a weapon that
        /// confers equipped stat offsets (i.e. a work tool: exactly the criterion GYT uses to pick a tool for a
        /// job — see <c>ToolMemoryTracker.HasRelevantStatModifiers</c>, which requires <c>def.equippedStatOffsets</c>).
        /// A plain combat weapon carrying no equipped offsets is NOT kept here (it is left to Simple Sidearms if it
        /// is a remembered sidearm, else it stays unloadable), so this targets tools, not the whole weapon rack.
        ///
        /// <para><c>Humanlike</c> (not <c>IsColonist</c>) is deliberate: <c>Pawn.IsColonist</c> excludes an
        /// insecure slave and a subhuman worker, both of which can still carry and use a GYT tool, so gating on it
        /// would ship their tools to storage. A non-player humanlike is never fed to <see cref="InventorySurplus.SurplusOf"/>
        /// / the tag guards (HD only manages its own colony's inventory), so the broader gate keeps nothing wrongly.</para>
        ///
        /// <para>Purely READ-ONLY: it inspects only the pawn's race and the def, and touches NO GYT state (GYT's
        /// <c>ToolMemoryTracker.GetMemory</c> LAZILY ALLOCATES a memory entry — a write — so it must never be
        /// called from a hot MP read path like <see cref="InventorySurplus.SurplusOf"/>). Deterministic across
        /// multiplayer clients. Returns false when GYT is absent, so every call site is inert without it.</para>
        ///
        /// <para>NOTE: this is a def-level "is this a tool this pawn would keep" test — it does not read GYT's
        /// per-pawn memory or verify the Thing is in the pawn's inventory (callers pass an inventory stack, where
        /// that already holds). Unlike the Simple Sidearms compat, it is def-only, not (def, stuff).</para>
        /// </summary>
        public static bool IsCarriedTool(Pawn pawn, Thing thing)
        {
            if (!IsActive || pawn?.def == null || thing?.def == null)
                return false;
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return false;
            var def = thing.def;
            if (!def.IsWeapon)
                return false;
            var offsets = def.equippedStatOffsets;
            return offsets != null && offsets.Count > 0;
        }
    }
}
