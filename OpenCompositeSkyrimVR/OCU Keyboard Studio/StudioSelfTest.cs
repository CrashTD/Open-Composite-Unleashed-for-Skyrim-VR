namespace OCUKeyboardStudio;

internal static class StudioSelfTest
{
    public static int Run(string outputDirectory)
    {
        try
        {
            string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
            string layoutPath = Path.Combine(assets, "en_gb.kb");
            var document = KeyboardDocument.Load(layoutPath);
            if (document.Keys.Count < 60)
                throw new InvalidDataException($"Expected the full OCU layout, got only {document.Keys.Count} keys.");

            KeyboardKey f10 = document.Keys.Single(key => key.Label == "F10");
            f10.LabelOffsetY = -1;
            f10.LabelScale = 0.95f;
            KeyboardKey upArrow = document.Keys.Single(key => key.Character == '\x04');
            upArrow.LabelOffsetX = 9;
            upArrow.LabelOffsetY = -6;
            upArrow.LabelScale = 0.5f;
            document.BaseTheme = "modern_green";
            document.FontName = "ocu_nordic";
            document.CustomStyleEnabled = true;
            document.KeyPlatesEnabled = false;
            document.TopButtonPlatesEnabled = false;
            document.InputBarPlateEnabled = false;
            document.ParchmentRibbonEnabled = false;
            document.FontColor = Color.FromArgb(255, 225, 245, 235);
            document.FontOutlineColor = Color.FromArgb(210, 31, 18, 52);
            document.FontGlowColor = Color.FromArgb(230, 70, 140, 255);
            document.FontGlowEnabled = true;
            document.FontGlowStrength = 68;
            document.FontGlowRadius = 5;
            document.FontBreatheEnabled = true;
            document.FontBreatheMinPercent = 25;
            document.FontBreathePeriodSeconds = 3.5f;
            document.FontBreathePhaseDegrees = 60;
            document.KeyColor = Color.FromArgb(190, 62, 190, 143);
            document.PlateFillColor = Color.FromArgb(90, 12, 18, 24);
            document.PlateOutlineWidth = 3;
            document.GlowRadius = 6;
            document.KeyRoundness = 24;
            document.KeyBreatheEnabled = true;
            document.KeyBreatheMinPercent = 30;
            document.KeyBreathePeriodSeconds = 2.5f;
            document.KeyBreathePhaseDegrees = 15;
            document.BackgroundImagePath = Path.Combine(assets, "skyui-bg.png");
            document.BackgroundFileName = "OCUKeyboardBackground.png";
            document.BackgroundX = 12;
            document.BackgroundY = 8;
            document.BackgroundWidth = 1000;
            document.BackgroundHeight = 544;
            document.BackgroundOpacity = 88;
            document.BackgroundEdgeFade = 12;
            document.BackgroundRotation = 1.5f;
            document.BackgroundRoundness = 36;
            document.Sprites.Add(new KeyboardSprite
            {
                SourcePath = Path.Combine(assets, "spacebar.png"),
                FileName = "OCUKeyboardSprite01.png",
                X = 80,
                Y = 24,
                Width = 360,
                Height = 70,
                Opacity = 72,
                EdgeFade = 5,
                Rotation = -4,
                GlowEnabled = true,
                GlowColor = Color.FromArgb(230, 90, 210, 255),
                GlowStrength = 70,
                GlowRadius = 16,
                BreatheEnabled = true,
                BreatheMinPercent = 20,
                BreathePeriodSeconds = 2,
                BreathePhaseDegrees = 0
            });
            document.Sprites.Add(new KeyboardSprite
            {
                SourcePath = Path.Combine(assets, "dwemer-spacebar.png"),
                FileName = "OCUKeyboardSprite02.png",
                X = 590,
                Y = 454,
                Width = 300,
                Height = 55,
                Opacity = 64,
                EdgeFade = 3,
                Rotation = 7
            });
            document.SizeControlOffsetX = 8;
            document.SizeControlOffsetY = -6;
            document.OpacityControlOffsetX = -5;
            document.TiltControlOffsetY = 9;
            document.TextBarOffsetX = 14;
            document.TextBarOffsetY = -7;
            document.ModeButtonOffsetX = 19;
            document.ModeButtonOffsetY = 8;
            document.LockButtonOffsetX = -11;
            document.LockButtonOffsetY = 6;
            document.SizeControlDesign.Width = 128;
            document.SizeControlDesign.Height = 116;
            document.SizeControlDesign.UpOffsetX = 11;
            document.SizeControlDesign.UpOffsetY = -3;
            document.SizeControlDesign.UpWidth = 31;
            document.SizeControlDesign.UpHeight = 23;
            document.SizeControlDesign.DownOffsetX = -7;
            document.SizeControlDesign.DownOffsetY = 5;
            document.SizeControlDesign.DownWidth = 29;
            document.SizeControlDesign.DownHeight = 19;
            document.SizeControlDesign.LabelOffsetX = 4;
            document.SizeControlDesign.LabelOffsetY = -2;
            document.SizeControlDesign.LabelScale = 0.72f;
            document.SizeControlDesign.ValueOffsetX = -3;
            document.SizeControlDesign.ValueOffsetY = 2;
            document.SizeControlDesign.ValueScale = 0.66f;
            document.ControlArrowImagePath = Path.Combine(assets, "spacebar.png");
            document.ControlArrowFileName = "OCUKeyboardControlArrow.png";
            document.ControlArrowRotation = 12;
            document.ControlArrowBreatheEnabled = true;
            document.ControlArrowBreatheMinPercent = 40;
            document.ControlArrowBreathePeriodSeconds = 3;
            document.ControlArrowBreathePhaseDegrees = 45;

            Directory.CreateDirectory(outputDirectory);
            string hostFixture = Path.Combine(outputDirectory, "ocu-host-fixture");
            string hostedStudio = Path.Combine(hostFixture, "OCU Keyboard Studio");
            string hostedRoot = Path.Combine(hostFixture, "root");
            Directory.CreateDirectory(hostedStudio);
            Directory.CreateDirectory(hostedRoot);
            File.WriteAllBytes(Path.Combine(hostedRoot, "openvr_api.dll"), [0x4f, 0x43, 0x55]);
            string? detectedRoot = MainForm.FindOcuRootFromStudioDirectory(hostedStudio);
            if (!string.Equals(Path.GetFullPath(hostedRoot), detectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Studio did not auto-detect its containing OCU mod root.");
            string roundTrip = Path.Combine(outputDirectory, "keyboard-studio-roundtrip.kb");
            document.Save(roundTrip);
            KeyboardDocument loaded = KeyboardDocument.Load(roundTrip);
            KeyboardDocument historyClone = loaded.Clone();
            if (!loaded.IsEquivalentForHistory(historyClone))
                throw new InvalidDataException("An unchanged history snapshot was not recognized as equivalent.");
            historyClone.Keys[0].X += 3f;
            if (loaded.IsEquivalentForHistory(historyClone))
                throw new InvalidDataException("A visible key edit was incorrectly treated as a no-op history snapshot.");
            historyClone = loaded.Clone();
            historyClone.BackgroundImagePath += ".different";
            if (loaded.IsEquivalentForHistory(historyClone))
                throw new InvalidDataException("A different authoring artwork path was incorrectly treated as a no-op history snapshot.");
            KeyboardKey loadedF10 = loaded.Keys.Single(key => key.Label == "F10");
            if (Math.Abs(loadedF10.LabelOffsetY + 1f) > 0.001f || Math.Abs(loadedF10.LabelScale - 0.95f) > 0.001f)
                throw new InvalidDataException("Layout label placement did not survive the save/load round trip.");
            KeyboardKey loadedUpArrow = loaded.Keys.Single(key => key.Character == '\x04');
            if (Math.Abs(loadedUpArrow.LabelOffsetX - 9f) > 0.001f
                || Math.Abs(loadedUpArrow.LabelOffsetY + 6f) > 0.001f
                || Math.Abs(loadedUpArrow.LabelScale - 0.5f) > 0.001f)
                throw new InvalidDataException("Arrow-key placement did not survive the save/load round trip.");
            if (loaded.BaseTheme != "modern_green" || loaded.FontName != "ocu_nordic"
                || !loaded.CustomStyleEnabled || loaded.KeyPlatesEnabled
                || loaded.TopButtonPlatesEnabled || loaded.InputBarPlateEnabled
                || loaded.ParchmentRibbonEnabled
                || loaded.KeyRoundness != 24
                || loaded.PlateOutlineWidth != 3 || loaded.PlateFillColor.A != 90
                || loaded.FontOutlineColor.R != 31 || loaded.FontOutlineColor.A != 210
                || loaded.FontGlowColor.B != 255 || !loaded.FontGlowEnabled
                || loaded.FontGlowStrength != 68 || loaded.FontGlowRadius != 5
                || !loaded.FontBreatheEnabled || loaded.FontBreatheMinPercent != 25
                || Math.Abs(loaded.FontBreathePeriodSeconds - 3.5f) > 0.001f
                || Math.Abs(loaded.FontBreathePhaseDegrees - 60f) > 0.001f
                || !loaded.KeyBreatheEnabled || loaded.KeyBreatheMinPercent != 30
                || loaded.GlowRadius != 6
                || loaded.BackgroundFileName != "OCUKeyboardBackground.png"
                || Math.Abs(loaded.BackgroundRotation - 1.5f) > 0.001f
                || loaded.BackgroundRoundness != 36
                || loaded.Sprites.Count != 2 || loaded.Sprites[0].Opacity != 72
                || !loaded.Sprites[0].GlowEnabled || loaded.Sprites[0].GlowColor.B != 255
                || loaded.Sprites[0].GlowStrength != 70 || loaded.Sprites[0].GlowRadius != 16
                || !loaded.Sprites[0].BreatheEnabled || loaded.Sprites[0].BreatheMinPercent != 20
                || Math.Abs(loaded.Sprites[1].Rotation - 7f) > 0.001f
                || loaded.SizeControlOffsetX != 8 || loaded.TiltControlOffsetY != 9
                || loaded.TextBarOffsetX != 14 || loaded.TextBarOffsetY != -7
                || loaded.ModeButtonOffsetX != 19 || loaded.ModeButtonOffsetY != 8
                || loaded.LockButtonOffsetX != -11 || loaded.LockButtonOffsetY != 6
                || loaded.SizeControlDesign.Width != 128 || loaded.SizeControlDesign.Height != 116
                || loaded.SizeControlDesign.UpOffsetX != 11 || loaded.SizeControlDesign.UpOffsetY != -3
                || loaded.SizeControlDesign.UpWidth != 31 || loaded.SizeControlDesign.UpHeight != 23
                || loaded.SizeControlDesign.DownOffsetX != -7 || loaded.SizeControlDesign.DownOffsetY != 5
                || loaded.SizeControlDesign.DownWidth != 29 || loaded.SizeControlDesign.DownHeight != 19
                || loaded.SizeControlDesign.LabelOffsetX != 4 || loaded.SizeControlDesign.LabelOffsetY != -2
                || Math.Abs(loaded.SizeControlDesign.LabelScale - 0.72f) > 0.001f
                || loaded.SizeControlDesign.ValueOffsetX != -3 || loaded.SizeControlDesign.ValueOffsetY != 2
                || Math.Abs(loaded.SizeControlDesign.ValueScale - 0.66f) > 0.001f
                || loaded.ControlArrowFileName != "OCUKeyboardControlArrow.png"
                || !loaded.ControlArrowBreatheEnabled || loaded.ControlArrowBreatheMinPercent != 40
                || Math.Abs(loaded.ControlArrowRotation - 12f) > 0.001f)
                throw new InvalidDataException("Keyboard appearance settings did not survive the save/load round trip.");
            // The serialized paths are portable names beside the layout. Point
            // this isolated test document back to its source art before render/export.
            loaded.BackgroundImagePath = Path.Combine(assets, "skyui-bg.png");
            loaded.Sprites[0].SourcePath = Path.Combine(assets, "spacebar.png");
            loaded.Sprites[1].SourcePath = Path.Combine(assets, "dwemer-spacebar.png");
            loaded.ControlArrowImagePath = Path.Combine(assets, "spacebar.png");

            string metadata = Directory.EnumerateFiles(assets, "OCU-Nordic-30.sfn").Single();
            string texture = Path.Combine(assets, "OCU-Nordic-30-texture.png");
            var renderer = new KeyboardRenderer(assets, new SudoFont(metadata, texture))
            {
                Theme = KeyboardTheme.BuiltIns.Single(theme => theme.Name == "Modern Green"),
                SelectedKeyId = -1
            };

            var plateTest = new KeyboardDocument
            {
                Width = 15,
                BaseTheme = "modern_green",
                FontName = "ocu_nordic",
                CustomStyleEnabled = true,
                KeyPlatesEnabled = true,
                TopButtonPlatesEnabled = false,
                InputBarPlateEnabled = false,
                FontColor = Color.Transparent,
                OutlineEnabled = false,
                GlowEnabled = false,
                PlateOutlineWidth = 3,
                KeyColor = Color.FromArgb(170, 240, 20, 30),
                PlateFillColor = Color.FromArgb(90, 15, 50, 180),
                KeyRoundness = 12
            };
            var plateTestKey = new KeyboardKey { Id = 8001, Character = 'x', X = 4, Y = 2, Label = "", ShiftLabel = "" };
            plateTest.Keys.Add(plateTestKey);
            Rectangle plateTestRectangle = Rectangle.Round(renderer.KeyRectangle(plateTest, plateTestKey));
            Point plateCenter = new(plateTestRectangle.Left + plateTestRectangle.Width / 2,
                plateTestRectangle.Top + plateTestRectangle.Height / 2);
            Point plateEdge = new(plateTestRectangle.Left + 1,
                plateTestRectangle.Top + plateTestRectangle.Height / 2);
            using Bitmap redOutline = renderer.Render(plateTest);
            plateTest.KeyColor = Color.FromArgb(170, 20, 240, 30);
            using Bitmap greenOutline = renderer.Render(plateTest);
            if (redOutline.GetPixel(plateCenter.X, plateCenter.Y).ToArgb()
                    != greenOutline.GetPixel(plateCenter.X, plateCenter.Y).ToArgb()
                || redOutline.GetPixel(plateEdge.X, plateEdge.Y).ToArgb()
                    == greenOutline.GetPixel(plateEdge.X, plateEdge.Y).ToArgb())
                throw new InvalidDataException("Plate outline color still bleeds through the translucent fill instead of staying on the border ring.");

            Color stableOutlinePixel = greenOutline.GetPixel(plateEdge.X, plateEdge.Y);
            plateTest.PlateFillColor = Color.FromArgb(90, 220, 160, 20);
            using Bitmap amberFill = renderer.Render(plateTest);
            if (amberFill.GetPixel(plateCenter.X, plateCenter.Y).ToArgb()
                    == greenOutline.GetPixel(plateCenter.X, plateCenter.Y).ToArgb()
                || amberFill.GetPixel(plateEdge.X, plateEdge.Y).ToArgb() != stableOutlinePixel.ToArgb())
                throw new InvalidDataException("Plate fill color is not isolated from the outline ring.");

            plateTest.PlateOutlineWidth = 0;
            plateTest.PlateFillColor = Color.Transparent;
            plateTest.GlowColor = Color.FromArgb(255, 255, 20, 80);
            plateTest.GlowStrength = 100;
            plateTest.GlowRadius = 4;
            using Bitmap noPlateGlow = renderer.Render(plateTest);
            plateTest.GlowEnabled = true;
            using Bitmap outsidePlateGlow = renderer.Render(plateTest);
            Point glowPoint = new(plateTestRectangle.Left - 2,
                plateTestRectangle.Top + plateTestRectangle.Height / 2);
            if (noPlateGlow.GetPixel(plateCenter.X, plateCenter.Y).ToArgb()
                    != outsidePlateGlow.GetPixel(plateCenter.X, plateCenter.Y).ToArgb()
                || noPlateGlow.GetPixel(glowPoint.X, glowPoint.Y).ToArgb()
                    == outsidePlateGlow.GetPixel(glowPoint.X, glowPoint.Y).ToArgb())
                throw new InvalidDataException("Plate glow is not isolated to the outside edge.");

            KeyboardDocument ribbonDocument = KeyboardDocument.Load(layoutPath);
            KeyboardKey ribbonKey = ribbonDocument.Keys.Single(key => key.Character == ' ');
            renderer.Theme = KeyboardTheme.BuiltIns.Single(theme => theme.Name == "Parchment");
            RectangleF ribbonBounds = renderer.KeyContentRectangle(ribbonDocument, ribbonKey);
            bool foundRibbonPixel = false;
            for (float y = ribbonBounds.Top; y < ribbonBounds.Bottom && !foundRibbonPixel; y += 1f)
                for (float x = ribbonBounds.Left; x < ribbonBounds.Right; x += 1f)
                    if (renderer.IsPointOnKeyContent(ribbonDocument, ribbonKey, new PointF(x, y))) { foundRibbonPixel = true; break; }
            if (!foundRibbonPixel)
                throw new InvalidDataException("The Parchment ribbon cannot be selected as key content.");
            ribbonDocument.ParchmentRibbonEnabled = false;
            if (renderer.IsPointOnKeyContent(ribbonDocument, ribbonKey,
                    new PointF(ribbonBounds.Left + ribbonBounds.Width / 2f, ribbonBounds.Top + ribbonBounds.Height / 2f)))
                throw new InvalidDataException("The removed Parchment ribbon retained a content hit target.");
            renderer.Theme = KeyboardTheme.BuiltIns.Single(theme => theme.Name == "Modern Green");

            KeyboardKey letterKey = loaded.Keys.First(key => key.Label.Length == 1 && char.IsAsciiLetter(key.Label[0]));
            RectangleF letterPlate = renderer.KeyRectangle(loaded, letterKey);
            RectangleF letterInk = renderer.KeyContentRectangle(loaded, letterKey);
            if (letterInk.Width >= letterPlate.Width * 0.75f || letterInk.Height >= letterPlate.Height * 0.85f)
                throw new InvalidDataException("Single-letter glyph selection still occupies most of its key plate.");
            if (renderer.IsPointOnKeyContent(loaded, letterKey, new PointF(letterPlate.Left + 2, letterPlate.Top + 2)))
                throw new InvalidDataException("Empty key-plate space incorrectly hit the glyph content.");
            bool foundPaintedGlyphPixel = false;
            for (float y = letterInk.Top; y < letterInk.Bottom && !foundPaintedGlyphPixel; y += 1f)
                for (float x = letterInk.Left; x < letterInk.Right; x += 1f)
                    if (renderer.IsPointOnKeyContent(loaded, letterKey, new PointF(x, y))) { foundPaintedGlyphPixel = true; break; }
            if (!foundPaintedGlyphPixel)
                throw new InvalidDataException("Painted glyph pixels were not selectable.");
            RectangleF arrowPlate = renderer.KeyRectangle(loaded, loadedUpArrow);
            RectangleF arrowContent = renderer.KeyContentRectangle(loaded, loadedUpArrow);
            if (Math.Abs(arrowContent.Width - 10f) > 0.001f || Math.Abs(arrowContent.Height - 8f) > 0.001f
                || Math.Abs((arrowContent.Left + arrowContent.Width / 2f) - (arrowPlate.Left + arrowPlate.Width / 2f + 9f)) > 0.001f
                || Math.Abs((arrowContent.Top + arrowContent.Height / 2f) - (arrowPlate.Top + arrowPlate.Height / 2f - 6f)) > 0.001f)
                throw new InvalidDataException("Arrow-key content did not apply its authored move and scale transform.");
            RectangleF textBar = renderer.TopElementRectangle(loaded, KeyboardTopElement.TextBar);
            RectangleF modeButton = renderer.TopElementRectangle(loaded, KeyboardTopElement.Mode);
            RectangleF lockButton = renderer.TopElementRectangle(loaded, KeyboardTopElement.Lock);
            if (Math.Abs(textBar.Left - (KeyboardRenderer.MarginHorizontal + 14)) > 0.001f
                || Math.Abs(textBar.Top - (KeyboardRenderer.GrabBarHeight + KeyboardRenderer.MarginTop - 7)) > 0.001f
                || Math.Abs(modeButton.Left - (KeyboardRenderer.MarginHorizontal + 19)) > 0.001f
                || Math.Abs(modeButton.Top - (KeyboardRenderer.GrabBarHeight + KeyboardRenderer.MarginTop - 36 + 8)) > 0.001f
                || Math.Abs(lockButton.Left - (KeyboardRenderer.TextureWidth - KeyboardRenderer.MarginHorizontal - 120 - 11)) > 0.001f
                || Math.Abs(lockButton.Top - (KeyboardRenderer.GrabBarHeight + KeyboardRenderer.MarginTop - 36 + 6)) > 0.001f)
                throw new InvalidDataException("Movable top-bar geometry did not apply its saved offsets.");
            KeyboardDocument visibleTopPlates = loaded.Clone();
            visibleTopPlates.TopButtonPlatesEnabled = true;
            visibleTopPlates.InputBarPlateEnabled = true;
            renderer.AnimationTimeSeconds = 0;
            using Bitmap hiddenTopPlateFrame = renderer.Render(loaded);
            using Bitmap visibleTopPlateFrame = renderer.Render(visibleTopPlates);
            using var hiddenTopPlateBytes = new MemoryStream();
            using var visibleTopPlateBytes = new MemoryStream();
            hiddenTopPlateFrame.Save(hiddenTopPlateBytes, System.Drawing.Imaging.ImageFormat.Png);
            visibleTopPlateFrame.Save(visibleTopPlateBytes, System.Drawing.Imaging.ImageFormat.Png);
            if (hiddenTopPlateBytes.ToArray().SequenceEqual(visibleTopPlateBytes.ToArray()))
                throw new InvalidDataException("Independent top-button/input-bar plate visibility did not change rendering.");
            RectangleF sizeGroup = renderer.RuntimeControlRectangle(loaded, KeyboardRuntimeControl.Size);
            RectangleF sizeUp = renderer.RuntimeControlPartRectangle(loaded, KeyboardRuntimeControl.Size, KeyboardControlPart.UpArrow);
            RectangleF sizeDown = renderer.RuntimeControlPartRectangle(loaded, KeyboardRuntimeControl.Size, KeyboardControlPart.DownArrow);
            if (Math.Abs(sizeGroup.Width - 128) > 0.001f || Math.Abs(sizeGroup.Height - 116) > 0.001f
                || Math.Abs(sizeUp.Width - 31) > 0.001f || Math.Abs(sizeUp.Height - 23) > 0.001f
                || Math.Abs(sizeDown.Width - 29) > 0.001f || Math.Abs(sizeDown.Height - 19) > 0.001f
                || Math.Abs(sizeUp.Left - (sizeGroup.Left + (128 - 31) / 2f + 11)) > 0.001f
                || Math.Abs(sizeDown.Bottom - (sizeGroup.Bottom + 5)) > 0.001f)
                throw new InvalidDataException("Runtime control child geometry does not match the authored nested boxes.");
            RectangleF sizeLabel = renderer.RuntimeControlPartRectangle(loaded, KeyboardRuntimeControl.Size, KeyboardControlPart.Label);
            bool foundControlGlyphPixel = false;
            bool foundControlGlyphGap = false;
            for (float y = sizeLabel.Top; y < sizeLabel.Bottom; y += 0.5f)
            {
                for (float x = sizeLabel.Left; x < sizeLabel.Right; x += 0.5f)
                {
                    bool hit = renderer.IsPointOnRuntimeControlText(loaded, KeyboardRuntimeControl.Size, true, new PointF(x, y));
                    foundControlGlyphPixel |= hit;
                    foundControlGlyphGap |= !hit;
                }
            }
            if (!foundControlGlyphPixel || !foundControlGlyphGap)
                throw new InvalidDataException("Runtime control font selection is not restricted to painted glyph pixels.");
            renderer.AnimationTimeSeconds = 0;
            using Bitmap preview = renderer.Render(loaded);
            if (preview.Width != 1024 || preview.Height != 560)
                throw new InvalidDataException("Preview dimensions are not OCU's 1024x560 texture dimensions.");
            if (preview.GetPixel((int)loaded.BackgroundX, (int)loaded.BackgroundY).A != 0)
                throw new InvalidDataException("Rounded background corner did not produce a transparent mask.");
            byte pillEdgeAlpha = preview.GetPixel((int)loaded.BackgroundX + 31, (int)loaded.BackgroundY + 31).A;
            byte pillInnerAlpha = preview.GetPixel((int)loaded.BackgroundX + 45, (int)loaded.BackgroundY + 45).A;
            if (pillEdgeAlpha >= pillInnerAlpha)
                throw new InvalidDataException("Background feathering does not begin at the rounded pill boundary.");
            preview.Save(Path.Combine(outputDirectory, "keyboard-studio-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
            renderer.AnimationTimeSeconds = 1;
            using Bitmap animatedPreview = renderer.Render(loaded);
            using var frameA = new MemoryStream();
            using var frameB = new MemoryStream();
            preview.Save(frameA, System.Drawing.Imaging.ImageFormat.Png);
            animatedPreview.Save(frameB, System.Drawing.Imaging.ImageFormat.Png);
            if (frameA.ToArray().SequenceEqual(frameB.ToArray()))
                throw new InvalidDataException("Breathing animation did not change the rendered keyboard frame.");
            animatedPreview.Save(Path.Combine(outputDirectory, "keyboard-studio-preview-breathing.png"), System.Drawing.Imaging.ImageFormat.Png);

            string highDpiBackground = Path.Combine(outputDirectory, "self-test-300dpi-background.png");
            using (var highDpiSource = new Bitmap(320, 180, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                highDpiSource.SetResolution(300, 300);
                using Graphics graphics = Graphics.FromImage(highDpiSource);
                graphics.Clear(Color.FromArgb(255, 37, 83, 149));
                highDpiSource.Save(highDpiBackground, System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var savedHighDpiSource = new Bitmap(highDpiBackground))
            {
                if (savedHighDpiSource.HorizontalResolution < 250)
                    throw new InvalidDataException("The high-DPI artwork regression fixture lost its DPI metadata.");
            }
            var highDpiDocument = new KeyboardDocument
            {
                BackgroundImagePath = highDpiBackground,
                BackgroundFileName = "OCUKeyboardBackground.png",
                BackgroundX = 0,
                BackgroundY = 0,
                BackgroundWidth = KeyboardRenderer.TextureWidth,
                BackgroundHeight = KeyboardRenderer.TextureHeight,
                BackgroundOpacity = 100,
                BackgroundEdgeFade = 0,
                BackgroundRotation = 0,
                BackgroundRoundness = 0
            };
            using (Bitmap highDpiPreview = renderer.Render(highDpiDocument))
            {
                Color farPixel = highDpiPreview.GetPixel(800, 400);
                if (farPixel.A != 255 || farPixel.R != 37 || farPixel.G != 83 || farPixel.B != 149)
                    throw new InvalidDataException("High-DPI background artwork was shrunk into the upper-left corner.");
            }

            string windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string systemTtf = new[] { "segoeui.ttf", "arial.ttf", "tahoma.ttf" }
                .Select(name => Path.Combine(windowsFonts, name)).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("A Windows TTF was not available for the import self-test.");
            var fontDocument = new KeyboardDocument();
            fontDocument.Keys.Add(new KeyboardKey { Id = 1, Label = "AB", ShiftLabel = "C" });
            fontDocument.Keys.Add(new KeyboardKey { Id = 2, Label = "1", ShiftLabel = "" });
            var nordicFallback = new SudoFont(metadata, texture);
            FontImportResult imported = FontImporter.Import(systemTtf, fontDocument, outputDirectory, nordicFallback);
            var importedFont = new SudoFont(imported.MetadataPath, imported.TexturePath);
            char[] expectedImportedCharacters = fontDocument.RequiredFontCharacters().ToArray();
            if (!importedFont.Characters.Order().SequenceEqual(expectedImportedCharacters.Order()))
                throw new InvalidDataException("TTF conversion audited characters outside the existing keyboard keys.");
            if (importedFont.ContainsGlyph('Z'))
                throw new InvalidDataException("TTF conversion included a hypothetical key that is not in the keyboard.");
            if (!importedFont.TryGetGlyphRaster('A', out FontGlyphRaster importedA)
                || importedA.Alpha.Length == 0 || !importedA.Alpha.Any(alpha => alpha > 0))
                throw new InvalidDataException("TTF conversion produced a blank raster for an existing key glyph.");
            fontDocument.Keys.Add(new KeyboardKey { Id = 3, Label = "Z", ShiftLabel = "" });
            FontImportResult rebuilt = FontImporter.Import(imported.SourceFontPath, fontDocument, outputDirectory,
                nordicFallback, imported.ConfigName);
            var rebuiltFont = new SudoFont(rebuilt.MetadataPath, rebuilt.TexturePath);
            if (!rebuiltFont.ContainsGlyph('Z'))
                throw new InvalidDataException("Adding key text did not rebuild the converted font with the new glyph.");
            fontDocument.FontName = rebuilt.ConfigName;
            fontDocument.CustomFontMetadataPath = rebuilt.MetadataPath;
            fontDocument.CustomFontTexturePath = rebuilt.TexturePath;
            string customFontArchive = Path.Combine(outputDirectory, "keyboard-studio-custom-font-mod.zip");
            Mo2ModExporter.Export(customFontArchive, fontDocument);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(customFontArchive))
            {
                if (archive.GetEntry("root/OCUKeyboardFont.sfn") is null
                    || archive.GetEntry("root/OCUKeyboardFont.png") is null)
                    throw new InvalidDataException("Custom font export did not carry its generated SFN and texture atlas.");
            }

            KeyboardDocument keyOnlyBreathing = loaded.Clone();
            keyOnlyBreathing.KeyPlatesEnabled = true;
            keyOnlyBreathing.FontBreatheEnabled = false;
            keyOnlyBreathing.BackgroundBreatheEnabled = false;
            keyOnlyBreathing.ControlArrowBreatheEnabled = false;
            foreach (KeyboardSprite sprite in keyOnlyBreathing.Sprites)
                sprite.BreatheEnabled = false;
            renderer.AnimationTimeSeconds = 0;
            using Bitmap keyFrameA = renderer.Render(keyOnlyBreathing);
            renderer.AnimationTimeSeconds = 1.25;
            using Bitmap keyFrameB = renderer.Render(keyOnlyBreathing);
            using var keyBytesA = new MemoryStream();
            using var keyBytesB = new MemoryStream();
            keyFrameA.Save(keyBytesA, System.Drawing.Imaging.ImageFormat.Png);
            keyFrameB.Save(keyBytesB, System.Drawing.Imaging.ImageFormat.Png);
            if (keyBytesA.ToArray().SequenceEqual(keyBytesB.ToArray()))
                throw new InvalidDataException("Breathing key glow did not change the rendered keyboard frame.");

            KeyboardDocument fontOnlyBreathing = loaded.Clone();
            fontOnlyBreathing.KeyBreatheEnabled = false;
            fontOnlyBreathing.BackgroundBreatheEnabled = false;
            fontOnlyBreathing.ControlArrowBreatheEnabled = false;
            foreach (KeyboardSprite sprite in fontOnlyBreathing.Sprites)
                sprite.BreatheEnabled = false;
            renderer.AnimationTimeSeconds = 0;
            using Bitmap fontFrameA = renderer.Render(fontOnlyBreathing);
            renderer.AnimationTimeSeconds = 1.75;
            using Bitmap fontFrameB = renderer.Render(fontOnlyBreathing);
            using var fontBytesA = new MemoryStream();
            using var fontBytesB = new MemoryStream();
            fontFrameA.Save(fontBytesA, System.Drawing.Imaging.ImageFormat.Png);
            fontFrameB.Save(fontBytesB, System.Drawing.Imaging.ImageFormat.Png);
            if (fontBytesA.ToArray().SequenceEqual(fontBytesB.ToArray()))
                throw new InvalidDataException("Breathing font glow did not change the rendered keyboard frame.");

            var animationWatch = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 0; frame < 20; frame++)
            {
                renderer.AnimationTimeSeconds = frame / 20.0;
                using Bitmap animationFrame = renderer.Render(loaded);
            }
            animationWatch.Stop();
            double averageAnimationFrameMs = animationWatch.Elapsed.TotalMilliseconds / 20.0;

            string jpegBackground = Path.Combine(outputDirectory, "self-test-background.jpg");
            using (var sourceBackground = new Bitmap(Path.Combine(assets, "skyui-bg.png")))
                sourceBackground.Save(jpegBackground, System.Drawing.Imaging.ImageFormat.Jpeg);
            loaded.BackgroundImagePath = jpegBackground;

            string modArchive = Path.Combine(outputDirectory, "keyboard-studio-mo2-mod.zip");
            Mo2ModExporter.Export(modArchive, loaded);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(modArchive))
            {
                if (archive.GetEntry("root/OCUKeyboard.kb") is null
                    || archive.GetEntry("root/OCUKeyboardBackground.png") is null
                    || archive.GetEntry("root/OCUKeyboardSprite01.png") is null
                    || archive.GetEntry("root/OCUKeyboardSprite02.png") is null
                    || archive.GetEntry("root/OCUKeyboardControlArrow.png") is null
                    || archive.GetEntry("OCU Keyboard README.txt") is null)
                    throw new InvalidDataException("MO2 archive is missing its layout, artwork, or README entry.");
                using Stream background = archive.GetEntry("root/OCUKeyboardBackground.png")!.Open();
                byte[] signature = new byte[8];
                if (background.Read(signature, 0, signature.Length) != signature.Length
                    || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new InvalidDataException("JPEG background was not transcoded to a real PNG in the MO2 archive.");
            }

            File.WriteAllText(Path.Combine(outputDirectory, "keyboard-studio-self-test.txt"),
                $"PASS\nKeys={loaded.Keys.Count}\nTheme=modern_green\nFont=ocu_nordic\nPreview=1024x560\nCustomStyle=True\nKeyPlates=False\nPlateLayers=Fill+OutlineRing+OutsideGlowIndependent\nParchmentRibbon=Selectable+Movable+Resizable+Removable\nOcuTarget=ContainingModRootAutoDetected\nTopPlates=ModeLock+InputBarIndependent\nArtwork=MovableBackground+2Sprites+ControlArrow\nControls=NestedSelectableBoxes\nControlFontHit=PaintedGlyphPixelsOnly\nArrowKeys=IndependentMove+Resize\nTopBar=TextBar+Mode+LockMovable\nHistory=NoOpFiltered+VisualChangesDetected\nBackgroundFeather=RoundedPillBoundary\nHighDpiArtwork=PixelSized\nTextEffects=OutlineColor+FontGlow+IndependentBreathing\nTTFImport=ExistingKeysOnly+NewKeyRebuild\nCustomFontExport=SFN+PNG\nBreathing=Keys+Font+Sprite+Arrow\nAnimationPreviewAverageMs={averageAnimationFrameMs:F2}\nSampling=PremultipliedBilinear\nJpegBackground=TranscodedToPng\nMO2Archive=root/OCUKeyboard.kb\n");
            return 0;
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "keyboard-studio-self-test.txt"), exception.ToString());
            return 1;
        }
    }
}
