# Crowns of Kemet — slot demo

A 5x3, ten-line slot machine built in Unity around RetroStyle Games' free
[Crowns of Kemet](https://assetstore.unity.com/packages/2d/gui/icons/free-egyptian-slot-game-art-symbols-wild-and-anubis-character-396700)
Egyptian art pack.

> **Demonstration build — not a gambling product.** A technical prototype of slot
> mechanics, not a published game and not a real-money product. Play-money credits with
> no monetary value: no wagering, no deposit, no purchase, no payout, and credits cannot
> be bought, redeemed or withdrawn. Outcomes come from an uncertified pseudorandom
> generator running in the browser; the paytable is illustrative and no return-to-player
> percentage has been calculated, audited or certified. Not operated by, affiliated with
> or endorsed by any casino, operator or licensee.

**Play it:** see the GitHub Pages link in this repository's About panel.

Unity **6000.6.0f1**, WebGL, no runtime dependencies.

## The art is not in this repository

The pack is free but licensed through the Unity Asset Store, which does not permit
redistributing its files. `Assets/RetroStyle Games/` is therefore gitignored.

To run the project yourself:

1. Open the [asset page](https://assetstore.unity.com/packages/2d/gui/icons/free-egyptian-slot-game-art-symbols-wild-and-anubis-character-396700)
   and add it to your assets (it costs nothing).
2. In Unity: **Window → Package Management → My Assets**, find it, Download, Import.
3. Run the two menu items under **Egyptian Slot** described below.

The build under `docs/` already has the art compiled into it, which is ordinary product
use rather than redistribution.

## Why the scene is generated

The scene and the paytable are built by an editor script rather than committed as assets.
An art pack's filenames are only knowable once it is imported, and the mapping from
filename to game role is the part most likely to need another pass. Regenerating is
cheaper than rewiring.

That mapping is read off the pack's own demo scene: the **framed** symbol variants belong
on the reels, and the `no_frame` cut-outs are showcase pieces.

## Pipeline

1. **Egyptian Slot → 1. Report Imported Art** — lists every sprite found and the role it
   matched. Check this before step 2 and adjust `Rules` in `SlotArtCatalog.cs` if anything
   landed in the wrong bucket. Step 2 logs it again anyway.
2. **Egyptian Slot → 2. Build Demo Scene** — writes `Assets/Scenes/SlotDemo.unity` and
   `Assets/Settings/SlotConfig.asset`, and adds the scene to Build Settings.
3. **Egyptian Slot → 3. Apply Web Texture Budget** — optional. Caps import sizes for a
   smaller download. Not needed at the pack's current size.
4. **Egyptian Slot → Build WebGL**, or headless:

   ```
   Unity.exe -quit -batchmode -projectPath <this folder> \
     -executeMethod EgyptianSlot.EditorTools.WebGlBuilder.Build
   ```

   Output lands in `Build/WebGL` and the build exits non-zero on failure. Copy it to
   `docs/Build/` to publish; `docs/index.html` is the hand-written page that loads it.

## Code layout

| File | Role |
| --- | --- |
| `SlotEngine.cs` | The maths. No scene involvement, so it can be reasoned about alone. |
| `SlotConfig.cs` | Grid, symbols, paylines, wallet. All tuning lives here, none in the engine. |
| `SlotSymbol.cs` | One symbol: art, weight, line pays, wild/scatter flags. |
| `Payline.cs` | One winning path: a row index per reel. |
| `SpinResult.cs` | A spin's outcome — the grid, line wins, scatter wins. |
| `ReelView.cs` | One spinning column. |
| `SlotMachineController.cs` | Spin lifecycle, wallet, autoplay. |
| `WinPresenter.cs` | Cycles the winning lines, raises the big-win banner. |
| `SlotHud.cs` | Buttons and readouts. Holds no state; reads it back from the controller. |
| `SlotArtCatalog.cs` | Sorts imported sprites into roles by path. |
| `SlotSceneBuilder.cs` | Generates the scene and paytable from the catalogue. |
| `TextureBudget.cs` | Caps import sizes for the web. |
| `WebGlBuilder.cs` | Batch-mode WebGL build. |

## How a reel works

The reel is a strip of symbol indices read through a continuous offset. Visible row `r`
shows `strip[floor(offset) + r]`, and the fractional part slides every row down by that
much of a cell. When the fraction rolls past 1 the strip steps on by one index, which
cancels the slide exactly — so it reads as seamless downward scrolling with only
`rows + 2` images.

Landing on a chosen outcome is then arithmetic rather than animation bookkeeping: pick an
integer stop offset and write the wanted symbols into the strip at the indices that offset
will show.

The reels start spinning *before* the outcome is resolved and keep going until told where
to stop, so a slow resolve can never force a reel to stop on something it was not told to
show.

## Paytable

Coins per line bet, for 3, 4 and 5 from the leftmost reel:

| Symbol | 3 | 4 | 5 | Weight |
| --- | ---: | ---: | ---: | ---: |
| Ankh | 4 | 13 | 40 | 30 |
| Scarab | 19 | 64 | 193 | 22 |
| Necklace | 34 | 115 | 347 | 14 |
| Eye of Horus | 50 | 166 | 500 | 6 |
| Wild | 50 | 200 | 1000 | 4 |
| Anubis (scatter) | 2 | 10 | 50 | 5 |

Weights and pays are derived rather than hand-authored: the catalogue's symbol order is
treated as low-to-high value and both taper across it, with the order itself set by
`SymbolValueOrder` in the scene builder.

Wild substitutes for every paying symbol. Leading wilds resolve against the first non-wild
cell on the line, so a wild joins whichever run it could most plausibly belong to; an
all-wild line pays as the wild. Anubis is a scatter — it pays on count anywhere on the
grid, times the total bet, and never forms line runs.

## Licence

The code here is mine to share. The art is RetroStyle Games', used under the Standard
Unity Asset Store EULA, and is not included — see above.
