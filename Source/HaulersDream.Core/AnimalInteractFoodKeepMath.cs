using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure "how much of a food stack a pawn is holding FOR a tame/train job" math — the keep source vanilla has
    /// but Hauler's Dream did not model, and the reason a hauler sheds the kibble it is carrying to train an
    /// animal (reported on Steam twice).
    ///
    /// <para><b>Why HD's existing food keep can never cover this.</b> HD's packable-food keep
    /// (<see cref="FoodKeepMath"/>, the runtime wrapper <c>InventorySurplus.FoodKeepCountOf</c>) is gated on
    /// vanilla <c>JobGiver_PackFood.IsGoodPackableFoodFor</c>, which requires
    /// <c>(int)ingestible.preferability &gt;= 7</c> (<see cref="MinPackablePreferability"/>). Vanilla's
    /// animal-interaction food test, <c>WorkGiver_InteractAnimal.HasFoodToInteractAnimal</c>, rejects anything with
    /// <c>(int)preferability &gt; 5</c> (<see cref="MaxInteractPreferability"/>). The two ranges are DISJOINT — no
    /// food a pawn can hand to an animal is ever packable food — so the packed-lunch keep is structurally incapable
    /// of protecting it. Kibble is <c>RawBad</c> (4). The oracle test pins that disjointness across the whole
    /// <c>FoodPreferability</c> range so a vanilla shift cannot quietly re-open the gap.</para>
    ///
    /// <para><b>Why vanilla itself never drops it.</b> Vanilla protects interaction food with a CLOCK, not a
    /// keep-list: <c>JobDriver_InteractAnimal.StartFeedAnimal</c> re-arms <c>lastInventoryRawFoodUseTick</c>, and
    /// <c>JobGiver_DropUnusedInventory</c>'s raw-food loop only fires ~2.5 in-game days after that (see
    /// <see cref="DropUnusedFoodPolicy"/>). HD's unload has no such clock, so the food reads as 100% surplus and
    /// is shipped to storage. That is ours to fix, and it is why this must be modelled as a real keep.</para>
    ///
    /// <para><b>Deliberately NOT reusing <see cref="DropUnusedFoodPolicy.IsRawFoodDropCandidate"/>.</b> The two
    /// expressions coincide today (<c>ingestible &amp;&amp; !drug &amp;&amp; preferability &lt;= 5</c>) but they model
    /// DIFFERENT vanilla methods — one the drop loop, one the interaction-food test — and must stay free to drift
    /// independently when either changes.</para>
    ///
    /// <para>Verse-free: the runtime wrapper (<c>InventorySurplus.AnimalInteractFoodKeepCountOf</c>) reads the live
    /// nutrition numbers and decides whether an interaction job is actually live; this leaf does the arithmetic.</para>
    /// </summary>
    public static class AnimalInteractFoodKeepMath
    {
        /// <summary>The highest <c>(int)ingestible.preferability</c> vanilla will hand to an animal.
        /// <c>WorkGiver_InteractAnimal.HasFoodToInteractAnimal</c> skips a stack when
        /// <c>(int)thing.def.ingestible.preferability &gt; 5</c>, so 5 (<c>RawTasty</c>) is inclusive. Kibble is
        /// <c>RawBad</c> (4).</summary>
        public const int MaxInteractPreferability = 5;

        /// <summary>The lowest <c>(int)ingestible.preferability</c> vanilla treats as PACKABLE food
        /// (<c>JobGiver_PackFood.IsGoodPackableFoodFor</c> requires <c>&gt;= 7</c>, i.e. <c>MealAwful</c> and up).
        /// Pinned here only to make the disjointness from <see cref="MaxInteractPreferability"/> a testable fact —
        /// it is what proves HD's packable-food keep could never have covered interaction food.</summary>
        public const int MinPackablePreferability = 7;

        /// <summary>Vanilla <c>JobDriver_InteractAnimal.NutritionPercentagePerFeed</c>: one feed is this fraction
        /// of the ANIMAL's food-need <c>MaxLevel</c>.</summary>
        public const float NutritionFractionPerFeed = 0.15f;

        /// <summary>Vanilla <c>JobDriver_InteractAnimal.MaxMinNutritionPerFeed</c>: the hard cap on one feed's
        /// nutrition, however large the animal. This is what bounds the whole keep — see
        /// <see cref="MaxReserveNutrition"/>.</summary>
        public const float MaxNutritionPerFeed = 0.3f;

        /// <summary>Feeds' worth of nutrition vanilla fetches in one trip: <c>WorkGiver_InteractAnimal</c>'s food
        /// searches all ask for <c>RequiredNutritionPerFeed(tamee) * 2f * 4f</c> (two feeds per interaction,
        /// four interactions' stock), so 8.</summary>
        public const float FeedsFetchedPerTrip = 8f;

        /// <summary>The absolute ceiling on a reserve: <see cref="MaxNutritionPerFeed"/> ×
        /// <see cref="FeedsFetchedPerTrip"/> = 2.4 nutrition, reached by any animal whose food need is large enough
        /// to hit the per-feed cap. LOAD-BEARING: without a nutrition ceiling a pawn that had swept a 200-unit
        /// kibble stack for hauling would pin the entire stack the moment it also had a training job.</summary>
        public const float MaxReserveNutrition = MaxNutritionPerFeed * FeedsFetchedPerTrip;

        /// <summary>
        /// EXACTLY the def-level half of vanilla's animal-interaction food test
        /// (<c>WorkGiver_InteractAnimal.HasFoodToInteractAnimal</c>): would vanilla accept a stack with these def
        /// properties as food to hand an animal? Vanilla's remaining term is the per-ANIMAL
        /// <c>tamee.WillEat(thing, pawn)</c>, which is not a def property and is deliberately left to the caller —
        /// omitting it makes this predicate WIDER, i.e. it can only ever over-protect, never shed food an animal
        /// would have eaten.
        /// </summary>
        /// <param name="isIngestible">The def has an <c>ingestible</c> block (<c>ThingDef.IsIngestible</c>).</param>
        /// <param name="isDrug">The def is a drug — vanilla excludes drugs from interaction food outright.</param>
        /// <param name="preferabilityInt">The def's <c>(int)ingestible.preferability</c>.</param>
        public static bool IsInteractFood(bool isIngestible, bool isDrug, int preferabilityInt)
            => isIngestible && !isDrug && preferabilityInt <= MaxInteractPreferability;

        /// <summary>
        /// Vanilla <c>JobDriver_InteractAnimal.RequiredNutritionPerFeed</c>:
        /// <c>Min(animal.needs.food.MaxLevel * 0.15f, 0.3f)</c>. A non-eating animal (vanilla's
        /// <c>needs.food == null</c>) is expressed here as a 0 max level, which yields 0.
        /// </summary>
        /// <param name="animalFoodMaxLevel">The animal's food-need <c>MaxLevel</c>; 0 or negative → 0 nutrition.</param>
        public static float RequiredNutritionPerFeed(float animalFoodMaxLevel)
        {
            if (animalFoodMaxLevel <= 0f)
                return 0f;
            float perFeed = animalFoodMaxLevel * NutritionFractionPerFeed;
            return perFeed < MaxNutritionPerFeed ? perFeed : MaxNutritionPerFeed;
        }

        /// <summary>
        /// The nutrition vanilla actually fetches for one animal-interaction trip:
        /// <c>RequiredNutritionPerFeed(animal) * 2f * 4f</c> — the amount asked of
        /// <c>FoodUtility.BestFoodSourceOnMap</c> by <c>WorkGiver_Tame</c>, and by
        /// <c>WorkGiver_InteractAnimal.TakeFoodForAnimalInteractJob</c> for training. Always
        /// <c>&lt;= <see cref="MaxReserveNutrition"/></c>.
        /// </summary>
        /// <param name="animalFoodMaxLevel">The animal's food-need <c>MaxLevel</c>.</param>
        public static float ReserveNutrition(float animalFoodMaxLevel)
            => RequiredNutritionPerFeed(animalFoodMaxLevel) * FeedsFetchedPerTrip;

        /// <summary>
        /// Units of a stack to KEEP for a live animal interaction: the unit count that covers
        /// <paramref name="reserveNutrition"/>, clamped to <c>[0, stackCount]</c>.
        ///
        /// <para><b>Why ceiling.</b> Vanilla sizes the fetch two slightly different ways —
        /// <c>WorkGiver_Tame</c> uses <c>Mathf.CeilToInt(reserve / nutrition)</c>, while
        /// <c>TakeFoodForAnimalInteractJob</c> uses <c>FoodUtility.StackCountForNutrition</c> =
        /// <c>Max(RoundToInt(reserve / nutrition), 1)</c>. Ceiling is <c>&gt;=</c> both for every positive input,
        /// so one formula covers taming and training without ever keeping less than what vanilla fetched.</para>
        ///
        /// <para>Discipline mirrors <see cref="FoodKeepMath.KeepCount"/>: a non-positive per-unit nutrition keeps
        /// NOTHING rather than dividing by zero and pinning the stack — an unbounded keep is a black hole, and a
        /// zero-nutrition "food" is not something vanilla would have fetched anyway.</para>
        /// </summary>
        /// <param name="reserveNutrition">Nutrition to hold back (see <see cref="ReserveNutrition"/>); 0 or
        /// negative keeps nothing.</param>
        /// <param name="perUnitNutrition">Per-unit nutrition of this stack's def; 0 or negative keeps nothing.</param>
        /// <param name="stackCount">Units in this stack — the cap on the result.</param>
        /// <returns>Units to keep, in <c>[0, stackCount]</c>.</returns>
        public static int KeepCount(float reserveNutrition, float perUnitNutrition, int stackCount)
        {
            if (stackCount <= 0 || perUnitNutrition <= 0f || reserveNutrition <= 0f)
                return 0;

            int units = (int)Math.Ceiling(reserveNutrition / perUnitNutrition);
            if (units <= 0)
                return 0;
            return units > stackCount ? stackCount : units;
        }
    }
}
