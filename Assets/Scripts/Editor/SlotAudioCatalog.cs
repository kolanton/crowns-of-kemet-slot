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
        /// <summary>
        /// Folders holding effect clips. Both packs are searched; which clip serves which
        /// moment is decided by <see cref="Rules"/>, not by which pack it came from.
        /// </summary>
        private static readonly string[] EffectRoots =
        {
            "Assets/Casual Game Sounds U6",
            "Assets/UI SFX Free Pack",
        };

        private const string PackRoot = "Assets/UI SFX Free Pack";

        /// <summary>
        /// Where the music pack lands. Searched separately from the effects pack because
        /// only one track is wanted and it is chosen by name, not by folder.
        /// </summary>
        private const string MusicRoot = "Assets";

        /// <summary>
        /// The looped exports live in their own folder and share filenames with the
        /// one-shot versions, so the folder is what distinguishes them. A bed has to run
        /// for as long as anyone plays, so the looped export is the one wanted.
        /// </summary>
        private const string MusicLoopFolder = "/loops/";

        /// <summary>
        /// Event name and the pack folders whose clips suit it, matched by path substring.
        /// A clip may serve more than one event; these are not exclusive.
        /// </summary>
        /// <summary>
        /// Clips are chosen by measurement, not by name: the casual pack's filenames
        /// (DM-CGS-nn) say nothing, so each was measured for length, attack and a
        /// brightness proxy, and picked to suit the moment it plays. The numbers in the
        /// comments are that brightness figure in Hz — lower is darker.
        ///
        /// The whole reason for choosing this way: the first attempt used the UI pack's
        /// "high pitched" folder for reel stops, at 1000-10000Hz, and it sounded like a
        /// menu rather than a machine.
        /// </summary>
        private static readonly (string Role, string[] Clips)[] Rules =
        {
            // 406Hz, 0.22s. One clip for all five reels, so the view varies its pitch
            // slightly per landing rather than firing the identical sample five times.
            ("reelStop", new[] { "dm-cgs-03" }),

            // 467Hz, 0.50s: the same tonal family as the stop but longer-bodied, so
            // pressing spin reads as its own event.
            ("spin", new[] { "dm-cgs-02" }),

            // The only purpose-made loop across both packs. Brighter than the drones that
            // were the alternative, but it wraps without a seam, which they do not.
            ("spinLoop", new[] { "slide_loop" }),

            // 572Hz, 1.9s: long enough to carry a celebration and still dark.
            ("bigWin", new[] { "dm-cgs-12" }),

            // 992Hz: light enough not to be mistaken for a reel landing.
            ("click", new[] { "dm-cgs-27" }),

            // 837Hz. The UI pack's coins were dropped: every one of them measures
            // 2770-11993Hz, and the brightest is the shrillest clip in either pack.
            ("win", new[] { "dm-cgs-42" }),
        };

        /// <summary>Clips found, keyed by event. Events with no clips are absent.</summary>
        public static Dictionary<string, List<AudioClip>> Build()
        {
            var catalog = new Dictionary<string, List<AudioClip>>();

            var roots = EffectRoots.Where(AssetDatabase.IsValidFolder).ToArray();
            if (roots.Length == 0) return catalog;

            var clips = AssetDatabase.FindAssets("t:AudioClip", roots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Select(path => (path, clip: AssetDatabase.LoadAssetAtPath<AudioClip>(path)))
                .Where(entry => entry.clip != null)
                .ToList();

            foreach (var rule in Rules)
            {
                // Ordered by the rule, not by the asset database, so the first clip named
                // for a role is the one a single-clip role gets.
                foreach (string token in rule.Clips)
                {
                    var match = clips.FirstOrDefault(entry =>
                        entry.clip.name.ToLowerInvariant().Contains(token) ||
                        entry.path.ToLowerInvariant().Contains(token));

                    if (match.clip == null)
                    {
                        Debug.LogWarning($"[SlotAudioCatalog] No clip matching \"{token}\" for " +
                                         $"{rule.Role}; that slot will be quieter than intended.");
                        continue;
                    }

                    if (!catalog.TryGetValue(rule.Role, out var list))
                        catalog[rule.Role] = list = new List<AudioClip>();

                    list.Add(match.clip);
                }
            }

            // Deliberately not sorted: the rules list each clip in the order it was
            // chosen, and a single-clip role must get the first one named.
            return catalog;
        }

        /// <summary>
        /// The background track: the longest looped clip from the desert music pack. Length
        /// is the tiebreak because a music bed is minutes long where every effect is under
        /// a second, so it separates the two without relying on folder names.
        /// </summary>
        public static AudioClip FindMusic()
        {
            var candidates = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !EffectRoots.Any(path.StartsWith))
                .Select(path => (path: path.ToLowerInvariant().Replace('\\', '/'),
                                 clip: AssetDatabase.LoadAssetAtPath<AudioClip>(path)))
                .Where(entry => entry.clip != null && entry.clip.length > 20f)
                .ToList();

            if (candidates.Count == 0) return null;

            var looped = candidates.Where(entry => entry.path.Contains(MusicLoopFolder)).ToList();
            var pool = looped.Count > 0 ? looped : candidates;

            // Ordered by name so the choice is the same on every rebuild rather than
            // whatever the asset database happened to return first.
            return pool.OrderBy(entry => entry.clip.name, System.StringComparer.Ordinal)
                .First().clip;
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
            var roots = EffectRoots.Where(AssetDatabase.IsValidFolder).ToArray();
            if (roots.Length == 0)
            {
                Debug.LogWarning("[SlotAudioCatalog] No effect packs found; nothing to set.");
                return;
            }

            int changed = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", roots))
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

            ApplyMusicSettings();
        }

        /// <summary>
        /// Settings for the one background track, which differ from the effects'. A
        /// three-minute bed decompressed on load would sit in memory as tens of megabytes
        /// of raw audio — the sort of cost that already broke this demo on a phone once —
        /// so it stays compressed and is decoded as it plays. It also keeps its stereo
        /// image, which matters for music and not for a button click.
        /// </summary>
        private static void ApplyMusicSettings()
        {
            var music = FindMusic();
            if (music == null) return;

            string path = AssetDatabase.GetAssetPath(music);
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) return;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.35f;
            settings.preloadAudioData = true;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = false;

            importer.SaveAndReimport();

            Debug.Log($"[SlotAudioCatalog] Music set to compressed-in-memory Vorbis: {path}");
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
