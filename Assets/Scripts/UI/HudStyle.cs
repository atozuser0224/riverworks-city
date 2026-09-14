using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Shared color, type and integer pixel scaling for the code-built uGUI.</summary>
    public static class HudStyle
    {
        public static readonly Color Surface = Hex("101820");
        public static readonly Color SurfaceRaised = Hex("253442");
        public static readonly Color Text = Hex("DCE6EB");
        public static readonly Color TextMuted = Hex("8FA1AC");
        public static readonly Color Accent = Hex("C89B55");
        public static readonly Color Positive = Hex("4A9F98");
        public static readonly Color Danger = Hex("E29484");
        public static readonly Color Dim = new Color(Surface.r, Surface.g, Surface.b, .78f);
        public const int BodySize = 11;
        public const int TitleSize = 22;
        public const int EmphasisSize = 33;
        public const int TouchSize = 44;

        public static Vector2 PixelDimensions(Canvas canvas)
        {
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RenderTexture target = camera != null ? camera.targetTexture : null;
            return target != null ? new Vector2(target.width, target.height) : new Vector2(Screen.width, Screen.height);
        }

        public static int IntegerScale(int width, int height, float dpi, bool mobile)
        {
            if (mobile)
            {
                float density = float.IsNaN(dpi) || float.IsInfinity(dpi) || dpi <= 0 ? 160f : dpi;
                return Mathf.Max(1, Mathf.RoundToInt(density / 160f));
            }
            return Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(width / 1280f, height / 720f)));
        }

        public static Color Foreground(Color background)
        {
            return Contrast(Text, background) >= Contrast(Surface, background) ? Text : Surface;
        }

        public static float Contrast(Color first, Color second)
        {
            float a = Luminance(first), b = Luminance(second);
            return (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);
        }

        public static void ApplyType(UnityEngine.UI.Text label, bool title = false)
        {
            if (label == null) return;
            label.fontSize = title ? TitleSize : BodySize;
            label.fontStyle = FontStyle.Normal;
        }

        static float Luminance(Color color)
        {
            Color linear = color.linear;
            return linear.r * .2126f + linear.g * .7152f + linear.b * .0722f;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
