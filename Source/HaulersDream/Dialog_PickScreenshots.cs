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
    /// A thumbnail grid of the player's recent RimWorld Steam screenshots, with multi-select. Returns the chosen
    /// full-resolution file paths to the caller. Thumbnails (Steam's small pre-made ones) are loaded lazily for
    /// visible cells only and freed when the dialog closes.
    /// </summary>
    public class Dialog_PickScreenshots : Window
    {
        private readonly List<ScreenshotEntry> entries;
        private readonly HashSet<string> selected;
        private readonly int maxSelectable;
        private readonly Action<List<string>> onConfirm;
        private readonly Dictionary<string, Texture2D> thumbCache = new Dictionary<string, Texture2D>();
        private Vector2 scroll;

        public Dialog_PickScreenshots(IEnumerable<string> alreadySelected, int maxSelectable, Action<List<string>> onConfirm)
        {
            entries = SteamScreenshots.FindRecent();
            selected = new HashSet<string>(alreadySelected ?? Enumerable.Empty<string>());
            this.maxSelectable = maxSelectable;
            this.onConfirm = onConfirm;

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
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

            // Buttons.
            var cancelRect = new Rect(inRect.width - 290f, btnRowY, 130f, 32f);
            var addRect = new Rect(inRect.width - 150f, btnRowY, 150f, 32f);
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

        // Lazily decode Steam's small thumbnail for a cell; cache the result (null = failed/missing, so we
        // don't retry every frame). The full-resolution image is only read later, at upload time.
        private Texture2D Thumb(ScreenshotEntry e)
        {
            if (thumbCache.TryGetValue(e.thumbPath, out var cached))
                return cached;

            Texture2D tex = null;
            if (File.Exists(e.thumbPath))
            {
                var data = File.ReadAllBytes(e.thumbPath);
                var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (t.LoadImage(data)) tex = t;
                else UnityEngine.Object.Destroy(t);
            }
            thumbCache[e.thumbPath] = tex;
            return tex;
        }

        public override void PostClose()
        {
            base.PostClose();
            foreach (var tex in thumbCache.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            thumbCache.Clear();
        }
    }
}
