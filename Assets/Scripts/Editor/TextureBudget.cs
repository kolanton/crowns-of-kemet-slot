using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Caps the art pack's import sizes for a web build.
    ///
    /// The pack exports at print-ish resolutions — a 2796x2796 background, 1024x1024 symbols
    /// — which is right for the source art and wrong for a 1280x720 canvas delivered over
    /// a network. Left alone they dominate the build's data file. The caps below are set
    /// from how large each asset is actually drawn on screen, roughly doubled so the art
    /// still holds up on a high-DPI display.
    /// </summary>
    public static class TextureBudget
    {
        private const string PackRoot = "Assets/RetroStyle Games";

        /// <summary>
        /// Maximum imported size per role, matched by path substring, first match winning.
        /// </summary>
        private static readonly (string Token, int MaxSize)[] Caps =
        {
            // Drawn at ~150px square in a reel cell, and the 1024 canvas is mostly the
            // transparent margin the pack preserves around each symbol.
            ("symbols/", 512),
            ("/wild", 512),

            // Full-screen, but it is a soft painted backdrop with no fine detail.
            ("background", 1024),

            // Ornate, and drawn nearly full-width, so it keeps the most detail.
            ("reel_frame", 1024),

            ("characters/", 512),
            ("logo", 1024),
            ("popups/", 1024),

            // Spine atlas: unused at runtime, but it would still ship in the build.
            ("spine/", 256),
        };

        [MenuItem("Egyptian Slot/3. Apply Web Texture Budget")]
        public static void Apply()
        {
            if (!AssetDatabase.IsValidFolder(PackRoot))
            {
                Debug.LogWarning($"[TextureBudget] {PackRoot} not found; nothing to do.");
                return;
            }

            var changed = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PackRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                int cap = CapFor(path);
                if (cap <= 0) continue;

                bool dirty = false;

                if (importer.maxTextureSize != cap)
                {
                    importer.maxTextureSize = cap;
                    dirty = true;
                }

                if (importer.textureCompression != TextureImporterCompression.Compressed)
                {
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    dirty = true;
                }

                // Crunch trades a slower import for a markedly smaller download, which is
                // the right side of that trade for a demo people load over the network.
                if (!importer.crunchedCompression)
                {
                    importer.crunchedCompression = true;
                    importer.compressionQuality = 50;
                    dirty = true;
                }

                if (!dirty) continue;

                importer.SaveAndReimport();
                changed.Add($"{path} -> {cap}");
            }

            Debug.Log($"[TextureBudget] Re-imported {changed.Count} textures:\n  " +
                      string.Join("\n  ", changed));
        }

        private static int CapFor(string assetPath)
        {
            string lower = assetPath.ToLowerInvariant().Replace('\\', '/');
            foreach (var entry in Caps)
                if (lower.Contains(entry.Token)) return entry.MaxSize;

            return 0;
        }
    }
}
