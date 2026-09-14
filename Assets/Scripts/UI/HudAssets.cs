using System.Collections.Generic;
using UnityEngine;

namespace Riverworks
{
    /// <summary>
    /// Loads the small, tintable Kenney UI set kept under Resources/UI/Kenney.
    /// Sprite instances are cached for the lifetime of the application.
    /// </summary>
    public static class HudAssets
    {
        public const string MenuIcon = "menu";
        public const string CloseIcon = "close";
        public const string ArrowLeftIcon = "arrow_left";
        public const string ArrowRightIcon = "arrow_right";
        public const string RotateIcon = "rotate";
        public const string PlayIcon = "play";
        public const string PauseIcon = "pause";
        public const string SettingsIcon = "settings";
        public const string InfoIcon = "info";
        public const string HomeIcon = "home";
        public const string MapIcon = "map";
        public const string ConfirmIcon = "confirm";

        const string Root = "UI/Kenney/";
        const float PixelsPerUnit = 100f;
        const float SurfaceBorder = 6f;

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Panel => Get(Root + "panel", SurfaceBorder);
        public static Sprite Button => Get(Root + "button", SurfaceBorder);
        public static Sprite IconButton => Get(Root + "icon_button", SurfaceBorder);
        public static Sprite Divider => Get(Root + "divider", 0f);

        public static Sprite Icon(string semanticName)
        {
            if (string.IsNullOrEmpty(semanticName)) return null;
            return Get(Root + "Icons/" + semanticName, 0f);
        }

        static Sprite Get(string resourcePath, float border)
        {
            string cacheKey = resourcePath + ":" + border;
            if (Cache.TryGetValue(cacheKey, out Sprite sprite)) return sprite;

            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;

            Vector4 spriteBorder = border > 0f
                ? new Vector4(border, border, border, border)
                : Vector4.zero;
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                0u,
                SpriteMeshType.FullRect,
                spriteBorder);
            sprite.name = "HudAssets/" + resourcePath;
            Cache.Add(cacheKey, sprite);
            return sprite;
        }
    }
}
