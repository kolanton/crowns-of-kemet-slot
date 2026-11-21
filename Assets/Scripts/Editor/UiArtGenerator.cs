using System.IO;
using UnityEditor;
using UnityEngine;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Draws the two UI shapes the free art pack does not ship.
    ///
    /// The pack's HUD exists only as paint inside its merged composition — the spin button
    /// and the readout plates are not separate sprites, and the number glyphs are in the
    /// paid edition. Rather than lay flat rectangles over Egyptian art, these two shapes
    /// are generated once into the project and tinted from the pack's own palette.
    /// </summary>
    public static class UiArtGenerator
    {
        private const string Folder = "Assets/Generated";
        public const string DiscPath = Folder + "/ui_spin_disc.png";
        public const string PlatePath = Folder + "/ui_plate.png";

        private const int DiscSize = 256;
        private const int PlateSize = 64;
        private const int PlateCorner = 18;

        /// <summary>Border for the plate's nine-slice, in pixels on each edge.</summary>
        private const int PlateBorder = 20;

        [MenuItem("Egyptian Slot/0. Generate UI Shapes")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);

            WritePng(DiscPath, DrawDisc());
            WritePng(PlatePath, DrawPlate());

            AssetDatabase.Refresh();

            ConfigureSprite(DiscPath, Vector4.zero);
            ConfigureSprite(PlatePath, new Vector4(PlateBorder, PlateBorder, PlateBorder, PlateBorder));

            Debug.Log($"[UiArtGenerator] Wrote {DiscPath} and {PlatePath}.");
        }

        /// <summary>A gilded disc: lit from above, with a darker rim to give it an edge.</summary>
        private static Color[] DrawDisc()
        {
            var pixels = new Color[DiscSize * DiscSize];

            var face = new Color(0.93f, 0.76f, 0.35f);
            var shade = new Color(0.70f, 0.49f, 0.16f);
            var rim = new Color(0.27f, 0.18f, 0.08f);

            float centre = (DiscSize - 1) * 0.5f;
            float radius = centre - 1f;
            float rimWidth = DiscSize * 0.055f;

            for (int y = 0; y < DiscSize; y++)
            {
                for (int x = 0; x < DiscSize; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // One pixel of feathering at the outer edge, so the disc is not jagged.
                    float alpha = Mathf.Clamp01(radius - distance);
                    if (alpha <= 0f) { pixels[y * DiscSize + x] = Color.clear; continue; }

                    // Vertical ramp: lighter at the top, as a struck metal boss would read.
                    float lift = Mathf.InverseLerp(-radius, radius, dy);
                    var colour = Color.Lerp(shade, face, lift);

                    float intoRim = Mathf.Clamp01((distance - (radius - rimWidth)) / rimWidth);
                    colour = Color.Lerp(colour, rim, intoRim * intoRim);

                    colour.a = alpha;
                    pixels[y * DiscSize + x] = colour;
                }
            }

            return pixels;
        }

        /// <summary>A rounded plate: dark fill, thin gilt edge, built for nine-slicing.</summary>
        private static Color[] DrawPlate()
        {
            var pixels = new Color[PlateSize * PlateSize];

            var fill = new Color(0.08f, 0.05f, 0.11f, 0.88f);
            var edge = new Color(0.68f, 0.52f, 0.24f, 0.95f);
            const float edgeWidth = 1.6f;

            for (int y = 0; y < PlateSize; y++)
            {
                for (int x = 0; x < PlateSize; x++)
                {
                    float distance = RoundedRectDistance(x, y);

                    float inside = Mathf.Clamp01(-distance);
                    if (inside <= 0f) { pixels[y * PlateSize + x] = Color.clear; continue; }

                    // Within edgeWidth of the outline, blend toward the gilt edge.
                    float edginess = Mathf.Clamp01(1f + distance / edgeWidth);
                    var colour = Color.Lerp(fill, edge, edginess);
                    colour.a = Mathf.Max(fill.a, edge.a * edginess) * inside;

                    pixels[y * PlateSize + x] = colour;
                }
            }

            return pixels;
        }

        /// <summary>
        /// Signed distance to a rounded rectangle covering the texture: negative inside,
        /// positive outside, which makes both the fill and the edge one calculation.
        /// </summary>
        private static float RoundedRectDistance(int x, int y)
        {
            float half = (PlateSize - 1) * 0.5f;
            float dx = Mathf.Abs(x - half) - (half - PlateCorner);
            float dy = Mathf.Abs(y - half) - (half - PlateCorner);

            float outsideX = Mathf.Max(dx, 0f);
            float outsideY = Mathf.Max(dy, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);

            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - PlateCorner;
        }

        private static void WritePng(string path, Color[] pixels)
        {
            int size = Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void ConfigureSprite(string path, Vector4 border)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"[UiArtGenerator] {path} did not import as a texture.");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;

            // Small UI shapes with soft edges: block compression would band them, and at
            // this size the saving is irrelevant.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            importer.SaveAndReimport();
        }
    }
}
