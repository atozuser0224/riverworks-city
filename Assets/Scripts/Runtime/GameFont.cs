using System;
using UnityEngine;

namespace Riverworks
{
    /// <summary>Loads a Korean-capable UI font on every supported platform.</summary>
    public static class GameFont
    {
        public const string BundledPixelResourcePath = "Fonts/Galmuri11";
        public const string BundledFallbackResourcePath = "Fonts/NanumGothic-Regular";

        // Kept as the public default resource path for callers that inspect the bundled font directly.
        public const string BundledResourcePath = BundledPixelResourcePath;

        static Font cachedFont;
        static Font cachedPixelFont;
        static bool textureRebuildHooked;

        public static Font Load()
        {
            if (cachedFont != null) return cachedFont;

            Font pixelFont = LoadBundledPixelFont();
            if (pixelFont != null) return cachedFont = pixelFont;

            if (!Application.isMobilePlatform)
            {
                Font desktopFont = LoadDesktopFont();
                if (desktopFont != null) return cachedFont = desktopFont;
            }

            Font bundledFallback = Resources.Load<Font>(BundledFallbackResourcePath);
            if (bundledFallback != null) return cachedFont = bundledFallback;

            return cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>
        /// Loads only the bundled pixel font. Tests can use this method to detect a missing asset
        /// without the OS, Nanum Gothic, or Unity built-in fallback hiding the problem.
        /// </summary>
        public static Font LoadBundledPixelFont()
        {
            if (cachedPixelFont != null) return cachedPixelFont;

            cachedPixelFont = Resources.Load<Font>(BundledPixelResourcePath);
            if (cachedPixelFont != null) ConfigurePixelFont(cachedPixelFont);
            return cachedPixelFont;
        }

        static void ConfigurePixelFont(Font pixelFont)
        {
            ApplyPointFiltering(pixelFont);

            if (textureRebuildHooked) return;
            Font.textureRebuilt += OnFontTextureRebuilt;
            textureRebuildHooked = true;
        }

        static void OnFontTextureRebuilt(Font rebuiltFont)
        {
            if (rebuiltFont == cachedPixelFont) ApplyPointFiltering(rebuiltFont);
        }

        static void ApplyPointFiltering(Font pixelFont)
        {
            if (pixelFont == null || pixelFont.material == null) return;

            Texture atlas = pixelFont.material.mainTexture;
            if (atlas != null) atlas.filterMode = FilterMode.Point;
        }

        static Font LoadDesktopFont()
        {
            try
            {
                string[] installedFonts = Font.GetOSInstalledFontNames();
                string[] preferredNames = { "Malgun Gothic", "맑은 고딕" };

                foreach (string preferredName in preferredNames)
                {
                    if (Array.Exists(installedFonts, installedName =>
                        string.Equals(installedName, preferredName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Font.CreateDynamicFontFromOSFont(preferredName, 16);
                    }
                }
            }
            catch (Exception)
            {
                // The bundled font below is the deterministic cross-platform fallback.
            }

            return null;
        }
    }
}
