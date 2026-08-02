using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using UnityEngine;

namespace HaulersDream
{
    /// <summary>
    /// One screenshot the in-game reporter can attach, from either source (see <see cref="ReportScreenshots"/>):
    /// the file that gets uploaded, plus whatever the picker should decode to preview it.
    /// </summary>
    public struct ScreenshotEntry
    {
        /// <summary>Absolute path of the image actually uploaded with the report — always the full-resolution file.</summary>
        public string fullPath;

        /// <summary>
        /// Absolute path of the image the picker decodes for its grid cell. Steam supplies a small pre-made
        /// thumbnail; everything else (RimWorld's own screenshots, images the player dropped in) has none, so this
        /// is the full-resolution file again and the picker shrinks it before caching.
        /// </summary>
        public string thumbPath;

        /// <summary>File name with extension — shown in the cell when the preview cannot be decoded.</summary>
        public string name;

        /// <summary>Last-write time, the sort key that puts the player's newest shot at the top of the grid.</summary>
        public DateTime modified;
    }

    /// <summary>
    /// Locates the player's Steam screenshots for RimWorld (Steam app id 294100). Steam stores them at
    /// <c>&lt;SteamRoot&gt;/userdata/&lt;accountId&gt;/760/remote/294100/screenshots/*.jpg</c>, with small thumbnails
    /// under <c>screenshots/thumbnails/</c>. The Steam root is found from the registry (Windows) or derived from the
    /// game's own install path.
    ///
    /// This is only ONE of the two places a RimWorld screenshot can land — vanilla writes to its own folder whenever
    /// the Steam overlay is off. The reporter therefore goes through <see cref="ReportScreenshots"/>, which merges
    /// both; see that type for why.
    /// </summary>
    public static class SteamScreenshots
    {
        private const string RimWorldAppId = "294100";

        /// <summary>The most-recent screenshots first, capped at <paramref name="max"/>. Empty if none/Steam not found.</summary>
        public static List<ScreenshotEntry> FindRecent(int max = 120)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<ScreenshotEntry>();

            foreach (var root in SteamRoots())
            {
                var userdata = Path.Combine(root, "userdata");
                if (!Directory.Exists(userdata)) continue;

                foreach (var account in Directory.GetDirectories(userdata))
                {
                    var dir = Path.Combine(account, "760", "remote", RimWorldAppId, "screenshots");
                    if (!Directory.Exists(dir)) continue;

                    var thumbs = Path.Combine(dir, "thumbnails");
                    foreach (var file in Directory.GetFiles(dir, "*.jpg")) // non-recursive: excludes thumbnails/
                    {
                        if (!seen.Add(file)) continue; // a registry root + derived root can resolve to the same path
                        var name = Path.GetFileName(file);
                        var thumb = Path.Combine(thumbs, name);
                        entries.Add(new ScreenshotEntry
                        {
                            fullPath = file,
                            thumbPath = File.Exists(thumb) ? thumb : file,
                            name = name,
                            modified = File.GetLastWriteTime(file)
                        });
                    }
                }
            }

            return entries.OrderByDescending(e => e.modified).Take(Mathf.Max(0, max)).ToList();
        }

        /// <summary>Candidate Steam install roots (each should contain a <c>userdata</c> folder), best first.</summary>
        private static IEnumerable<string> SteamRoots()
        {
            // 1. Registry (Windows): HKCU\Software\Valve\Steam\SteamPath is the install root (forward-slashed).
            if (Application.platform == RuntimePlatform.WindowsPlayer)
            {
                string fromReg = null;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    fromReg = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(fromReg) && Directory.Exists(fromReg))
                    yield return fromReg;
            }

            // 2. Derive from the game's own install path: the first ancestor of dataPath that holds a userdata/
            //    folder is the Steam root (RimWorld runs from <SteamRoot>/steamapps/common/RimWorld/...).
            var d = SafeDir(Application.dataPath);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "userdata")))
                {
                    yield return d.FullName;
                    break;
                }
                d = d.Parent;
            }
        }

        private static DirectoryInfo SafeDir(string path)
        {
            return string.IsNullOrEmpty(path) ? null : new DirectoryInfo(path);
        }
    }
}
