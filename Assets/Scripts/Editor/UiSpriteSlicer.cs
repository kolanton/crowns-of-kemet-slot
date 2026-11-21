using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Trims the UI layers exported from the pack's Photoshop source down to their artwork.
    ///
    /// Each layer is exported on the document's full 2796x2796 canvas, so the artwork is a
    /// small island in a sea of transparency. Defining a sprite rect over that island is
    /// not enough: Unity would still import the whole 2796x2796 sheet, costing several
    /// megabytes of texture memory per button for a few hundred pixels of art. So the PNG
    /// is rewritten cropped to its alpha bounds, which is both smaller and simpler.
    ///
    /// Running it twice is harmless — an already-trimmed file is left alone.
    /// </summary>
    public static class UiSpriteSlicer
    {
        private const string UiFolder = "Assets/RetroStyle Games/Crowns of Kemet (FREE)/Sprites/ui";

        /// <summary>Alpha at or above this counts as content when finding the bounds.</summary>
        private const byte AlphaThreshold = 8;

        /// <summary>
        /// Nine-slice border as a fraction of the sprite's shorter side, per file. A plate
        /// that stretches to fit its text needs its ornamented ends held fixed; a button
        /// drawn at one fixed size does not, and gets none.
        /// </summary>
        private static readonly Dictionary<string, float> BorderFractions = new()
        {
            { "ui_readout", 0.32f },
            { "ui_button", 0.32f },
        };

        [MenuItem("Egyptian Slot/0b. Trim Exported UI Layers")]
        public static void Slice()
        {
            if (!AssetDatabase.IsValidFolder(UiFolder))
            {
                Debug.LogWarning($"[UiSpriteSlicer] {UiFolder} not found; nothing to trim.");
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiFolder }))
                TrimOne(AssetDatabase.GUIDToAssetPath(guid));

            AssetDatabase.Refresh();
        }

        private static void TrimOne(string path)
        {
            if (!File.Exists(path)) return;

            string name = Path.GetFileNameWithoutExtension(path);

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(source);
                Debug.LogError($"[UiSpriteSlicer] Could not decode {path}.");
                return;
            }

            int width = source.width;
            int height = source.height;
            var pixels = source.GetPixels32();

            if (!TryFindContent(pixels, width, height, out var bounds))
            {
                Object.DestroyImmediate(source);
                Debug.LogWarning($"[UiSpriteSlicer] {name} is fully transparent; skipped.");
                return;
            }

            bool alreadyTrimmed = bounds.width == width && bounds.height == height;

            if (!alreadyTrimmed)
            {
                // GetPixels takes a region; GetPixels32 does not, so the copy goes through
                // the float overload.
                var cropped = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false);
                cropped.SetPixels(source.GetPixels(bounds.xMin, bounds.yMin,
                    bounds.width, bounds.height));
                cropped.Apply();

                File.WriteAllBytes(path, cropped.EncodeToPNG());
                Object.DestroyImmediate(cropped);

                Debug.Log($"[UiSpriteSlicer] {name}: trimmed {width}x{height} -> " +
                          $"{bounds.width}x{bounds.height}");
            }
            else
            {
                Debug.Log($"[UiSpriteSlicer] {name}: already trimmed at {width}x{height}");
            }

            Object.DestroyImmediate(source);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(path, name, bounds.width, bounds.height);
        }

        private static bool TryFindContent(Color32[] pixels, int width, int height,
            out RectInt bounds)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x].a < AlphaThreshold) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
            {
                bounds = default;
                return false;
            }

            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        private static void ConfigureImporter(string path, string name, int width, int height)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"[UiSpriteSlicer] {path} did not import as a texture.");
                return;
            }

            var border = Vector4.zero;
            if (BorderFractions.TryGetValue(name, out float fraction))
            {
                float inset = Mathf.Floor(Mathf.Min(width, height) * fraction);
                border = new Vector4(inset, inset, inset, inset);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;

            // These are small, soft-edged UI shapes shown at roughly their native size.
            // Block compression would band the gradients for a saving measured in kilobytes.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;

            importer.SaveAndReimport();
        }
    }
}
