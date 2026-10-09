using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed partial class TableView
    {
        readonly Dictionary<string, Sprite> guideSlideSprites = new Dictionary<string, Sprite>();
        const string NoLineStart = "、。，．・：；？！）】」』％%";
        const string NoLineEnd = "（【「『";

        void DrawGuide(float screenW, float screenH)
        {
            var panelW = Mathf.Min(980f, screenW - 48f);
            var panelH = Mathf.Min(screenH - 80f, screenW > screenH ? 820f : 1400f);
            var panel = Portrait.Box(frame, "setup-dialog", (screenW - panelW) * 0.5f, (screenH - panelH) * 0.5f, panelW, panelH, 7f, 1f, Paper, Ink, false);
            Bold(TextAt(panel, GuideCopy.Title(setupPage), 32f, 16f, panelW - 64f, 56f, 40, Ink, nameFont, TextAnchor.MiddleLeft));
            var viewW = panelW - 64f;
            const float viewTop = 84f;
            var viewH = panelH - viewTop - 100f;
            var viewport = Portrait.Rect(panel, "guide-view", 32f, viewTop, viewW, viewH);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.01f);
            hit.raycastTarget = true;
            var content = Portrait.Rect(viewport, "guide-body", 0f, 0f, viewW, viewH);
            var y = 12f;
            if (setupPage == "play") y = DrawGuideSlides(content, viewW, y);
            foreach (var block in GuideCopy.Blocks(setupPage))
            {
                switch (block.Kind)
                {
                    case GuideCopy.BlockKind.Section: y = DrawGuideSection(content, block.Text, viewW, y); break;
                    case GuideCopy.BlockKind.Bullet: y = DrawGuideBullet(content, block.Text, block.Rows, viewW, y); break;
                    case GuideCopy.BlockKind.Table: y = DrawGuideTable(content, block.Rows, viewW, y); break;
                    default: y = DrawGuideParagraph(content, block.Text, viewW, y); break;
                }
            }
            content.sizeDelta = new Vector2(viewW, Mathf.Max(viewH, y + 20f));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
            Pill(panel, "OK", 32f, panelH - 84f, panelW - 64f, 64f, 28, () =>
            {
                setupPage = null;
                ShowSetup();
            });
        }

        float DrawGuideSlides(RectTransform content, float viewW, float y)
        {
            var width = viewW - 24f;
            var pictureH = width * 642f / 1074f;
            var panelH = pictureH + 52f;
            var panel = GuideFill(content, "guide-slides", 12f, y, width, panelH, Color.white);
            var imageRoot = Portrait.Rect(panel, "slide-image", 10f, 10f, width - 20f, pictureH);
            var image = imageRoot.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            var counter = TextAt(panel, "", 10f, pictureH + 12f, width - 20f, 32f, 22, Ink, nameFont, TextAnchor.MiddleRight);
            var slides = new Sprite[9];
            for (var i = 0; i < slides.Length; i++) slides[i] = GuideSlide("slide" + (i + 1).ToString("00"));
            panel.gameObject.AddComponent<GuideSlideShow>().Initialize(image, counter, slides);
            return y + panelH + 22f;
        }

        Sprite GuideSlide(string name)
        {
            if (guideSlideSprites.TryGetValue(name, out var sprite)) return sprite;
            sprite = Application.platform == RuntimePlatform.WebGLPlayer
                ? LoadBundledSprite("how_to_play_slides/" + name)
                : ReadSprite("how_to_play_slides/" + name + ".jpg");
            guideSlideSprites[name] = sprite;
            return sprite;
        }

        float DrawGuideSection(RectTransform content, string title, float viewW, float y)
        {
            y += 16f;
            var band = GuideFill(content, "section-" + title, 12f, y, viewW - 24f, 64f, Accent);
            Bold(TextAt(band, title, 18f, 4f, viewW - 60f, 56f, 34, Color.white, nameFont, TextAnchor.MiddleLeft));
            return y + 80f;
        }

        float DrawGuideParagraph(RectTransform content, string value, float viewW, float y)
        {
            var width = viewW - 48f;
            var wrapped = WrapGuideText(value, 30, width);
            var h = GuideTextHeight(wrapped, 30) + 18f;
            GuideText(content, wrapped, 24f, y + 4f, width, h, 30, Ink);
            return y + h + 8f;
        }

        float DrawGuideBullet(RectTransform content, string value, List<string[]> rows, float viewW, float y)
        {
            var width = viewW - 100f;
            var wrapped = WrapGuideText(value, 30, width);
            var textH = Mathf.Max(58f, GuideTextHeight(wrapped, 30) + 16f);
            var cardWidth = viewW - 24f;
            var card = GuideFill(content, "guide-bullet", 12f, y, cardWidth, textH, new Color(1f, 1f, 1f, 0.56f));
            TextAt(card, "•", 14f, 6f, 38f, 42f, 36, Accent, nameFont, TextAnchor.UpperLeft);
            GuideText(card, wrapped, 58f, 8f, width, textH - 12f, 30, Ink);
            var h = rows == null ? textH : DrawGuideTable(card, rows, cardWidth - 70f, textH, 58f) + 8f;
            card.sizeDelta = new Vector2(cardWidth, h);
            return y + h + 8f;
        }

        float DrawGuideTable(RectTransform content, List<string[]> rows, float viewW, float y, float x = 12f)
        {
            if (rows == null || rows.Count == 0) return y;
            y += 10f;
            var width = viewW - 24f;
            var leftW = width * 0.39f;
            for (var i = 0; i < rows.Count; i++)
            {
                var left = WrapGuideText(rows[i][0], 27, leftW - 26f);
                var right = WrapGuideText(rows[i][1], 27, width - leftW - 30f);
                var h = Mathf.Max(54f, Mathf.Max(GuideTextHeight(left, 27), GuideTextHeight(right, 27)) + 20f);
                var row = GuideFill(content, i == 0 ? "table-head" : "table-row", x, y, width, h,
                    i == 0 ? Accent : i % 2 == 0 ? new Color(1f, 1f, 1f, 0.74f) : Ecru);
                var color = i == 0 ? Color.white : Ink;
                var first = GuideText(row, left, 12f, 8f, leftW - 24f, h - 12f, 27, color);
                var second = GuideText(row, right, leftW + 12f, 8f, width - leftW - 24f, h - 12f, 27, color);
                if (i == 0) { Bold(first); Bold(second); }
                y += h + 2f;
            }
            return y + 16f;
        }

        static RectTransform GuideFill(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var rect = Portrait.Rect(parent, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        Text GuideText(Transform parent, string value, float x, float y, float width, float height, int size, Color color)
        {
            var label = TextAt(parent, value, x, y, width, height, size, color, nameFont, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        static float GuideTextHeight(string wrapped, int size)
        {
            var lines = 1;
            foreach (var ch in wrapped) if (ch == '\n') lines++;
            return lines * size * 1.42f;
        }

        string WrapGuideText(string value, int size, float maxWidth)
        {
            var output = new StringBuilder();
            var line = new StringBuilder();
            foreach (var ch in value)
            {
                if (ch == '\n') { AppendGuideLine(output, line); continue; }
                if (line.Length > 0 && MeasuredTextWidth(line.ToString() + ch, size) > maxWidth)
                {
                    if (NoLineStart.IndexOf(ch) >= 0 && line.Length > 1)
                    {
                        var previous = line[line.Length - 1];
                        line.Length--;
                        AppendGuideLine(output, line);
                        line.Append(previous);
                    }
                    else if (NoLineEnd.IndexOf(line[line.Length - 1]) >= 0 && line.Length > 1)
                    {
                        var opening = line[line.Length - 1];
                        line.Length--;
                        AppendGuideLine(output, line);
                        line.Append(opening);
                    }
                    else AppendGuideLine(output, line);
                }
                line.Append(ch);
            }
            output.Append(line);
            return output.ToString();
        }

        static void AppendGuideLine(StringBuilder output, StringBuilder line)
        {
            output.Append(line).Append('\n');
            line.Length = 0;
        }
    }
}
