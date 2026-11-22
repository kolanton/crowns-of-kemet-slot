using UnityEngine;

namespace EgyptianSlot
{
    /// <summary>
    /// The machine's sound. One place that owns every clip and the single AudioSource they
    /// play through, so the rest of the code asks for an event rather than juggling clips.
    ///
    /// Each event holds several interchangeable clips where the pack offers them: a reel
    /// stop fires five times in quick succession, and the same sample five times reads as a
    /// glitch rather than a machine.
    /// </summary>
    public class SlotAudio : MonoBehaviour
    {
        [Header("Output")]
        [SerializeField] private AudioSource _source;
        [Range(0f, 1f)][SerializeField] private float _volume = 0.7f;

        [Header("Clips")]
        [Tooltip("Pressing spin.")]
        [SerializeField] private AudioClip[] _spin;

        [Tooltip("A reel coming to rest. Played once per reel as each lands.")]
        [SerializeField] private AudioClip[] _reelStop;

        [Tooltip("A paying line, played as each win is presented.")]
        [SerializeField] private AudioClip[] _win;

        [Tooltip("A win large enough to raise the banner.")]
        [SerializeField] private AudioClip[] _bigWin;

        [Tooltip("Stake and wallet controls.")]
        [SerializeField] private AudioClip[] _click;

        [Header("Reel bed")]
        [Tooltip("Looped while the reels turn. Its own source, so stopping it does not " +
                 "cut off a reel-stop landing on top of it.")]
        [SerializeField] private AudioSource _spinLoopSource;
        [SerializeField] private AudioClip _spinLoop;
        [Range(0f, 1f)][SerializeField] private float _spinLoopVolume = 0.45f;

        [Header("Music")]
        [Tooltip("Its own source, so a looping bed is not disturbed by one-shot effects.")]
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private AudioClip _music;
        [Range(0f, 1f)][SerializeField] private float _musicVolume = 0.3f;

        /// <summary>
        /// Whether sound is allowed to play. Off until the viewer asks for it: a web page
        /// that starts making noise on its own is a nuisance, and browsers block it anyway
        /// until the page has been interacted with.
        /// </summary>
        public bool Muted { get; private set; } = true;

        [Tooltip("Pitch spread applied to each reel stop, as a fraction either side of 1.")]
        [Range(0f, 0.2f)][SerializeField] private float _reelStopPitchSpread = 0.06f;

        [Tooltip("Reel stops play here so their pitch can vary without affecting other sounds.")]
        [SerializeField] private AudioSource _reelStopSource;

        private int _lastReelStopIndex = -1;

        private void Awake()
        {
            if (_source != null) return;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            UpdateMusic();
        }

        /// <summary>
        /// Called from the hosting page through SendMessage, which can carry an int but
        /// not a bool. 1 unmutes, 0 mutes.
        /// </summary>
        public void SetSoundEnabled(int enabled) => SetMuted(enabled == 0);

        /// <summary>
        /// Starts the music bed when sound is switched on and stops it when it is switched
        /// off. It deliberately does not start by itself: browsers refuse audio until the
        /// page has been interacted with, so the toggle is the only honest cue to begin.
        /// </summary>
        private void UpdateMusic()
        {
            if (_musicSource == null || _music == null) return;

            if (Muted)
            {
                _musicSource.Stop();
                return;
            }

            if (_musicSource.isPlaying) return;

            _musicSource.clip = _music;
            _musicSource.loop = true;
            _musicSource.volume = _musicVolume;
            _musicSource.Play();
        }

        public void PlaySpin() => PlayOne(_spin);

        /// <summary>Starts the reel bed. Silent if muted or if no loop clip was found.</summary>
        public void StartSpinLoop()
        {
            if (Muted || _spinLoopSource == null || _spinLoop == null) return;
            if (_spinLoopSource.isPlaying) return;

            _spinLoopSource.clip = _spinLoop;
            _spinLoopSource.loop = true;
            _spinLoopSource.volume = _spinLoopVolume;
            _spinLoopSource.Play();
        }

        /// <summary>
        /// Stops the reel bed, fading over a few frames rather than cutting, since a
        /// drone ending abruptly is more noticeable than one that falls away.
        /// </summary>
        public void StopSpinLoop()
        {
            if (_spinLoopSource == null || !_spinLoopSource.isPlaying) return;
            StartCoroutine(FadeOutSpinLoop());
        }

        private System.Collections.IEnumerator FadeOutSpinLoop()
        {
            const float fade = 0.18f;
            float start = _spinLoopSource.volume;

            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                _spinLoopSource.volume = Mathf.Lerp(start, 0f, t / fade);
                yield return null;
            }

            _spinLoopSource.Stop();
            _spinLoopSource.volume = _spinLoopVolume;
        }
        public void PlayWin() => PlayOne(_win);
        public void PlayBigWin() => PlayOne(_bigWin);
        public void PlayClick() => PlayOne(_click);

        /// <summary>
        /// A reel landing. Avoids repeating the clip it used last time, so five reels
        /// stopping in sequence sound like five events rather than one stutter.
        /// </summary>
        public void PlayReelStop()
        {
            if (Muted || _reelStop == null || _reelStop.Length == 0) return;

            int index = Random.Range(0, _reelStop.Length);
            if (_reelStop.Length > 1 && index == _lastReelStopIndex)
                index = (index + 1) % _reelStop.Length;

            _lastReelStopIndex = index;

            // Five reels land about a fifth of a second apart. With a single clip that
            // is the same sample five times, which reads as a stuck loop rather than
            // five events, so each landing is nudged slightly off pitch.
            var source = _reelStopSource != null ? _reelStopSource : _source;
            if (source == null) return;

            source.pitch = 1f + Random.Range(-_reelStopPitchSpread, _reelStopPitchSpread);
            source.PlayOneShot(_reelStop[index], _volume);
        }

        private void PlayOne(AudioClip[] clips)
        {
            if (Muted || clips == null || clips.Length == 0) return;
            Play(clips[Random.Range(0, clips.Length)]);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || _source == null) return;

            // PlayOneShot rather than Play, so overlapping events layer instead of cutting
            // each other off — reels land while a win is still counting up.
            _source.PlayOneShot(clip, _volume);
        }
    }
}
