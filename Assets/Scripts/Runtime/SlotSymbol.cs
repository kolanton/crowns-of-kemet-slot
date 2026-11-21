using System;
using UnityEngine;

namespace EgyptianSlot
{
    /// <summary>One reel symbol: its art, how often it lands, and what it pays.</summary>
    [Serializable]
    public class SlotSymbol
    {
        [Tooltip("Stable identifier used in logs and save data.")]
        public string Id = "symbol";

        [Tooltip("Name shown in the paytable.")]
        public string DisplayName = "Symbol";

        public Sprite Sprite;

        [Tooltip("Relative chance of this symbol appearing on a reel. Higher = more common.")]
        [Min(0f)] public float Weight = 10f;

        [Tooltip("Coins paid per line bet for 3, 4 and 5 of a kind, in that order.")]
        public int[] LinePays = { 5, 20, 100 };

        [Tooltip("Substitutes for every paying symbol on a line.")]
        public bool IsWild;

        [Tooltip("Pays on count alone, anywhere on the grid, ignoring paylines.")]
        public bool IsScatter;

        [Tooltip("Covers a whole reel when it lands, rather than a single cell. For art " +
                 "drawn a column tall, such as a standing character.")]
        public bool IsFullColumn;

        [Tooltip("Coins paid per total bet for 3, 4 and 5 scatters. Only read when IsScatter.")]
        public int[] ScatterPays = { 2, 10, 50 };

        /// <summary>Line payout for a run of <paramref name="count"/>, or 0 if that run pays nothing.</summary>
        public int LinePayFor(int count)
        {
            int index = count - 3;
            if (index < 0 || LinePays == null || index >= LinePays.Length) return 0;
            return LinePays[index];
        }

        /// <summary>Scatter payout for <paramref name="count"/> scatters, or 0 if that count pays nothing.</summary>
        public int ScatterPayFor(int count)
        {
            int index = count - 3;
            if (index < 0 || ScatterPays == null || index >= ScatterPays.Length) return 0;
            return ScatterPays[index];
        }
    }
}
