using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The mods Hauler's Dream REPLACES, plus detection of whether the user still has any of them active. Drives
    /// the settings "Migration" tab, which only appears when at least one replaced mod is active — a clean-transition
    /// guide, because running a replaced mod alongside Hauler's Dream makes them fight over the same hauling jobs
    /// (e.g. Pick Up And Haul wins the haul scan and HD's sweep no-ops), which a user perceives as flaky/missing
    /// pickup. See COMPATIBILITY.md.
    ///
    /// Detection is deliberately BROAD, so community translations and continuations are caught even though their
    /// packageId differs from the original:
    ///   1. Exact packageId variants via <see cref="ModLister.GetActiveModWithIdentifier"/> with <c>ignorePostfix</c>
    ///      (case-insensitive, strips RimWorld's <c>_steam</c>/<c>_copy</c> suffix) — the precise, known ids.
    ///   2. Normalized SUBSTRING match against every active mod's display <see cref="ModMetaData.Name"/> AND its
    ///      <see cref="ModMetaData.PackageId"/>. Normalizing to lowercase ASCII letters/digits (spaces/punctuation/CJK
    ///      stripped) means a translated "Pick Up And Haul 日本語" → "pickupandhaul…" and a continuation
    ///      "SomeAuthor.PickUpAndHaul" → "someauthorpickupandhaul" both contain the token "pickupandhaul" and match,
    ///      regardless of the new packageId. The replaced-mod titles are long and mutually non-overlapping, so the
    ///      substring test does not cross-match between entries or collide with unrelated mods.
    ///
    /// The ids/titles were cross-verified against each mod's own About.xml / Steam Workshop metadata. "Allow Dumb
    /// Labor" (from HD's "Replaces …" blurb) is deliberately NOT here: it is not a real standalone mod (an HD feature
    /// label; its niche is Everyone Hauls), and the pre-1.1 kevlou "While You're Up" is irrelevant on 1.6.
    /// </summary>
    public static class ModReplacements
    {
        public readonly struct Replaced
        {
            public readonly string Name;
            public readonly string[] PackageIds;
            public Replaced(string name, params string[] packageIds) { Name = name; PackageIds = packageIds; }
        }

        public static readonly Replaced[] All =
        {
            new Replaced("Pick Up And Haul", "Mehni.PickUpAndHaul"),
            new Replaced("Harvest and Haul", "laredson.harvestandhaul"),
            new Replaced("Auto Strip on Haul", "Fuu.AutoStripOnHaul"),
            new Replaced("Haul After Stripping", "Ilarion.HaulAfterStripping"),
            new Replaced("Everyone Hauls", "defi.everyonehauls", "assassinsbro.everyonehauls.unofficial"),
            new Replaced("Haul to Stack", "jkluch.HaulToStack", "com.jkluch.HaulToStack"),
            new Replaced("Bulk Load For Transporters", "Ilarion.BulkLoadForTransporters"),
            new Replaced("While You're Up", "codeoptimist.jobsofopportunity", "hoodie.whileyoureup", "kevlou127.WhileHYouOreHUpHQ1V0S"),
            new Replaced("Meals on Wheels", "Uuugggg.MealsOnWheels", "Memegoddess.MealsOnWheels"),
            new Replaced("Haul After Slaughter", "puremj.mjrimmods.vanillafixhaulafterslaughter"),
        };

        // The active mod list is fixed for a session (changing it requires a restart), so resolve once and cache.
        private static List<ModMetaData> _active;

        /// <summary>The replaced mods the user CURRENTLY has active (resolved once, then cached), deduped — each
        /// active mod appears once even if it matches by both packageId and title. Empty when none.</summary>
        public static List<ModMetaData> ActiveReplaced
        {
            get
            {
                if (_active != null)
                    return _active;
                _active = new List<ModMetaData>();
                var seen = new HashSet<string>();

                // (1) precise: exact packageId variants (original + known continuation/reupload ids).
                foreach (var r in All)
                    for (int i = 0; i < r.PackageIds.Length; i++)
                    {
                        var m = ModLister.GetActiveModWithIdentifier(r.PackageIds[i], ignorePostfix: true);
                        if (m != null && seen.Add(m.PackageIdNonUnique))
                            _active.Add(m);
                    }

                // (2) broad: normalized title/packageId substring over the active load order — catches translations
                // and continuations whose packageId we don't list.
                var tokens = new string[All.Length];
                for (int i = 0; i < All.Length; i++)
                    tokens[i] = Norm(All[i].Name);

                foreach (var mod in ModsConfig.ActiveModsInLoadOrder)
                {
                    if (mod == null || mod.IsCoreMod)
                        continue;
                    if (seen.Contains(mod.PackageIdNonUnique))
                        continue;
                    // Don't flag Hauler's Dream itself, and skip anything we already matched.
                    if (mod.SamePackageId(HaulersDreamMod.PackageId, ignorePostfix: true))
                        continue;
                    // Name and packageId joined with a space separator: Norm() emits only ASCII letters/digits, so
                    // the space can't be part of any token, meaning a match can never span the name->packageId seam.
                    string hay = Norm(mod.Name) + " " + Norm(mod.PackageId);
                    for (int i = 0; i < tokens.Length; i++)
                        if (tokens[i].Length > 0 && hay.Contains(tokens[i]))
                        {
                            if (seen.Add(mod.PackageIdNonUnique))
                                _active.Add(mod);
                            break;
                        }
                }
                return _active;
            }
        }

        /// <summary>Display names of the active replaced mods (the mod's own <see cref="ModMetaData.Name"/>).</summary>
        public static List<string> ActiveNames
        {
            get
            {
                var list = ActiveReplaced;
                var names = new List<string>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    names.Add(list[i].Name.NullOrEmpty() ? list[i].PackageId : list[i].Name);
                return names;
            }
        }

        /// <summary>True if the user has at least one mod HD replaces active — gates the settings "Migration" tab.</summary>
        public static bool AnyActive => ActiveReplaced.Count > 0;

        /// <summary>Disable every active replaced mod and restart the game (RimWorld must restart for a mod-list
        /// change to take effect — exactly what the vanilla Mods menu does). Caller is expected to confirm first.</summary>
        public static void DisableAllAndRestart()
        {
            var list = ActiveReplaced;
            for (int i = 0; i < list.Count; i++)
                ModsConfig.SetActive(list[i], active: false);
            ModsConfig.Save();
            GenCommandLine.Restart();
        }

        // Lowercase ASCII letters/digits only (spaces, punctuation, and non-ASCII such as CJK are dropped). This
        // makes the substring test robust to translation formatting ("Pick Up And Haul (日本語)" → "pickupandhaul…")
        // and to author-prefixed continuation packageIds ("Mlie.PickUpAndHaul" → "mliepickupandhaul").
        private static string Norm(string s)
        {
            if (s.NullOrEmpty())
                return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                if (ch < 128 && char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }
    }
}
