using HarmonyLib;
using HaulersDream.Core;
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
    ///   • <c>autoOpenCarrierGear</c> — selecting one of the PLAYER's non-humanlike carriers (pack animal, colony
    ///     mech, Vehicle Framework vehicle) that is currently holding something opens the tab that shows its load:
    ///     <see cref="ITab_Pawn_Gear"/> for a pawn-shaped carrier, or VF's <c>ITab_Vehicle_Cargo</c> for a vehicle
    ///     (which has no Gear tab at all — see the carrier branch below).
    ///
    /// AUDIENCE (#224). The carrier branch USED to fire for any pawn holding HD-tagged cargo, which in practice meant
    /// COLONISTS and essentially never the pack animals the setting advertises:
    ///   – HD's tags live on the pawn that SCOOPED the stack. The pack-animal deposit toil moves cargo into the
    ///     carrier and then DEREGISTERS the tag from the HAULER (JobDriver_LoadPackAnimal.cs:253-255); no code path
    ///     anywhere registers a tag on a carrier's own comp, so a loaded muffalo's PeekHashSet is EMPTY.
    ///   – Animals are not scoop-eligible at all unless allowAnimals is on (EligibilityPolicy.cs:22-26; the setting
    ///     defaults false), so they cannot acquire tags on their own either.
    /// The tag test therefore selected exactly the audience the label excludes. The audience is now the label's one:
    /// non-humanlike + player-faction + non-empty inventory (<see cref="TabAutoOpenPolicy.CarrierGearApplies"/>).
    ///
    /// SELECTION-CHANGED MEMO (#224). <c>Selector.Select</c> has NO already-selected early-out (SelectInternal only
    /// skips the list ADD via its <c>!IsSelected(obj)</c> guard, decompile-verified), and a plain click runs
    /// <c>ClearSelection(); Select(obj)</c> on EVERY click — including a re-click of the pawn that is already
    /// selected. Without a memo, closing the tab by hand and clicking the same thing again re-forced it open and
    /// replayed SoundDefOf.TabOpen. We now remember the <c>thingIDNumber</c> of the last single-selected thing and
    /// only auto-open when the selection genuinely CHANGED. An id (not a Thing reference) so the memo can never keep
    /// a despawned pawn alive or go stale across a save/load; <see cref="Reset"/> is registered with
    /// <see cref="CacheRegistry"/> so every new game / load starts clean (ids restart per game). The memo's rules
    /// live in the game-free <see cref="SelectionMemo"/> so the whole click SEQUENCE is unit-tested headlessly;
    /// this patch only maps Selector events onto it, with <see cref="Patch_Selector_ClearSelection"/> and
    /// <see cref="Patch_Selector_Deselect"/> reporting the two ways a selection is emptied.
    ///
    /// HOOK (decompile-verified, RimWorld 1.6): postfix on <c>RimWorld.Selector.Select(object obj, bool playSound = true,
    /// bool forceDesignatorDeselect = true)</c> — the single funnel every selection (click / drag-box single result /
    /// keyboard / CameraJumper) passes through. We only act when EXACTLY one thing is selected
    /// (<c>NumSelected == 1</c>) so we never fight a multi-select.
    ///
    /// OPEN API (decompile-verified, 1.6): <c>InspectPaneUtility.OpenTab(System.Type)</c>. It scans the selected
    /// thing's live <c>CurTabs</c> (which reads straight off Find.Selector, so it already reflects THIS selection)
    /// for a tab whose type is assignable to the requested one and, IFF such a tab exists, switches the main button
    /// to Inspect and toggles that tab open. Strictly safer than BLFT's manual <c>OpenTabType =</c> assignment:
    ///   – if the thing does NOT have that tab (Gear hidden, an exotic transporter), OpenTab is a clean no-op;
    ///   – the IsAssignableFrom match means a mod that SUBCLASSES the vanilla tab (e.g. an inventory-tab replacer)
    ///     is opened correctly, and one that swaps in an unrelated ITab simply gets the no-op — HD can never open
    ///     the WRONG tab.
    ///
    /// NEAR-INERT WHEN OFF: with both toggles off the postfix still maintains the selection memo (three static field
    /// writes per selection) so flipping a toggle on mid-game behaves correctly on the very next click — HD settings
    /// toggle live without a restart. No game-state mutation, no ledger/tag touch, no exception suppression (HD
    /// idiom: a fault here is a real bug to surface). Selector state is client-local, so nothing here is MP-synced.
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class Patch_Selector_Select
    {
        // The per-session selection memo (never scribed; this is UI state, not world state). One instance, mutated
        // in place — see SelectionMemo for why it is a class rather than a struct.
        private static readonly SelectionMemo memo = new SelectionMemo();

        // Self-register the memo reset with the game-load hygiene sweep (see CacheRegistry): thingIDNumber counters
        // restart with each game, so a memo carried across a quickload could otherwise collide with a different
        // thing in the new session and swallow one auto-open.
        static Patch_Selector_Select() => CacheRegistry.Register(Reset);

        internal static void Reset() => memo.Reset();

        /// <summary>Record that the selection was emptied, on the CURRENT frame. Called from the two companion
        /// postfixes below (<c>ClearSelection</c>, and a <c>Deselect</c> that emptied the selection). See
        /// <see cref="SelectionMemo.NotifyCleared"/> for why an already-pending gap is never overwritten.</summary>
        internal static void NotifyCleared() => memo.NotifyCleared(RealTime.frameCount);

        [HarmonyPostfix]
        static void Postfix(Selector __instance)
        {
            // Only act on an unambiguous single selection — never override the player's tab during a multi-select.
            // A multi-select also invalidates the memo: whatever is single-selected next is a fresh selection.
            if (__instance == null || __instance.NumSelected != 1)
            {
                memo.Invalidate();
                return;
            }

            Thing thing = __instance.SingleSelectedThing;
            if (thing == null)
            {
                memo.Invalidate();
                return;
            }

            // Selection-changed gate. Maintained even when both toggles are off so a mid-game flip is correct on the
            // very next click — NotifySelected records the id and consumes the gap whether or not we use the result.
            if (!memo.NotifySelected(thing.thingIDNumber, RealTime.frameCount))
                return;

            var s = HaulersDreamMod.Settings;
            if (s == null || (!s.autoOpenTransporterContents && !s.autoOpenCarrierGear))
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

            // --- Carrier branch: one of the PLAYER's non-humanlike carriers actually holding something. RaceProps
            // is null only for a malformed def; treat unknown as humanlike (conservative -> excluded). ---
            if (s.autoOpenCarrierGear && thing is Pawn pawn
                && TabAutoOpenPolicy.CarrierGearApplies(
                    pawn.RaceProps?.Humanlike ?? true,
                    pawn.Faction != null && pawn.Faction == Faction.OfPlayerSilentFail,
                    (pawn.inventory?.innerContainer?.Count ?? 0) > 0))
            {
                // A VF VehiclePawn shares this audience gate (non-humanlike, player-faction, cargo aboard) but NOT
                // the tab: it has no Gear tab at all. VehicleDef "BaseVehiclePawn" is abstract with no ParentName,
                // so it never inherits BasePawn's tab list, and its own <inspectorTabs> lists only the VF/caravan
                // tabs — asking OpenTab for ITab_Pawn_Gear on a vehicle matched nothing and silently did nothing.
                // Only the tab TYPE differs, so the swap lives inside the branch rather than ahead of it.
                var tabType = VehicleFrameworkCompat.IsVehicle(thing)
                    ? VehicleFrameworkCompat.VehicleCargoTabType
                    : typeof(ITab_Pawn_Gear);
                // Null = VF inactive or a fork that renamed the cargo tab. SKIP rather than fall back to Gear or
                // anything else arbitrary: the failure mode is then exactly the pre-fix behaviour (nothing opens),
                // never the wrong tab. OpenTab itself does NOT null-check its argument, so this guard is required.
                //
                // KNOWN COSMETIC EDGE (not a bug — don't "fix" it): ITab_Vehicle_Cargo.IsVisible is
                // `!Vehicle.beached`, but OpenTab's match predicate never consults IsVisible. So selecting a
                // BEACHED loaded boat plays the tab-open sound and sets an OpenTabType that the pane's own
                // UpdateTabs closes again next frame. Pre-checking it would mean reflecting VehiclePawn.beached —
                // a whole new compat handle for a tab that closes itself — which is not worth it. The vanilla
                // side has no equivalent edge: the non-empty-inventory gate above is exactly
                // ITab_Pawn_Gear.ShouldShowInventory's condition for a non-humanlike.
                if (tabType != null)
                    InspectPaneUtility.OpenTab(tabType);
            }
        }
    }

    /// <summary>
    /// Companion to <see cref="Patch_Selector_Select"/>: records an emptied selection so the auto-open memo re-arms
    /// after the player clicks bare ground. See <see cref="SelectionMemo.NotifyCleared"/> for why a naive "reset on
    /// clear" would be wrong (vanilla clears before EVERY select). Pure bookkeeping — two static field writes, no
    /// game state touched.
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.ClearSelection))]
    public static class Patch_Selector_ClearSelection
    {
        [HarmonyPostfix]
        static void Postfix() => Patch_Selector_Select.NotifyCleared();
    }

    /// <summary>
    /// The OTHER way a selection is emptied: shift-clicking your one selected thing removes it via
    /// <c>Selector.Deselect</c>, which never calls <c>ClearSelection</c>. Without this the memo kept the id and a
    /// following click on that same thing read as a re-click, so the tab stayed shut — contradicting "click away and
    /// select it again and it opens".
    ///
    /// <para>Only an EMPTIED selection counts (<c>NumSelected == 0</c>): a deselect that leaves other things
    /// selected is a multi-select edit, not a return to nothing. That one condition is what makes this correct at
    /// EVERY <c>Deselect</c> caller — and there are more of them than the player-facing ones, including per-tick
    /// (<c>Pawn.IsHiddenFromPlayer</c>), despawn/destroy (<c>Thing.DeSpawnOrDeselect</c> / <c>DeSpawn</c> /
    /// <c>Destroy</c>) and per-frame designator (<c>Designator_ZoneAdd</c> / <c>Designator_Plan_Add</c>
    /// <c>SelectedUpdate</c>) sources. Do NOT reason caller-by-caller; the two outcomes cover all of them:
    ///   – a <c>Select</c> DOES follow in the same frame (the shift-select loops, the cross-map prune): the gap is
    ///     consumed harmlessly by the same-frame rule in <see cref="TabAutoOpenPolicy.IsNewSelection"/>;
    ///   – no <c>Select</c> follows (a lone shift-deselect, a hidden-pawn deselect at tick time, a despawn or
    ///     destroy, a designator dropping its Zone/Plan): the selection really IS empty, so a later click on that
    ///     same thing genuinely IS a new selection and SHOULD open.
    /// The invariant that closes it — and the thing to re-check if this ever looks wrong — is that nothing
    /// re-populates <c>Selector.selected</c> without going through <c>Select</c> (<c>SelectInternal</c> is private,
    /// reached only from <c>Select</c> and its own <c>CompSelectProxy</c> recursion). So an armed gap can never
    /// outlive the next selection: whatever refills the selection consumes it.</para>
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.Deselect))]
    public static class Patch_Selector_Deselect
    {
        [HarmonyPostfix]
        static void Postfix(Selector __instance)
        {
            if (__instance != null && __instance.NumSelected == 0)
                Patch_Selector_Select.NotifyCleared();
        }
    }
}
