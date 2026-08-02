namespace HaulersDream.Core
{
    /// <summary>Why a withdrawing pawn may not take a kept drug from another colonist, or Allow.</summary>
    public enum DrugShareVerdict
    {
        /// <summary>Every clause passed: take one dose from the holder.</summary>
        Allow = 0,

        /// <summary>The player turned the feature off.</summary>
        FeatureOff = 1,

        /// <summary>Vanilla's own search produced a job — HD must never override it.</summary>
        VanillaFoundDrug = 2,

        /// <summary>No <c>Need_Chemical</c> at Desire-or-worse, so nothing to satisfy.</summary>
        NoChemicalNeed = 3,

        /// <summary>Holder is self / unspawned / dead / downed / drafted / mental / caravan / unreachable.</summary>
        HolderNotEligible = 4,

        /// <summary>The stack is in that inventory for a VANILLA reason — leave it alone.</summary>
        NotPinnedByHaulersDream = 5,

        /// <summary>The holder has none of it left.</summary>
        NothingHeld = 6,
    }

    /// <summary>
    /// Decides whether a colonist in withdrawal may walk to another colonist and take a dose of the drug they
    /// are addicted to (issue #229, the "Keep in inventory" drug lockout).
    ///
    /// <para>THE EXPLOIT. All four vanilla drug searches are spawned-only or colony-ANIMAL-only:
    /// <c>JobGiver_SatisfyChemicalNeed.FindDrugFor</c> and <c>JobGiver_TakeDrugsForDrugPolicy.FindDrugFor</c>
    /// each check (1) the seeker's own inventory, (2) <c>GenClosest.ClosestThingReachable</c> over
    /// <c>ThingRequestGroup.Drug</c> (spawned things only), (3) <c>mapPawns.SpawnedColonyAnimals</c>; and
    /// <c>JobGiver_BingeDrug.BestIngestTarget</c> / <c>AddictionUtility.CanBingeOnNow</c> are spawned-only. A
    /// drug in a COLONIST's inventory is therefore invisible to every one of them. Hauler's Dream then pins it
    /// there permanently (its keep-in-inventory surplus rule, plus the two #81 guards that veto vanilla's
    /// drop-unused loop for a kept drug), so telling one colonist to keep a drug hides it from an addict for
    /// good. Vanilla's own invariant is the opposite: its drop loop sheds any drug a colonist has no policy or
    /// addiction reason to hold, and <c>FloatMenuOptionProvider_PickUpItem</c> will not even offer "Pick up" for
    /// such a drug on the home map.</para>
    ///
    /// <para>THE SCOPING RULE (the whole safety argument). Only stacks HAULER'S DREAM ITSELF pinned are
    /// reachable this way. A drug vanilla put in an inventory — a drug-policy <c>takeToInventory</c> supply, an
    /// addicted holder's own stash — stays exactly as invisible as it is in vanilla. HD fixes what HD caused and
    /// rebalances nothing. Note the game layer must measure that honestly: a stack merely PRESENT in HD's tag set
    /// is not proof HD is why it is held (the tag self-heal adopts same-def stacks the pawn already carried), so
    /// it clamps the tagged case on "still surplus above the holder's own keep-stock" before reporting it here.
    /// This policy therefore treats <c>stackPinnedByHaulersDream</c> as an assertion about WHY the stack is held,
    /// not about tag membership — see the game layer's <c>IsPinnedByHd</c>.</para>
    ///
    /// <para>Pure: the game layer extracts the primitives and applies the effects.</para>
    /// </summary>
    public static class DrugSharePolicy
    {
        /// <summary>Units one take may move. Vanilla's own two TakeFromOtherInventory sites both set
        /// <c>job.count = 1</c>, so a take never drains a holder: the addict comes back for the next dose and
        /// every trip re-tests eligibility.</summary>
        public const int UnitsPerTake = 1;

        /// <summary>
        /// Whether a take may be built, and if not, which clause refused. Clause ORDER is part of the contract:
        /// the cheap feature/vanilla gates run before any scan, and the HD-pinned check runs before the held
        /// count so a vanilla-held stack is never even counted.
        /// </summary>
        /// <param name="featureEnabled">The player's "let a colonist in withdrawal take a kept drug" setting.</param>
        /// <param name="vanillaFoundDrug">Whether vanilla's own drug search already produced a job. HD must
        /// never override a result vanilla found — it only fills the gap vanilla cannot see into.</param>
        /// <param name="seekerHasChemicalNeed">Whether the seeker has a <c>Need_Chemical</c> at
        /// <c>DrugDesireCategory.Desire</c> or worse — vanilla's own trigger for this think node.</param>
        /// <param name="holderEligible">Whether the candidate holder may be drawn from: a distinct, spawned,
        /// alive, undrafted, non-mental, reachable player-faction colonist not forming a caravan.</param>
        /// <param name="stackPinnedByHaulersDream">Whether HD itself is why the stack sits in that inventory: a
        /// keep-in-inventory pin, or HD haul cargo the holder still carries as surplus above its own keep-stock.
        /// False for a drug vanilla put there — including one HD's tag self-heal merely adopted, which the caller
        /// must exclude by measuring surplus rather than tag membership.</param>
        /// <param name="heldUnits">Units of the drug in that stack. Zero or less means nothing to take.</param>
        /// <returns><see cref="DrugShareVerdict.Allow"/>, or the first clause that refused.</returns>
        public static DrugShareVerdict Evaluate(
            bool featureEnabled, bool vanillaFoundDrug, bool seekerHasChemicalNeed,
            bool holderEligible, bool stackPinnedByHaulersDream, int heldUnits)
        {
            if (!featureEnabled) return DrugShareVerdict.FeatureOff;
            if (vanillaFoundDrug) return DrugShareVerdict.VanillaFoundDrug;
            if (!seekerHasChemicalNeed) return DrugShareVerdict.NoChemicalNeed;
            if (!holderEligible) return DrugShareVerdict.HolderNotEligible;
            if (!stackPinnedByHaulersDream) return DrugShareVerdict.NotPinnedByHaulersDream;
            if (heldUnits <= 0) return DrugShareVerdict.NothingHeld;
            return DrugShareVerdict.Allow;
        }

        /// <summary>
        /// How many units one take moves out of a stack of <paramref name="heldUnits"/>: the single dose, or the
        /// whole remainder when the holder has less than a dose left.
        /// </summary>
        /// <param name="heldUnits">Units in the holder's stack. Zero or negative yields 0.</param>
        /// <returns><c>min(heldUnits, <see cref="UnitsPerTake"/>)</c>, floored at 0.</returns>
        public static int UnitsToTake(int heldUnits)
            => heldUnits <= 0 ? 0 : (heldUnits < UnitsPerTake ? heldUnits : UnitsPerTake);
    }
}
