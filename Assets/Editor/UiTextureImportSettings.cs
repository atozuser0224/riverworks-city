using System;
using UnityEditor;
using UnityEngine;

namespace Riverworks.Editor
{
    /// <summary>Enforces crisp, compact import settings for the bundled Kenney HUD textures.</summary>
    public sealed class UiTextureImportSettings : AssetPostprocessor
    {
        const string KenneyRoot = "Assets/Resources/UI/Kenney/";

        void OnPreprocessTexture()
        {
            if (!IsKenneyPng(assetPath)) return;
            Apply((TextureImporter)assetImporter);
        }

        /// <summary>
        /// Applies the same settings to Kenney PNGs that were imported before this postprocessor existed.
        /// Only textures whose importer values differ are reimported.
        /// </summary>
        public static void ApplyAll()
        {
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < assetPaths.Length; i++)
            {
                string path = assetPaths[i];
                if (!IsKenneyPng(path)) continue;

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && Apply(importer)) importer.SaveAndReimport();
            }
        }

        static bool IsKenneyPng(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(KenneyRoot, StringComparison.Ordinal)
                && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        static bool Apply(TextureImporter importer)
        {
            bool changed = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }
            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }
            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                changed = true;
            }
            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }
            if (importer.maxTextureSize != 256)
            {
                importer.maxTextureSize = 256;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }
            return changed;
        }
    }
}
