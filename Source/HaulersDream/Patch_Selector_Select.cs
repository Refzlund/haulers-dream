using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Auto-open the relevant inspect tab when the player SELECTS a single thing (BLFT parity, gap #10 / C2). Two
    /// independent, default-OFF conveniences:
    ///   • <c>autoOpenTransporterContents</c> — selecting a transporter or shuttle opens its Contents tab
    ///     (<see cref="ITab_ContentsTransporter"/>; a shuttle is a <see cref="CompTransporter"/> parent with an added
    ///     <see cref="CompShuttle"/> and uses the SAME contents tab — there is no separate ITab_ContentsShuttle in 1.6).
    ///   • <c>autoOpenCarrierGear</c> — selecting a pawn that is an HD courier currently holding HD-tagged cargo opens
    ///     its Gear tab (<see cref="ITab_Pawn_Gear"/>), so the player can see what it is carrying. (BLFT keyed the Gear
    ///     auto-open on the trader-caravan "Carrier" role; HD has no pack-animal trade role to read, so it keys on the
    ///     HD-specific "is a courier with tagged cargo" signal instead — the natural HD analogue.)
    ///
    /// HOOK (decompile-verified, RimWorld 1.6): postfix on <c>RimWorld.Selector.Select(object obj, bool playSound = true,
    /// bool forceDesignatorDeselect = true)</c> — the single funnel every selection (click / drag-box single result /
    /// keyboard) passes through. We only act when EXACTLY one thing is selected (<c>NumSelected == 1</c>) so we never
    /// fight a multi-select.
    ///
    /// OPEN API (decompile-verified, 1.6): <c>InspectPaneUtility.OpenTab(System.Type)</c>. It scans the selected thing's
    /// resolved <c>CurTabs</c> for a tab whose type is assignable to the requested one and, IFF such a tab exists, switches
    /// the main button to Inspect and toggles that tab open (setting <c>MainTabWindow_Inspect.OpenTabType</c> internally).
    /// This is strictly safer than BLFT's manual <c>OpenTabType =</c> assignment:
    ///   – if the thing does NOT have that tab (e.g. a transporter with no contents tab, or a pawn with the Gear tab
    ///     hidden), <c>OpenTab</c> is a clean no-op — it never leaves a dangling OpenTabType that matches no visible tab;
    ///   – it only toggles when the tab is not ALREADY open (its internal <c>!IsOpen</c> guard), so a re-select of the
    ///     same thing does not flicker the tab and there is NO per-tick churn (this runs ONCE per Select call, not per
    ///     frame — unlike BLFT's <c>DoWindowContents</c> executor twin, which we don't need).
    ///
    /// BYTE-INERT WHEN OFF: both settings default false; the postfix's first lines return before touching any tab when
    /// neither is on. We do NOT gate this out in <see cref="Prepare"/> (which runs once at PatchAll) because HD settings
    /// toggle live without a restart — the runtime gate keeps the patch installed-but-dormant so flipping a toggle on
    /// mid-game takes effect immediately, matching the OFF-default HD idiom (e.g. Patch_OpportunisticLoadDeposit). The
    /// Select-time cost when dormant is a single bool check; when active, one int compare + at most two TryGetComp on a
    /// single-selected thing — negligible.
    ///
    /// PURE UI: opens a tab the player could open by hand. No game-state mutation, no ledger/tag touch (the carrier
    /// branch reads <see cref="CompHauledToInventory.PeekHashSet"/> — the read-only, no-self-heal view). No exception
    /// suppression (HD idiom: a fault here is a real bug to surface).
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class Patch_Selector_Select
    {
        [HarmonyPostfix]
        static void Postfix(Selector __instance)
        {
            var s = HaulersDreamMod.Settings;
            // Dormant when neither convenience is enabled -> a single bool check and out (byte-inert default).
            if (s == null || (!s.autoOpenTransporterContents && !s.autoOpenCarrierGear))
                return;

            // Only act on an unambiguous single selection — never override the player's tab during a multi-select.
            if (__instance == null || __instance.NumSelected != 1)
                return;

            Thing thing = __instance.SingleSelectedThing;
            if (thing == null)
                return;

            // --- Transporter / shuttle branch: open the Contents tab. CompShuttle parents are CompTransporter
            // parents too, so the single CompTransporter check covers both, and ITab_ContentsTransporter is the
            // contents tab for both in 1.6 (no separate shuttle contents tab exists). OpenTab no-ops if the tab
            // isn't present, so this is safe even for an exotic transporter without a resolved contents tab. ---
            if (s.autoOpenTransporterContents && thing.TryGetComp<CompTransporter>() != null)
            {
                InspectPaneUtility.OpenTab(typeof(ITab_ContentsTransporter));
                return;
            }

            // --- Carrier branch: a pawn that is an HD courier currently holding HD-tagged cargo -> open Gear, so
            // the player can see the load. PeekHashSet is the read-only view (no self-heal / CE re-notify), correct
            // for this UI touchpoint; we still require at least one tag to currently sit in the pawn's inventory so
            // a stale/destroyed tag never triggers an open on a pawn carrying nothing. ---
            if (s.autoOpenCarrierGear && thing is Pawn pawn)
            {
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var inner = pawn.inventory?.innerContainer;
                if (hcomp != null && inner != null)
                {
                    foreach (var carried in hcomp.PeekHashSet())
                    {
                        if (carried != null && !carried.Destroyed && inner.Contains(carried))
                        {
                            InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Gear));
                            break;
                        }
                    }
                }
            }
        }
    }
}
