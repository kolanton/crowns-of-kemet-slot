using System;
using UnityEngine;

namespace EgyptianSlot
{
    /// <summary>A single winning path across the grid: one row per reel, left to right.</summary>
    [Serializable]
    public class Payline
    {
        public string Name = "Line";

        [Tooltip("Row index on each reel, left to right. Length must equal the reel count.")]
        public int[] Rows = { 1, 1, 1, 1, 1 };

        [Tooltip("Colour used when this line is highlighted during a win.")]
        public Color HighlightColor = Color.yellow;

        public static Payline Of(string name, params int[] rows) => new() { Name = name, Rows = rows };
    }
}
