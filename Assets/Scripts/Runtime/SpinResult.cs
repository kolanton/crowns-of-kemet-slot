using System.Collections.Generic;

namespace EgyptianSlot
{
    /// <summary>One payline that paid, and what it paid.</summary>
    public readonly struct LineWin
    {
        /// <summary>Index into <see cref="SlotConfig.Paylines"/>.</summary>
        public readonly int LineIndex;
        /// <summary>Index into <see cref="SlotConfig.Symbols"/> of the symbol that formed the run.</summary>
        public readonly int SymbolIndex;
        /// <summary>How many reels the run covered, counting from the leftmost reel.</summary>
        public readonly int MatchCount;
        public readonly int Payout;
        /// <summary>Whether a wild stood in for the paying symbol anywhere in the run.</summary>
        public readonly bool UsedWild;

        public LineWin(int lineIndex, int symbolIndex, int matchCount, int payout, bool usedWild)
        {
            LineIndex = lineIndex;
            SymbolIndex = symbolIndex;
            MatchCount = matchCount;
            Payout = payout;
            UsedWild = usedWild;
        }
    }

    /// <summary>A scatter pay, which ignores paylines and counts the whole grid.</summary>
    public readonly struct ScatterWin
    {
        public readonly int SymbolIndex;
        public readonly int Count;
        public readonly int Payout;

        public ScatterWin(int symbolIndex, int count, int payout)
        {
            SymbolIndex = symbolIndex;
            Count = count;
            Payout = payout;
        }
    }

    /// <summary>The outcome of a single spin: where every symbol landed and what it won.</summary>
    public class SpinResult
    {
        /// <summary>Symbol index per cell, addressed as [reel, row].</summary>
        public int[,] Grid;

        public readonly List<LineWin> LineWins = new();
        public readonly List<ScatterWin> ScatterWins = new();

        public int TotalBet;

        /// <summary>Everything won this spin, lines and scatters together.</summary>
        public int TotalWin;

        public bool IsWin => TotalWin > 0;

        /// <summary>Win as a multiple of the stake, used to pick the celebration tier.</summary>
        public float WinMultiplier => TotalBet > 0 ? (float)TotalWin / TotalBet : 0f;
    }
}
