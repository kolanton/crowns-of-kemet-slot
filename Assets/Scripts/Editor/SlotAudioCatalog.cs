using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Sorts the imported sound pack's clips into the events the machine raises, and sets
    /// import settings suited to a web build.
    ///
    /// The pack is a general UI library rather than a slot one, so the mapping is a
    /// judgement about which recordings suit which moment: coins for a payout, crisp
    /// high-pitched ticks for reels landing, a physical button press for the spin.
    /// </summary>
    public static class SlotAudioCatalog
    {
        private const string PackRoot = "Assets/UI SFX Free Pack";

        /// <summary>
        /// Event name and the pack folders whose clips suit it, matched by path substring.
        /// A clip may serve more than one event; these are not exclusive.
        /// </summary>
        private static readonly (string Role, string[] Folders)[] Rules =
        {
            // Recorded physical buttons: the most tactile thing in the pack, so the spin
            // itself gets them.
            ("spin", new[] { "/buttons/" }),

            // Short and bright, which is what a reel landing needs — five fire in a row
            // and anything with a tail would smear into the next.
            ("reelStop", new[] { "/high_pitched/" }),

            // Metal coins. The pack intends them for purchases; for a slot they are the
            // payout.
            ("win", new[] { "/purchase/" }),

            // An affirmative flourish for the moment the banner rises.
            ("bigWin", new[] { "/ok/" }),

            // Quieter interface clicks for the stake and wallet controls.
            ("click", new[] { "/cancel/", "/sliders/" }),
        };

        /// <summary>Clips found, keyed by event. Events with no clips are absent.</summary>
        public static Dictionary<string, List<AudioClip>> Build()
        {
            var catalog = new Dictionary<string, List<AudioClip>>();

            if (!AssetDatabase.IsValidFolder(PackRoot)) return catalog;

            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { PackRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Select(path => (path, clip: AssetDatabase.LoadAssetAtPath<AudioClip>(path)))
                .Where(entry => entry.clip != null);

            foreach (var (path, clip) in clips)
            {
                string haystack = path.ToLowerInvariant().Replace('\\', '/');

                foreach (var rule in Rules)
                {
                    if (!rule.Folders.Any(haystack.Contains)) continue;

                    if (!catalog.TryGetValue(rule.Role, out var list))
                        catalog[rule.Role] = list = new List<AudioClip>();

                    list.Add(clip);
                }
            }

            foreach (var list in catalog.Values)
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            return catalog;
        }

        public static AudioClip[] For(Dictionary<string, List<AudioClip>> catalog, string role) =>
            catalog.TryGetValue(role, out var list) ? list.ToArray() : System.Array.Empty<AudioClip>();

        /// <summary>
        /// Import settings for the web: mono and Vorbis. These are short interface sounds
        /// where stereo buys nothing a listener would notice, and the saving comes off
        /// every download.
        /// </summary>
        [MenuItem("Egyptian Slot/0c. Apply Web Audio Settings")]
        public static void ApplyWebSettings()
        {
            if (!AssetDatabase.IsValidFolder(PackRoot))
            {
                Debug.LogWarning($"[SlotAudioCatalog] {PackRoot} not found; nothing to set.");
                return;
            }

            int changed = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { PackRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.4f;

                // Preloading moved onto the per-platform sample settings in Unity 6; the
                // importer-level property is obsolete.
                settings.preloadAudioData = true;

                importer.defaultSampleSettings = settings;
                importer.forceToMono = true;
                importer.loadInBackground = false;

                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[SlotAudioCatalog] Applied web audio settings to {changed} clips.");
        }

        [MenuItem("Egyptian Slot/1b. Report Imported Audio")]
        public static void Report()
        {
            var catalog = Build();

            if (catalog.Count == 0)
            {
                Debug.LogWarning("[SlotAudioCatalog] No clips found. Is the sound pack imported?");
                return;
            }

            var report = new StringBuilder("[SlotAudioCatalog] Clips by event:\n");
            foreach (var pair in catalog.OrderBy(pair => pair.Key))
            {
                report.AppendLine($"  {pair.Key} ({pair.Value.Count}):");
                foreach (var clip in pair.Value)
                    report.AppendLine($"    {clip.name}  {clip.length:0.00}s");
            }

            Debug.Log(report.ToString());
        }
    }
}
