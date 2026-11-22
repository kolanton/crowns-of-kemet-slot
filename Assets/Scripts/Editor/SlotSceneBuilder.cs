using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace EgyptianSlot.EditorTools
{
    /// <summary>
    /// Generates the demo scene and its config from whatever art the pack imported.
    ///
    /// The scene is built in code rather than committed as a .unity file so that it can be
    /// regenerated after the art mapping changes, which it will: an art pack's filenames
    /// are only known once it is imported.
    /// </summary>
    public static class SlotSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/SlotDemo.unity";
        private const string ConfigPath = "Assets/Settings/SlotConfig.asset";
        private const string FontPath = "Assets/Fonts/Amarante-Regular.ttf";

        private const float ReferenceWidth = 1280f;
        private const float ReferenceHeight = 720f;

        private const int ReelCount = 5;
        private const int RowCount = 3;

        // The frame is drawn at this size; the grid inside it is measured from the art
        // rather than fixed, so the cells follow whatever window the frame actually has.
        private const float ReelGap = 5f;
        // The frame's painted window turns out to be about 69% x 61% of the art, so the
        // frame is drawn large enough that the grid inside it still reads at a usable size.
        // Its foot runs behind the HUD bar, as it does in the pack's own mock-up.
        private const float FrameWidth = 960f;
        private const float FrameHeight = 648f;
        private const float GridCenterY = 24f;

        private const float BarHeight = 128f;

        /// <summary>
        /// The readout plate's own height. Its art is 248x64 with chamfered corners, so
        /// drawing it taller stretches the thin rails top and bottom and pulls the
        /// chamfers out of shape. Kept at the native height, it is never distorted
        /// vertically and only stretches along its length, which is what the nine-slice
        /// is for.
        /// </summary>
        private const float PlateHeight = 64f;

        /// <summary>
        /// How far the plates and buttons sit below the bar's centre line, leaving room
        /// for their captions above them.
        /// </summary>
        private const float BarContentY = -12f;

        /// <summary>
        /// Symbol value ranking, low to high, matched by filename substring. The pack ships
        /// four mid-tier symbols with no inherent order, so the paytable needs one stated.
        /// Anything unlisted keeps its catalogue position, after the listed ones.
        /// </summary>
        private static readonly string[] SymbolValueOrder = { "ankh", "scarab", "necklace", "eye" };

        /// <summary>
        /// Paytable names, matched by filename substring. The pack's filenames carry tier
        /// prefixes ("high_") and shorthand ("eye") that would read badly in the UI.
        /// </summary>
        private static readonly (string Token, string Name)[] FriendlyNames =
        {
            ("eye", "Eye of Horus"),
            ("ankh", "Ankh"),
            ("scarab", "Scarab"),
            ("necklace", "Necklace"),
            ("wild", "Wild"),
            ("anubis", "Anubis"),
        };

        // Palette, taken from the pack's own art: gilt and lapis on dark stone.
        private static readonly Color Gold = new(1f, 0.86f, 0.42f);
        private static readonly Color GoldDim = new(0.78f, 0.70f, 0.55f);
        private static readonly Color Carved = new(0.10f, 0.07f, 0.12f);
        private static readonly Color Stone = new(0.08f, 0.05f, 0.11f, 0.88f);

        [MenuItem("Egyptian Slot/2. Build Demo Scene")]
        public static void BuildScene()
        {
            // The HUD shapes are generated rather than shipped, so make sure they exist
            // before the catalogue is read.
            UiArtGenerator.Generate();

            // Logged every time: the mapping from filename to role is the part most likely
            // to be wrong, and the log is the only record of it in a batch-mode run.
            SlotArtCatalog.Report();

            var catalog = SlotArtCatalog.Build();
            if (!catalog.Any())
            {
                Debug.LogError("[SlotSceneBuilder] No art found. Import the pack and run " +
                               "\"1. Report Imported Art\" first.");
                return;
            }

            var config = CreateConfig(catalog);
            if (config == null) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var canvas = CreateCanvas();
            CreateBackground(canvas, catalog);

            var reels = CreateReels(canvas, config, catalog);
            var winPresenter = CreateWinPresenter(canvas, catalog);
            var hud = CreateHud(canvas, catalog);

            var machine = new GameObject("SlotMachine").AddComponent<SlotMachineController>();

            // Named "SlotMachine" because the hosting page addresses it by that name
            // through SendMessage to toggle sound.
            var audio = machine.gameObject.AddComponent<SlotAudio>();
            WireAudio(audio);

            SetPrivateField(machine, "_config", config);
            SetPrivateField(machine, "_reels", reels);
            SetPrivateField(machine, "_hud", hud);
            SetPrivateField(machine, "_winPresenter", winPresenter);
            SetPrivateField(machine, "_audio", audio);
            SetPrivateField(hud, "_audio", audio);
            SetPrivateField(winPresenter, "_audio", audio);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();

            Debug.Log($"[SlotSceneBuilder] Built {ScenePath} with {config.Symbols.Count} symbols " +
                      $"and {config.Paylines.Count} paylines.");
        }

        /// <summary>
        /// Builds the config from the catalogued art: wild and scatter get their special
        /// behaviour, and the remaining symbols are weighted and paid in descending order
        /// of how many there are, so the pack's own symbol count decides the paytable shape.
        /// </summary>
        private static SlotConfig CreateConfig(Dictionary<string, List<Sprite>> catalog)
        {
            var symbolSprites = catalog.TryGetValue("symbol", out var found)
                ? new List<Sprite>(found)
                : new List<Sprite>();

            if (symbolSprites.Count < 3)
            {
                Debug.LogError($"[SlotSceneBuilder] Only found {symbolSprites.Count} symbol " +
                               "sprites; need at least 3. Check the rules in " +
                               $"{nameof(SlotArtCatalog)}.");
                return null;
            }

            symbolSprites = OrderByValue(symbolSprites);

            var config = ScriptableObject.CreateInstance<SlotConfig>();
            config.ReelCount = ReelCount;
            config.RowCount = RowCount;
            config.Symbols = new List<SlotSymbol>();

            // Low-value symbols are common, high-value ones rare. Walking the list and
            // tapering both weight and pay gives a usable curve without hand-tuning each.
            int count = symbolSprites.Count;
            for (int i = 0; i < count; i++)
            {
                float rank = count == 1 ? 0f : (float)i / (count - 1);   // 0 = low, 1 = high
                int topPay = Mathf.RoundToInt(Mathf.Lerp(40f, 500f, rank));

                config.Symbols.Add(new SlotSymbol
                {
                    Id = symbolSprites[i].name,
                    DisplayName = FriendlyName(symbolSprites[i].name),
                    Sprite = symbolSprites[i],
                    Weight = Mathf.Lerp(30f, 6f, rank),
                    LinePays = new[] { topPay / 10, topPay / 3, topPay },
                });
            }

            if (catalog.TryGetValue("wild", out var wilds) && wilds.Count > 0)
            {
                config.Symbols.Add(new SlotSymbol
                {
                    Id = "wild",
                    DisplayName = "Wild",
                    Sprite = wilds[0],
                    Weight = 4f,
                    IsWild = true,
                    LinePays = new[] { 50, 200, 1000 },
                });
            }

            if (catalog.TryGetValue("scatter", out var scatters) && scatters.Count > 0)
            {
                // The character art is a column tall by design — the pack's own mock-up
                // shows it filling a whole reel — so it lands as a full-column symbol and
                // is counted once per reel rather than once per cell.
                config.Symbols.Add(new SlotSymbol
                {
                    Id = "anubis",
                    DisplayName = "Anubis",
                    Sprite = scatters[0],
                    Weight = 5f,
                    IsScatter = true,
                    IsFullColumn = true,
                    ScatterPays = new[] { 2, 10, 50 },
                });
            }

            config.ApplyDefaultPaylines();

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();

            return config;
        }

        /// <summary>
        /// Sorts symbols low value to high using <see cref="SymbolValueOrder"/>. Sprites it
        /// does not name keep their relative order and sit above the listed ones, so adding
        /// art to the pack degrades into "treated as high value" rather than an exception.
        /// </summary>
        private static List<Sprite> OrderByValue(List<Sprite> sprites)
        {
            return sprites
                .Select(sprite => new
                {
                    Sprite = sprite,
                    Rank = System.Array.FindIndex(SymbolValueOrder,
                        token => sprite.name.ToLowerInvariant().Contains(token)),
                })
                .OrderBy(entry => entry.Rank < 0 ? int.MaxValue : entry.Rank)
                .Select(entry => entry.Sprite)
                .ToList();
        }

        /// <summary>The display name for a sprite, falling back to a tidied filename.</summary>
        private static string FriendlyName(string rawName)
        {
            string lower = rawName.ToLowerInvariant();
            foreach (var entry in FriendlyNames)
                if (lower.Contains(entry.Token)) return entry.Name;

            return Prettify(rawName);
        }

        /// <summary>Turns a filename like "symbol_scarab_01" into "Scarab".</summary>
        private static string Prettify(string rawName)
        {
            // Tier prefixes and the export's framing suffix describe the file, not the
            // symbol, so they are dropped rather than capitalised.
            string[] noise = { "symbol", "high", "low", "no", "frame" };

            var words = rawName
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ')
                .Where(word => word.Length > 0)
                .Where(word => !word.All(char.IsDigit))
                .Where(word => !noise.Contains(word.ToLowerInvariant()))
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant());

            string pretty = string.Join(" ", words);
            return pretty.Length > 0 ? pretty : rawName;
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            // Match height, so the reels keep their size on a wide browser window and the
            // background is what stretches instead.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            return canvas;
        }

        private static void CreateBackground(Canvas canvas, Dictionary<string, List<Sprite>> catalog)
        {
            var sprite = First(catalog, "background");

            var image = NewImage("Background", canvas.transform, sprite);
            image.color = sprite != null ? Color.white : new Color(0.08f, 0.06f, 0.12f);

            if (sprite == null)
            {
                Stretch(image.rectTransform);
            }
            else
            {
                // The pack's background is square and the canvas never is, so it has to
                // cover rather than fit: stretching it would widen the art by half again,
                // and a fixed size leaves bare edges as soon as the screen is wider than
                // it was sized for — which a phone held sideways always is.
                //
                // Envelope mode keeps the art's own proportions and grows it until it
                // covers the canvas, cropping the overflow, at any aspect.
                var backgroundRect = image.rectTransform;
                Stretch(backgroundRect);

                var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite.rect.height > 0f
                    ? sprite.rect.width / sprite.rect.height
                    : 1f;
            }

            var logo = First(catalog, "logo");
            if (logo == null) return;

            var logoImage = NewImage("Logo", canvas.transform, logo);
            var rect = logoImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -6f);
            rect.sizeDelta = FitInto(logo, 440f, 116f);
        }

        private static ReelView[] CreateReels(Canvas canvas, SlotConfig config,
            Dictionary<string, List<Sprite>> catalog)
        {
            // Added before the reels so it sits behind them. The "filled" frame has its
            // reel interior painted in, which is the backing the symbols sit on.
            var frameSprite = First(catalog, "frame") ?? First(catalog, "frameEmpty");
            if (frameSprite != null)
            {
                var backing = NewImage("ReelFrame", canvas.transform, frameSprite);
                var backingRect = backing.rectTransform;
                backingRect.anchorMin = backingRect.anchorMax = new Vector2(0.5f, 0.5f);
                backingRect.anchoredPosition = new Vector2(0f, GridCenterY);
                backingRect.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            }

            // The grid has to sit inside the frame's window, not merely near it. The window
            // is measured off the "empty" frame, whose interior is transparent, so the
            // numbers come from the art instead of from guesswork.
            var window = MeasureFrameWindow(First(catalog, "frameEmpty"));

            float gridWidth = FrameWidth * window.width;
            float gridHeight = FrameHeight * window.height;

            // x and y hold the window's offset from the frame's centre, not a corner, so
            // they are used directly rather than through Rect.center.
            float gridCentreY = GridCenterY + FrameHeight * window.y;
            float gridCentreX = FrameWidth * window.x;

            float cellWidth = (gridWidth - (ReelCount - 1) * ReelGap) / ReelCount;
            float cellHeight = gridHeight / RowCount;

            var frameRoot = new GameObject("Reels", typeof(RectTransform));
            var frameRect = (RectTransform)frameRoot.transform;
            frameRect.SetParent(canvas.transform, false);
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = new Vector2(gridCentreX, gridCentreY);
            frameRect.sizeDelta = new Vector2(gridWidth, gridHeight);

            var reels = new ReelView[ReelCount];

            for (int i = 0; i < ReelCount; i++)
            {
                var column = new GameObject($"Reel{i}", typeof(RectTransform), typeof(RectMask2D));
                var columnRect = (RectTransform)column.transform;
                columnRect.SetParent(frameRect, false);

                // Anchored to the grid's centre, not its left edge: the offset below is
                // measured from the centre, and anchoring left would shift the whole grid
                // out by half its width.
                columnRect.anchorMin = columnRect.anchorMax = new Vector2(0.5f, 0.5f);
                columnRect.pivot = new Vector2(0f, 0.5f);
                columnRect.sizeDelta = new Vector2(cellWidth, gridHeight);
                columnRect.anchoredPosition = new Vector2(
                    i * (cellWidth + ReelGap) - gridWidth * 0.5f, 0f);

                // The mask clips the strip; cells live under a child anchored to its top so
                // the reel can position them in plain top-down coordinates.
                var content = new GameObject("Content", typeof(RectTransform));
                var contentRect = (RectTransform)content.transform;
                contentRect.SetParent(columnRect, false);
                Stretch(contentRect);

                var view = column.AddComponent<ReelView>();
                SetPrivateField(view, "_content", contentRect);
                SetPrivateField(view, "_cellHeight", cellHeight);

                reels[i] = view;
            }

            Debug.Log($"[SlotSceneBuilder] Reel window {window.width:P0} x {window.height:P0} " +
                      $"of the frame -> grid {gridWidth:0} x {gridHeight:0}, " +
                      $"cell {cellWidth:0} x {cellHeight:0}.");

            return reels;
        }

        /// <summary>
        /// Finds the transparent window in the middle of the reel frame, as fractions of
        /// the sprite: <c>width</c> and <c>height</c> are its size, and <c>center</c> is its
        /// offset from the sprite's centre with y pointing up.
        ///
        /// The PNG is decoded from disk rather than read through the imported texture, so
        /// the asset's own import settings do not have to be made readable.
        /// </summary>
        private static Rect MeasureFrameWindow(Sprite emptyFrame)
        {
            // Sensible fallback if the frame cannot be measured: a window a little inside
            // the art, which is at least not wider than the frame itself.
            var fallback = new Rect(0f, 0f, 0.84f, 0.78f);

            if (emptyFrame == null) return fallback;

            string path = AssetDatabase.GetAssetPath(emptyFrame);
            if (!File.Exists(path)) return fallback;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(texture);
                return fallback;
            }

            int width = texture.width;
            int height = texture.height;
            var pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);

            int midX = width / 2;
            int midY = height / 2;
            const byte opaque = 128;

            if (pixels[midY * width + midX].a >= opaque)
            {
                Debug.LogWarning("[SlotSceneBuilder] The empty reel frame is not transparent at " +
                                 "its centre, so the reel window could not be measured.");
                return fallback;
            }

            int left = midX;
            while (left > 0 && pixels[midY * width + left - 1].a < opaque) left--;

            int right = midX;
            while (right < width - 1 && pixels[midY * width + right + 1].a < opaque) right++;

            int bottom = midY;
            while (bottom > 0 && pixels[(bottom - 1) * width + midX].a < opaque) bottom--;

            int top = midY;
            while (top < height - 1 && pixels[(top + 1) * width + midX].a < opaque) top++;

            float windowWidth = (right - left + 1) / (float)width;
            float windowHeight = (top - bottom + 1) / (float)height;

            // Centre of the window relative to the centre of the sprite.
            float centreX = ((left + right) * 0.5f) / width - 0.5f;
            float centreY = ((bottom + top) * 0.5f) / height - 0.5f;

            return new Rect(centreX, centreY, windowWidth, windowHeight);
        }

        private static WinPresenter CreateWinPresenter(Canvas canvas, Dictionary<string, List<Sprite>> catalog)
        {
            var winLabel = NewLabel("WinLabel", canvas.transform, 36, TextAnchor.MiddleCenter);
            var winRect = winLabel.rectTransform;
            winRect.anchorMin = winRect.anchorMax = new Vector2(0.5f, 0.5f);
            winRect.anchoredPosition = new Vector2(0f, -220f);
            winRect.sizeDelta = new Vector2(760f, 52f);
            winLabel.color = Gold;

            var bannerGo = new GameObject("BigWinBanner", typeof(RectTransform), typeof(CanvasGroup));
            var bannerRect = (RectTransform)bannerGo.transform;
            bannerRect.SetParent(canvas.transform, false);
            Stretch(bannerRect);

            var decorSprite = First(catalog, "bigwinDecor");
            if (decorSprite != null)
            {
                var decor = NewImage("Decor", bannerRect, decorSprite);
                var decorRect = decor.rectTransform;
                decorRect.anchorMin = decorRect.anchorMax = new Vector2(0.5f, 0.5f);
                decorRect.anchoredPosition = new Vector2(0f, -30f);
                decorRect.sizeDelta = FitInto(decorSprite, 520f, 410f);
            }

            var bannerSprite = First(catalog, "bigwin");
            if (bannerSprite != null)
            {
                var art = NewImage("Art", bannerRect, bannerSprite);
                var artRect = art.rectTransform;
                artRect.anchorMin = artRect.anchorMax = new Vector2(0.5f, 0.5f);
                artRect.anchoredPosition = new Vector2(0f, 40f);
                artRect.sizeDelta = FitInto(bannerSprite, 820f, 300f);
            }

            var bannerLabel = NewLabel("Label", bannerRect, 90, TextAnchor.MiddleCenter);
            Stretch(bannerLabel.rectTransform);
            bannerLabel.color = Gold;

            // The pack ships one pop-up, which already reads "Big Win", so drawing a tier
            // caption over it would only fight the art. Higher tiers reuse the same
            // artwork rather than inventing a Mega pop-up the pack does not have.
            if (bannerSprite != null) bannerLabel.enabled = false;

            var presenter = canvas.gameObject.AddComponent<WinPresenter>();
            SetPrivateField(presenter, "_winLabel", winLabel);
            SetPrivateField(presenter, "_bigWinBanner", bannerGo.GetComponent<CanvasGroup>());
            SetPrivateField(presenter, "_bigWinLabel", bannerLabel);

            return presenter;
        }

        /// <summary>
        /// Builds the control bar in the arrangement the pack's own mock-up uses: stake on
        /// the left, spin in the middle, wallet on the right. The pack ships no button art,
        /// so the plates and disc come from <see cref="UiArtGenerator"/>.
        /// </summary>
        private static SlotHud CreateHud(Canvas canvas, Dictionary<string, List<Sprite>> catalog)
        {
            // The pack's own HUD art, exported from its Photoshop source, is preferred over
            // the shapes this project generates. The generated ones remain the fallback so
            // the scene still builds for anyone who has only the plain sprite pack.
            var packReadout = First(catalog, "packReadout");
            var packButton = First(catalog, "packButton");
            var packSpin = First(catalog, "packSpin");
            var packSpinPressed = First(catalog, "packSpinPressed");
            var packArrow = First(catalog, "packArrow");

            var plate = packReadout
                        ?? AssetDatabase.LoadAssetAtPath<Sprite>(UiArtGenerator.PlatePath);
            var buttonPlate = packButton ?? plate;
            var disc = packSpin
                       ?? AssetDatabase.LoadAssetAtPath<Sprite>(UiArtGenerator.DiscPath);

            var bar = new GameObject("Hud", typeof(RectTransform), typeof(Image));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(canvas.transform, false);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(0f, BarHeight);

            // The art behind the HUD is a lit sunset over water, so the readouts need a
            // ground of their own rather than relying on the scene being dark down there.
            var scrim = bar.GetComponent<Image>();
            scrim.color = new Color(0.05f, 0.03f, 0.07f, 0.74f);
            scrim.raycastTarget = false;

            NewPlate(barRect, "Bet", 36f, 140f, "BET", plate, out var betValue);

            // Narrower than the other readouts so its arrows sit close to the number
            // rather than stranded at the far ends of a wide plate.
            var levelPlate = NewPlate(barRect, "Level", 196f, 190f, "LEVEL", plate, out var levelValue);
            var levelDown = NewArrow(levelPlate, "LevelDown", "<", packArrow, plate, left: true);
            var levelUp = NewArrow(levelPlate, "LevelUp", ">", packArrow, plate, left: false);

            // The pack's button plate is solid gilt, so its caption has to be dark to read;
            // the generated fallback plate is dark stone and wants the opposite.
            Color buttonCaption = packButton != null ? Carved : Gold;

            // The two action plates sit either side of the spin button and the disc drops
            // into the gap between them, overlapping each slightly — the arrangement the
            // pack's own mock-up uses.
            const float buttonWidth = 152f;
            const float buttonHeight = 72f;
            const float discDiameter = 104f;

            var autoButton = NewPlateButton(barRect, "AutoPlay", -116f, buttonWidth, buttonHeight,
                "AUTO\nPLAY", buttonPlate, out var autoLabel, buttonCaption);
            var maxBetButton = NewPlateButton(barRect, "MaxBet", 116f, buttonWidth, buttonHeight,
                "MAX\nBET", buttonPlate, out _, buttonCaption, mirrored: packButton != null);

            // Wallet readouts hold the right edge, so they stay put on a wide phone in
            // landscape instead of drifting toward the middle.
            NewPlate(barRect, "Coins", 36f, 204f, "COINS", plate, out var balanceValue,
                fromRight: true);
            NewPlate(barRect, "LineBet", 254f, 150f, "LINE BET", plate, out var lineBetValue,
                fromRight: true);

            // Added last so it draws over both plates, and lifted so it rides above the
            // line of the bar rather than sitting flush with it.
            var spinButton = NewDisc(barRect, "Spin", discDiameter, disc,
                out var spinImage, out var spinLabel, riseY: BarContentY + 10f);

            // The pack's spin button is painted with its own symbol, so a caption over it
            // would only compete with the art.
            if (packSpin != null) spinLabel.enabled = false;

            // Reset has no counterpart in the pack's mock-up, so it sits out of the bar
            // rather than crowding a layout the background art was composed around.
            var resetButton = NewPlateButton(canvas.transform, "Reset", 0f, 112f, 54f, "RESET",
                plate, out _);
            var resetRect = (RectTransform)resetButton.transform;
            resetRect.anchorMin = resetRect.anchorMax = new Vector2(1f, 1f);
            resetRect.pivot = new Vector2(1f, 1f);
            resetRect.anchoredPosition = new Vector2(-18f, -18f);
            resetRect.sizeDelta = new Vector2(112f, 54f);

            var message = NewLabel("Message", canvas.transform, 22, TextAnchor.MiddleCenter);
            var messageRect = message.rectTransform;
            messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.pivot = new Vector2(0.5f, 0f);
            messageRect.anchoredPosition = new Vector2(0f, BarHeight + 10f);
            messageRect.sizeDelta = new Vector2(620f, 30f);
            message.color = new Color(1f, 1f, 1f, 0.55f);

            var hud = canvas.gameObject.AddComponent<SlotHud>();
            SetPrivateField(hud, "_betLabel", betValue);
            SetPrivateField(hud, "_levelLabel", levelValue);
            SetPrivateField(hud, "_lineBetLabel", lineBetValue);
            SetPrivateField(hud, "_balanceLabel", balanceValue);
            SetPrivateField(hud, "_messageLabel", message);
            SetPrivateField(hud, "_spinButton", spinButton);
            SetPrivateField(hud, "_levelUpButton", levelUp);
            SetPrivateField(hud, "_levelDownButton", levelDown);
            SetPrivateField(hud, "_maxBetButton", maxBetButton);
            SetPrivateField(hud, "_autoplayButton", autoButton);
            SetPrivateField(hud, "_autoplayLabel", autoLabel);
            SetPrivateField(hud, "_resetButton", resetButton);
            SetPrivateField(hud, "_spinImage", spinImage);
            SetPrivateField(hud, "_spinLabel", spinLabel);
            SetPrivateField(hud, "_spinIdleSprite", packSpin);
            SetPrivateField(hud, "_spinPressedSprite", packSpinPressed);

            return hud;
        }

        /// <summary>
        /// Fills the audio component from the imported sound pack. Absent the pack, every
        /// event simply has no clips and the machine runs silently.
        /// </summary>
        private static void WireAudio(SlotAudio audio)
        {
            var clips = SlotAudioCatalog.Build();

            if (clips.Count == 0)
            {
                Debug.LogWarning("[SlotSceneBuilder] No sound pack found; the demo will be silent.");
                return;
            }

            SetPrivateField(audio, "_spin", SlotAudioCatalog.For(clips, "spin"));
            SetPrivateField(audio, "_reelStop", SlotAudioCatalog.For(clips, "reelStop"));
            SetPrivateField(audio, "_win", SlotAudioCatalog.For(clips, "win"));
            SetPrivateField(audio, "_bigWin", SlotAudioCatalog.For(clips, "bigWin"));
            SetPrivateField(audio, "_click", SlotAudioCatalog.For(clips, "click"));

            var source = audio.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            SetPrivateField(audio, "_source", source);

            // Reel stops get their own source so varying their pitch cannot bend a win
            // chime or the music playing at the same time.
            var reelStopSource = audio.gameObject.AddComponent<AudioSource>();
            reelStopSource.playOnAwake = false;
            SetPrivateField(audio, "_reelStopSource", reelStopSource);

            var loopSource = audio.gameObject.AddComponent<AudioSource>();
            loopSource.playOnAwake = false;
            loopSource.loop = true;
            SetPrivateField(audio, "_spinLoopSource", loopSource);
            SetPrivateField(audio, "_spinLoop", SlotAudioCatalog.For(clips, "spinLoop").FirstOrDefault());

            // The scene is built empty, which leaves out the default camera and with it
            // the AudioListener. Unity plays nothing without one, and nothing else in a
            // screen-space-overlay scene misses a camera, so the gap is silent in both
            // senses: no warning, no sound.
            if (Object.FindFirstObjectByType<AudioListener>() == null)
                audio.gameObject.AddComponent<AudioListener>();

            var music = SlotAudioCatalog.FindMusic();
            if (music == null)
            {
                Debug.Log("[SlotSceneBuilder] No music track found; effects only.");
                return;
            }

            var musicSource = audio.gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            SetPrivateField(audio, "_musicSource", musicSource);
            SetPrivateField(audio, "_music", music);

            Debug.Log($"[SlotSceneBuilder] Music: {music.name} ({music.length:0}s)");

            Debug.Log("[SlotSceneBuilder] Audio wired: " +
                      string.Join(", ", clips.Select(pair => $"{pair.Key}={pair.Value.Count}")));
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(scene => scene.path == ScenePath)) return;

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---- HUD pieces --------------------------------------------------------------

        /// <summary>A plate with a small caption above a large value, anchored from the left.</summary>
        /// <summary>
        /// A readout plate. <paramref name="x"/> is measured from the bar's left edge, or
        /// from its right edge when <paramref name="fromRight"/> is set. The bar stretches
        /// with the screen, so each plate has to hold on to the edge it belongs near;
        /// absolute offsets from one side leave the far group adrift on a wide screen and
        /// off the edge on a narrow one.
        /// </summary>
        private static RectTransform NewPlate(Transform parent, string name, float x, float width,
            string caption, Sprite plateSprite, out Text value, bool fromRight = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            float edge = fromRight ? 1f : 0f;
            rect.anchorMin = rect.anchorMax = new Vector2(edge, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.anchoredPosition = new Vector2(fromRight ? -x : x, BarContentY);
            rect.sizeDelta = new Vector2(width, PlateHeight);

            PaintPlate(go.GetComponent<Image>(), plateSprite);

            // The caption sits above the plate rather than inside it. The plate is only
            // 64 high; stacking a caption and a value within it forced it taller, which
            // is what distorted the art.
            var captionLabel = NewLabel("Caption", rect, 16, TextAnchor.MiddleCenter);
            var captionRect = captionLabel.rectTransform;
            captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 1f);
            captionRect.pivot = new Vector2(0.5f, 0f);
            captionRect.anchoredPosition = new Vector2(0f, 4f);
            captionRect.sizeDelta = new Vector2(width, 20f);
            captionLabel.text = caption;
            captionLabel.color = GoldDim;

            // The value fills the plate, so anything aligned to the plate's centre is
            // also aligned to the number.
            value = NewLabel("Value", rect, 28, TextAnchor.MiddleCenter);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = new Vector2(8f, 4f);
            valueRect.offsetMax = new Vector2(-8f, -4f);
            value.color = Gold;
            value.text = "0";

            return rect;
        }

        /// <summary>
        /// A plate that is a button, positioned by its centre so it can be mirrored: the
        /// pack ships one angled plate, and the pair either side of the spin button are
        /// that shape facing opposite ways.
        /// </summary>
        /// <summary>
        /// A plate that is a button. <paramref name="offsetFromCentre"/> places it relative
        /// to the bar's middle, so the spin cluster stays together and centred whatever
        /// the screen's width.
        /// </summary>
        private static Button NewPlateButton(Transform parent, string name, float offsetFromCentre,
            float width, float height, string caption, Sprite plateSprite, out Text label,
            Color? captionColor = null, bool mirrored = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(offsetFromCentre, BarContentY);
            rect.sizeDelta = new Vector2(width, height);

            PaintPlate(go.GetComponent<Image>(), plateSprite, raycast: true);

            label = NewLabel("Label", rect, 20, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = caption;
            label.color = captionColor ?? Gold;

            if (mirrored)
            {
                // Flip the plate, then flip the caption back so the words still read
                // left to right.
                rect.localScale = new Vector3(-1f, 1f, 1f);
                label.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            }

            return go.GetComponent<Button>();
        }

        /// <summary>
        /// A stepper arrow pinned inside a plate's left or right end. With the pack's arrow
        /// art it is drawn near its native 21x22 so it stays crisp, and the right-hand one
        /// is the same sprite mirrored. Without it, a text glyph on a plate stands in.
        /// </summary>
        private static Button NewArrow(RectTransform plate, string name, string glyph,
            Sprite arrowSprite, Sprite plateSprite, bool left)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(plate, false);
            // Pivoted at its centre: mirroring scales about the pivot, so an edge pivot
            // would throw the flipped arrow outside the plate instead of turning it.
            // Vertically centred on the plate, which is now where the number is.
            rect.anchorMin = rect.anchorMax = new Vector2(left ? 0f : 1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Inset past the plate's chamfered corner. The nine-slice holds 20px of
            // unstretched corner at each end, plus the gold rail, so anything closer than
            // this sits on the frame rather than inside it.
            rect.anchoredPosition = new Vector2(left ? 34f : -34f, 0f);

            var image = go.GetComponent<Image>();

            if (arrowSprite != null)
            {
                rect.sizeDelta = new Vector2(24f, 26f);
                image.sprite = arrowSprite;
                image.preserveAspect = true;
                image.raycastTarget = true;

                // The pack ships one arrow and it points right, so the left-hand one is
                // that sprite mirrored.
                if (left) rect.localScale = new Vector3(-1f, 1f, 1f);

                return go.GetComponent<Button>();
            }

            rect.sizeDelta = new Vector2(40f, 56f);
            PaintPlate(image, plateSprite, raycast: true);

            var label = NewLabel("Label", rect, 24, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = glyph;
            label.color = Gold;

            return go.GetComponent<Button>();
        }

        /// <summary>
        /// The round spin button, centred in the bar and raised by <paramref name="riseY"/>
        /// so it sits proud of the plates on either side.
        /// </summary>
        private static Button NewDisc(RectTransform parent, string name, float diameter,
            Sprite discSprite, out Image image, out Text label, float riseY = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, riseY);
            rect.sizeDelta = new Vector2(diameter, diameter);

            image = go.GetComponent<Image>();
            image.sprite = discSprite;
            image.preserveAspect = true;
            if (discSprite == null) image.color = Gold;

            label = NewLabel("Label", rect, 22, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "SPIN";
            label.color = Carved;

            return go.GetComponent<Button>();
        }

        /// <summary>
        /// Draws a plate sprite. A sprite with a nine-slice border stretches to its rect;
        /// one without keeps its own shape, which is what an angled plate needs — slicing
        /// such a shape pulls its middle apart.
        /// </summary>
        private static void PaintPlate(Image image, Sprite plateSprite, bool raycast = false)
        {
            bool hasBorder = plateSprite != null && plateSprite.border != Vector4.zero;

            image.sprite = plateSprite;
            image.type = hasBorder ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = !hasBorder && plateSprite != null;
            image.raycastTarget = raycast;
            if (plateSprite == null) image.color = Stone;
        }

        // ---- small UI helpers ---------------------------------------------------------

        private static Sprite First(Dictionary<string, List<Sprite>> catalog, string role) =>
            catalog.TryGetValue(role, out var list) && list.Count > 0 ? list[0] : null;

        private static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text NewLabel(string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = UiFont();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// The HUD typeface. Amarante is an Art Nouveau display face whose weight and
        /// flared serifs sit with the pack's gilt ornament far better than a UI sans.
        /// It ships with the project under the Open Font License.
        ///
        /// Falls back to Unity's built-in font, whose name changed in newer editors, so
        /// the scene still builds if the font file is missing.
        /// </summary>
        private static Font UiFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font != null) return font;

            Debug.LogWarning($"[SlotSceneBuilder] {FontPath} not found; falling back to the " +
                             "built-in font.");

            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                          ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (builtin == null)
                Debug.LogWarning("[SlotSceneBuilder] No built-in font either; labels will be blank.");

            return builtin;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Largest size that fits a sprite inside a box without distorting it.</summary>
        private static Vector2 FitInto(Sprite sprite, float maxWidth, float maxHeight)
        {
            float width = sprite.rect.width;
            float height = sprite.rect.height;
            if (width <= 0f || height <= 0f) return new Vector2(maxWidth, maxHeight);

            float scale = Mathf.Min(maxWidth / width, maxHeight / height);
            return new Vector2(width * scale, height * scale);
        }

        /// <summary>
        /// Assigns a [SerializeField] private field. The scene is generated, so there is no
        /// inspector pass to do this by hand, and widening the fields to public purely for
        /// the builder's benefit would leak editor concerns into the runtime API.
        /// </summary>
        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (field == null)
            {
                Debug.LogError($"[SlotSceneBuilder] {target.GetType().Name} has no field " +
                               $"\"{fieldName}\"; the scene will be wired incompletely.");
                return;
            }

            field.SetValue(target, value);

            // Reflection bypasses the usual change tracking, so mark the object itself.
            EditorUtility.SetDirty(target);
        }
    }
}
