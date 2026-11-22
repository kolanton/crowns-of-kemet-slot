using System;
using System.Collections.Generic;
using UnityEngine;

namespace EgyptianSlot
{
    /// <summary>
    /// The maths of the machine, with no Unity scene or coroutine involvement, so it
    /// can be exercised and reasoned about on its own.
    /// </summary>
    public class SlotEngine
    {
        private readonly SlotConfig _config;
        private readonly System.Random _random;

        /// <summary>Cumulative weights over every symbol, used for the per-reel roll.</summary>
        private readonly float[] _allWeights;
        private readonly float _allTotal;

        /// <summary>
        /// Cumulative weights over the single-cell symbols only, used for each cell once a
        /// reel is known not to be a full-column one. Parallel to <see cref="_cellSymbols"/>.
        /// </summary>
        private readonly float[] _cellWeights;
        private readonly int[] _cellSymbols;
        private readonly float _cellTotal;

        public SlotEngine(SlotConfig config, int? seed = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.Symbols == null || config.Symbols.Count == 0)
                throw new ArgumentException("Slot config has no symbols.", nameof(config));

            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // Precompute the cumulative tables once; every roll is then a binary search.
            _allWeights = new float[config.Symbols.Count];
            float running = 0f;
            for (int i = 0; i < config.Symbols.Count; i++)
            {
                running += Mathf.Max(0f, config.Symbols[i].Weight);
                _allWeights[i] = running;
            }

            _allTotal = running;
            if (_allTotal <= 0f)
                throw new ArgumentException("Every symbol has zero weight; nothing could ever land.", nameof(config));

            var cellIndices = new List<int>();
            var cellCumulative = new List<float>();
            running = 0f;
            for (int i = 0; i < config.Symbols.Count; i++)
            {
                if (config.Symbols[i].IsFullColumn) continue;
                running += Mathf.Max(0f, config.Symbols[i].Weight);
                cellIndices.Add(i);
                cellCumulative.Add(running);
            }

            _cellSymbols = cellIndices.ToArray();
            _cellWeights = cellCumulative.ToArray();
            _cellTotal = running;

            if (_cellTotal <= 0f)
                throw new ArgumentException(
                    "Every symbol covers a full column; a reel could never be filled cell by cell.",
                    nameof(config));
        }

        /// <summary>Picks one symbol index from the full set, full-column symbols included.</summary>
        public int RandomSymbolIndex() => Pick(_allWeights, _allTotal, null);

        /// <summary>Picks one symbol index from the single-cell symbols only.</summary>
        public int RandomCellSymbolIndex() => Pick(_cellWeights, _cellTotal, _cellSymbols);

        private int Pick(float[] cumulative, float total, int[] mapping)
        {
            float roll = (float)_random.NextDouble() * total;

            int low = 0;
            int high = cumulative.Length - 1;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (roll < cumulative[mid]) high = mid;
                else low = mid + 1;
            }

