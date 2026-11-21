using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EgyptianSlot
{
    /// <summary>
    /// One spinning column.
    ///
    /// The reel is a strip of symbol indices read through a continuous <see cref="_offset"/>.
    /// Visible row <c>r</c> shows <c>_strip[floor(offset) + r]</c>, and the fractional part
    /// of the offset slides every row downward by that much of a cell. When the fraction
    /// rolls past 1 the whole strip steps on by one index, which cancels the slide exactly,
    /// so it reads as seamless downward scrolling with only a handful of images.
    ///
    /// Landing on a chosen outcome is then arithmetic rather than animation bookkeeping:
    /// pick an integer stop offset and write the wanted symbols into the strip at the
    /// indices that offset will show.
    ///
    /// A full-column symbol occupies three consecutive strip entries, tagged part 0, 1 and
    /// 2. Only part 0 draws, at three cells tall, and the other two stay dark beneath it.
    /// </summary>
    public class ReelView : MonoBehaviour
    {
        /// <summary>Strip length. Longer simply takes longer to visibly repeat while spinning.</summary>
        private const int StripLength = 48;

        /// <summary>
        /// Spare cells above the visible rows. A full-column symbol is drawn by its part-0
        /// cell, which can sit up to two rows above the top visible row, so the pool has to
        /// reach that far up or the character's lower half would vanish.
        /// </summary>
        private const int CellsAbove = 3;
        private const int CellsBelow = 1;

        [Header("Layout")]
        [Tooltip("Clipped viewport. Cells are parented here and anchored to its top edge.")]
        [SerializeField] private RectTransform _content;
        [SerializeField] private float _cellHeight = 155f;

        [Header("Motion")]
        [Tooltip("Cells travelled per second at full speed.")]
        [SerializeField] private float _spinSpeed = 16f;
        [Tooltip("Seconds spent easing from full speed down to the stop.")]
        [SerializeField] private float _stopDuration = 0.5f;
        [Tooltip("How far past the stop the reel overshoots before springing back, in cells.")]
        [SerializeField] private float _bounceCells = 0.18f;
        [SerializeField] private float _bounceDuration = 0.16f;

        private readonly List<Image> _cells = new();

        private SlotConfig _config;
        private SlotEngine _engine;

        private int[] _strip;

        /// <summary>Position within a full-column run: 0, 1 or 2. Always 0 for single cells.</summary>
        private int[] _part;

        private float _offset;
        private State _state = State.Idle;

        private float _timer;
        private float _offsetAtStopStart;
        private float _stopOffset;
        private float _cellsUntilStop;
        private int[] _targetSymbols;

        private enum State { Idle, Spinning, Stopping, Bouncing }

        public bool IsSpinning => _state != State.Idle;

        /// <summary>Raised once this reel has fully settled, bounce included.</summary>
        public event Action Stopped;

        public void Initialize(SlotConfig config, SlotEngine engine)
        {
            _config = config;
            _engine = engine;

            _strip = new int[StripLength];
            _part = new int[StripLength];
            FillStrip();

            for (int i = 0; i < config.RowCount + CellsAbove + CellsBelow; i++)
            {
                var cell = new GameObject($"Cell{i}", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)cell.transform;
                rect.SetParent(_content, false);

                var image = cell.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;

                _cells.Add(image);
            }

            Refresh();
        }

        /// <summary>
        /// Fills the strip with random symbols, writing every full-column symbol as a
        /// complete run of three. Runs are kept away from the wrap so one never appears
        /// cut in half where the strip joins back onto itself.
        /// </summary>
        private void FillStrip()
        {
            int i = 0;
            while (i < StripLength)
            {
                int symbol = _engine.RandomSymbolIndex();
                bool roomForRun = i + 2 < StripLength;

                if (_engine.IsFullColumn(symbol) && roomForRun)
                {
                    for (int part = 0; part < 3; part++)
                    {
                        _strip[i + part] = symbol;
                        _part[i + part] = part;
                    }

                    i += 3;
                    continue;
                }

                if (_engine.IsFullColumn(symbol)) symbol = _engine.RandomCellSymbolIndex();

                _strip[i] = symbol;
                _part[i] = 0;
                i++;
            }
        }

        /// <summary>
        /// Starts spinning. <paramref name="cellsToTravel"/> is the minimum distance before
        /// this reel may stop, which is how the reels are staggered left to right.
        /// </summary>
        public void Spin(float cellsToTravel)
        {
            _state = State.Spinning;
            _cellsUntilStop = Mathf.Max(_config.RowCount + 2, cellsToTravel);
            _targetSymbols = null;
        }

        /// <summary>
        /// Sets what this reel must show once settled, top row first. Safe to call at any
        /// point during the spin; the reel finishes its remaining travel either way.
        /// </summary>
        public void SetStopSymbols(int[] symbolsTopFirst) => _targetSymbols = symbolsTopFirst;

        private void Update()
        {
            switch (_state)
            {
                case State.Idle: return;
                case State.Spinning: UpdateSpinning(); break;
                case State.Stopping: UpdateStopping(); break;
                case State.Bouncing: UpdateBouncing(); break;
            }

            Refresh();
        }

        private void UpdateSpinning()
        {
            float step = _spinSpeed * Time.deltaTime;
            _offset += step;
            _cellsUntilStop -= step;

            // Hold full speed until the outcome is known, so a slow resolve never forces
            // the reel to stop on something it was not told to show.
            if (_cellsUntilStop > 0f || _targetSymbols == null) return;

            BeginStop();
        }

        private void BeginStop()
        {
            _state = State.Stopping;
            _timer = 0f;
            _offsetAtStopStart = _offset;

            // Keep a couple of cells of run-up so the written symbols scroll into view
            // rather than appearing in place.
            _stopOffset = Mathf.Ceil(_offset) + 2f;

            int stopIndex = Mathf.RoundToInt(_stopOffset);

            // A reel the engine filled with one full-column symbol is written as a run of
            // three, so the view draws it as the single tall character it is.
            bool fullColumn = _engine.IsFullColumn(_targetSymbols[0]);

            for (int row = 0; row < _config.RowCount; row++)
            {
                _strip[WrapIndex(stopIndex + row)] = _targetSymbols[row];
                _part[WrapIndex(stopIndex + row)] = fullColumn ? row : 0;
            }
        }

        private void UpdateStopping()
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _stopDuration);

            // Ease out, so the reel decelerates into the stop instead of halting dead.
            float eased = 1f - (1f - t) * (1f - t);
            _offset = Mathf.Lerp(_offsetAtStopStart, _stopOffset, eased);

            if (t < 1f) return;

            _offset = _stopOffset;

            if (_bounceCells <= 0f)
            {
                Settle();
                return;
            }

            _state = State.Bouncing;
            _timer = 0f;
        }

        private void UpdateBouncing()
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _bounceDuration);

            // One half sine: a little past the stop, then back onto it.
            _offset = _stopOffset + Mathf.Sin(t * Mathf.PI) * _bounceCells;

            if (t < 1f) return;

            _offset = _stopOffset;
            Settle();
        }

        private void Settle()
        {
            _state = State.Idle;
            Stopped?.Invoke();
        }

        /// <summary>Points every cell at its current strip symbol and position.</summary>
        private void Refresh()
        {
            int baseIndex = Mathf.FloorToInt(_offset);
            float fraction = _offset - baseIndex;

            for (int i = 0; i < _cells.Count; i++)
            {
                int row = i - CellsAbove;
                int stripIndex = WrapIndex(baseIndex + row);
                int symbolIndex = _strip[stripIndex];
                var symbol = _config.Symbols[symbolIndex];
                var image = _cells[i];
                var rect = (RectTransform)image.transform;

                if (symbol.IsFullColumn)
                {
                    // Only the top of the run draws, three cells tall. The sprite is taller
                    // than one column is wide, so it is sized to fill the height and the
                    // column's mask trims its transparent margins.
                    if (_part[stripIndex] != 0)
                    {
                        image.enabled = false;
                        continue;
                    }

                    float height = _cellHeight * _config.RowCount;
                    float aspect = symbol.Sprite != null && symbol.Sprite.rect.height > 0f
                        ? symbol.Sprite.rect.width / symbol.Sprite.rect.height
                        : 1f;

                    image.enabled = true;
                    image.sprite = symbol.Sprite;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.sizeDelta = new Vector2(height * aspect, height);
                    rect.anchoredPosition = new Vector2(0f, -(row - fraction) * _cellHeight);
                    continue;
                }

                image.enabled = true;
                image.sprite = symbol.Sprite;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
                rect.sizeDelta = new Vector2(0f, _cellHeight);
                rect.anchoredPosition = new Vector2(0f, -(row - fraction) * _cellHeight);
            }
        }

        private static int WrapIndex(int index)
        {
            int wrapped = index % StripLength;
            return wrapped < 0 ? wrapped + StripLength : wrapped;
        }

        /// <summary>
        /// The image showing a given visible row, for win highlighting. Only meaningful
        /// once the reel has settled.
        /// </summary>
        public Image CellAt(int row) => _cells[row + CellsAbove];
    }
}
