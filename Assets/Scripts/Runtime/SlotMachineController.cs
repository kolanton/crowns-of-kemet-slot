using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EgyptianSlot
{
    /// <summary>
    /// Drives a spin from end to end: takes the stake, asks the engine for an outcome,
    /// starts the reels, feeds each one its landing symbols, then presents whatever won.
    /// </summary>
    public class SlotMachineController : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private SlotConfig _config;

        [Tooltip("Fixed RNG seed for reproducible spins. Leave at 0 for a random seed.")]
        [SerializeField] private int _seed;

        [Header("Scene references")]
        [Tooltip("One per reel, ordered left to right.")]
        [SerializeField] private ReelView[] _reels;
        [SerializeField] private SlotHud _hud;
        [SerializeField] private WinPresenter _winPresenter;
        [SerializeField] private SlotAudio _audio;

        [Header("Spin feel")]
        [Tooltip("Cells the leftmost reel travels before it may stop.")]
        [SerializeField] private float _baseTravelCells = 20f;
        [Tooltip("Extra travel added per reel, which is what staggers the stops.")]
        [SerializeField] private float _travelPerReel = 7f;
        [Tooltip("Pause after the last reel settles, before wins are shown.")]
        [SerializeField] private float _preWinDelay = 0.15f;

        [Header("Autoplay")]
        [SerializeField] private float _autoplayGap = 0.6f;

        private SlotEngine _engine;
        private int _balance;
        private int _lineBetIndex;
        private int _reelsStillSpinning;
        private bool _isSpinning;
        private bool _autoplay;

        public int Balance => _balance;
        public int LineBet => _config.LineBets[_lineBetIndex];
        public int TotalBet => _config.TotalBet(LineBet);
        public bool IsSpinning => _isSpinning;
        public bool AutoplayEnabled => _autoplay;
        public SlotConfig Config => _config;

        private void Awake()
        {
            _engine = new SlotEngine(_config, _seed == 0 ? null : _seed);
            _balance = _config.StartingBalance;

            foreach (var reel in _reels)
            {
                reel.Initialize(_config, _engine);
                reel.Stopped += OnReelStopped;
            }
        }

        private void OnDestroy()
        {
            foreach (var reel in _reels)
                if (reel != null) reel.Stopped -= OnReelStopped;
        }

        private void Start() => _hud.Bind(this);

        /// <summary>True when a spin could start right now.</summary>
        public bool CanSpin() => !_isSpinning && _balance >= TotalBet;

        /// <summary>Starts a spin, or does nothing if one is already running or the balance is short.</summary>
        public void Spin()
        {
            if (!CanSpin()) return;
            StartCoroutine(SpinRoutine());
        }

        /// <summary>Stake level as the HUD shows it, counting from one.</summary>
        public int Level => _lineBetIndex + 1;

        public int LevelCount => _config.LineBets.Length;

        /// <summary>Moves the stake one level up or down, clamped at both ends.</summary>
        public void StepLineBet(int delta)
        {
            if (_isSpinning) return;
            _lineBetIndex = Mathf.Clamp(_lineBetIndex + delta, 0, _config.LineBets.Length - 1);
            _hud.Refresh();
        }

        /// <summary>Jumps to the highest stake the configuration offers.</summary>
        public void MaxBet()
        {
            if (_isSpinning) return;
            _lineBetIndex = _config.LineBets.Length - 1;
            _hud.Refresh();
        }

        public void ToggleAutoplay()
        {
            _autoplay = !_autoplay;
            _hud.Refresh();

            if (_autoplay && CanSpin()) Spin();
        }

        /// <summary>Tops the wallet back up, so a browser demo never dead-ends at zero.</summary>
        public void ResetBalance()
        {
            if (_isSpinning) return;
            _balance = _config.StartingBalance;
            _hud.Refresh();
        }

        private IEnumerator SpinRoutine()
        {
            _isSpinning = true;
            _winPresenter.Clear();

            if (_audio != null) _audio.PlaySpin();

            _balance -= TotalBet;
            _hud.Refresh();

            // Start every reel first so they are all already at speed, then resolve the
            // outcome. The reels keep spinning until told where to stop, so the order
            // here never races the maths.
            _reelsStillSpinning = _reels.Length;
            for (int reel = 0; reel < _reels.Length; reel++)
                _reels[reel].Spin(_baseTravelCells + _travelPerReel * reel);

            var result = _engine.Spin(LineBet);

            var column = new int[_config.RowCount];
            for (int reel = 0; reel < _reels.Length; reel++)
            {
                for (int row = 0; row < _config.RowCount; row++)
                    column[row] = result.Grid[reel, row];

                _reels[reel].SetStopSymbols((int[])column.Clone());
            }

            while (_reelsStillSpinning > 0) yield return null;

            yield return new WaitForSeconds(_preWinDelay);

            if (result.IsWin)
            {
                _balance += result.TotalWin;
                _hud.Refresh();
                yield return _winPresenter.Present(result, _reels, _config, _engine);
            }
            else
            {
                _winPresenter.ShowNoWin();
            }

            _isSpinning = false;
            _hud.Refresh();

            if (!_autoplay) yield break;

            yield return new WaitForSeconds(_autoplayGap);

            if (CanSpin()) Spin();
            else { _autoplay = false; _hud.Refresh(); }
        }

        private void OnReelStopped()
        {
            _reelsStillSpinning--;
            if (_audio != null) _audio.PlayReelStop();
        }
    }
}
