using System.Collections.Generic;
using UnityEngine;

namespace EgyptianSlot
{
    /// <summary>
    /// Everything that defines the machine's maths and shape. Authored once in the
    /// inspector so the engine itself holds no tuning numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Egyptian Slot/Slot Config", fileName = "SlotConfig")]
    public class SlotConfig : ScriptableObject
    {
        [Header("Grid")]
        [Min(3)] public int ReelCount = 5;
        [Min(1)] public int RowCount = 3;

        [Header("Symbols")]
        public List<SlotSymbol> Symbols = new();

        [Header("Paylines")]
        [Tooltip("One entry per line. Each holds a row index per reel, left to right.")]
        public List<Payline> Paylines = new();

        [Header("Wallet")]
        public int StartingBalance = 1000;
        [Tooltip("Selectable coin amounts staked on every active payline.")]
        public int[] LineBets = { 1, 2, 5, 10, 25 };

        /// <summary>Total stake for a spin at the given per-line bet.</summary>
        public int TotalBet(int lineBet) => lineBet * Mathf.Max(1, Paylines.Count);

        /// <summary>
        /// Fills in the ten classic 5x3 paylines. Used by the setup tooling so the
        /// config never ships with an empty line set.
        /// </summary>
        public void ApplyDefaultPaylines()
        {
            if (ReelCount != 5 || RowCount != 3)
            {
                Debug.LogWarning($"{nameof(ApplyDefaultPaylines)} only covers a 5x3 grid; " +
                                 $"this config is {ReelCount}x{RowCount}. Leaving paylines untouched.");
                return;
            }

            Paylines = new List<Payline>
            {
                Payline.Of("Middle",     1, 1, 1, 1, 1),
                Payline.Of("Top",        0, 0, 0, 0, 0),
                Payline.Of("Bottom",     2, 2, 2, 2, 2),
                Payline.Of("V",          0, 1, 2, 1, 0),
                Payline.Of("Caret",      2, 1, 0, 1, 2),
                Payline.Of("Zigzag Up",  0, 0, 1, 2, 2),
                Payline.Of("Zigzag Down",2, 2, 1, 0, 0),
                Payline.Of("Valley",     1, 2, 2, 2, 1),
                Payline.Of("Peak",       1, 0, 0, 0, 1),
                Payline.Of("Wave",       1, 2, 1, 0, 1),
            };
        }
    }
}
