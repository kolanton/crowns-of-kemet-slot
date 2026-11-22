using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EgyptianSlot
{
    /// <summary>
    /// Shows what a spin won: pulses the cells on each winning line in turn, counts the
    /// total up, and raises a banner once the win is large enough relative to the stake.
    /// </summary>
    public class WinPresenter : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private Text _winLabel;

        [Tooltip("Plate behind the win text. Gold on a sunlit background is unreadable, so " +
                 "the figure carries its own dark ground, shown only when there is a win.")]
        [SerializeField] private GameObject _winBacking;
        [SerializeField] private CanvasGroup _bigWinBanner;
        [SerializeField] private Text _bigWinLabel;

        [Tooltip("The figure under the BIG WIN artwork. The pack's own numerals are in its " +
                 "paid edition, so this is set in the project's display face instead.")]
        [SerializeField] private Text _bigWinAmount;
        [SerializeField] private SlotAudio _audio;

        [Tooltip("Told to stand its prompt down while a win is displayed; they share a strip.")]
        [SerializeField] private SlotHud _hud;

        [Header("Line cycling")]
        [Tooltip("Seconds each winning line is held before the next is shown.")]
        [SerializeField] private float _lineHoldDuration = 0.8f;
        [Tooltip("Dim applied to cells outside the line currently being shown.")]
        [Range(0f, 1f)][SerializeField] private float _dimAlpha = 0.25f;

        [Header("Win tiers")]
        [Tooltip("Stake multiples at which the banner reads Big Win, Mega Win and Epic Win.")]
        [SerializeField] private float[] _tierThresholds = { 10f, 25f, 60f };
        [SerializeField] private string[] _tierNames = { "BIG WIN", "MEGA WIN", "EPIC WIN" };

        private readonly List<Vector2Int> _cells = new();
        private readonly List<Image> _dimmed = new();

        private void Awake() => Clear();

        /// <summary>Resets the labels and undims anything a previous spin left dimmed.</summary>
        public void Clear()
        {
            RestoreDimmed();
            SetWinText(string.Empty);
            if (_bigWinBanner != null) _bigWinBanner.alpha = 0f;

            if (_hud == null) return;
            _hud.SuppressMessage(false);
            _hud.SetWin(0);
        }

        public void ShowNoWin()
        {
            RestoreDimmed();
            SetWinText(string.Empty);
        }

        /// <summary>
        /// Sets the win readout, showing its plate only when there is something to read.
        /// An empty plate sitting under the reels between spins would read as chrome.
        /// </summary>
        private void SetWinText(string text)
        {
            if (_winLabel != null) _winLabel.text = text;
            if (_winBacking != null) _winBacking.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// Walks the winning lines one at a time. Scatters are reported in the label but
        /// not highlighted, since they pay from anywhere on the grid.
        /// </summary>
        public IEnumerator Present(SpinResult result, ReelView[] reels, SlotConfig config, SlotEngine engine)
        {
            if (_hud != null)
            {
                _hud.SuppressMessage(true);
                _hud.SetWin(result.TotalWin);
            }

            // The total lives in the bar's win field. This strip carries only the
            // breakdown, line by line, and clears itself afterwards.
            float multiplier = result.WinMultiplier;
            int tier = -1;
            for (int i = 0; i < _tierThresholds.Length; i++)
                if (multiplier >= _tierThresholds[i]) tier = i;

            if (tier >= 0)
                yield return ShowBanner(_tierNames[Mathf.Min(tier, _tierNames.Length - 1)],
                    result.TotalWin);

            foreach (var scatter in result.ScatterWins)
            {
                if (_winLabel == null) break;
                var symbol = config.Symbols[scatter.SymbolIndex];
                SetWinText($"{scatter.Count}x {symbol.DisplayName}  +{scatter.Payout}");
                yield return new WaitForSeconds(_lineHoldDuration);
            }

            foreach (var win in result.LineWins)
            {
                engine.CellsForLineWin(win, _cells);
                HighlightOnly(_cells, reels, config);

                if (_audio != null) _audio.PlayWin();

                if (_winLabel != null)
                {
                    var symbol = config.Symbols[win.SymbolIndex];
                    string wild = win.UsedWild ? " (wild)" : string.Empty;
                    SetWinText($"{win.MatchCount}x {symbol.DisplayName}{wild}  +{win.Payout}");
                }

                yield return new WaitForSeconds(_lineHoldDuration);
            }

            RestoreDimmed();
            SetWinText(string.Empty);

            // The prompt comes back once the breakdown has finished; the bar keeps the
            // figure until the next spin.
            if (_hud != null) _hud.SuppressMessage(false);
        }

        private IEnumerator ShowBanner(string text, int amount)
        {
            if (_bigWinBanner == null) yield break;

            if (_audio != null) _audio.PlayBigWin();
            if (_bigWinLabel != null) _bigWinLabel.text = text;

            const float fade = 0.3f;
            const float countUp = 0.9f;
            const float hold = 0.9f;

            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                _bigWinBanner.alpha = t / fade;
                if (_bigWinAmount != null) _bigWinAmount.text = "0";
                yield return null;
            }

            _bigWinBanner.alpha = 1f;

            // The figure counts up rather than appearing. A big win is the one moment
            // worth drawing out, and a number that climbs is what makes it feel earned.
            if (_bigWinAmount != null)
            {
                for (float t = 0f; t < countUp; t += Time.deltaTime)
                {
                    float eased = 1f - (1f - t / countUp) * (1f - t / countUp);
                    _bigWinAmount.text = Mathf.RoundToInt(amount * eased).ToString("N0");
                    yield return null;
                }

                _bigWinAmount.text = amount.ToString("N0");
            }

            yield return new WaitForSeconds(hold);

            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                _bigWinBanner.alpha = 1f - t / fade;
                yield return null;
            }

            _bigWinBanner.alpha = 0f;
        }

        /// <summary>
        /// Dims every cell that is not part of the given run, which reads more clearly
        /// than brightening the winners against an already bright background.
        /// </summary>
        private void HighlightOnly(List<Vector2Int> winningCells, ReelView[] reels, SlotConfig config)
        {
            RestoreDimmed();

            for (int reel = 0; reel < reels.Length; reel++)
            {
                for (int row = 0; row < config.RowCount; row++)
                {
                    bool isWinner = false;
                    foreach (var cell in winningCells)
                    {
                        if (cell.x != reel || cell.y != row) continue;
                        isWinner = true;
                        break;
                    }

                    if (isWinner) continue;

                    var image = reels[reel].CellAt(row);
                    var color = image.color;
                    image.color = new Color(color.r, color.g, color.b, _dimAlpha);
                    _dimmed.Add(image);
                }
            }
        }

        private void RestoreDimmed()
        {
            foreach (var image in _dimmed)
            {
                if (image == null) continue;
                var color = image.color;
                image.color = new Color(color.r, color.g, color.b, 1f);
            }

            _dimmed.Clear();
        }
    }
}
