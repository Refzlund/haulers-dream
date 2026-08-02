using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Every screenshot the in-game issue reporter can offer for attachment, gathered from BOTH of the places a
    /// RimWorld screenshot can end up.
    ///
    /// There are two places because vanilla decides per shot, not per player: <c>Verse.ScreenshotTaker.TakeShot()</c>
    /// hands the capture to Steam only while <c>SteamManager.Initialized &amp;&amp; SteamUtils.IsOverlayEnabled()</c>,
    /// and otherwise falls through to <c>TakeNonSteamShot()</c>, which writes a .png straight into
    /// <see cref="GenFilePaths.ScreenshotFolderPath"/> and toasts "screenshot saved as…" either way. So a player who
    /// has the Steam overlay switched off — or whose overlay hotkey collides with another binding, which is exactly
    /// what issue #167 described — takes screenshots the game cheerfully confirms saving and Steam never sees.
    /// Scanning either source alone leaves one of those two players staring at an empty grid with nothing they can
    /// do about it, so both are scanned and merged rather than one being picked.
    ///
    /// RimWorld's folder doubles as the drop box for images the player made outside the game — the picker's
    /// "open screenshots folder" button leads here, standing in for the file browser RimWorld has no API for —
    /// which is why discovery accepts .jpg/.jpeg as well as vanilla's own .png.
    /// </summary>
    public static class ReportScreenshots
    {
        /// <summary>
        /// RimWorld's own screenshot folder — and, as a side effect, CREATES it when it is missing.
        ///
        /// The creation is vanilla's own doing, not ours: <c>GenFilePaths.ScreenshotFolderPath</c> is
        /// <c>FolderUnderSaveData("Screenshots")</c>, whose getter calls <c>DirectoryInfo.Create()</c> on a missing
        /// folder. That is precisely what lets the picker's "open screenshots folder" button work for a player who
        /// has never taken a non-Steam shot, so it is leaned on deliberately — but nobody reading a call site would
        /// guess that a property getter creates a directory, which is why this is a named method and not a
        /// pass-through property.
        /// </summary>
        /// <returns>The absolute folder path, which exists by the time this returns.</returns>
        public static string EnsureFolder() => GenFilePaths.ScreenshotFolderPath;

        /// <summary>
        /// The player's screenshots from both sources, most-recently-modified first.
        /// </summary>
        /// <param name="max">
        /// Hard cap on the returned count, applied AFTER merging so the newest entries win regardless of which
        /// folder they came from. The picker decodes a thumbnail per cell the player scrolls past, so this also
        /// bounds its memory. Anything below 0 is treated as 0.
        /// </param>
        /// <returns>A fresh list the caller owns; empty when neither source holds anything.</returns>
        public static List<ScreenshotEntry> FindRecent(int max = 120)
        {
            // Steam first: its entries carry a pre-made thumbnail, so on the (impossible-in-practice, but free to
            // honour) chance that both scans see the same file, the cheap-to-display one is the survivor.
            var found = SteamScreenshots.FindRecent(max);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < found.Count; i++)
                seen.Add(found[i].fullPath);

            string folder = EnsureFolder();
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder)) // non-recursive: vanilla writes flat into here
                {
                    if (!IsAttachableImage(file) || !seen.Add(file)) continue;
                    found.Add(new ScreenshotEntry
                    {
                        fullPath = file,
                        // Nothing pre-made to point at — vanilla writes the full-resolution png and nothing
                        // else — so the picker shrinks the full image itself for the grid.
                        thumbPath = file,
                        name = Path.GetFileName(file),
                        modified = File.GetLastWriteTime(file)
                    });
                }
            }

            // Re-sort and re-cap across BOTH sources: a shot taken a minute ago has to outrank an old Steam one
            // whichever folder it landed in. Letting the Steam scan cap itself first is safe — anything it dropped
            // was already older than `max` surviving Steam entries, so it could never belong in the merged top N.
            return found.OrderByDescending(e => e.modified).Take(Mathf.Max(0, max)).ToList();
        }

        /// <summary>
        /// Reveal RimWorld's screenshot folder in the player's file manager, so an image made outside the game can
        /// be dropped in without this mod having to grow a file browser (RimWorld exposes no native picker).
        ///
        /// Vanilla does the same thing the same way — Options' "Save game data folder" button and the mod list's
        /// "Mod folder" entry both call <c>Application.OpenURL</c> on a plain folder path — but both gate
        /// themselves to <c>WindowsPlayer</c>/<c>WindowsEditor</c>. That gate is deliberately NOT copied: hiding
        /// the button on macOS/Linux would leave those players with no way at all to attach an arbitrary image,
        /// and the picker shows the folder path in this button's tooltip regardless, so a platform where the OS
        /// ignores the request still leaves the player somewhere to go.
        /// </summary>
        /// <returns>
        /// false only when the folder could not even be resolved or created (the reason is logged); true once the
        /// request has been handed to the OS, which is as much as <c>Application.OpenURL</c> ever reports back.
        /// </returns>
        public static bool TryOpenFolder()
        {
            try
            {
                Application.OpenURL(EnsureFolder());
                return true;
            }
            catch (Exception ex)
            {
                // REPORTed at a UI boundary, never swallowed: the caller tells the player the path instead, and
                // this line is what answers "why did the button do nothing?" when they ask. Caught broadly on
                // purpose — opening a folder is a convenience, and letting any platform-specific failure escape
                // would take down the report the player is halfway through writing.
                HDLog.Warn("could not open RimWorld's screenshot folder: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Whether a file sitting in the screenshot folder should be offered in the picker.
        /// </summary>
        /// <param name="path">Any file path; only the extension is read, case-insensitively.</param>
        /// <returns>
        /// true for the image types that BOTH upload (<see cref="ReportApi.ContentTypeFor"/>) and decode into a
        /// preview (Unity's <c>ImageConversion.LoadImage</c> handles png and jpg only). The upload side accepts
        /// more than this — webp, bmp, even video — but those would sit in the grid as a bare filename with no
        /// preview, so they are left out of discovery rather than shown looking broken.
        /// </returns>
        private static bool IsAttachableImage(string path)
        {
            switch (Path.GetExtension(path ?? string.Empty).ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg": return true;
                default: return false;
            }
        }
    }
}
