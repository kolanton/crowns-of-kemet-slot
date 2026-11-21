using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Finds the sprites an imported art pack brought in and sorts them into the roles the
    /// demo needs. Matching is by path substring, because art packs name their files
    /// descriptively but never identically, and one changed rule here is cheaper than
    /// rewiring a scene.
    ///
    /// Tuned against RetroStyle Games' "Crowns of Kemet (FREE)", whose layout is read off
    /// its own demo scene: the framed symbol variants sit on the reels, with the cut-out
    /// "no_frame" variants used only as showcase pieces.
    /// </summary>
    public static class SlotArtCatalog
    {
        /// <summary>Folders searched for sprites, in order. The first that exists wins.</summary>
        private static readonly string[] SearchRoots = { "Assets" };

        /// <summary>
        /// One role and how to recognise it. A sprite takes the first role whose
        /// <see cref="Include"/> it matches and whose <see cref="Exclude"/> it does not, so
        /// specific rules belong above general ones. All patterns are lowercase and are
        /// matched against the asset path, which carries the pack's folder names too.
        /// </summary>
        private readonly struct Rule
        {
            public readonly string Role;
            public readonly string[] Include;
            public readonly string[] Exclude;

            public Rule(string role, string[] include, string[] exclude = null)
            {
                Role = role;
                Include = include;
                Exclude = exclude ?? System.Array.Empty<string>();
            }

            public bool Matches(string haystack) =>
                Include.Any(haystack.Contains) && !Exclude.Any(haystack.Contains);
        }

        private static readonly Rule[] Rules =
        {
            // Shapes this project generates for the HUD. Caught first so they can never be
            // mistaken for reel art.
            new("ui", new[] { "assets/generated" }),

            // The framed portrait is the one that fits a reel cell; the full-body art is
            // kept apart so it can be used as scenery rather than landing on the reels.
            new("scatter",    new[] { "anubis_frame" }),
            new("character",  new[] { "anubis" }),

            new("wild",       new[] { "wild" }),

            // Cut-out variants are showcase pieces in the pack's own demo, not reel art.
            new("symbol",     new[] { "symbols/high", "symbols\\high" }, new[] { "no_frame" }),
            new("symbolAlt",  new[] { "no_frame" }),

            new("background", new[] { "background" }),

            // "filled" has the reel interior painted in and belongs behind the grid;
            // "empty" is the same border with a transparent middle.
            new("frame",      new[] { "reel_frame_filled" }),
            new("frameEmpty", new[] { "reel_frame_empty" }),

            new("logo",       new[] { "logo_long" }),
            new("logoAlt",    new[] { "logo" }),

            new("bigwinDecor",new[] { "big_win_decor" }),
            new("bigwin",     new[] { "big_win" }),
        };

        /// <summary>Sprites found, keyed by role. Roles with no matches are absent.</summary>
        public static Dictionary<string, List<Sprite>> Build()
        {
            var catalog = new Dictionary<string, List<Sprite>>();
            var unmatched = new List<Sprite>();

            foreach (var sprite in LoadAllSprites())
            {
                string haystack = $"{AssetDatabase.GetAssetPath(sprite)} {sprite.name}".ToLowerInvariant();

                var rule = Rules.FirstOrDefault(candidate => candidate.Matches(haystack));
                if (rule.Role == null) { unmatched.Add(sprite); continue; }

                if (!catalog.TryGetValue(rule.Role, out var list))
                    catalog[rule.Role] = list = new List<Sprite>();

                list.Add(sprite);
            }

            if (unmatched.Count > 0) catalog["unmatched"] = unmatched;

            foreach (var list in catalog.Values)
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            return catalog;
        }

        private static IEnumerable<Sprite> LoadAllSprites()
        {
            string root = SearchRoots.FirstOrDefault(AssetDatabase.IsValidFolder);
            if (root == null) return Enumerable.Empty<Sprite>();

            return AssetDatabase.FindAssets("t:Sprite", new[] { root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .SelectMany(path => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                .Where(sprite => sprite != null);
        }

        /// <summary>
        /// Dumps what was found to the console. Run this after importing an art pack: it is
        /// how the rules above get checked against the pack's real filenames.
        /// </summary>
        [MenuItem("Egyptian Slot/1. Report Imported Art")]
        public static void Report()
        {
            var catalog = Build();

            if (catalog.Count == 0)
            {
                Debug.LogWarning("[SlotArtCatalog] No sprites found. Is the art pack imported, " +
                                 "and are its textures set to Sprite type?");
                return;
            }

            var report = new StringBuilder("[SlotArtCatalog] Imported art by role:\n");
            foreach (var pair in catalog.OrderBy(pair => pair.Key))
            {
                report.AppendLine($"  {pair.Key} ({pair.Value.Count}):");
                foreach (var sprite in pair.Value)
                    report.AppendLine($"    {sprite.name}  " +
                                      $"[{sprite.rect.width:0}x{sprite.rect.height:0}]");
            }

            Debug.Log(report.ToString());
        }
    }
}
