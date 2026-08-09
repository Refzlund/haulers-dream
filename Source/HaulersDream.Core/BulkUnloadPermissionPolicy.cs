namespace HaulersDream.Core
{
    /// <summary>
    /// MAY THE PLAYER HAVE THIS CARRIER'S POCKETS EMPTIED? The permission question behind the bulk-unload
    /// feature ("Prioritize bulk unloading …", the work-scan takeover, and the job driver's flag write) —
    /// deliberately separate from <see cref="BulkUnloadCarrierPolicy"/>, which answers the different question of
    /// HOW MUCH to pull once permission is settled.
    ///
    /// <para>WHY THIS EXISTS AS A RULE RATHER THAN A CONDITION. HD's three entry points originally reused
    /// vanilla's own job-time predicate — <c>carrier.Faction == pawn.Faction || carrier.HostFaction ==
    /// pawn.Faction</c> — which is sound in vanilla for one reason that does not survive the copy: vanilla only
    /// ever evaluates it AFTER game flow has already set <c>Pawn_InventoryTracker.UnloadEverything</c> on that
    /// carrier, and every writer of that flag in the shipped assembly is game flow acting on pawns the colony
    /// already controls (caravan arrival and forming, transport pods, shuttle unload, portal entry, farskip).
    /// No player action can raise it. Reused as an OFFER predicate the flag gate is gone, and the
    /// <c>HostFaction</c> arm on its own admits every pawn the colony merely HOSTS — a Hospitality visitor, a
    /// rescued wanderer, a guest-status quest pawn such as a downed Bestower carrying a psylink neuroformer —
    /// none of whom the player may take anything from. Vanilla protects them with no fewer than four separate
    /// gates (the unreachable flag; <c>StrippableUtility.CanBeStrippedByColony</c> plus the strip menu's own
    /// quest-related refusal; <c>ITab_Pawn_Gear.CanControl</c>), and HD's corpse path already honours the same
    /// set. This rule is what puts the living-pawn path back in line with them.</para>
    ///
    /// <para>WHAT THE HOST-FACTION ARM WAS ACTUALLY FOR, and is preserved here: <b>prisoners</b>. A colony
    /// prisoner's <c>Faction</c> is its ORIGINAL faction and only its <c>HostFaction</c> is the player, so the
    /// faction arm alone cannot see one — yet a caravan arriving with prisoners flags every pawn it brought
    /// (<c>CaravanEnterMapUtility</c>, faction-blind over the caravan's pawn list), and vanilla's
    /// <c>HostFaction</c> arm is exactly what lets a colonist then unload them. Taking a prisoner's goods is
    /// vanilla-sanctioned by two independent routes — <c>ITab_Pawn_Gear.CanControl</c> admits
    /// <c>IsPrisonerOfColony</c>, and <c>CanBeStrippedByColony</c> admits a secure prisoner — so the prisoner
    /// case is kept rather than lost to the fix.</para>
    ///
    /// <para>SLAVES need no arm of their own and are unaffected: an enslaved pawn's <c>Faction</c> IS the
    /// player's (<c>Pawn.IsSlaveOfColony</c> reads <c>Faction.IsPlayer</c>), so
    /// <paramref name="sharesHaulerFaction"/> already covers them exactly as before.</para>
    ///
    /// <para>NOT A FACT THIS RULE ACCEPTS: whether the carrier's <c>UnloadEverything</c> flag is already set.
    /// Trusting it would look like tidy vanilla parity and would be a trapdoor — the flag is SCRIBED, so a save
    /// made while the old offer existed still carries it on a guest, and "the flag is set" would then re-grant
    /// permission on exactly the pawns this rule exists to protect. Permission is decided from who the carrier
    /// IS, never from a bit an earlier bug may have written.</para>
    /// </summary>
    public static class BulkUnloadPermissionPolicy
    {
        /// <summary>
        /// The single permission rule shared by every HD entry point that can empty a carrier: the player may
        /// bulk-unload a pawn it OWNS (its own faction) or HOLDS PRISONER, and never one that is quest-related.
        ///
        /// <para>The quest clause vetoes both arms rather than only the prisoner one. That is deliberate and it
        /// mirrors vanilla's strip menu, which refuses any pawn with an extra home faction outright ("Cannot
        /// strip: quest related") without first asking whose pawn it is; it also matches what every sibling HD
        /// entry point already does with a quest lodger (<c>BulkHaul</c>, <c>EnRoutePickup</c>,
        /// <c>CorpseStripper</c>, <c>SlaughterHaul</c>, <c>UrgentHaulBulk</c>). A quest pawn's belongings leave
        /// with the quest, so emptying its pockets is a failed quest waiting to happen whichever faction tag it
        /// currently wears.</para>
        ///
        /// <para>A refusal costs the BULK path only. Every caller falls through to whatever vanilla would have
        /// done unaided, so this can never suppress an unload the game itself authorised — it only stops HD
        /// originating one.</para>
        /// </summary>
        /// <param name="sharesHaulerFaction">
        /// Is the carrier of the hauler's OWN faction? True for the feature's whole intended population — colony
        /// pack animals, colony mechs, colony slaves — and the reason a refusal here is invisible in ordinary
        /// play. A carrier with no faction at all (a wild animal) is false, as is a hauler with none.
        /// </param>
        /// <param name="isPrisonerOfHaulerFaction">
        /// Is the carrier a PRISONER held by the hauler's faction — guest status Prisoner, host faction the
        /// hauler's? This is the whole legitimate content of the old host-faction arm. Note that it must be the
        /// prisoner status specifically and not "hosted": a guest, a rescued pawn and a quest lodger all carry
        /// the same host faction and none of them may be taken from.
        /// </param>
        /// <param name="questRelated">
        /// Does a live quest have a claim on this pawn (an extra HOME or MINI faction — vanilla's
        /// <c>IsQuestLodger()</c>, the superset of the strip menu's own <c>HasExtraHomeFaction()</c> test)?
        /// </param>
        /// <returns>True only for a carrier the player genuinely owns or holds, with no quest claim on it.</returns>
        public static bool MayBulkUnload(bool sharesHaulerFaction, bool isPrisonerOfHaulerFaction, bool questRelated)
            => (sharesHaulerFaction || isPrisonerOfHaulerFaction) && !questRelated;
    }
}
