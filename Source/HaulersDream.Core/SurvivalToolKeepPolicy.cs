namespace HaulersDream.Core
{
    /// <summary>
    /// Pure "is this inventory stack a Survival Tools tool the pawn keeps for its work" rule. The runtime wrapper
    /// (<c>SurvivalToolsCompat.IsCarriedTool</c>) reflects the mod's two identity types and reads the pawn's race
    /// from vanilla; this leaf composes the decision, so it is unit-testable headlessly and multiplayer-deterministic
    /// (no game types, no ordering, no side effects).
    ///
    /// THE MODEL, and why it is keep-ALL rather than count-precise. Survival Tools ("Survival Tools Reborn",
    /// packageId <c>jellypowered.survivaltools</c>, and the older Survival Tools it continues) holds a colonist's
    /// pickaxes/axes/sickles in <c>inventory.innerContainer</c> and re-fetches them on its own schedule: an auto-pickup
    /// postfix on <c>JobGiver_Work.TryIssueJobPackage</c> queues a <c>TakeInventory</c> for the best map tool before a
    /// gated job, and <c>JobGiver_OptimizeSurvivalTools</c> rescans every 3600-14400 ticks — and its acquisition policy
    /// treats a stockpiled tool as a valid SOURCE (<c>ToolIsAcquirableByPolicy</c> accepts any
    /// <c>tool.IsInAnyStorage()</c>). So a tool HD ships to a shelf is a re-fetch candidate BY DESIGN: without this
    /// keep, HD's "unload all surplus" and the mod's pickup trade the same toolkit back and forth forever — the
    /// unload<->re-fetch loop the Simple Sidearms / Grab Your Tool / Combat Extended keeps already sever.
    ///
    /// The mod polices its own EXCESS (an idle drop once the pawn is over its <c>SurvivalToolCarryCapacity</c>, and
    /// the optimizer's own dedup/downgrade drops, both of which store to stockpiles themselves), so HD has no count to
    /// compute and no shortfall to model: the correct contract is simply "never take an inventory survival tool off a
    /// pawn that can use one". Reading the mod's per-pawn assignment filter for a count-precise keep would add
    /// reflection risk to shave nothing.
    ///
    /// WHY OVER-KEEPING CANNOT STRAND HD'S OWN CARGO here, unlike the Compositable Loadouts case
    /// (<see cref="CompositableLoadoutKeepPolicy"/>): the caller applies this keep only to a stack HD did NOT sweep
    /// (<c>InventorySurplus.SurplusOf</c> guards the branch with <c>!hdSwept</c>), so a loose tool HD bulk-hauled off
    /// the ground stays unloadable and reaches storage normally. The keep can only ever pin a tool the pawn acquired
    /// for itself — which is exactly the set the mod would re-fetch — and an explicit per-def "Unload always" rule
    /// still wins over it.
    ///
    /// EVERY FACT FAILS CLOSED TO <c>false</c> ("not a keep-item"), which is the deliberate direction: an unresolved
    /// binding, an unknown race or an unrecognised def re-opens a self-correcting churn loop, whereas failing OPEN
    /// would pin arbitrary inventory a colonist can never shed.
    /// </summary>
    public static class SurvivalToolKeepPolicy
    {
        /// <summary>
        /// Whether HD must treat this inventory stack as the pawn's own survival-tool kit and leave the WHOLE stack
        /// alone (no surplus, no adoption, no auto-tag). All three facts must hold; the caller supplies each one
        /// already read, so this rule holds no game state and can be exercised for every combination.
        /// </summary>
        /// <param name="modResolved">Whether the Survival Tools identity types both bound. False both when the mod is
        /// absent (the ordinary case — HD is then inert) and when it is present but a load-bearing type did not
        /// resolve, e.g. a rename in a fork; the shim warns once in the latter case and lands here either way.</param>
        /// <param name="pawnIsHumanlike">Whether the carrier is a humanlike — a colonist or a slave, i.e. anyone who
        /// does work with tools. Mirrors the mod's OWN carrier gate (<c>CanUseSurvivalTools</c> requires
        /// <c>RaceProps.Humanlike</c>), so a tool loaded onto a pack animal or a mech is not kept and still unloads
        /// normally. Deliberately broader than "is a colonist": an insecure slave carries and uses tools too, and HD
        /// only ever asks this about its own colony's pawns.</param>
        /// <param name="thingIsSurvivalTool">Whether the stack is one of the mod's tool objects — its
        /// <c>SurvivalTool</c> thing-class AND the <c>SurvivalToolProperties</c> mod extension that carries the
        /// work-stat factors. Both halves, because that pair is the mod's own <c>IsSurvivalTool</c> predicate: a def
        /// missing the extension is not a tool the mod manages or would re-fetch, so keeping it would buy nothing.</param>
        /// <returns>True when the stack is the pawn's survival-tool kit and must not be unloaded.</returns>
        public static bool KeepsCarriedTool(bool modResolved, bool pawnIsHumanlike, bool thingIsSurvivalTool)
            => modResolved && pawnIsHumanlike && thingIsSurvivalTool;
    }
}
