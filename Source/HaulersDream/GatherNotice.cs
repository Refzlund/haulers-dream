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
    /// haul-all-ingredients option on, HD is not gathering at all — it cedes the ingredient gather — so a player
    /// switching those controls off watched their colonists keep pocketing everything and reasonably concluded the
    /// controls were broken. Nothing here CHANGES behaviour; it only stops the UI claiming credit for behaviour
    /// that belongs to another mod, and names the option that actually governs it.</para>
    ///
    /// <para>HD deliberately does NOT suppress or override the other mod's gather. Common Sense's gathering is
    /// Common Sense's feature; the player asked for it by installing it, and reaching in to disable it is not a
    /// hauling mod's business.</para>
    ///
    /// <para>THE GAP THAT USED TO BE DOCUMENTED HERE IS CLOSED, and not by adding a fourth sentence. It was:
    /// Common Sense installed with its CLEANING option on and its haul-all-ingredients option OFF — Common Sense
    /// held the driver so HD ceded, Common Sense gathered nothing either, and the bench description overclaimed
    /// about a gather nobody was doing (Lensrub, 2026-08-03: the button stops the gathering correctly but cannot
    /// start it). It was fixed at the SOURCE rather than in the wording: HD now cedes only when Common Sense will
    /// genuinely gather, so in that configuration HD gathers and the plain description is simply true. The three
    /// existing sentences are now exhaustive, because the notice and the cede read ONE fact
    /// (<see cref="CommonSenseCompat.GathersIngredients"/>): a foreign gatherer means HD stood down, no foreign
    /// gatherer plus HD's gather on means HD is doing it, and no foreign gatherer plus HD's gather off is the only
    /// way nobody gathers — which is exactly what <see cref="BenchGatherNotice.GlobalGatherOff"/> already says, and
    /// it now names a checkbox the player really did touch. <c>GatherOwnershipPolicy.ResolveGatherer</c> pins that
    /// correspondence as a test rather than as a claim in this comment.</para>
    /// </summary>
    public static class GatherNotice
    {
        /// <summary>
        /// Is HD's own one-sweep gather switched on? Reads the live settings; missing settings (very early init)
        /// read as ON, matching the fields' own defaults and every other early-init read in the mod.
        ///
        /// <para>The three-checkbox AND is no longer restated here: it is
        /// <see cref="GatherOwnershipPolicy.PlainGatherEnabled"/>, which
        /// <c>Patch_WorkGiver_DoBill_InventoryRoute</c> also calls, so the notice and the route it describes read
        /// one expression. Restating it was how the overclaim got in the first time — the notice read
        /// inventoryCraftDeliver alone, so switching the gather off via either of the other two left the bench
        /// button still promising that pawns gather here.</para>
        /// </summary>
        private static bool PlainGatherEnabled
        {
            get
            {
                var s = HaulersDreamMod.Settings;
                if (s == null)
                    return true;
                return GatherOwnershipPolicy.PlainGatherEnabled(s.inventoryCraftDeliver, s.shareForCrafting, s.markForUnload);
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