            return mapping == null ? low : mapping[low];
        }

        public bool IsFullColumn(int symbolIndex) => _config.Symbols[symbolIndex].IsFullColumn;

        /// <summary>Rolls a fresh grid and scores it.</summary>
        public SpinResult Spin(int lineBet)
        {
            var result = new SpinResult
            {
                Grid = new int[_config.ReelCount, _config.RowCount],
                TotalBet = _config.TotalBet(lineBet),
            };

            for (int reel = 0; reel < _config.ReelCount; reel++)
            {
                // One roll decides the reel. A full-column symbol takes the whole reel, so
                // it is drawn per reel rather than per cell; otherwise each cell is drawn
                // from the symbols that occupy a single cell.
                int candidate = RandomSymbolIndex();

                if (_config.Symbols[candidate].IsFullColumn)
                {
                    for (int row = 0; row < _config.RowCount; row++)
                        result.Grid[reel, row] = candidate;

                    continue;
                }

                for (int row = 0; row < _config.RowCount; row++)
                    result.Grid[reel, row] = RandomCellSymbolIndex();
            }

            Evaluate(result, lineBet);
            return result;
        }

        /// <summary>
        /// Rolls a grid rigged to pay well: the first payline is filled with the highest
        /// paying ordinary symbol, and every other cell is left to chance. Five of the top
        /// symbol pays many times the stake, which clears the big-win threshold on any
        /// bet level.
        ///
        /// This exists only so the big-win presentation can be demonstrated without
        /// waiting on the odds. It is not how the machine behaves otherwise.
        /// </summary>
        public SpinResult SpinWithForcedBigWin(int lineBet)
        {
            var result = new SpinResult
            {
                Grid = new int[_config.ReelCount, _config.RowCount],
                TotalBet = _config.TotalBet(lineBet),
            };

            for (int reel = 0; reel < _config.ReelCount; reel++)
                for (int row = 0; row < _config.RowCount; row++)
                    result.Grid[reel, row] = RandomCellSymbolIndex();

            int top = HighestPayingSymbol();
            if (top >= 0 && _config.Paylines.Count > 0)
            {
                var line = _config.Paylines[0];
                for (int reel = 0; reel < _config.ReelCount && reel < line.Rows.Length; reel++)
                    result.Grid[reel, line.Rows[reel]] = top;
            }

            Evaluate(result, lineBet);
            return result;
        }

        /// <summary>
        /// The ordinary symbol with the largest five-of-a-kind payout. Wilds and scatters
        /// are skipped: a wild line is a special case and a scatter forms no line at all.
        /// </summary>
        private int HighestPayingSymbol()
        {
            int best = -1;
            int bestPay = 0;

            for (int i = 0; i < _config.Symbols.Count; i++)
            {
                var symbol = _config.Symbols[i];
                if (symbol.IsWild || symbol.IsScatter || symbol.IsFullColumn) continue;

                int pay = symbol.LinePayFor(_config.ReelCount);
                if (pay <= bestPay) continue;

                bestPay = pay;
                best = i;
            }

            return best;
        }

        /// <summary>Scores an already-populated grid. Public so a fixed grid can be scored in tests.</summary>
        public void Evaluate(SpinResult result, int lineBet)
        {
            result.LineWins.Clear();
            result.ScatterWins.Clear();
            result.TotalWin = 0;

            ScoreLines(result, lineBet);
            ScoreScatters(result);

            foreach (var win in result.LineWins) result.TotalWin += win.Payout;
            foreach (var win in result.ScatterWins) result.TotalWin += win.Payout;
        }

        private void ScoreLines(SpinResult result, int lineBet)
        {
            for (int lineIndex = 0; lineIndex < _config.Paylines.Count; lineIndex++)
            {
                var line = _config.Paylines[lineIndex];
                if (line.Rows == null || line.Rows.Length < _config.ReelCount) continue;

                // A run always starts on reel 0. Leading wilds are ambiguous: they could
                // belong to whichever symbol appears next, so resolve the paying symbol
                // from the first non-wild cell and let the wilds join it.
                int payingSymbol = -1;
                int leadingWilds = 0;

                for (int reel = 0; reel < _config.ReelCount; reel++)
                {
                    int symbolIndex = result.Grid[reel, line.Rows[reel]];
                    var symbol = _config.Symbols[symbolIndex];

                    if (symbol.IsWild) { leadingWilds++; continue; }
                    if (symbol.IsScatter) break;   // scatters never form line runs

                    payingSymbol = symbolIndex;
                    break;
                }

                // An all-wild line pays as the wild itself.
                if (payingSymbol < 0)
                {
                    if (leadingWilds < 3) continue;
                    int wildIndex = result.Grid[0, line.Rows[0]];
                    int wildPay = _config.Symbols[wildIndex].LinePayFor(leadingWilds) * lineBet;
                    if (wildPay > 0)
                        result.LineWins.Add(new LineWin(lineIndex, wildIndex, leadingWilds, wildPay, true));
                    continue;
                }

                int matchCount = 0;
                bool usedWild = false;
                for (int reel = 0; reel < _config.ReelCount; reel++)
                {
                    int symbolIndex = result.Grid[reel, line.Rows[reel]];
                    if (symbolIndex == payingSymbol) { matchCount++; continue; }
                    if (_config.Symbols[symbolIndex].IsWild) { matchCount++; usedWild = true; continue; }
                    break;
                }

                int payout = _config.Symbols[payingSymbol].LinePayFor(matchCount) * lineBet;
                if (payout > 0)
                    result.LineWins.Add(new LineWin(lineIndex, payingSymbol, matchCount, payout, usedWild));
            }
        }

        private void ScoreScatters(SpinResult result)
        {
            for (int symbolIndex = 0; symbolIndex < _config.Symbols.Count; symbolIndex++)
            {
                var symbol = _config.Symbols[symbolIndex];
                if (!symbol.IsScatter) continue;

                // A full-column scatter is counted once per reel. Counting its cells would
                // treat a single landing as three scatters and pay out almost every spin.
                int count = 0;
                for (int reel = 0; reel < _config.ReelCount; reel++)
                {
                    for (int row = 0; row < _config.RowCount; row++)
                    {
                        if (result.Grid[reel, row] != symbolIndex) continue;

                        count++;
                        if (symbol.IsFullColumn) break;
                    }
                }

                int payout = symbol.ScatterPayFor(count) * result.TotalBet;
                if (payout > 0)
                    result.ScatterWins.Add(new ScatterWin(symbolIndex, count, payout));
            }
        }

        /// <summary>Cells a line win covers, for highlighting. Reused across calls by the caller.</summary>
        public void CellsForLineWin(LineWin win, List<Vector2Int> into)
        {
            into.Clear();
            var line = _config.Paylines[win.LineIndex];
            for (int reel = 0; reel < win.MatchCount; reel++)
                into.Add(new Vector2Int(reel, line.Rows[reel]));
        }
    }
}
