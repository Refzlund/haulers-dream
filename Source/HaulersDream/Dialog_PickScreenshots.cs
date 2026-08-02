using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// A thumbnail grid of the player's recent screenshots — from Steam AND from RimWorld's own folder, see
    /// <see cref="ReportScreenshots"/> — with multi-select. Returns the chosen full-resolution file paths to the
    /// caller.
    ///
    /// Previews are decoded lazily for visible cells only, shrunk to cell size before caching, and freed when the
    /// dialog closes. Shrinking matters because only Steam ships a small pre-made thumbnail: a RimWorld screenshot
    /// is a multi-megabyte full-resolution png, and caching one decoded texture per cell scrolled past would run
    /// into hundreds of megabytes over a full 120-entry scan.
    /// </summary>
    public class Dialog_PickScreenshots : Window
    {
        // Longest edge, in pixels, a cached preview may have. Grid cells are ~185 GUI units wide, so this leaves
        // headroom for a magnified UI scale while capping each cached entry near 110 KB (RGB24) — about 13 MB
        // across the whole 120-entry scan, whatever resolution the player plays at.
        private const int ThumbMaxEdge = 256;

        private readonly List<ScreenshotEntry> entries = new List<ScreenshotEntry>();
        private readonly HashSet<string> selected;
        private readonly int maxSelectable;
        private readonly Action<List<string>> onConfirm;
        private readonly Dictionary<string, Texture2D> thumbCache = new Dictionary<string, Texture2D>();
        private Vector2 scroll;

        // RimWorld's screenshot folder, resolved once per scan: the tooltip below re-reads it every frame, and
        // the vanilla getter behind it touches the disk (it creates the folder when missing).
        private string screenshotFolder;

        public Dialog_PickScreenshots(IEnumerable<string> alreadySelected, int maxSelectable, Action<List<string>> onConfirm)
        {
            selected = new HashSet<string>(alreadySelected ?? Enumerable.Empty<string>());
            this.maxSelectable = maxSelectable;
            this.onConfirm = onConfirm;

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;

            Rescan();
        }

        public override Vector2 InitialSize => new Vector2(820f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            var prevFont = Text.Font;
            var prevColor = GUI.color;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "HaulersDream.Report.PickTitle".Translate());
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 24f),
                "HaulersDream.Report.PickSubtitle".Translate(selected.Count, maxSelectable));
            GUI.color = prevColor;

            float btnRowY = inRect.height - 36f;
            var gridRect = new Rect(0f, 62f, inRect.width, btnRowY - 70f);

            if (entries.Count == 0)
            {
                var pa = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(gridRect, "HaulersDream.Report.PickEmpty".Translate());
                GUI.color = prevColor;
                Text.Anchor = pa;
            }
            else
            {
                DrawGrid(gridRect);
            }

            // Buttons. The left pair is the escape hatch for a player whose image is in neither folder yet:
            // open the folder, drop the file in, press Refresh. RimWorld has no native file browser, and a
            // hand-rolled one would be wildly out of proportion to attaching a picture to a bug report.
            var openRect = new Rect(0f, btnRowY, 250f, 32f);
            var refreshRect = new Rect(260f, btnRowY, 110f, 32f);
            var cancelRect = new Rect(inRect.width - 290f, btnRowY, 130f, 32f);
            var addRect = new Rect(inRect.width - 150f, btnRowY, 150f, 32f);

            if (Widgets.ButtonText(openRect, "HaulersDream.Report.PickOpenFolder".Translate()))
            {
                if (!ReportScreenshots.TryOpenFolder())
                    Messages.Message("HaulersDream.Report.PickOpenFolderFailed".Translate(screenshotFolder),
                        MessageTypeDefOf.RejectInput, false);
            }
            TooltipHandler.TipRegion(openRect, "HaulersDream.Report.PickOpenFolder.Help".Translate(screenshotFolder));

            // Re-scan on demand rather than automatically on window focus: a rescan triggered the instant the
            // player alt-tabs back can catch a file mid-copy, decode a truncated image, and cache the failure —
            // leaving a permanently blank cell for a picture that is perfectly fine. A button also costs one
            // field fewer than tracking focus edges.
            if (Widgets.ButtonText(refreshRect, "HaulersDream.Report.PickRefresh".Translate()))
                Rescan();

            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
                Close();
            if (Widgets.ButtonText(addRect, "HaulersDream.Report.PickConfirm".Translate(selected.Count)))
            {
                onConfirm?.Invoke(entries.Where(e => selected.Contains(e.fullPath)).Select(e => e.fullPath).ToList());
                Close();
            }

            Text.Font = prevFont;
            GUI.color = prevColor;
        }

        private void DrawGrid(Rect rect)
        {
            const float gap = 10f;
            const float targetCell = 175f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((rect.width + gap) / (targetCell + gap)));
            float cellW = (rect.width - 16f - (cols - 1) * gap) / cols; // 16 = scrollbar reservation
            float imgH = cellW * 9f / 16f;
            float labelH = 18f;
            float cellH = imgH + labelH + 4f;
            int rows = Mathf.CeilToInt(entries.Count / (float)cols);

            var view = new Rect(0f, 0f, rect.width - 16f, rows * (cellH + gap));
            Widgets.BeginScrollView(rect, ref scroll, view);

            float visTop = scroll.y - cellH;
            float visBottom = scroll.y + rect.height + cellH;

            for (int i = 0; i < entries.Count; i++)
            {
                int col = i % cols, row = i / cols;
                float y = row * (cellH + gap);
                if (y < visTop || y > visBottom) continue; // only build/load cells near the viewport

                var cell = new Rect(col * (cellW + gap), y, cellW, cellH);
                DrawCell(cell, entries[i], imgH);
            }

            Widgets.EndScrollView();
        }

        private void DrawCell(Rect cell, ScreenshotEntry e, float imgH)
        {
            var imgRect = new Rect(cell.x, cell.y, cell.width, imgH);
            bool isSelected = selected.Contains(e.fullPath);

            Widgets.DrawBoxSolid(imgRect, new Color(0f, 0f, 0f, 0.35f));
            var tex = Thumb(e);
            if (tex != null)
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit);
            else
                Widgets.Label(imgRect.ContractedBy(6f), e.name);

            if (Mouse.IsOver(imgRect) && !isSelected)
                Widgets.DrawBoxSolid(imgRect, new Color(1f, 1f, 1f, 0.08f));

            if (isSelected)
            {
                Widgets.DrawBoxSolid(imgRect, new Color(0.2f, 0.8f, 0.4f, 0.18f));
                Widgets.DrawBox(imgRect, 2);
                var check = new Rect(imgRect.xMax - 26f, imgRect.y + 6f, 20f, 20f);
                Widgets.DrawBoxSolid(check, new Color(0.16f, 0.6f, 0.32f, 0.95f));
                var pa = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(check, "✓");
                Text.Anchor = pa;
            }

            var prevFont = Text.Font;
            var prevColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            Widgets.Label(new Rect(cell.x, imgRect.yMax + 2f, cell.width, 16f), e.modified.ToString("yyyy-MM-dd HH:mm"));
            GUI.color = prevColor;
            Text.Font = prevFont;

            if (Widgets.ButtonInvisible(imgRect))
                Toggle(e);
        }

        private void Toggle(ScreenshotEntry e)
        {
            if (selected.Contains(e.fullPath))
            {
                selected.Remove(e.fullPath);
                return;
            }
            if (selected.Count >= maxSelectable)
            {
                Messages.Message("HaulersDream.Report.PickMax".Translate(maxSelectable), MessageTypeDefOf.RejectInput, false);
                return;
            }
            selected.Add(e.fullPath);
        }

        // Lazily decode a cell's preview; cache the result (null = failed/missing, so a bad file is not retried
        // every frame). The image that ends up cached is never bigger than a cell — see ShrinkToThumbnail. The
        // full-resolution file is read again later, at upload time.
        private Texture2D Thumb(ScreenshotEntry e)
        {
            if (thumbCache.TryGetValue(e.thumbPath, out var cached))
                return cached;

            Texture2D tex = null;
            if (File.Exists(e.thumbPath))
            {
                var data = File.ReadAllBytes(e.thumbPath);
                // Mipmapped on purpose: the shrink below is a straight GPU resample, and a 1920x1080 screenshot
                // reduced to 256 wide without mip levels is a point-sample of one texel in seven — visibly
                // speckled. Costs a third more memory on a texture that is about to be thrown away anyway.
                var full = new Texture2D(2, 2, TextureFormat.RGB24, true);
                if (full.LoadImage(data)) tex = ShrinkToThumbnail(full);
                else UnityEngine.Object.Destroy(full);
            }
            thumbCache[e.thumbPath] = tex;
            return tex;
        }

        /// <summary>
        /// Reduce a freshly decoded image to something no larger than a grid cell, TAKING OWNERSHIP of
        /// <paramref name="full"/> — it is destroyed here (or handed straight back, if it was already small
        /// enough), so the caller must not keep a reference to it.
        ///
        /// Mirrors vanilla's own resample idiom (<c>Verse.TextureAtlasHelper.MakeReadableTextureInstance</c>):
        /// blit into a temporary RenderTexture at the target size, read it back, restore the previously active
        /// target. The one deliberate departure is the RGB24 result format instead of vanilla's default RGBA32 —
        /// a screenshot has no alpha worth keeping and this halves what the cache holds.
        /// </summary>
        /// <param name="full">The decoded source image, at whatever resolution the file happened to be.</param>
        /// <returns>A texture whose longest edge is at most <see cref="ThumbMaxEdge"/>, with aspect preserved.</returns>
        private static Texture2D ShrinkToThumbnail(Texture2D full)
        {
            int longest = Mathf.Max(full.width, full.height);
            // Steam's pre-made thumbnails already arrive under the cap; handing them straight back keeps their
            // path exactly as it was before RimWorld's folder joined the picture — no GPU round-trip, no resample.
            if (longest <= ThumbMaxEdge)
                return full;

            float scale = ThumbMaxEdge / (float)longest;
            int w = Mathf.Max(1, Mathf.RoundToInt(full.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(full.height * scale));

            var prevActive = RenderTexture.active;
            RenderTexture rt = null;
            Texture2D small = null;
            try
            {
                full.filterMode = FilterMode.Trilinear; // let the blit pick a mip level instead of one texel
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
                Graphics.Blit(full, rt);
                RenderTexture.active = rt;

                small = new Texture2D(w, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                small.Apply();

                var result = small;
                small = null; // ownership handed to the caller; the finally must not free it
                return result;
            }
            finally
            {
                // The multi-megabyte source dies here whatever happened — including when the blit or the readback
                // threw, which is exactly why this is a finally and not a trailing Destroy on the success path.
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(full);
                if (small != null) UnityEngine.Object.Destroy(small); // only reachable when we threw mid-way
            }
        }

        // Re-read both screenshot sources and start the previews over. Cached textures are dropped rather than
        // kept, so Refresh also retries a file that failed to decode last time — the case that matters right
        // after the player copies an image in.
        private void Rescan()
        {
            ReleaseThumbs();
            entries.Clear();
            entries.AddRange(ReportScreenshots.FindRecent());
            screenshotFolder = ReportScreenshots.EnsureFolder();
            scroll = Vector2.zero; // newest first, so anything just added is at the top
        }

        // Free every decoded preview. Null entries are the remembered failures, and have nothing to free.
        private void ReleaseThumbs()
        {
            foreach (var tex in thumbCache.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            thumbCache.Clear();
        }

        public override void PostClose()
        {
            base.PostClose();
            ReleaseThumbs();
        }
    }
}
