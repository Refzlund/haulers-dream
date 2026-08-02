using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The Verse-side reader and renderer for <see cref="GatherOwnershipPolicy"/> — one place where the live facts
    /// (is Common Sense pocketing ingredients? is HD's own one-sweep gather enabled?) are turned into the sentence
    /// the player reads, so the per-bench button and the Build &amp; Craft settings tab can never say different
    /// things about the same situation.
    ///
    /// <para>WHY this exists (issue #243): the per-bench "Gather ingredients" button and the crafting-ingredient
    /// settings both described HD's gather as a thing they controlled. When Common Sense is installed with its
    /// haul-all-ingredients option on, HD is not gathering at all — it cedes the whole DoBill flow — so a player
    /// switching those controls off watched their colonists keep pocketing everything and reasonably concluded the
    /// controls were broken. Nothing here CHANGES behaviour; it only stops the UI claiming credit for behaviour
    /// that belongs to another mod, and names the option that actually governs it.</para>
    ///
    /// <para>HD deliberately does NOT suppress or override the other mod's gather. Common Sense's gathering is
    /// Common Sense's feature; the player asked for it by installing it, and reaching in to disable it is not a
    /// hauling mod's business.</para>
    ///
    /// <para>KNOWN GAP, stated rather than papered over: one configuration is still not covered — Common Sense
    /// installed with its CLEANING option on and its haul-all-ingredients option OFF. CS owns the DoBill driver
    /// there (so HD cedes and does not gather), but CS does not gather either — its toils run vanilla's
    /// carry-in-hands collect — so NOBODY gathers into an inventory and the bench description still overclaims.
    /// It is deliberately not folded into <see cref="BenchGatherNotice.ForeignModGathers"/> (nothing foreign is
    /// gathering, so that sentence would be false) nor into <see cref="BenchGatherNotice.GlobalGatherOff"/> (that
    /// sentence blames a checkbox the player has not touched). It wants its own wording, in 16 languages, for a
    /// non-default combination; worth doing if it is ever reported, not worth guessing at now.</para>
    /// </summary>
    public static class GatherNotice
    {
        /// <summary>
        /// Is HD's own one-sweep gather switched on? Reads the live settings; missing settings (very early init)
        /// read as ON, matching the fields' own defaults and every other early-init read in the mod.
        ///
        /// <para>This must mirror the route's OWN entry condition in
        /// <c>Patch_WorkGiver_DoBill_InventoryRoute</c> — all THREE of inventoryCraftDeliver, shareForCrafting and
        /// markForUnload — not just the one checkbox the notice happens to name. Reading only the first let a
        /// player switch the gather off via either of the other two and still be told, by the bench button, that
        /// pawns gather here: the exact overclaim this whole change exists to remove.</para>
        /// </summary>
        private static bool PlainGatherEnabled
        {
            get
            {
                var s = HaulersDreamMod.Settings;
                if (s == null)
                    return true;
                return s.inventoryCraftDeliver && s.shareForCrafting && s.markForUnload;
            }
        }

        /// <summary>Which caveat, if any, applies to HD's gather controls at this moment.</summary>
        public static BenchGatherNotice Current =>
            GatherOwnershipPolicy.ResolveNotice(CommonSenseCompat.GathersIngredients, PlainGatherEnabled);

        /// <summary>
        /// Does the per-bench switch actually govern the plain one-sweep gather right now? False means its state
        /// description alone would overclaim and wants a <see cref="Text"/> notice in front of it. It does NOT
        /// mean the switch is inert — it still vetoes batch crafting and the move-ingredients-closer detour there.
        /// </summary>
        public static bool BenchSwitchGovernsPlainGather =>
            GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(CommonSenseCompat.GathersIngredients, PlainGatherEnabled);

        /// <summary>
        /// The player-facing sentence for a caveat.
        /// </summary>
        /// <param name="notice">The caveat to render.</param>
        /// <returns>The sentence, or null for <see cref="BenchGatherNotice.None"/> (nothing to say).</returns>
        /// <remarks>
        /// Both sentences quote the option the player should actually go and change, and both pull that option's
        /// LABEL rather than repeating it as prose: the foreign one from that mod's OWN keyed translations at
        /// runtime (see <see cref="CommonSenseCompat.HaulAllIngredientsOptionLabel"/>) so it matches what the
        /// player sees in their language, and HD's own from HD's key so the notice cannot drift from the checkbox
        /// it points at.
        /// <para><see cref="BenchGatherNotice.GlobalGatherOff"/> is worded for the per-bench BUTTON, which is its
        /// only caller — the settings tab shows the foreign-gatherer caveat only, because there the "HD's own
        /// gather is off" caveat would just restate the checkbox above it.</para>
        /// </remarks>
        public static string Text(BenchGatherNotice notice)
        {
            switch (notice)
            {
                case BenchGatherNotice.ForeignModGathers:
                {
                    string foreign = "HaulersDream.Notice.ForeignModGathers"
                        .Translate(CommonSenseCompat.ModName, CommonSenseCompat.HaulAllIngredientsOptionLabel);
                    return foreign;
                }
                case BenchGatherNotice.GlobalGatherOff:
                {
                    // Name whichever setting is ACTUALLY off, not always the first one. Three separate checkboxes
                    // can each switch the one-sweep gather off (see PlainGatherEnabled); pointing the player at a
                    // box that is still ticked would be its own small lie. Quote each by its key rather than
                    // repeating its words, so the notice and the control cannot drift apart across 16 languages.
                    string globalOff = "HaulersDream.Notice.GatherOffGlobally".Translate(OffSettingLabel());
                    return globalOff;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// The label of the crafting-gather setting the player has actually switched off, for the
        /// <see cref="BenchGatherNotice.GlobalGatherOff"/> sentence. Checked in the same order the route itself
        /// tests them, so the first blocker is the one named. Falls back to the primary checkbox when settings are
        /// missing (very early init), which is the only one the player would look for anyway.
        /// </summary>
        private static string OffSettingLabel()
        {
            var s = HaulersDreamMod.Settings;
            if (s != null && !s.shareForCrafting)
                return "HaulersDream.Setting.ShareForCrafting".Translate();
            if (s != null && !s.markForUnload)
                return "HaulersDream.Setting.MarkForUnload".Translate();
            return "HaulersDream.Setting.InventoryCraftDeliver".Translate();
        }
    }
}
