using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public static class Portrait
    {
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Sprite white;
        static Material glass;

        public static bool IsGlassFill(Color fill)
        {
            return fill.a < 1f && fill.r > 0.9f && fill.g > 0.85f && fill.b > 0.8f;
        }

        public static void ApplyGlass(Image image)
        {
            if (!IsGlassFill(image.color)) return;
            if (glass == null)
            {
                var shader = Resources.Load<Shader>("Quota/Glass");
                if (shader == null) return;
                glass = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            image.material = glass;
        }


        public static Sprite SlicedRound
        {
            get
            {
                Sprite sprite;
                if (sprites.TryGetValue("sliced7", out sprite)) return sprite;
                const int size = 48;
                const float radius = 7f;
                var texture = NewTexture(size, size);
                for (var py = 0; py < size; py++)
                {
                    for (var px = 0; px < size; px++)
                    {
                        var alpha = Coverage(px + 0.5f, (size - py) - 0.5f, size, size, radius, false);
                        texture.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
                    }
                }
                texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius), false);
                sprites["sliced7"] = sprite;
                return sprite;
            }
        }

        public static Sprite White
        {
            get
            {
                if (white != null) return white;
                var texture = NewTexture(4, 4);
                var pixels = new Color[16];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                texture.SetPixels(pixels);
                texture.Apply();
                white = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100);
                return white;
            }
        }

        public static RectTransform Box(Transform parent, string name, float x, float y, float width, float height, float radius, float border, Color fill, Color stroke, bool rightCornersOnly)
        {
            var host = Rect(parent, name, x, y, width, height);
            // Keep borders above subpixel width after the WebGL canvas is scaled.
            if (border > 0f && Application.platform == RuntimePlatform.WebGLPlayer)
                border = Mathf.Max(border, 1.5f / Mathf.Max(0.001f, Mathf.Min(Mathf.Abs(host.lossyScale.x), Mathf.Abs(host.lossyScale.y))));
            var strokeImage = host.gameObject.AddComponent<Image>();
            var hollow = border > 0f && fill.a < 1f;
            strokeImage.sprite = hollow
                ? RoundedRing(width, height, radius, border, rightCornersOnly)
                : Rounded(width, height, radius, rightCornersOnly);
            strokeImage.type = Image.Type.Simple;
            strokeImage.color = border > 0 ? stroke : fill;
            strokeImage.raycastTarget = false;
            if (border <= 0f) ApplyGlass(strokeImage);
            if (border > 0)
            {
                var inner = radius > border ? radius - border : 0f;
                var fillHost = Rect(host, "fill", border, border, width - border * 2f, height - border * 2f);
                var fillImage = fillHost.gameObject.AddComponent<Image>();
                fillImage.sprite = Rounded(width - border * 2f, height - border * 2f, inner, rightCornersOnly);
                fillImage.color = fill;
                fillImage.raycastTarget = false;
                ApplyGlass(fillImage);
            }
            return host;
        }

        public static RectTransform Solid(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var host = Rect(parent, name, x, y, width, height);
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = White;
            image.color = color;
            image.raycastTarget = false;
            return host;
        }

        public static RectTransform Circle(Transform parent, string name, float x, float y, float diameter, Color color)
        {
            var host = Rect(parent, name, x, y, diameter, diameter);
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = Disk(diameter);
            image.color = color;
            image.raycastTarget = false;
            return host;
        }

        public static RectTransform Gradient(Transform parent, string name, float x, float y, float width, float height, Color from, Color to, float angleDegrees, float opacity)
        {
            var host = Rect(parent, name, x, y, width, height);
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = LinearGradient(Mathf.RoundToInt(width), Mathf.RoundToInt(height), from, to, angleDegrees, opacity);
            image.raycastTarget = false;
            return host;
        }

        public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Sprite Rounded(float width, float height, float radius, bool rightCornersOnly)
        {
            var w = Mathf.Max(1, Mathf.RoundToInt(width));
            var h = Mathf.Max(1, Mathf.RoundToInt(height));
            var key = "r" + w + "x" + h + ":" + radius.ToString("0.##") + (rightCornersOnly ? "R" : "A");
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite)) return sprite;
            var texture = NewTexture(w, h);
            var limit = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f);
            for (var py = 0; py < h; py++)
            {
                for (var px = 0; px < w; px++)
                {
                    var alpha = Coverage(px + 0.5f, (h - py) - 0.5f, w, h, limit, rightCornersOnly);
                    texture.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprites[key] = sprite;
            return sprite;
        }

        static Sprite RoundedRing(float width, float height, float radius, float border, bool rightCornersOnly)
        {
            var w = Mathf.Max(1, Mathf.RoundToInt(width));
            var h = Mathf.Max(1, Mathf.RoundToInt(height));
            var key = "ring" + w + "x" + h + ":" + radius.ToString("0.##") + ":" + border.ToString("0.##") + (rightCornersOnly ? "R" : "A");
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite)) return sprite;
            var texture = NewTexture(w, h);
            var limit = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f);
            var innerW = w - border * 2f;
            var innerH = h - border * 2f;
            var innerRadius = Mathf.Max(0f, radius - border);
            for (var py = 0; py < h; py++)
            {
                for (var px = 0; px < w; px++)
                {
                    var y = (h - py) - 0.5f;
                    var outer = Coverage(px + 0.5f, y, w, h, limit, rightCornersOnly);
                    var inner = innerW > 1f && innerH > 1f
                        ? Coverage(px + 0.5f - border, y - border, innerW, innerH, innerRadius, rightCornersOnly)
                        : 0f;
                    texture.SetPixel(px, py, new Color(1f, 1f, 1f, Mathf.Clamp01(outer - inner)));
                }
            }
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprites[key] = sprite;
            return sprite;
        }

        static Sprite Disk(float diameter)
        {
            var size = Mathf.Max(1, Mathf.RoundToInt(diameter));
            var key = "c" + size;
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite)) return sprite;
            var texture = NewTexture(size, size);
            var radius = size * 0.5f;
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var dx = px + 0.5f - radius;
                    var dy = py + 0.5f - radius;
                    var alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    texture.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprites[key] = sprite;
            return sprite;
        }

        static Sprite LinearGradient(int width, int height, Color from, Color to, float angleDegrees, float opacity)
        {
            var w = Mathf.Max(1, width);
            var h = Mathf.Max(1, height);
            var key = "g" + w + "x" + h + ":" + angleDegrees.ToString("0.#") + ":" + ColorUtility.ToHtmlStringRGBA(from) + ":" + ColorUtility.ToHtmlStringRGBA(to) + ":" + opacity.ToString("0.##");
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite)) return sprite;
            var radians = angleDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
            var corners = new[] { Vector2.zero, new Vector2(w, 0f), new Vector2(0f, h), new Vector2(w, h) };
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var corner in corners)
            {
                var dot = Vector2.Dot(corner, direction);
                if (dot < min) min = dot;
                if (dot > max) max = dot;
            }
            var span = Mathf.Max(0.001f, max - min);
            var texture = NewTexture(w, h);
            for (var py = 0; py < h; py++)
            {
                for (var px = 0; px < w; px++)
                {
                    var yDown = h - (py + 0.5f);
                    var t = (Vector2.Dot(new Vector2(px + 0.5f, yDown), direction) - min) / span;
                    var color = Color.Lerp(from, to, Mathf.Clamp01(t));
                    color.a = opacity;
                    texture.SetPixel(px, py, color);
                }
            }
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprites[key] = sprite;
            return sprite;
        }

        static float Coverage(float x, float y, float width, float height, float radius, bool rightCornersOnly)
        {
            var px = x - width * 0.5f;
            var py = height * 0.5f - y;
            var corner = radius;
            if (rightCornersOnly && px < 0f) corner = 0f;
            var qx = Mathf.Abs(px) - width * 0.5f + corner;
            var qy = Mathf.Abs(py) - height * 0.5f + corner;
            var outside = Mathf.Min(Mathf.Max(qx, qy), 0f) + new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude - corner;
            return Mathf.Clamp01(0.75f - outside);
        }

        static Texture2D NewTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
