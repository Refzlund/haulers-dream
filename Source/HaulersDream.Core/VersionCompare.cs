using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Dotted-numeric version comparison, tolerant of differing component counts and non-numeric suffixes.
    /// Used to decide whether the running mod version is behind the latest version published to the Steam
    /// Workshop. The mod reports a 4-part assembly version (e.g. "1.13.0.0"); the published value is the
    /// 3-part package version (e.g. "1.13.0"), so a tolerant component-wise compare is required.
    /// </summary>
    public static class VersionCompare
    {
        /// <summary>
        /// True when <paramref name="latest"/> is a strictly higher version than <paramref name="current"/>
        /// (i.e. the player is behind). Returns false when either is missing or unparseable, so a bad value
        /// can never raise a false "out of date" warning.
        /// </summary>
        public static bool IsOutdated(string current, string latest)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return false;
            return Compare(latest, current) > 0;
        }

        /// <summary>Compare two dotted versions: -1 if a&lt;b, 0 if equal, 1 if a&gt;b. Missing components are 0.</summary>
        public static int Compare(string a, string b)
        {
            int[] pa = Parse(a), pb = Parse(b);
            int n = pa.Length > pb.Length ? pa.Length : pb.Length;
            for (int i = 0; i < n; i++)
            {
                int x = i < pa.Length ? pa[i] : 0;
                int y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        static int[] Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return new int[0];
            var parts = v.Split('.');
            var nums = new List<int>(parts.Length);
            foreach (var p in parts)
            {
                int val = 0;
                foreach (char c in p)
                {
                    if (c >= '0' && c <= '9') val = val * 10 + (c - '0');
                    else break; // stop at the first non-digit (e.g. "0-beta" -> 0)
                }
                nums.Add(val);
            }
            return nums.ToArray();
        }
    }
}
