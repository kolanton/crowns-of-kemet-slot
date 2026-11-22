# Crowns of Kemet — slot demo

A 5x3, ten-line slot machine built in Unity around RetroStyle Games' free
[Crowns of Kemet](https://assetstore.unity.com/packages/2d/gui/icons/free-egyptian-slot-game-art-symbols-wild-and-anubis-character-396700)
Egyptian art pack.

> **Demonstration build — not a gambling product.** A technical prototype of slot
> mechanics, not a published game and not a real-money product. It was built as a Unity
> learning exercise, to practise and show a particular set of techniques: 2D sprite and
> atlas handling, reel animation and stop timing, weighted random outcomes, payline
> evaluation with wild substitution and full-column symbols, assembling a game UI from a
> third-party art pack, and packaging the result as a WebGL build small enough to load
> on a phone. Play-money credits with
> no monetary value: no wagering, no deposit, no purchase, no payout, and credits cannot
> be bought, redeemed or withdrawn. Outcomes come from an uncertified pseudorandom
> generator running in the browser **and are deliberately rigged**: every fifth spin is
> forced to pay a large win, so the win presentation can be demonstrated on demand
> (`SlotConfig.ForcedBigWinEverySpins`; set it to 0 to leave every spin to chance).
> The paytable is illustrative and no return-to-player
> percentage has been calculated, audited or certified. Not operated by, affiliated with
> or endorsed by any casino, operator or licensee.

**Play it:** see the GitHub Pages link in this repository's About panel. The build is
password-gated — ask me for the password.

Unity **6000.6.0f1**, WebGL, no runtime dependencies.

## The password gate

A static host has no server to check a password against, so a prompt drawn in JavaScript
would be theatre: the files sit right there, and anyone who opened the network tab would
walk straight past it. Here the password *is* the key.

`tools/encrypt-build.ps1` derives two keys from the password with PBKDF2-HMAC-SHA256
(310,000 iterations, random 16-byte salt), encrypts each of the three build files with
AES-256-CBC under the first, and authenticates each with HMAC-SHA256 under the second,
over the IV and ciphertext together. Each published file is `iv || ciphertext || tag`.
`docs/Build/manifest.json` carries the salt, the iteration count and the file list.

The page asks for the password, derives the same two keys in the browser with Web Crypto,
decrypts the three files into memory and hands Unity blob URLs. Without the password the
host is serving fifteen megabytes of noise.

Two details that are easy to get wrong:

- The manifest holds a `check` value — an HMAC of a fixed string under the MAC key — so a
  wrong password is rejected in a few hundred milliseconds instead of after a fifteen
  megabyte download fails its tag. It leaks nothing the encrypted files do not.
- The loader is told `cacheControl: () => "no-store"`. Unity's own cache would otherwise
  copy the decrypted build into IndexedDB, leaving the unlocked game on disk and making
  the password a formality on the next visit.

The password is never stored, so changing it means re-running the encrypt step and
pushing; that also revokes it for everyone who had the old one. Web Crypto is only
available in a secure context, so the page needs an `https://` or `localhost` address —
opening `docs/index.html` from the filesystem will say so rather than fail obscurely.

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

   Output lands in `Build/WebGL` and the build exits non-zero on failure.

5. **Publish.** Two scripts, from the repository root:

   ```
   powershell -File tools/encrypt-build.ps1
   powershell -File tools/build-page.ps1
   ```

   The first prompts for the password twice and writes `docs/Build` — three `.enc` files,
   `manifest.json`, and the Unity loader in clear. It takes a `-Password` argument too,
   which is for testing: it puts the password in your shell history. The second wraps
   `web/slot-demo.html` in a document skeleton and writes `docs/index.html`.

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
| `tools/encrypt-build.ps1` | Encrypts the build under a password. |
| `tools/build-page.ps1` | Wraps the page fragment into `docs/index.html`. |

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

## Credits and licence

The code here is mine to share. The third-party assets are not included in this
repository and must be acquired from the Asset Store, which is where their licences
allow them to come from:

| Asset | Author | Terms |
| --- | --- | --- |
| [Crowns of Kemet (FREE)](https://assetstore.unity.com/packages/2d/gui/icons/free-egyptian-slot-game-art-symbols-wild-and-anubis-character-396700) | RetroStyle Games | Unity Asset Store EULA |
| [FREE Casual Game SFX Pack](https://assetstore.unity.com/packages/audio/sound-fx/free-casual-game-sfx-pack-54116) | Dustyroom | CC0 (delivered via the Asset Store) |
| [UI SFX Free Pack](https://assetstore.unity.com/packages/audio/sound-fx/ui-sfx-free-pack-245925) | Skril Studio | Unity Asset Store EULA |
| [Free 6 Desert Tracks Music Pack](https://assetstore.unity.com/packages/audio/music/free-6-desert-tracks-music-pack-338276) | alkakrab | Unity Asset Store EULA |

The one exception is the typeface, [Amarante](https://fonts.google.com/specimen/Amarante)
by Sorkin Type Co. It is under the SIL Open Font License, which permits redistribution,
so it ships in `Assets/Fonts/` with its licence text alongside.

Everything under `Assets/` that belongs to the packs above is gitignored. The build in
`docs/` has their content compiled into it, which is ordinary product use rather than
redistribution.
