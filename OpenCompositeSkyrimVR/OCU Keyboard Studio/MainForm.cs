using System.Globalization;

namespace OCUKeyboardStudio;

internal sealed record KeyboardDesignChoice(string Name, string Path, bool BuiltIn)
{
    public override string ToString() => BuiltIn ? $"{Name} (Built-in)" : Name;
}

internal sealed class MainForm : Form
{
    private static readonly Color Surface = StudioTheme.Window;
    private static readonly Color SurfaceRaised = StudioTheme.Surface;
    private static readonly Color Edge = StudioTheme.Border;
    private static readonly Color Accent = StudioTheme.KeyGlow;
    private static readonly Color AccentBright = StudioTheme.KeyGlowBright;
    private static readonly Color TextPrimary = StudioTheme.TextPrimary;
    private static readonly Color TextMuted = StudioTheme.TextMuted;

    private readonly KeyboardCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly ModernTabControl _inspectorTabs = new();
    private readonly ComboBox _themeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _fontCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _designCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _stateCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ModernCheckBox _pressedCheck = new() { Text = "Pressed", AutoSize = true };
    private readonly ModernCheckBox _snapCheck = new() { Text = "Snap", AutoSize = true, Checked = true };
    private readonly Label _statusLabel = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _historyLabel = new() { AutoSize = false, Dock = DockStyle.Right, Width = 190, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _selectedLabel = new() { AutoSize = true };
    private readonly TextBox _baseLabel = new();
    private readonly TextBox _shiftLabel = new();
    private readonly ModernPillButton _assignedKeyButton = new() { Text = "Select a key first", AutoSize = false, Height = 34 };
    private readonly NumericUpDown _x = NumberBox(-100, 100, 2, 0.05m);
    private readonly NumericUpDown _y = NumberBox(-100, 100, 2, 0.05m);
    private readonly NumericUpDown _width = NumberBox(0.25m, 30, 2, 0.05m);
    private readonly NumericUpDown _height = NumberBox(0.25m, 20, 2, 0.05m);
    private readonly NumericUpDown _offsetX = NumberBox(-250, 250, 1, 1);
    private readonly NumericUpDown _offsetY = NumberBox(-250, 250, 1, 1);
    private readonly NumericUpDown _scale = NumberBox(0.25m, 3, 2, 0.05m);
    private readonly NumericUpDown _layoutWidth = NumberBox(5, 30, 0, 1);
    private readonly Label _topElementLabel = new() { AutoSize = true };
    private readonly NumericUpDown _topElementX = NumberBox(-1024, 1024, 0, 1);
    private readonly NumericUpDown _topElementY = NumberBox(-560, 560, 0, 1);
    private readonly NumericUpDown _topElementWidth = NumberBox(8, 2048, 0, 1);
    private readonly NumericUpDown _topElementHeight = NumberBox(8, 1120, 0, 1);
    private readonly NumericUpDown _topElementFontScale = NumberBox(0.2m, 3, 2, 0.05m);
    private readonly ModernCheckBox _customStyle = new() { Text = "Use custom keyboard colors", AutoSize = true };
    private readonly ModernCheckBox _keyPlatesEnabled = new() { Text = "Show key plates", AutoSize = true };
    private readonly ModernCheckBox _topButtonPlatesEnabled = new() { Text = "Show PC/VR Mode + Lock plates", AutoSize = true };
    private readonly ModernCheckBox _inputBarPlateEnabled = new() { Text = "Show input bar plate", AutoSize = true };
    private readonly ModernCheckBox _parchmentRibbonEnabled = new() { Text = "Show Parchment spacebar ribbon", AutoSize = true };
    private readonly ModernCheckBox _glowEnabled = new() { Text = "Plate outline glow", AutoSize = true };
    private readonly ModernCheckBox _hoverEnabled = new() { Text = "Hover effect", AutoSize = true };
    private readonly ModernCheckBox _outlineEnabled = new() { Text = "Font outline", AutoSize = true };
    private readonly Button _fontColor = SwatchButton();
    private readonly Button _fontOutlineColor = SwatchButton();
    private readonly Button _fontGlowColor = SwatchButton();
    private readonly Button _keyColor = SwatchButton();
    private readonly Button _plateFillColor = SwatchButton();
    private readonly Button _glowColor = SwatchButton();
    private readonly Button _hoverColor = SwatchButton();
    private readonly NumericUpDown _glowStrength = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _glowRadius = NumberBox(1, 8, 0, 1);
    private readonly NumericUpDown _hoverStrength = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _keyRoundness = NumberBox(0, 24, 0, 1);
    private readonly NumericUpDown _plateOutlineWidth = NumberBox(0, 8, 0, 1);
    private readonly ModernCheckBox _keyBreathe = new() { Text = "Breathing plate glow", AutoSize = true };
    private readonly NumericUpDown _keyBreatheMin = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _keyBreathePeriod = NumberBox(0.5m, 10, 2, 0.25m);
    private readonly NumericUpDown _keyBreathePhase = NumberBox(0, 360, 0, 15);
    private readonly ModernCheckBox _fontGlowEnabled = new() { Text = "Font glow", AutoSize = true };
    private readonly NumericUpDown _fontGlowStrength = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _fontGlowRadius = NumberBox(1, 8, 0, 1);
    private readonly ModernCheckBox _fontBreathe = new() { Text = "Breathing font glow", AutoSize = true };
    private readonly NumericUpDown _fontBreatheMin = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _fontBreathePeriod = NumberBox(0.5m, 10, 2, 0.25m);
    private readonly NumericUpDown _fontBreathePhase = NumberBox(0, 360, 0, 15);
    private readonly Label _backgroundName = new() { AutoSize = true, MaximumSize = new Size(280, 0) };
    private readonly Label _overlayName = new() { AutoSize = true, MaximumSize = new Size(280, 0) };
    private readonly ListBox _spriteList = new() { Height = 105, Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly NumericUpDown _backgroundX = NumberBox(-2048, 2048, 0, 1);
    private readonly NumericUpDown _backgroundY = NumberBox(-1120, 1120, 0, 1);
    private readonly NumericUpDown _backgroundWidth = NumberBox(1, 4096, 0, 1);
    private readonly NumericUpDown _backgroundHeight = NumberBox(1, 2240, 0, 1);
    private readonly NumericUpDown _backgroundOpacity = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _backgroundFade = NumberBox(0, 300, 0, 2);
    private readonly NumericUpDown _backgroundRotation = NumberBox(-360, 360, 0, 5);
    private readonly NumericUpDown _backgroundRoundness = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _overlayX = NumberBox(-1024, 2048, 0, 1);
    private readonly NumericUpDown _overlayY = NumberBox(-560, 1120, 0, 1);
    private readonly NumericUpDown _overlayWidth = NumberBox(1, 2048, 0, 1);
    private readonly NumericUpDown _overlayHeight = NumberBox(1, 1120, 0, 1);
    private readonly NumericUpDown _overlayOpacity = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _spriteFade = NumberBox(0, 300, 0, 2);
    private readonly NumericUpDown _spriteRotation = NumberBox(-360, 360, 0, 5);
    private readonly ModernCheckBox _spriteGlow = new() { Text = "Procedural glow", AutoSize = true };
    private readonly Button _spriteGlowColor = SwatchButton();
    private readonly NumericUpDown _spriteGlowStrength = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _spriteGlowRadius = NumberBox(1, 48, 0, 1);
    private readonly ModernCheckBox _spriteBreathe = new() { Text = "Breathing glow", AutoSize = true };
    private readonly NumericUpDown _spriteBreatheMin = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _spriteBreathePeriod = NumberBox(0.5m, 10, 2, 0.25m);
    private readonly NumericUpDown _spriteBreathePhase = NumberBox(0, 360, 0, 15);
    private readonly ComboBox _controlCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _controlPartLabel = new() { AutoSize = true };
    private readonly NumericUpDown _controlX = NumberBox(-1024, 1024, 0, 1);
    private readonly NumericUpDown _controlY = NumberBox(-560, 560, 0, 1);
    private readonly NumericUpDown _controlWidth = NumberBox(24, 600, 1, 1);
    private readonly NumericUpDown _controlHeight = NumberBox(40, 560, 1, 1);
    private readonly NumericUpDown _controlPartX = NumberBox(-500, 500, 1, 1);
    private readonly NumericUpDown _controlPartY = NumberBox(-500, 500, 1, 1);
    private readonly NumericUpDown _controlPartWidth = NumberBox(4, 400, 1, 1);
    private readonly NumericUpDown _controlPartHeight = NumberBox(4, 400, 1, 1);
    private readonly NumericUpDown _controlPartScale = NumberBox(0.2m, 3, 2, 0.05m);
    private readonly Label _controlArrowName = new() { AutoSize = true, MaximumSize = new Size(280, 0) };
    private readonly NumericUpDown _controlArrowRotation = NumberBox(-360, 360, 0, 5);
    private readonly ModernCheckBox _controlArrowGlow = new() { Text = "Procedural arrow glow", AutoSize = true };
    private readonly Button _controlArrowGlowColor = SwatchButton();
    private readonly NumericUpDown _controlArrowGlowStrength = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _controlArrowGlowRadius = NumberBox(1, 48, 0, 1);
    private readonly ModernCheckBox _controlArrowBreathe = new() { Text = "Breathing arrow glow", AutoSize = true };
    private readonly NumericUpDown _controlArrowBreatheMin = NumberBox(0, 100, 0, 5);
    private readonly NumericUpDown _controlArrowBreathePeriod = NumberBox(0.5m, 10, 2, 0.25m);
    private readonly NumericUpDown _controlArrowBreathePhase = NumberBox(0, 360, 0, 15);
    private readonly Stack<KeyboardDocument> _undo = [];
    private readonly Stack<KeyboardDocument> _redo = [];
    private readonly List<FontChoice> _fonts = [];
    private readonly List<KeyboardDesignChoice> _designs = [];
    private readonly ToolTip _toolTip = new();
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _saveButton = null!;
    private Button _installButton = null!;

    private KeyboardDocument _document = new();
    private KeyboardRenderer? _renderer;
    private KeyboardDocument? _pendingCanvasUndo;
    private bool _updatingEditor;
    private bool _updatingDesignLibrary;
    private string _assetsDirectory = "";
    private string? _hostOcuRoot;
    private string? _currentPackagePath;
    private bool _capturingAssignedKey;

    private static string DesignLibraryDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenCompositeUnleashed", "KeyboardStudio", "Designs");
    private static string DesignRegistryPath => Path.Combine(DesignLibraryDirectory, "design-library.txt");

    public MainForm(string? requestedOcuRoot = null, string? keyboardFile = null)
    {
        _hostOcuRoot = ResolveSelectedOcuRoot(requestedOcuRoot)
            ?? FindOcuRootFromStudioDirectory(AppContext.BaseDirectory);
        Text = "OCU Keyboard Studio";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1540, 900);
        BackColor = Surface;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StudioTheme.EnableDarkWindowChrome(this);

        Controls.Add(BuildMainLayout());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildStatusBar());

        DiscoverAssets();
        WireEvents();
        _toolTip.SetToolTip(_canvas,
            "When text is selected, the mouse wheel resizes that font anywhere on the canvas. Select a plate, artwork, background, or empty canvas to wheel-zoom the keyboard. Middle-drag pans while zoomed.");
        LoadDefaultLayout();
        if (!string.IsNullOrWhiteSpace(keyboardFile))
            LoadKeyboardFile(keyboardFile);
        UpdateTitle();
    }

    private Control BuildToolbar()
    {
        EnsureHistoryButtons();
        _saveButton = ActionButton("Save", (_, _) => SaveLayout(false));
        _installButton = ActionButton("Install for Next Launch", (_, _) => InstallToOcu());
        _toolTip.SetToolTip(_installButton,
            "Writes this keyboard into the OCU installation that opened Studio. Restart Skyrim VR to load it.");
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 98,
            Padding = new Padding(12, 7, 8, 5),
            BackColor = SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty
        };
        actions.Controls.AddRange([
            ActionButton("Open / Import", (_, _) => OpenLayout()),
            _saveButton,
            ActionButton("Save As", (_, _) => SaveLayout(true)),
            ActionButton("Export PNG", (_, _) => ExportPng()),
            ActionButton("Export MO2 Mod", (_, _) => ExportMo2Mod()),
            _installButton
        ]);

        var design = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty
        };
        Label baseThemeCaption = Caption("Base Theme");
        const string baseThemeHelp = "The built-in visual foundation: fallback background/colors, plate treatment, Parchment ribbon eligibility, and theme-specific top-button offsets. A custom background and custom colors override most of its visible appearance.";
        _toolTip.SetToolTip(baseThemeCaption, baseThemeHelp);
        _toolTip.SetToolTip(_themeCombo, baseThemeHelp);
        design.Controls.AddRange([
            Caption("Keyboard Design"), _designCombo,
            Spacer(12),
            baseThemeCaption, _themeCombo,
            Caption("Font"), _fontCombo, ActionButton("Add TTF", (_, _) => ImportFont(), compact: true),
            Caption("State"), _stateCombo,
            _pressedCheck,
            _snapCheck
        ]);

        _designCombo.Width = 245;
        _themeCombo.Width = 145;
        _fontCombo.Width = 185;
        _stateCombo.Width = 90;
        StyleCombo(_designCombo);
        StyleCombo(_themeCombo);
        StyleCombo(_fontCombo);
        StyleCombo(_stateCombo);
        StyleCheck(_pressedCheck);
        StyleCheck(_snapCheck);
        toolbar.Controls.Add(actions, 0, 0);
        toolbar.Controls.Add(design, 0, 1);
        return toolbar;
    }

    private Control BuildMainLayout()
    {
        EnsureHistoryButtons();
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1510, 800),
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6,
            BackColor = Edge,
            Panel1MinSize = 700,
            Panel2MinSize = 440,
            SplitterDistance = 1045
        };
        split.Panel1.BackColor = Color.FromArgb(9, 11, 15);
        split.Panel1.Padding = new Padding(12);
        var canvasLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 2
        };
        canvasLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        canvasLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        canvasLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var historyButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 4, 3),
            BackColor = Color.FromArgb(9, 11, 15),
            ColumnCount = 3,
            RowCount = 1
        };
        historyButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        historyButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        _undoButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _redoButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        historyButtons.Controls.Add(_undoButton, 1, 0);
        historyButtons.Controls.Add(_redoButton, 2, 0);
        canvasLayout.Controls.Add(_canvas, 0, 0);
        canvasLayout.Controls.Add(historyButtons, 0, 1);
        split.Panel1.Controls.Add(canvasLayout);
        split.Panel2.BackColor = Surface;
        split.Panel2.Padding = new Padding(12);
        split.Panel2.Controls.Add(BuildInspector());
        return split;
    }

    private void EnsureHistoryButtons()
    {
        _undoButton ??= ActionButton("Undo", (_, _) => Undo(), compact: true);
        _redoButton ??= ActionButton("Redo", (_, _) => Redo(), compact: true);
    }

    private Control BuildInspector()
    {
        _inspectorTabs.Dock = DockStyle.Fill;
        TabPage keyPage = InspectorPage("Key");
        TabPage appearancePage = InspectorPage("Appearance");
        TabPage artworkPage = InspectorPage("Artwork");
        TabPage controlsPage = InspectorPage("Controls");
        keyPage.Controls.Add(BuildKeyInspector());
        appearancePage.Controls.Add(BuildAppearanceInspector());
        artworkPage.Controls.Add(BuildArtworkInspector());
        controlsPage.Controls.Add(BuildControlsInspector());
        _inspectorTabs.TabPages.AddRange([keyPage, appearancePage, artworkPage, controlsPage]);
        return _inspectorTabs;
    }

    private Control BuildKeyInspector()
    {
        var inspector = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(2),
            BackColor = Surface
        };
        inspector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        inspector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSection(inspector, "DIRECT EDITING");
        AddHint(inspector, "Click directly on a letter or symbol to move its content. Click the empty part of its plate to move the whole key. Drag plate handles to resize; mouse wheel resizes selected content. The text bar, PC/VR Mode, and Lock are also directly draggable.");

        AddSection(inspector, "SELECTED KEY");
        AddWide(inspector, _selectedLabel);
        AddRow(inspector, "Assigned key", _assignedKeyButton);
        AddHint(inspector, "Click Assigned key, then press the real key on your physical keyboard. Studio translates its normal and Shift values using your Windows keyboard layout.");
        AddRow(inspector, "Key text", _baseLabel);
        AddRow(inspector, "Shift text", _shiftLabel);
        AddRow(inspector, "Key X", _x);
        AddRow(inspector, "Key Y", _y);
        AddRow(inspector, "Key width", _width);
        AddRow(inspector, "Key height", _height);
        AddRow(inspector, "Font X px", _offsetX);
        AddRow(inspector, "Font Y px", _offsetY);
        AddRow(inspector, "Font size / scale", _scale);

        var keyActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        keyActions.Controls.Add(ActionButton("Duplicate", (_, _) => DuplicateKey(), compact: true));
        keyActions.Controls.Add(ActionButton("Delete", (_, _) => DeleteKey(), compact: true));
        AddWide(inspector, keyActions);

        AddSection(inspector, "SELECTED TOP-BAR ELEMENT");
        AddWide(inspector, _topElementLabel);
        AddRow(inspector, "Offset X", _topElementX);
        AddRow(inspector, "Offset Y", _topElementY);
        AddRow(inspector, "Plate width px", _topElementWidth);
        AddRow(inspector, "Plate height px", _topElementHeight);
        AddRow(inspector, "Font size / scale", _topElementFontScale);
        AddHint(inspector, "Drag visible plate edges to resize. The mouse wheel changes its font size. Saved geometry also moves and resizes the in-game laser hit area.");

        AddSection(inspector, "GRID COLUMN COUNT");
        AddRow(inspector, "Columns across", _layoutWidth);
        AddHint(inspector, "This controls density, not pixel width: 14 columns makes larger keys than 15 because the same 784 px area is divided into fewer cells.");

        return inspector;
    }

    private Control BuildAppearanceInspector()
    {
        var inspector = InspectorTable();
        AddSection(inspector, "INTERACTION / SELECTION");
        StyleCheck(_keyPlatesEnabled);
        StyleCheck(_topButtonPlatesEnabled);
        StyleCheck(_inputBarPlateEnabled);
        StyleCheck(_parchmentRibbonEnabled);
        AddWide(inspector, _keyPlatesEnabled);
        AddWide(inspector, _topButtonPlatesEnabled);
        AddWide(inspector, _inputBarPlateEnabled);
        AddWide(inspector, _parchmentRibbonEnabled);
        AddHint(inspector, "Visible plates can be selected anywhere inside their shape. Hidden plates are click-through in Studio; select their visible text or artwork instead. Hiding a plate changes the look, not the key assigned to that location in game.");

        AddSection(inspector, "CUSTOM COLORS");
        StyleCheck(_customStyle);
        AddWide(inspector, _customStyle);
        AddHint(inspector, "Each swatch opens a real hue/saturation wheel. These values are stored in the keyboard layout and reproduced by OCU.");
        AddRow(inspector, "Font", _fontColor);
        AddRow(inspector, "Font outline", _fontOutlineColor);
        AddRow(inspector, "Font glow", _fontGlowColor);
        AddRow(inspector, "Plate fill", _plateFillColor);
        AddRow(inspector, "Plate outline", _keyColor);
        AddRow(inspector, "Outline width", _plateOutlineWidth);
        AddRow(inspector, "Plate glow", _glowColor);
        AddRow(inspector, "Hover", _hoverColor);

        AddSection(inspector, "EFFECTS");
        StyleCheck(_glowEnabled);
        StyleCheck(_hoverEnabled);
        StyleCheck(_outlineEnabled);
        StyleCheck(_fontGlowEnabled);
        StyleCheck(_fontBreathe);
        AddWide(inspector, _glowEnabled);
        AddRow(inspector, "Plate glow strength", _glowStrength);
        AddRow(inspector, "Plate glow radius", _glowRadius);
        StyleCheck(_keyBreathe);
        AddWide(inspector, _keyBreathe);
        AddRow(inspector, "Plate minimum %", _keyBreatheMin);
        AddRow(inspector, "Plate period seconds", _keyBreathePeriod);
        AddRow(inspector, "Plate phase degrees", _keyBreathePhase);
        AddWide(inspector, _fontGlowEnabled);
        AddRow(inspector, "Font glow strength", _fontGlowStrength);
        AddRow(inspector, "Font glow radius", _fontGlowRadius);
        AddWide(inspector, _fontBreathe);
        AddRow(inspector, "Font minimum %", _fontBreatheMin);
        AddRow(inspector, "Font period seconds", _fontBreathePeriod);
        AddRow(inspector, "Font phase degrees", _fontBreathePhase);
        AddWide(inspector, _hoverEnabled);
        AddRow(inspector, "Hover strength", _hoverStrength);
        AddWide(inspector, _outlineEnabled);
        AddRow(inspector, "Key roundness", _keyRoundness);
        AddHint(inspector, "Font and plate glows have independent colors and breathing cycles. Their base font and outline stay readable while the halo breathes.");
        return inspector;
    }

    private Control BuildArtworkInspector()
    {
        var inspector = InspectorTable();
        AddSection(inspector, "BACKGROUND IMAGE");
        var backgroundActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        backgroundActions.Controls.Add(ActionButton("Choose image", (_, _) => ChooseBackground(), compact: true));
        backgroundActions.Controls.Add(ActionButton("Clear", (_, _) => ClearBackground(), compact: true));
        AddWide(inspector, backgroundActions);
        _backgroundName.ForeColor = TextMuted;
        AddWide(inspector, _backgroundName);
        AddRow(inspector, "X", _backgroundX);
        AddRow(inspector, "Y", _backgroundY);
        AddRow(inspector, "Width", _backgroundWidth);
        AddRow(inspector, "Height", _backgroundHeight);
        AddRow(inspector, "Opacity", _backgroundOpacity);
        AddRow(inspector, "Edge fade px", _backgroundFade);
        AddRow(inspector, "Rotation deg", _backgroundRotation);
        AddRow(inspector, "Corner roundness %", _backgroundRoundness);
        AddHint(inspector, "Click the background in the preview, then drag it, use its edge/corner handles, or turn it into a pill with Corner roundness. JPEG is background-only and converted to PNG on save/export.");

        AddSection(inspector, "SPRITES / RIBBONS");
        var overlayActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        overlayActions.Controls.Add(ActionButton("Add PNG", (_, _) => ChooseOverlay(), compact: true));
        overlayActions.Controls.Add(ActionButton("Add Parchment Ribbon", (_, _) => AddParchmentRibbon(), compact: true));
        overlayActions.Controls.Add(ActionButton("Remove", (_, _) => ClearOverlay(), compact: true));
        AddWide(inspector, overlayActions);
        _spriteList.BackColor = StudioTheme.Input;
        _spriteList.ForeColor = TextPrimary;
        _spriteList.BorderStyle = BorderStyle.FixedSingle;
        AddWide(inspector, _spriteList);
        var orderActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        orderActions.Controls.Add(ActionButton("Send back", (_, _) => MoveSpriteLayer(-1), compact: true));
        orderActions.Controls.Add(ActionButton("Bring front", (_, _) => MoveSpriteLayer(1), compact: true));
        AddWide(inspector, orderActions);
        _overlayName.ForeColor = TextMuted;
        AddWide(inspector, _overlayName);

        AddRow(inspector, "Sprite X", _overlayX);
        AddRow(inspector, "Sprite Y", _overlayY);
        AddRow(inspector, "Sprite width", _overlayWidth);
        AddRow(inspector, "Sprite height", _overlayHeight);
        AddRow(inspector, "Opacity", _overlayOpacity);
        AddRow(inspector, "Edge fade px", _spriteFade);
        AddRow(inspector, "Rotation deg", _spriteRotation);
        StyleCheck(_spriteGlow);
        AddWide(inspector, _spriteGlow);
        AddRow(inspector, "Glow color", _spriteGlowColor);
        AddRow(inspector, "Glow strength", _spriteGlowStrength);
        AddRow(inspector, "Glow radius px", _spriteGlowRadius);
        StyleCheck(_spriteBreathe);
        AddWide(inspector, _spriteBreathe);
        AddRow(inspector, "Breathe min %", _spriteBreatheMin);
        AddRow(inspector, "Period seconds", _spriteBreathePeriod);
        AddRow(inspector, "Phase degrees", _spriteBreathePhase);
        AddHint(inspector, "Drop, paste, or right-click transparent PNGs. Drag the image itself; drag corners or edges to resize; use the circle above it to rotate. Glow has its own color wheel and can breathe without fading the PNG.");
        return inspector;
    }

    private Control BuildControlsInspector()
    {
        var inspector = InspectorTable();
        AddSection(inspector, "IN-GAME SIDE CONTROLS");
        _controlCombo.Items.AddRange(Enum.GetNames<KeyboardRuntimeControl>());
        _controlCombo.SelectedIndex = 0;
        StyleCombo(_controlCombo);
        AddRow(inspector, "Control", _controlCombo);
        AddRow(inspector, "Selected part", _controlPartLabel);
        AddRow(inspector, "Offset X", _controlX);
        AddRow(inspector, "Offset Y", _controlY);
        AddRow(inspector, "Box width", _controlWidth);
        AddRow(inspector, "Box height", _controlHeight);
        AddSection(inspector, "SELECTED CHILD");
        AddRow(inspector, "Child X", _controlPartX);
        AddRow(inspector, "Child Y", _controlPartY);
        AddRow(inspector, "Child width", _controlPartWidth);
        AddRow(inspector, "Child height", _controlPartHeight);
        AddRow(inspector, "Text scale", _controlPartScale);
        AddSection(inspector, "ARROW SYMBOL");
        var arrowActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        arrowActions.Controls.Add(ActionButton("Choose PNG", (_, _) => ChooseControlArrow(), compact: true));
        arrowActions.Controls.Add(ActionButton("Use triangle", (_, _) => ClearControlArrow(), compact: true));
        AddWide(inspector, arrowActions);
        _controlArrowName.ForeColor = TextMuted;
        AddWide(inspector, _controlArrowName);
        AddRow(inspector, "Rotation deg", _controlArrowRotation);
        StyleCheck(_controlArrowGlow);
        AddWide(inspector, _controlArrowGlow);
        AddRow(inspector, "Glow color", _controlArrowGlowColor);
        AddRow(inspector, "Glow strength", _controlArrowGlowStrength);
        AddRow(inspector, "Glow radius px", _controlArrowGlowRadius);
        StyleCheck(_controlArrowBreathe);
        AddWide(inspector, _controlArrowBreathe);
        AddRow(inspector, "Breathe min %", _controlArrowBreatheMin);
        AddRow(inspector, "Period seconds", _controlArrowBreathePeriod);
        AddRow(inspector, "Phase degrees", _controlArrowBreathePhase);
        AddHint(inspector, "Click empty space inside a settings control to select its outer box; drag its handles to widen or resize the group. Click directly on an arrow, label glyph, or value glyph to select and move that child alone. Arrow children have their own resize handles. Procedural glow and breathing work with both the built-in triangles and replacement PNGs. Down reuses Up rotated 180 degrees.");
        return inspector;
    }

    private Control BuildStatusBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = SurfaceRaised,
            Padding = new Padding(12, 0, 12, 0),
            ColumnCount = 2,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        _statusLabel.ForeColor = TextMuted;
        _historyLabel.ForeColor = AccentBright;
        _historyLabel.Dock = DockStyle.Fill;
        panel.Controls.Add(_statusLabel, 0, 0);
        panel.Controls.Add(_historyLabel, 1, 0);
        return panel;
    }

    private void DiscoverAssets()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, "Assets");
        string sourceAssets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets"));
        _assetsDirectory = Directory.Exists(besideExe) ? besideExe : sourceAssets;
        if (!Directory.Exists(_assetsDirectory))
            throw new DirectoryNotFoundException("OCU Keyboard Studio could not find its Assets folder.");

        foreach (string metadata in Directory.EnumerateFiles(_assetsDirectory, "*.sfn").OrderBy(Path.GetFileName))
        {
            string stem = Path.GetFileNameWithoutExtension(metadata);
            string texture = Path.Combine(_assetsDirectory, stem + "-texture.png");
            if (!File.Exists(texture))
                continue;
            string display = stem.EndsWith("-30", StringComparison.OrdinalIgnoreCase) ? stem[..^3] : stem;
            string configName = display.ToLowerInvariant() switch
            {
                "parchmentmf" => "parchment",
                "medievalsharp" => "medieval",
                "ocu-nordic" => "ocu_nordic",
                "ocu-unease" => "ocu_unease",
                _ => display.ToLowerInvariant().Replace('-', '_')
            };
            FontImportManifest? manifest = FontImporter.ReadManifest(_assetsDirectory, configName);
            string? sourceFont = manifest is null ? null : Path.Combine(_assetsDirectory, manifest.SourceFile);
            if (sourceFont is not null && !File.Exists(sourceFont)) sourceFont = null;
            _fonts.Add(new FontChoice(manifest?.DisplayName ?? display, metadata, texture, configName, sourceFont));
        }
        if (_fonts.Count == 0)
            throw new InvalidDataException("No matching .sfn and -texture.png font pairs were found.");

        _fontCombo.Items.AddRange(_fonts.Cast<object>().ToArray());
        _themeCombo.Items.AddRange(KeyboardTheme.BuiltIns.Cast<object>().ToArray());
        _stateCombo.Items.AddRange(Enum.GetNames<KeyboardPreviewState>());
        int parchmentTheme = KeyboardTheme.BuiltIns.ToList().FindIndex(theme => ThemeConfigName(theme) == "parchment");
        _themeCombo.SelectedIndex = parchmentTheme >= 0 ? parchmentTheme : 0;
        int parchmentFont = _fonts.FindIndex(font => font.ConfigName == "parchment");
        _fontCombo.SelectedIndex = parchmentFont >= 0 ? parchmentFont : 0;
        _stateCombo.SelectedIndex = 0;

        SudoFont font = LoadSelectedFont();
        _renderer = new KeyboardRenderer(_assetsDirectory, font)
        {
            Theme = (KeyboardTheme)_themeCombo.SelectedItem!
        };
        _canvas.Renderer = _renderer;
    }

    private void LoadDefaultLayout()
    {
        _currentPackagePath = null;
        string path = Path.Combine(_assetsDirectory, "en_gb.kb");
        if (!File.Exists(path))
            throw new FileNotFoundException("The bundled en_gb.kb layout is missing.", path);
        SetDocument(KeyboardDocument.Load(path), clearHistory: true);
        RefreshDesignChoices(path);
        SetStatus(_hostOcuRoot is null
            ? "Loaded the real OCU en_gb.kb layout. Drag a key label to start editing."
            : $"Loaded Parchment. Save applies directly to this OCU: {_hostOcuRoot}");
    }

    private void WireEvents()
    {
        _canvas.SelectionChanged += (_, _) =>
        {
            if (!_canvas.HasMultipleSelection)
                _inspectorTabs.SelectedIndex = _canvas.SelectionKind switch
                {
                    CanvasSelectionKind.Sprite or CanvasSelectionKind.Background => 2,
                    CanvasSelectionKind.Control or CanvasSelectionKind.ControlUpArrow or CanvasSelectionKind.ControlLabel
                        or CanvasSelectionKind.ControlValue or CanvasSelectionKind.ControlDownArrow => 3,
                    CanvasSelectionKind.KeyContent or CanvasSelectionKind.KeyPlate => 0,
                    CanvasSelectionKind.TopTextBar or CanvasSelectionKind.TopMode or CanvasSelectionKind.TopLock => 0,
                    _ => _inspectorTabs.SelectedIndex
                };
            if (_canvas.HasMultipleSelection)
            {
                SetStatus($"{_canvas.SelectionCount} visible items selected. Drag any outlined item to move the group; Ctrl/Shift-drag adds more.");
            }
            PopulateInspector();
            PopulateArtworkInspector();
            PopulateControlsInspector();
        };
        _canvas.EditStarted += (_, _) => _pendingCanvasUndo ??= _document.Clone();
        _canvas.ViewZoomChanged += (_, _) => SetStatus(
            $"Canvas zoom: {_canvas.ViewZoomPercent}%. Scroll over selected text to resize it; scroll elsewhere to zoom. Middle-drag pans.");
        _canvas.DocumentChanged += (_, _) =>
        {
            PopulateInspector();
            PopulateAppearanceInspector();
            PopulateArtworkInspector();
            PopulateControlsInspector();
            UpdateTitle();
        };
        _canvas.EditCompleted += (_, _) =>
        {
            if (_pendingCanvasUndo is null)
                return;
            if (!_pendingCanvasUndo.IsEquivalentForHistory(_document))
            {
                PushUndo(_pendingCanvasUndo);
                _redo.Clear();
            }
            else
            {
                // Pointer jitter can enter the drag path without changing any
                // authored value. Keep the prior dirty state and Redo chain.
                _document.IsDirty = _pendingCanvasUndo.IsDirty;
            }
            _pendingCanvasUndo = null;
            UpdateHistoryControls();
        };
        _canvas.ChooseControlArrowRequested += (_, _) => ChooseControlArrow();
        _canvas.UseBuiltInControlArrowRequested += (_, _) => ClearControlArrow();
        _canvas.KeyboardFileDropped += path =>
        {
            if (ConfirmDiscard())
                LoadKeyboardFile(path);
        };

        _designCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingDesignLibrary || _designCombo.SelectedItem is not KeyboardDesignChoice choice)
                return;
            string? currentPath = _document.SourcePath;
            if (!string.IsNullOrWhiteSpace(currentPath)
                && Path.GetFullPath(currentPath).Equals(Path.GetFullPath(choice.Path), StringComparison.OrdinalIgnoreCase))
                return;
            if (!ConfirmDiscard())
            {
                RefreshDesignChoices(currentPath);
                return;
            }
            try
            {
                _currentPackagePath = null;
                SetDocument(KeyboardDocument.Load(choice.Path), clearHistory: true);
                RefreshDesignChoices(choice.Path);
                SetStatus(_hostOcuRoot is null
                    ? $"Loaded keyboard design: {choice.Name}. Install for Next Launch when you want to activate it."
                    : $"Loaded keyboard design: {choice.Name}. Save applies it directly to {_hostOcuRoot}.");
            }
            catch (Exception exception)
            {
                RefreshDesignChoices(currentPath);
                ShowError("Keyboard design load failed", exception);
            }
        };

        _themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_renderer is null || _themeCombo.SelectedItem is not KeyboardTheme theme)
                return;
            _renderer.Theme = theme;
            if (!_updatingEditor)
            {
                string configName = ThemeConfigName(theme);
                if (_document.BaseTheme != configName)
                {
                    PushUndo();
                    _document.BaseTheme = configName;
                    MarkChanged();
                }
                SetStatus(_document.CustomStyleEnabled || !string.IsNullOrWhiteSpace(_document.BackgroundImagePath)
                    ? $"Base Theme: {theme.Name}. Custom colors/artwork override most theme visuals; its fallback styling and geometry remain."
                    : $"Base Theme: {theme.Name}. This supplies the keyboard's built-in background, colors, plate treatment, and offsets.");
            }
            _canvas.RefreshPreview();
            PopulateAppearanceInspector();
        };
        _fontCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_renderer is null)
                return;
            try
            {
                _renderer.Font = LoadSelectedFont();
                if (!_updatingEditor && _fontCombo.SelectedItem is FontChoice choice)
                {
                    string? metadata = choice.IsCustom ? choice.MetadataPath : null;
                    string? texture = choice.IsCustom ? choice.TexturePath : null;
                    if (_document.FontName != choice.ConfigName
                        || _document.CustomFontMetadataPath != metadata
                        || _document.CustomFontTexturePath != texture)
                    {
                        PushUndo();
                        _document.FontName = choice.ConfigName;
                        _document.CustomFontMetadataPath = metadata;
                        _document.CustomFontTexturePath = texture;
                        MarkChanged();
                    }
                    SetStatus($"Design font: {_fontCombo.SelectedItem}. Shared exports carry this exact selection.");
                }
                _canvas.RefreshPreview();
            }
            catch (Exception exception)
            {
                ShowError("Font load failed", exception);
            }
        };
        _stateCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_renderer is null)
                return;
            _renderer.State = (KeyboardPreviewState)_stateCombo.SelectedIndex;
            _canvas.RefreshPreview();
            PopulateInspector();
        };
        _pressedCheck.CheckedChanged += (_, _) =>
        {
            if (_renderer is not null)
                _renderer.Pressed = _pressedCheck.Checked;
            _canvas.RefreshPreview();
        };
        _snapCheck.CheckedChanged += (_, _) => _canvas.SnapToTenth = _snapCheck.Checked;
        _customStyle.CheckedChanged += (_, _) => ToggleCustomStyle();
        _keyPlatesEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.KeyPlatesEnabled = _keyPlatesEnabled.Checked);
        _topButtonPlatesEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.TopButtonPlatesEnabled = _topButtonPlatesEnabled.Checked);
        _inputBarPlateEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.InputBarPlateEnabled = _inputBarPlateEnabled.Checked);
        _parchmentRibbonEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.ParchmentRibbonEnabled = _parchmentRibbonEnabled.Checked);
        _glowEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.GlowEnabled = _glowEnabled.Checked);
        _fontGlowEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.FontGlowEnabled = _fontGlowEnabled.Checked);
        _hoverEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.HoverEnabled = _hoverEnabled.Checked);
        _outlineEnabled.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.OutlineEnabled = _outlineEnabled.Checked);
        _glowStrength.ValueChanged += (_, _) => ApplyDocumentChange(document => document.GlowStrength = (int)_glowStrength.Value);
        _glowRadius.ValueChanged += (_, _) => ApplyDocumentChange(document => document.GlowRadius = (int)_glowRadius.Value);
        _fontGlowStrength.ValueChanged += (_, _) => ApplyDocumentChange(document => document.FontGlowStrength = (int)_fontGlowStrength.Value);
        _fontGlowRadius.ValueChanged += (_, _) => ApplyDocumentChange(document => document.FontGlowRadius = (int)_fontGlowRadius.Value);
        _hoverStrength.ValueChanged += (_, _) => ApplyDocumentChange(document => document.HoverStrength = (int)_hoverStrength.Value);
        _keyRoundness.ValueChanged += (_, _) => ApplyDocumentChange(document => document.KeyRoundness = (int)_keyRoundness.Value);
        _plateOutlineWidth.ValueChanged += (_, _) => ApplyDocumentChange(document => document.PlateOutlineWidth = (int)_plateOutlineWidth.Value);
        _keyBreathe.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.KeyBreatheEnabled = _keyBreathe.Checked);
        _keyBreatheMin.ValueChanged += (_, _) => ApplyDocumentChange(document => document.KeyBreatheMinPercent = (int)_keyBreatheMin.Value);
        _keyBreathePeriod.ValueChanged += (_, _) => ApplyDocumentChange(document => document.KeyBreathePeriodSeconds = (float)_keyBreathePeriod.Value);
        _keyBreathePhase.ValueChanged += (_, _) => ApplyDocumentChange(document => document.KeyBreathePhaseDegrees = (float)_keyBreathePhase.Value);
        _fontBreathe.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.FontBreatheEnabled = _fontBreathe.Checked);
        _fontBreatheMin.ValueChanged += (_, _) => ApplyDocumentChange(document => document.FontBreatheMinPercent = (int)_fontBreatheMin.Value);
        _fontBreathePeriod.ValueChanged += (_, _) => ApplyDocumentChange(document => document.FontBreathePeriodSeconds = (float)_fontBreathePeriod.Value);
        _fontBreathePhase.ValueChanged += (_, _) => ApplyDocumentChange(document => document.FontBreathePhaseDegrees = (float)_fontBreathePhase.Value);
        _fontColor.Click += (_, _) => ChooseVisualColor("Font color", _document.FontColor, color => _document.FontColor = color);
        _fontOutlineColor.Click += (_, _) => ChooseVisualColor("Font outline color and transparency", _document.FontOutlineColor, color => _document.FontOutlineColor = color);
        _fontGlowColor.Click += (_, _) => ChooseVisualColor("Font glow color and transparency", _document.FontGlowColor, color => _document.FontGlowColor = color);
        _plateFillColor.Click += (_, _) => ChooseVisualColor("Plate fill color and transparency", _document.PlateFillColor, color => _document.PlateFillColor = color);
        _keyColor.Click += (_, _) => ChooseVisualColor("Plate outline color and transparency", _document.KeyColor, color => _document.KeyColor = color);
        _glowColor.Click += (_, _) => ChooseVisualColor("Glow color", _document.GlowColor, color => _document.GlowColor = color);
        _hoverColor.Click += (_, _) => ChooseVisualColor("Hover color", _document.HoverColor, color => _document.HoverColor = color);

        _backgroundX.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundX = (float)_backgroundX.Value);
        _backgroundY.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundY = (float)_backgroundY.Value);
        _backgroundWidth.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundWidth = (float)_backgroundWidth.Value);
        _backgroundHeight.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundHeight = (float)_backgroundHeight.Value);
        _backgroundOpacity.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundOpacity = (int)_backgroundOpacity.Value);
        _backgroundFade.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundEdgeFade = (int)_backgroundFade.Value);
        _backgroundRotation.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundRotation = (float)_backgroundRotation.Value);
        _backgroundRoundness.ValueChanged += (_, _) => ApplyDocumentChange(document => document.BackgroundRoundness = (int)_backgroundRoundness.Value);
        _overlayX.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.X = (float)_overlayX.Value);
        _overlayY.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.Y = (float)_overlayY.Value);
        _overlayWidth.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.Width = (float)_overlayWidth.Value);
        _overlayHeight.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.Height = (float)_overlayHeight.Value);
        _overlayOpacity.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.Opacity = (int)_overlayOpacity.Value);
        _spriteFade.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.EdgeFade = (int)_spriteFade.Value);
        _spriteRotation.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.Rotation = (float)_spriteRotation.Value);
        _spriteGlow.CheckedChanged += (_, _) => ApplySpriteChange(sprite => sprite.GlowEnabled = _spriteGlow.Checked);
        _spriteGlowColor.Click += (_, _) => ChooseSpriteGlowColor();
        _spriteGlowStrength.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.GlowStrength = (int)_spriteGlowStrength.Value);
        _spriteGlowRadius.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.GlowRadius = (int)_spriteGlowRadius.Value);
        _spriteBreathe.CheckedChanged += (_, _) => ApplySpriteChange(sprite => sprite.BreatheEnabled = _spriteBreathe.Checked);
        _spriteBreatheMin.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.BreatheMinPercent = (int)_spriteBreatheMin.Value);
        _spriteBreathePeriod.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.BreathePeriodSeconds = (float)_spriteBreathePeriod.Value);
        _spriteBreathePhase.ValueChanged += (_, _) => ApplySpriteChange(sprite => sprite.BreathePhaseDegrees = (float)_spriteBreathePhase.Value);
        _spriteList.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingEditor)
                return;
            _canvas.SelectSprite(_spriteList.SelectedItem as KeyboardSprite);
            PopulateArtworkInspector();
        };
        _controlCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingEditor || _controlCombo.SelectedIndex < 0)
                return;
            _canvas.SelectControl((KeyboardRuntimeControl)_controlCombo.SelectedIndex);
            PopulateControlsInspector();
            _canvas.RefreshPreview();
        };
        _controlX.ValueChanged += (_, _) => ApplyControlChange((float)_controlX.Value, (float)_controlY.Value);
        _controlY.ValueChanged += (_, _) => ApplyControlChange((float)_controlX.Value, (float)_controlY.Value);
        _controlWidth.ValueChanged += (_, _) => ApplyControlDesignChange(design => design.Width = (float)_controlWidth.Value);
        _controlHeight.ValueChanged += (_, _) => ApplyControlDesignChange(design => design.Height = (float)_controlHeight.Value);
        _controlPartX.ValueChanged += (_, _) => ApplySelectedControlPartChange((design, part) =>
        {
            if (part == KeyboardControlPart.UpArrow) design.UpOffsetX = (float)_controlPartX.Value;
            else if (part == KeyboardControlPart.DownArrow) design.DownOffsetX = (float)_controlPartX.Value;
            else if (part == KeyboardControlPart.Label) design.LabelOffsetX = (float)_controlPartX.Value;
            else if (part == KeyboardControlPart.Value) design.ValueOffsetX = (float)_controlPartX.Value;
        });
        _controlPartY.ValueChanged += (_, _) => ApplySelectedControlPartChange((design, part) =>
        {
            if (part == KeyboardControlPart.UpArrow) design.UpOffsetY = (float)_controlPartY.Value;
            else if (part == KeyboardControlPart.DownArrow) design.DownOffsetY = (float)_controlPartY.Value;
            else if (part == KeyboardControlPart.Label) design.LabelOffsetY = (float)_controlPartY.Value;
            else if (part == KeyboardControlPart.Value) design.ValueOffsetY = (float)_controlPartY.Value;
        });
        _controlPartWidth.ValueChanged += (_, _) => ApplySelectedControlPartChange((design, part) =>
        {
            if (part == KeyboardControlPart.UpArrow) design.UpWidth = (float)_controlPartWidth.Value;
            else if (part == KeyboardControlPart.DownArrow) design.DownWidth = (float)_controlPartWidth.Value;
        });
        _controlPartHeight.ValueChanged += (_, _) => ApplySelectedControlPartChange((design, part) =>
        {
            if (part == KeyboardControlPart.UpArrow) design.UpHeight = (float)_controlPartHeight.Value;
            else if (part == KeyboardControlPart.DownArrow) design.DownHeight = (float)_controlPartHeight.Value;
        });
        _controlPartScale.ValueChanged += (_, _) => ApplySelectedControlPartChange((design, part) =>
        {
            if (part == KeyboardControlPart.Label) design.LabelScale = (float)_controlPartScale.Value;
            else if (part == KeyboardControlPart.Value) design.ValueScale = (float)_controlPartScale.Value;
        });
        _controlArrowRotation.ValueChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowRotation = (float)_controlArrowRotation.Value);
        _controlArrowGlow.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowGlowEnabled = _controlArrowGlow.Checked);
        _controlArrowGlowColor.Click += (_, _) => ChooseControlArrowGlowColor();
        _controlArrowGlowStrength.ValueChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowGlowStrength = (int)_controlArrowGlowStrength.Value);
        _controlArrowGlowRadius.ValueChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowGlowRadius = (int)_controlArrowGlowRadius.Value);
        _controlArrowBreathe.CheckedChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowBreatheEnabled = _controlArrowBreathe.Checked);
        _controlArrowBreatheMin.ValueChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowBreatheMinPercent = (int)_controlArrowBreatheMin.Value);
        _controlArrowBreathePeriod.ValueChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowBreathePeriodSeconds = (float)_controlArrowBreathePeriod.Value);
        _controlArrowBreathePhase.ValueChanged += (_, _) => ApplyDocumentChange(document => document.ControlArrowBreathePhaseDegrees = (float)_controlArrowBreathePhase.Value);

        _baseLabel.TextChanged += (_, _) => ApplyInspectorChange(key => key.Label = _baseLabel.Text);
        _shiftLabel.TextChanged += (_, _) => ApplyInspectorChange(key => key.ShiftLabel = _shiftLabel.Text);
        _baseLabel.Leave += (_, _) => AuditSelectedCustomFont(showMessage: true);
        _shiftLabel.Leave += (_, _) => AuditSelectedCustomFont(showMessage: true);
        _assignedKeyButton.Click += (_, _) => BeginAssignedKeyCapture();
        _assignedKeyButton.LostFocus += (_, _) =>
        {
            if (!_capturingAssignedKey)
                return;
            _capturingAssignedKey = false;
            PopulateInspector();
            SetStatus("Physical-key capture cancelled.");
        };
        _x.ValueChanged += (_, _) => ApplyInspectorChange(key => key.X = (float)_x.Value);
        _y.ValueChanged += (_, _) => ApplyInspectorChange(key => key.Y = (float)_y.Value);
        _width.ValueChanged += (_, _) => ApplyInspectorChange(key =>
        {
            key.Width = (float)_width.Value;
            key.SpansToRight = false;
        });
        _height.ValueChanged += (_, _) => ApplyInspectorChange(key => key.Height = (float)_height.Value);
        _offsetX.ValueChanged += (_, _) => ApplyInspectorChange(key => key.LabelOffsetX = (float)_offsetX.Value);
        _offsetY.ValueChanged += (_, _) => ApplyInspectorChange(key => key.LabelOffsetY = (float)_offsetY.Value);
        _scale.ValueChanged += (_, _) => ApplyInspectorChange(key => key.LabelScale = (float)_scale.Value);
        _topElementX.ValueChanged += (_, _) => ApplyTopElementChange((float)_topElementX.Value, (float)_topElementY.Value);
        _topElementY.ValueChanged += (_, _) => ApplyTopElementChange((float)_topElementX.Value, (float)_topElementY.Value);
        _topElementWidth.ValueChanged += (_, _) => ApplyTopElementDesignChange();
        _topElementHeight.ValueChanged += (_, _) => ApplyTopElementDesignChange();
        _topElementFontScale.ValueChanged += (_, _) => ApplyTopElementDesignChange();
        _layoutWidth.ValueChanged += (_, _) =>
        {
            if (_updatingEditor)
                return;
            PushUndo();
            _document.Width = (int)_layoutWidth.Value;
            MarkChanged();
        };

        FormClosing += OnFormClosing;
        KeyPreview = true;
        KeyDown += OnShortcutKeyDown;
    }

    private void ApplyInspectorChange(Action<KeyboardKey> apply)
    {
        if (_updatingEditor || _canvas.SelectedKey is not KeyboardKey key)
            return;
        PushUndo();
        apply(key);
        MarkChanged();
    }

    private void ApplyDocumentChange(Action<KeyboardDocument> apply)
    {
        if (_updatingEditor)
            return;
        PushUndo();
        apply(_document);
        MarkChanged();
        PopulateAppearanceInspector();
        PopulateArtworkInspector();
        PopulateControlsInspector();
    }

    private void ToggleCustomStyle()
    {
        if (_updatingEditor)
            return;
        PushUndo();
        if (_customStyle.Checked && _themeCombo.SelectedItem is KeyboardTheme theme)
        {
            bool seeded = !_document.CustomStyleInitialized;
            _document.EnableCustomStyleFromTheme(theme);
            SetStatus(seeded
                ? $"Custom colors enabled from the visible {theme.Name} template. Nothing was replaced; choose any swatch to change only that color."
                : "Custom colors enabled with your previous color settings restored.");
        }
        else
        {
            _document.CustomStyleEnabled = false;
            SetStatus("Custom colors disabled. The selected Base Theme colors are visible again.");
        }
        MarkChanged();
        PopulateAppearanceInspector();
    }

    private void ApplyTopElementChange(float x, float y)
    {
        if (_updatingEditor || _canvas.SelectedTopElement is not KeyboardTopElement element)
            return;
        PushUndo();
        switch (element)
        {
            case KeyboardTopElement.TextBar: _document.TextBarOffsetX = x; _document.TextBarOffsetY = y; break;
            case KeyboardTopElement.Mode: _document.ModeButtonOffsetX = x; _document.ModeButtonOffsetY = y; break;
            case KeyboardTopElement.Lock: _document.LockButtonOffsetX = x; _document.LockButtonOffsetY = y; break;
        }
        MarkChanged();
        PopulateInspector();
    }

    private void ApplyTopElementDesignChange()
    {
        if (_updatingEditor || _canvas.SelectedTopElement is not KeyboardTopElement element)
            return;
        PushUndo();
        float width = (float)_topElementWidth.Value;
        float height = (float)_topElementHeight.Value;
        float scale = (float)_topElementFontScale.Value;
        switch (element)
        {
            case KeyboardTopElement.TextBar:
                _document.TextBarWidth = width; _document.TextBarHeight = height;
                _document.TextBarFontScale = scale; break;
            case KeyboardTopElement.Mode:
                _document.ModeButtonWidth = width; _document.ModeButtonHeight = height;
                _document.ModeButtonFontScale = scale; break;
            case KeyboardTopElement.Lock:
                _document.LockButtonWidth = width; _document.LockButtonHeight = height;
                _document.LockButtonFontScale = scale; break;
        }
        MarkChanged();
        PopulateInspector();
    }

    private void ApplySpriteChange(Action<KeyboardSprite> apply)
    {
        if (_updatingEditor || _canvas.SelectedSprite is not KeyboardSprite sprite)
            return;
        PushUndo();
        apply(sprite);
        MarkChanged();
        PopulateArtworkInspector();
    }

    private void ApplyControlChange(float x, float y)
    {
        if (_updatingEditor)
            return;
        PushUndo();
        switch (_canvas.SelectedControl)
        {
            case KeyboardRuntimeControl.Size: _document.SizeControlOffsetX = x; _document.SizeControlOffsetY = y; break;
            case KeyboardRuntimeControl.Opacity: _document.OpacityControlOffsetX = x; _document.OpacityControlOffsetY = y; break;
            case KeyboardRuntimeControl.Tilt: _document.TiltControlOffsetX = x; _document.TiltControlOffsetY = y; break;
        }
        MarkChanged();
        PopulateControlsInspector();
    }

    private void ApplyControlDesignChange(Action<KeyboardControlDesign> apply)
    {
        if (_updatingEditor)
            return;
        PushUndo();
        apply(_document.GetControlDesign(_canvas.SelectedControl));
        MarkChanged();
        PopulateControlsInspector();
    }

    private void ApplySelectedControlPartChange(Action<KeyboardControlDesign, KeyboardControlPart> apply)
    {
        if (_updatingEditor || _canvas.SelectedControlPart == KeyboardControlPart.Group)
            return;
        PushUndo();
        apply(_document.GetControlDesign(_canvas.SelectedControl), _canvas.SelectedControlPart);
        MarkChanged();
        PopulateControlsInspector();
    }

    private void ChooseVisualColor(string title, Color initial, Action<Color> apply)
    {
        using var dialog = new ColorWheelDialog(title, initial);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        PushUndo();
        _document.CustomStyleEnabled = true;
        _document.CustomStyleInitialized = true;
        apply(dialog.SelectedColor);
        MarkChanged();
        PopulateAppearanceInspector();
    }

    private void ChooseSpriteGlowColor()
    {
        if (_canvas.SelectedSprite is not KeyboardSprite sprite)
            return;
        using var dialog = new ColorWheelDialog("Sprite / ribbon glow color", sprite.GlowColor);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        PushUndo();
        sprite.GlowEnabled = true;
        sprite.GlowColor = dialog.SelectedColor;
        MarkChanged();
        PopulateArtworkInspector();
    }

    private void ChooseControlArrowGlowColor()
    {
        using var dialog = new ColorWheelDialog("Control triangle glow color", _document.ControlArrowGlowColor);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        PushUndo();
        _document.ControlArrowGlowEnabled = true;
        _document.ControlArrowGlowColor = dialog.SelectedColor;
        MarkChanged();
        PopulateControlsInspector();
    }

    private void BeginAssignedKeyCapture()
    {
        if (_canvas.SelectedKey is not KeyboardKey)
        {
            SetStatus("Select a keyboard key before assigning a physical key.");
            return;
        }
        _capturingAssignedKey = true;
        _assignedKeyButton.Text = "Press a physical key...";
        _assignedKeyButton.Focus();
        SetStatus("Press the physical key to assign. Normal and Shift output will be translated automatically.");
    }

    private void CaptureAssignedKey(Keys keyCode)
    {
        if (_canvas.SelectedKey is not KeyboardKey key)
        {
            _capturingAssignedKey = false;
            PopulateInspector();
            return;
        }

        if (!PhysicalKeyboardTranslator.TryTranslate(keyCode, out PhysicalKeyAssignment assignment))
        {
            System.Media.SystemSounds.Beep.Play();
            _assignedKeyButton.Text = "Unsupported - press another key";
            SetStatus($"{keyCode} is not an OCU keyboard output. Press a letter, number, symbol, arrow, function key, or supported control key.");
            return;
        }

        _capturingAssignedKey = false;
        if (key.Character == assignment.Normal && key.ShiftCharacter == assignment.Shifted)
        {
            PopulateInspector();
            SetStatus($"{DescribeAssignment(assignment.Normal, assignment.Shifted)} was already assigned.");
            return;
        }

        PushUndo();
        key.Character = assignment.Normal;
        key.ShiftCharacter = assignment.Shifted;
        key.Label = DefaultKeyText(assignment.Normal);
        key.ShiftLabel = DefaultKeyText(assignment.Shifted);
        MarkChanged();
        PopulateInspector();
        AuditSelectedCustomFont(showMessage: true);
        SetStatus($"Assigned {DescribeAssignment(assignment.Normal, assignment.Shifted)} using the active Windows keyboard layout.");
    }

    private static string DescribeAssignment(char normal, char shifted)
        => normal == shifted
            ? DisplayKeyName(normal)
            : $"{DisplayKeyName(normal)} / Shift + {DisplayKeyName(shifted)}";

    private static string DefaultKeyText(char key) => key switch
    {
        '\t' => "tab",
        '\n' => "Enter",
        '\b' => "back",
        ' ' => "",
        '\x01' => "shift",
        '\x02' => "caps",
        '\x03' => "Done",
        '\x04' or '\x05' or '\x06' or '\x07' => "",
        '\x0E' => "ESC",
        '\x1D' => "End",
        '\x1E' => "Ctrl",
        '\x1F' => "PrtSc",
        >= '\x10' and <= '\x1B' => $"F{key - '\x10' + 1}",
        _ => key.ToString()
    };

    private void MarkChanged()
    {
        _document.IsDirty = true;
        _redo.Clear();
        UpdateHistoryControls();
        _canvas.RefreshPreview();
        UpdateTitle();
    }

    private void PushUndo() => PushUndo(_document.Clone());

    private void PushUndo(KeyboardDocument snapshot)
    {
        _undo.Push(snapshot);
        if (_undo.Count > 150)
        {
            KeyboardDocument[] recent = _undo.Take(150).Reverse().ToArray();
            _undo.Clear();
            foreach (KeyboardDocument item in recent)
                _undo.Push(item);
        }
        UpdateHistoryControls();
    }

    private void Undo()
    {
        KeyboardDocument current = _document.Clone();
        KeyboardDocument? target = PopDifferentDocument(_undo, current);
        if (target is null)
        {
            UpdateHistoryControls();
            return;
        }
        _redo.Push(current);
        SetDocument(target, clearHistory: false, preserveCanvasSelection: true);
        SetStatus("Undid the last edit.");
    }

    private void Redo()
    {
        KeyboardDocument current = _document.Clone();
        KeyboardDocument? target = PopDifferentDocument(_redo, current);
        if (target is null)
        {
            UpdateHistoryControls();
            return;
        }
        _undo.Push(current);
        SetDocument(target, clearHistory: false, preserveCanvasSelection: true);
        SetStatus("Redid the edit.");
    }

    private static KeyboardDocument? PopDifferentDocument(Stack<KeyboardDocument> history, KeyboardDocument current)
    {
        while (history.Count > 0)
        {
            KeyboardDocument candidate = history.Pop();
            if (!candidate.IsEquivalentForHistory(current))
                return candidate;
        }
        return null;
    }

    private void UpdateHistoryControls()
    {
        // Keep the toolbar text bright even when a stack is empty. WinForms'
        // disabled-button renderer ignores ForeColor and turns these nearly
        // black against the dark toolbar; clicking an empty stack is harmless.
        if (_undoButton is not null)
            _undoButton.Enabled = true;
        if (_redoButton is not null)
            _redoButton.Enabled = true;
        _historyLabel.Text = $"History: {_undo.Count} undo / {_redo.Count} redo";
    }

    private void DuplicateKey()
    {
        if (_canvas.SelectedKey is not KeyboardKey selected)
            return;
        PushUndo();
        KeyboardKey copy = selected.Clone();
        copy.Id = _document.Keys.Count == 0 ? 0 : _document.Keys.Max(key => key.Id) + 1;
        copy.X += 0.25f;
        copy.Y += 0.25f;
        _document.Keys.Add(copy);
        _canvas.SelectedKeyId = copy.Id;
        MarkChanged();
    }

    private void DeleteKey()
    {
        if (_canvas.SelectedKey is not KeyboardKey selected)
            return;
        PushUndo();
        int index = _document.Keys.IndexOf(selected);
        _document.Keys.Remove(selected);
        _canvas.SelectedKeyId = _document.Keys.Count == 0 ? -1 : _document.Keys[Math.Clamp(index, 0, _document.Keys.Count - 1)].Id;
        MarkChanged();
    }

    private void SetDocument(KeyboardDocument document, bool clearHistory, bool preserveCanvasSelection = false)
    {
        _document = document;
        if (clearHistory)
        {
            _undo.Clear();
            _redo.Clear();
        }
        SelectDocumentThemeAndFont();
        _canvas.ReplaceDocument(_document, preserveCanvasSelection);
        PopulateInspector();
        PopulateAppearanceInspector();
        PopulateArtworkInspector();
        PopulateControlsInspector();
        _canvas.RefreshPreview();
        _canvas.Update();
        UpdateTitle();
        UpdateHistoryControls();
    }

    private void SelectDocumentThemeAndFont()
    {
        _updatingEditor = true;
        try
        {
            int themeIndex = KeyboardTheme.BuiltIns.ToList().FindIndex(theme => ThemeConfigName(theme) == _document.BaseTheme);
            _themeCombo.SelectedIndex = themeIndex >= 0 ? themeIndex
                : KeyboardTheme.BuiltIns.ToList().FindIndex(theme => ThemeConfigName(theme) == "parchment");
            int fontIndex = _fonts.FindIndex(font => font.ConfigName.Equals(_document.FontName, StringComparison.OrdinalIgnoreCase));
            if (fontIndex < 0
                && _document.FontName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)
                && File.Exists(_document.CustomFontMetadataPath)
                && File.Exists(_document.CustomFontTexturePath))
            {
                string display = _document.FontName["custom_".Length..].Replace('_', ' ');
                _fonts.Add(new FontChoice(display, _document.CustomFontMetadataPath!,
                    _document.CustomFontTexturePath!, _document.FontName, null));
                _fontCombo.Items.Add(_fonts[^1]);
                fontIndex = _fonts.Count - 1;
            }
            _fontCombo.SelectedIndex = fontIndex >= 0 ? fontIndex : _fonts.FindIndex(font => font.ConfigName == "parchment");

            if (_renderer is not null && _themeCombo.SelectedItem is KeyboardTheme theme)
                _renderer.Theme = theme;
            if (_renderer is not null)
                _renderer.Font = LoadSelectedFont();
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void PopulateInspector()
    {
        _updatingEditor = true;
        try
        {
            _layoutWidth.Value = ClampDecimal(_document.Width, _layoutWidth);
            PopulateTopElementInspectorFields();
            bool enabled = !_canvas.HasMultipleSelection
                && _canvas.SelectionKind is CanvasSelectionKind.KeyContent or CanvasSelectionKind.KeyPlate
                && _canvas.SelectedKey is KeyboardKey;
            foreach (Control control in new Control[] { _assignedKeyButton, _baseLabel, _shiftLabel, _x, _y, _width, _height, _offsetX, _offsetY, _scale })
                control.Enabled = enabled;

            if (!enabled || _canvas.SelectedKey is not KeyboardKey key)
            {
                _selectedLabel.Text = _canvas.HasMultipleSelection
                    ? $"{_canvas.SelectionCount} visible items selected"
                    : "No key selected";
                _assignedKeyButton.Text = _canvas.HasMultipleSelection ? "Group selection" : "Select a key first";
                return;
            }
            _selectedLabel.Text = $"Key #{key.Id} — {DisplayKeyName(key.Character)}";
            if (!_capturingAssignedKey)
                _assignedKeyButton.Text = DescribeAssignment(key.Character, key.ShiftCharacter);
            _baseLabel.Text = key.Label;
            _shiftLabel.Text = key.ShiftLabel;
            _x.Value = ClampDecimal((decimal)key.X, _x);
            _y.Value = ClampDecimal((decimal)key.Y, _y);
            float displayedWidth = key.SpansToRight && _renderer is not null
                ? _renderer.KeyRectangle(_document, key).Width / Math.Max(1, _renderer.KeySize(_document))
                : key.Width;
            _width.Value = ClampDecimal((decimal)displayedWidth, _width);
            _height.Value = ClampDecimal((decimal)key.Height, _height);
            _offsetX.Value = ClampDecimal((decimal)key.LabelOffsetX, _offsetX);
            _offsetY.Value = ClampDecimal((decimal)key.LabelOffsetY, _offsetY);
            _scale.Value = ClampDecimal((decimal)key.LabelScale, _scale);
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void PopulateTopElementInspectorFields()
    {
        KeyboardTopElement? topElement = _canvas.HasMultipleSelection ? null : _canvas.SelectedTopElement;
        _topElementLabel.Text = topElement switch
        {
            KeyboardTopElement.TextBar => "Text bar",
            KeyboardTopElement.Mode => "PC / VR Mode",
            KeyboardTopElement.Lock => "Lock",
            _ => "Click a top-bar element"
        };
        (float topX, float topY) = topElement switch
        {
            KeyboardTopElement.TextBar => (_document.TextBarOffsetX, _document.TextBarOffsetY),
            KeyboardTopElement.Mode => (_document.ModeButtonOffsetX, _document.ModeButtonOffsetY),
            KeyboardTopElement.Lock => (_document.LockButtonOffsetX, _document.LockButtonOffsetY),
            _ => (0, 0)
        };
        _topElementX.Value = ClampDecimal((decimal)topX, _topElementX);
        _topElementY.Value = ClampDecimal((decimal)topY, _topElementY);
        float topWidth = topElement is KeyboardTopElement selected && _renderer is not null
            ? _renderer.TopElementWidth(_document, selected) : 8;
        float topHeight = topElement is KeyboardTopElement selectedHeight && _renderer is not null
            ? _renderer.TopElementHeight(_document, selectedHeight) : 8;
        float topFontScale = topElement is KeyboardTopElement selectedScale && _renderer is not null
            ? _renderer.TopElementFontScale(_document, selectedScale) : 1;
        _topElementWidth.Value = ClampDecimal((decimal)topWidth, _topElementWidth);
        _topElementHeight.Value = ClampDecimal((decimal)topHeight, _topElementHeight);
        _topElementFontScale.Value = ClampDecimal((decimal)topFontScale, _topElementFontScale);
        foreach (Control control in new Control[]
        {
            _topElementX, _topElementY, _topElementWidth, _topElementHeight, _topElementFontScale
        })
            control.Enabled = topElement is not null;
    }

    private void PopulateAppearanceInspector()
    {
        _updatingEditor = true;
        try
        {
            _customStyle.Checked = _document.CustomStyleEnabled;
            _keyPlatesEnabled.Checked = _document.KeyPlatesEnabled;
            _topButtonPlatesEnabled.Checked = _document.TopButtonPlatesEnabled;
            _inputBarPlateEnabled.Checked = _document.InputBarPlateEnabled;
            _parchmentRibbonEnabled.Checked = _document.ParchmentRibbonEnabled;
            _parchmentRibbonEnabled.Enabled = _document.BaseTheme.Equals("parchment", StringComparison.OrdinalIgnoreCase);
            _glowEnabled.Checked = _document.GlowEnabled;
            _fontGlowEnabled.Checked = _document.FontGlowEnabled;
            _hoverEnabled.Checked = _document.HoverEnabled;
            _outlineEnabled.Checked = _document.OutlineEnabled;
            _glowStrength.Value = ClampDecimal(_document.GlowStrength, _glowStrength);
            _glowRadius.Value = ClampDecimal(_document.GlowRadius, _glowRadius);
            _fontGlowStrength.Value = ClampDecimal(_document.FontGlowStrength, _fontGlowStrength);
            _fontGlowRadius.Value = ClampDecimal(_document.FontGlowRadius, _fontGlowRadius);
            _hoverStrength.Value = ClampDecimal(_document.HoverStrength, _hoverStrength);
            _keyRoundness.Value = ClampDecimal(_document.KeyRoundness, _keyRoundness);
            _plateOutlineWidth.Value = ClampDecimal(_document.PlateOutlineWidth, _plateOutlineWidth);
            _keyBreathe.Checked = _document.KeyBreatheEnabled;
            _keyBreatheMin.Value = ClampDecimal(_document.KeyBreatheMinPercent, _keyBreatheMin);
            _keyBreathePeriod.Value = ClampDecimal((decimal)_document.KeyBreathePeriodSeconds, _keyBreathePeriod);
            _keyBreathePhase.Value = ClampDecimal((decimal)_document.KeyBreathePhaseDegrees, _keyBreathePhase);
            _fontBreathe.Checked = _document.FontBreatheEnabled;
            _fontBreatheMin.Value = ClampDecimal(_document.FontBreatheMinPercent, _fontBreatheMin);
            _fontBreathePeriod.Value = ClampDecimal((decimal)_document.FontBreathePeriodSeconds, _fontBreathePeriod);
            _fontBreathePhase.Value = ClampDecimal((decimal)_document.FontBreathePhaseDegrees, _fontBreathePhase);
            SetSwatch(_fontColor, _document.FontColor);
            SetSwatch(_fontOutlineColor, _document.FontOutlineColor);
            SetSwatch(_fontGlowColor, _document.FontGlowColor);
            SetSwatch(_keyColor, _document.KeyColor);
            SetSwatch(_plateFillColor, _document.PlateFillColor);
            SetSwatch(_glowColor, _document.GlowColor);
            SetSwatch(_hoverColor, _document.HoverColor);

            foreach (Control control in new Control[]
            {
                _fontColor, _fontOutlineColor, _fontGlowColor, _plateFillColor, _keyColor, _glowColor, _hoverColor,
                _glowEnabled, _fontGlowEnabled, _hoverEnabled, _outlineEnabled,
                _glowStrength, _glowRadius, _fontGlowStrength, _fontGlowRadius,
                _hoverStrength, _keyRoundness, _plateOutlineWidth, _keyBreathe, _fontBreathe
            })
                control.Enabled = _document.CustomStyleEnabled;
            _keyBreathe.Enabled = _document.CustomStyleEnabled && _document.GlowEnabled;
            foreach (Control control in new Control[] { _keyBreatheMin, _keyBreathePeriod, _keyBreathePhase })
                control.Enabled = _document.CustomStyleEnabled && _document.GlowEnabled && _document.KeyBreatheEnabled;
            foreach (Control control in new Control[] { _fontGlowColor, _fontGlowStrength, _fontGlowRadius, _fontBreathe })
                control.Enabled = _document.CustomStyleEnabled && _document.FontGlowEnabled;
            _fontBreathe.Enabled = _document.CustomStyleEnabled && _document.FontGlowEnabled;
            foreach (Control control in new Control[] { _fontBreatheMin, _fontBreathePeriod, _fontBreathePhase })
                control.Enabled = _document.CustomStyleEnabled && _document.FontGlowEnabled && _document.FontBreatheEnabled;
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void PopulateArtworkInspector()
    {
        _updatingEditor = true;
        try
        {
            _backgroundName.Text = string.IsNullOrWhiteSpace(_document.BackgroundImagePath)
                ? "Theme background"
                : Path.GetFileName(_document.BackgroundImagePath);
            _backgroundX.Value = ClampDecimal((decimal)_document.BackgroundX, _backgroundX);
            _backgroundY.Value = ClampDecimal((decimal)_document.BackgroundY, _backgroundY);
            _backgroundWidth.Value = ClampDecimal((decimal)_document.BackgroundWidth, _backgroundWidth);
            _backgroundHeight.Value = ClampDecimal((decimal)_document.BackgroundHeight, _backgroundHeight);
            _backgroundOpacity.Value = ClampDecimal(_document.BackgroundOpacity, _backgroundOpacity);
            _backgroundFade.Value = ClampDecimal(_document.BackgroundEdgeFade, _backgroundFade);
            _backgroundRotation.Value = ClampDecimal((decimal)_document.BackgroundRotation, _backgroundRotation);
            _backgroundRoundness.Value = ClampDecimal(_document.BackgroundRoundness, _backgroundRoundness);
            bool hasBackground = !string.IsNullOrWhiteSpace(_document.BackgroundImagePath);
            foreach (Control control in new Control[]
            {
                _backgroundX, _backgroundY, _backgroundWidth, _backgroundHeight,
                _backgroundOpacity, _backgroundFade, _backgroundRotation, _backgroundRoundness
            })
                control.Enabled = hasBackground;
            Guid? selectedId = _canvas.HasMultipleSelection ? null : _canvas.SelectedSpriteId;
            _spriteList.BeginUpdate();
            _spriteList.Items.Clear();
            _spriteList.Items.AddRange(_document.Sprites.Cast<object>().ToArray());
            int selectedIndex = selectedId is Guid id ? _document.Sprites.FindIndex(sprite => sprite.Id == id) : -1;
            if (!_canvas.HasMultipleSelection && selectedIndex < 0 && _document.Sprites.Count > 0)
                selectedIndex = 0;
            _spriteList.SelectedIndex = selectedIndex;
            _spriteList.EndUpdate();

            KeyboardSprite? sprite = selectedIndex >= 0 ? _document.Sprites[selectedIndex] : null;
            _overlayName.Text = sprite is null ? "No sprite selected" : sprite.ToString();
            _overlayX.Value = ClampDecimal((decimal)(sprite?.X ?? 0), _overlayX);
            _overlayY.Value = ClampDecimal((decimal)(sprite?.Y ?? 0), _overlayY);
            _overlayWidth.Value = ClampDecimal((decimal)(sprite?.Width ?? 1), _overlayWidth);
            _overlayHeight.Value = ClampDecimal((decimal)(sprite?.Height ?? 1), _overlayHeight);
            _overlayOpacity.Value = ClampDecimal(sprite?.Opacity ?? 100, _overlayOpacity);
            _spriteFade.Value = ClampDecimal(sprite?.EdgeFade ?? 0, _spriteFade);
            _spriteRotation.Value = ClampDecimal((decimal)(sprite?.Rotation ?? 0), _spriteRotation);
            _spriteGlow.Checked = sprite?.GlowEnabled ?? false;
            SetSwatch(_spriteGlowColor, sprite?.GlowColor ?? Color.FromArgb(255, 132, 242, 158));
            _spriteGlowStrength.Value = ClampDecimal(sprite?.GlowStrength ?? 55, _spriteGlowStrength);
            _spriteGlowRadius.Value = ClampDecimal(sprite?.GlowRadius ?? 12, _spriteGlowRadius);
            _spriteBreathe.Checked = sprite?.BreatheEnabled ?? false;
            _spriteBreatheMin.Value = ClampDecimal(sprite?.BreatheMinPercent ?? 35, _spriteBreatheMin);
            _spriteBreathePeriod.Value = ClampDecimal((decimal)(sprite?.BreathePeriodSeconds ?? 2f), _spriteBreathePeriod);
            _spriteBreathePhase.Value = ClampDecimal((decimal)(sprite?.BreathePhaseDegrees ?? 0f), _spriteBreathePhase);
            bool hasSprite = sprite is not null;
            foreach (Control control in new Control[]
            {
                _overlayX, _overlayY,
                _overlayWidth, _overlayHeight, _overlayOpacity, _spriteFade, _spriteRotation,
                _spriteGlow, _spriteGlowColor, _spriteGlowStrength, _spriteGlowRadius, _spriteBreathe
            })
                control.Enabled = hasSprite;
            foreach (Control control in new Control[] { _spriteGlowColor, _spriteGlowStrength, _spriteGlowRadius, _spriteBreathe })
                control.Enabled = hasSprite && sprite!.GlowEnabled;
            foreach (Control control in new Control[] { _spriteBreatheMin, _spriteBreathePeriod, _spriteBreathePhase })
                control.Enabled = hasSprite && sprite!.GlowEnabled && sprite.BreatheEnabled;
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void PopulateControlsInspector()
    {
        _updatingEditor = true;
        try
        {
            _controlCombo.SelectedIndex = (int)_canvas.SelectedControl;
            (float x, float y) = _canvas.SelectedControl switch
            {
                KeyboardRuntimeControl.Size => (_document.SizeControlOffsetX, _document.SizeControlOffsetY),
                KeyboardRuntimeControl.Opacity => (_document.OpacityControlOffsetX, _document.OpacityControlOffsetY),
                _ => (_document.TiltControlOffsetX, _document.TiltControlOffsetY)
            };
            _controlX.Value = ClampDecimal((decimal)x, _controlX);
            _controlY.Value = ClampDecimal((decimal)y, _controlY);
            KeyboardControlDesign design = _document.GetControlDesign(_canvas.SelectedControl);
            _controlWidth.Value = ClampDecimal((decimal)design.Width, _controlWidth);
            _controlHeight.Value = ClampDecimal((decimal)design.Height, _controlHeight);
            KeyboardControlPart part = _canvas.SelectedControlPart;
            _controlPartLabel.Text = part switch
            {
                KeyboardControlPart.Group => "Outer settings box",
                KeyboardControlPart.UpArrow => "Up arrow",
                KeyboardControlPart.DownArrow => "Down arrow",
                KeyboardControlPart.Label => "Label font",
                _ => "Value font"
            };
            (float partX, float partY, float partWidth, float partHeight, float partScale) = part switch
            {
                KeyboardControlPart.UpArrow => (design.UpOffsetX, design.UpOffsetY, design.UpWidth, design.UpHeight, 1f),
                KeyboardControlPart.DownArrow => (design.DownOffsetX, design.DownOffsetY, design.DownWidth, design.DownHeight, 1f),
                KeyboardControlPart.Label => (design.LabelOffsetX, design.LabelOffsetY, 4f, 4f, design.LabelScale),
                KeyboardControlPart.Value => (design.ValueOffsetX, design.ValueOffsetY, 4f, 4f, design.ValueScale),
                _ => (0f, 0f, 4f, 4f, 1f)
            };
            _controlPartX.Value = ClampDecimal((decimal)partX, _controlPartX);
            _controlPartY.Value = ClampDecimal((decimal)partY, _controlPartY);
            _controlPartWidth.Value = ClampDecimal((decimal)partWidth, _controlPartWidth);
            _controlPartHeight.Value = ClampDecimal((decimal)partHeight, _controlPartHeight);
            _controlPartScale.Value = ClampDecimal((decimal)partScale, _controlPartScale);
            bool arrowPart = part is KeyboardControlPart.UpArrow or KeyboardControlPart.DownArrow;
            bool textPart = part is KeyboardControlPart.Label or KeyboardControlPart.Value;
            _controlPartX.Enabled = arrowPart || textPart;
            _controlPartY.Enabled = arrowPart || textPart;
            _controlPartWidth.Enabled = arrowPart;
            _controlPartHeight.Enabled = arrowPart;
            _controlPartScale.Enabled = textPart;
            _controlArrowName.Text = string.IsNullOrWhiteSpace(_document.ControlArrowImagePath)
                ? "Built-in triangle"
                : Path.GetFileName(_document.ControlArrowImagePath);
            _controlArrowRotation.Value = ClampDecimal((decimal)_document.ControlArrowRotation, _controlArrowRotation);
            _controlArrowGlow.Checked = _document.ControlArrowGlowEnabled;
            SetSwatch(_controlArrowGlowColor, _document.ControlArrowGlowColor);
            _controlArrowGlowStrength.Value = ClampDecimal(_document.ControlArrowGlowStrength, _controlArrowGlowStrength);
            _controlArrowGlowRadius.Value = ClampDecimal(_document.ControlArrowGlowRadius, _controlArrowGlowRadius);
            _controlArrowBreathe.Checked = _document.ControlArrowBreatheEnabled;
            _controlArrowBreatheMin.Value = ClampDecimal(_document.ControlArrowBreatheMinPercent, _controlArrowBreatheMin);
            _controlArrowBreathePeriod.Value = ClampDecimal((decimal)_document.ControlArrowBreathePeriodSeconds, _controlArrowBreathePeriod);
            _controlArrowBreathePhase.Value = ClampDecimal((decimal)_document.ControlArrowBreathePhaseDegrees, _controlArrowBreathePhase);
            _controlArrowGlowColor.Enabled = _document.ControlArrowGlowEnabled;
            _controlArrowGlowStrength.Enabled = _document.ControlArrowGlowEnabled;
            _controlArrowGlowRadius.Enabled = _document.ControlArrowGlowEnabled;
            _controlArrowBreathe.Enabled = _document.ControlArrowGlowEnabled;
            foreach (Control control in new Control[] { _controlArrowBreatheMin, _controlArrowBreathePeriod, _controlArrowBreathePhase })
                control.Enabled = _document.ControlArrowGlowEnabled && _document.ControlArrowBreatheEnabled;
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void OpenLayout()
    {
        if (!ConfirmDiscard())
            return;
        using var dialog = new OpenFileDialog
        {
            Title = "Open or import an OCU keyboard",
            Filter = "OCU keyboard package or layout (*.ocukb;*.kb)|*.ocukb;*.kb|OCU keyboard package (*.ocukb)|*.ocukb|Legacy keyboard layout (*.kb)|*.kb",
            InitialDirectory = Path.GetDirectoryName(_currentPackagePath ?? _document.SourcePath) ?? _assetsDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        LoadKeyboardFile(dialog.FileName);
    }

    private void LoadKeyboardFile(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (Path.GetExtension(fullPath).Equals(KeyboardPackage.Extension, StringComparison.OrdinalIgnoreCase))
            {
                KeyboardPackageImportResult imported = KeyboardPackage.Import(fullPath, DesignLibraryDirectory);
                _currentPackagePath = fullPath;
                SetDocument(KeyboardDocument.Load(imported.LayoutPath), clearHistory: true);
                string registeredPath = RegisterDesign(imported.LayoutPath);
                RefreshDesignChoices(registeredPath);
                SetStatus($"Imported {imported.DisplayName}: {imported.ArtworkCount} artwork file(s)" +
                    (imported.HasCustomFont ? " plus its custom font." : "."));
                MessageBox.Show(this,
                    $"{imported.DisplayName} imported successfully.\n\n" +
                    $"Artwork files: {imported.ArtworkCount}\n" +
                    $"Custom font: {(imported.HasCustomFont ? "included" : "not used")}\n\n" +
                    "The design is now in Keyboard Design. Use Install for Next Launch to activate it.",
                    "OCU keyboard imported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Path.GetExtension(fullPath).Equals(".kb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose an .ocukb package or a legacy .kb layout.");
            _currentPackagePath = null;
            SetDocument(KeyboardDocument.Load(fullPath), clearHistory: true);
            string legacyRegisteredPath = RegisterDesign(fullPath);
            RefreshDesignChoices(legacyRegisteredPath);
            SetStatus($"Loaded legacy .kb layout: {fullPath}. Keep its PNG files beside it.");
        }
        catch (Exception exception)
        {
            ShowError("Keyboard import failed", exception);
        }
    }

    private bool SaveLayout(bool saveAs)
    {
        if (!saveAs && !_document.IsDirty)
        {
            SetStatus("No unsaved changes. Use Save As to create another copy.");
            return true;
        }

        string? packagePath = saveAs ? null : _currentPackagePath;
        if (packagePath is null)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save portable OCU keyboard project",
                Filter = "OCU keyboard package (*.ocukb)|*.ocukb",
                DefaultExt = "ocukb",
                AddExtension = true,
                FileName = $"{SuggestedPackageName()}.ocukb",
                InitialDirectory = Path.GetDirectoryName(_currentPackagePath)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;
            packagePath = dialog.FileName;
        }

        try
        {
            packagePath = Path.GetFullPath(packagePath);
            string displayName = Path.GetFileNameWithoutExtension(packagePath);
            KeyboardPackage.Export(packagePath, _document, displayName);
            KeyboardPackageImportResult imported = KeyboardPackage.Import(packagePath, DesignLibraryDirectory);
            _currentPackagePath = packagePath;
            SetDocument(KeyboardDocument.Load(imported.LayoutPath), clearHistory: false);
            string registeredPath = RegisterDesign(imported.LayoutPath);
            RefreshDesignChoices(registeredPath);
            string? installedTarget = _hostOcuRoot is null ? null : InstallDocumentToRoot(_hostOcuRoot);
            UpdateTitle();
            SetStatus(installedTarget is null
                ? $"Saved portable keyboard project: {packagePath}"
                : $"Saved {packagePath} and applied it to this OCU. Restart Skyrim VR to load it.");
            return true;
        }
        catch (Exception exception)
        {
            ShowError("Layout save failed", exception);
            return false;
        }
    }

    private string SuggestedPackageName()
    {
        string? sourceName = Path.GetFileNameWithoutExtension(_currentPackagePath ?? _document.SourcePath);
        return string.IsNullOrWhiteSpace(sourceName) || sourceName.Equals("en_gb", StringComparison.OrdinalIgnoreCase)
            ? "My OCU Keyboard"
            : sourceName;
    }

    private void ExportPng()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export exact 1024x560 preview",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{Path.GetFileNameWithoutExtension(_document.SourcePath) ?? "OCU-Keyboard"}-preview.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            using Bitmap preview = _canvas.RenderExact();
            preview.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            SetStatus($"Exported exact 1024x560 preview to {dialog.FileName}");
        }
        catch (Exception exception)
        {
            ShowError("PNG export failed", exception);
        }
    }

    private void ChooseBackground()
    {
        using var dialog = BackgroundArtworkDialog("Choose a custom keyboard background");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        string extension = Path.GetExtension(dialog.FileName);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            DialogResult warning = MessageBox.Show(this,
                "JPEG backgrounds have no transparency and may show compression artifacts around detailed edges. PNG is strongly recommended.\n\nKeyboard Studio will convert this JPEG to PNG when you Save, Install, or export a shareable mod. Continue?",
                "JPEG background warning", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (warning != DialogResult.OK)
                return;
        }
        PushUndo();
        _document.BackgroundImagePath = dialog.FileName;
        _document.BackgroundFileName = "OCUKeyboardBackground.png";
        _document.BackgroundX = 0;
        _document.BackgroundY = 0;
        _document.BackgroundWidth = KeyboardRenderer.TextureWidth;
        _document.BackgroundHeight = KeyboardRenderer.TextureHeight;
        _document.BackgroundOpacity = 100;
        _document.BackgroundEdgeFade = 0;
        _document.BackgroundRotation = 0;
        _document.BackgroundRoundness = 0;
        _document.BackgroundBreatheEnabled = false;
        _canvas.SelectBackground();
        MarkChanged();
        PopulateArtworkInspector();
        SetStatus("Custom background selected. It will be copied into Save, Install, and MO2 exports.");
    }

    private void ClearBackground()
    {
        if (string.IsNullOrWhiteSpace(_document.BackgroundImagePath) && string.IsNullOrWhiteSpace(_document.BackgroundFileName))
            return;
        PushUndo();
        _document.BackgroundImagePath = null;
        _document.BackgroundFileName = null;
        _document.BackgroundBreatheEnabled = false;
        MarkChanged();
        PopulateArtworkInspector();
    }

    private void ChooseOverlay()
    {
        using var dialog = PngArtworkDialog("Add a transparent sprite, ribbon, border, or decoration PNG");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AddSprite(dialog.FileName);
    }

    private void AddParchmentRibbon()
    {
        string path = Path.Combine(_assetsDirectory, "spacebar.png");
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "The bundled Parchment ribbon asset is missing.", "Asset missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        AddSprite(path);
    }

    private void AddSprite(string path)
    {
        PushUndo();
        var sprite = new KeyboardSprite { SourcePath = path };
        using (var image = new Bitmap(path))
        {
            float width = Math.Min(520, image.Width);
            float height = Math.Max(1, width * image.Height / Math.Max(1f, image.Width));
            if (height > 280)
            {
                height = 280;
                width = Math.Max(1, height * image.Width / Math.Max(1f, image.Height));
            }
            sprite.Width = width;
            sprite.Height = height;
            sprite.X = (KeyboardRenderer.TextureWidth - width) / 2f;
            sprite.Y = 62 + _document.Sprites.Count * 12;
        }
        _document.Sprites.Add(sprite);
        _canvas.SelectSprite(sprite);
        MarkChanged();
        PopulateArtworkInspector();
        SetStatus("Sprite added. Drag it in the preview, resize it, fade its edges, or change its layer order.");
    }

    private void ClearOverlay()
    {
        if (_canvas.SelectedSprite is not KeyboardSprite sprite)
            return;
        PushUndo();
        int index = _document.Sprites.IndexOf(sprite);
        _document.Sprites.Remove(sprite);
        KeyboardSprite? next = _document.Sprites.Count == 0 ? null : _document.Sprites[Math.Clamp(index, 0, _document.Sprites.Count - 1)];
        _canvas.SelectSprite(next);
        MarkChanged();
        PopulateArtworkInspector();
    }

    private void MoveSpriteLayer(int direction)
    {
        if (_canvas.SelectedSprite is not KeyboardSprite sprite)
            return;
        int index = _document.Sprites.IndexOf(sprite);
        int target = Math.Clamp(index + direction, 0, _document.Sprites.Count - 1);
        if (target == index)
            return;
        PushUndo();
        _document.Sprites.RemoveAt(index);
        _document.Sprites.Insert(target, sprite);
        MarkChanged();
        PopulateArtworkInspector();
    }

    private void ChooseControlArrow()
    {
        using var dialog = PngArtworkDialog("Choose a transparent PNG for the side-control Up arrow");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        PushUndo();
        _document.ControlArrowImagePath = dialog.FileName;
        _document.ControlArrowFileName = "OCUKeyboardControlArrow.png";
        MarkChanged();
        PopulateControlsInspector();
        SetStatus("Custom side-control arrow selected. OCU rotates the same PNG 180 degrees for Down.");
    }

    private void ClearControlArrow()
    {
        if (string.IsNullOrWhiteSpace(_document.ControlArrowImagePath)
            && string.IsNullOrWhiteSpace(_document.ControlArrowFileName))
            return;
        PushUndo();
        _document.ControlArrowImagePath = null;
        _document.ControlArrowFileName = null;
        _document.ControlArrowRotation = 0;
        MarkChanged();
        PopulateControlsInspector();
    }

    private void ExportMo2Mod()
    {
        string? sourceName = Path.GetFileNameWithoutExtension(_document.SourcePath);
        if (string.IsNullOrWhiteSpace(sourceName) || sourceName.Equals("en_gb", StringComparison.OrdinalIgnoreCase))
            sourceName = "My OCU Keyboard";
        using var dialog = new SaveFileDialog
        {
            Title = "Export installable MO2 keyboard mod",
            Filter = "MO2 mod archive (*.zip)|*.zip",
            FileName = $"{sourceName}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Mo2ModExporter.Export(dialog.FileName, _document);

            SetStatus($"Exported installable MO2 keyboard mod: {dialog.FileName}");
            MessageBox.Show(this,
                "Shareable keyboard mod exported. Send this ZIP to another user, or install it as a normal MO2 mod after Open Composite Unleashed. It carries the layout, exact theme/font selection, colors, and artwork without replacing opencomposite.ini.",
                "Shareable keyboard exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("MO2 mod export failed", exception);
        }
    }

    private void InstallToOcu()
    {
        string? root = _hostOcuRoot;
        if (root is null)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose the Skyrim VR game root, an OCU mod folder containing root, or the OCU mod's root folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            root = ResolveSelectedOcuRoot(dialog.SelectedPath);
            if (root is null)
            {
                MessageBox.Show(this, "That folder does not contain openvr_api.dll, either directly or inside a root subfolder. Choose the Skyrim VR game root or the active OCU MO2 mod.",
                    "Wrong folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _hostOcuRoot = root;
        }

        try
        {
            string serialized = _document.Serialize();
            string expectedTarget = Path.Combine(root, "OCUKeyboard.kb");
            bool changed = !File.Exists(expectedTarget)
                || !string.Equals(File.ReadAllText(expectedTarget), serialized, StringComparison.Ordinal);
            string target = InstallDocumentToRoot(root);
            UpdateTitle();
            SetStatus($"Installed {target}. Restart Skyrim VR to load it.");
            MessageBox.Show(this,
                $"Keyboard installed successfully.\n\n" +
                $"Layout:\n{target}\n\n" +
                $"OCU installation:\n{root}\n\n" +
                (changed
                    ? "The installed layout was updated.\n\n"
                    : "That exact layout was already installed, so no file-content change was needed.\n\n") +
                "This is not a live reload. Restart Skyrim VR to see it in game.",
                "Keyboard installed for next launch", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("OCU install failed", exception);
        }
    }

    private string InstallDocumentToRoot(string root)
    {
        string target = Path.Combine(root, "OCUKeyboard.kb");
        string serialized = _document.Serialize();
        File.WriteAllText(target, serialized, new System.Text.UTF8Encoding(false));
        CopyArtworkBesideLayout(root);
        SetIniValue(Path.Combine(root, "opencomposite.ini"), "keyboard", "layout", "auto");
        SetIniValue(Path.Combine(root, "opencomposite.ini"), "keyboard", "design", CurrentInstalledDesignId());
        if (!File.Exists(target)
            || !string.Equals(File.ReadAllText(target), serialized, StringComparison.Ordinal))
            throw new IOException($"OCU Keyboard Studio could not verify the installed layout at {target}.");
        return target;
    }

    private string CurrentInstalledDesignId()
    {
        string? sourcePath = _document.SourcePath;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
            bool bundledParchment = sourceName.Equals("en_gb", StringComparison.OrdinalIgnoreCase)
                && Path.GetFullPath(sourcePath).StartsWith(
                    Path.GetFullPath(_assetsDirectory), StringComparison.OrdinalIgnoreCase);
            if (!bundledParchment && !string.IsNullOrWhiteSpace(sourceName))
                return sourceName;
        }
        return "keyboard-studio";
    }

    internal static string? FindOcuRootFromStudioDirectory(string studioDirectory)
    {
        DirectoryInfo? directory;
        try { directory = new DirectoryInfo(Path.GetFullPath(studioDirectory)); }
        catch { return null; }

        for (int depth = 0; directory is not null && depth < 5; depth++, directory = directory.Parent)
        {
            string? direct = ResolveSelectedOcuRoot(directory.FullName);
            if (direct is not null)
                return direct;
        }
        return null;
    }

    internal static string? ResolveSelectedOcuRoot(string? selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder))
            return null;
        string direct = Path.Combine(selectedFolder, "openvr_api.dll");
        if (File.Exists(direct))
            return Path.GetFullPath(selectedFolder);
        string nested = Path.Combine(selectedFolder, "root", "openvr_api.dll");
        return File.Exists(nested) ? Path.GetFullPath(Path.Combine(selectedFolder, "root")) : null;
    }

    private void RefreshDesignChoices(string? preferredPath = null)
    {
        _updatingDesignLibrary = true;
        try
        {
            string? preferred = NormalizeExistingPath(preferredPath ?? _document.SourcePath);
            var choices = new List<KeyboardDesignChoice>();
            IEnumerable<string> builtInLayouts = Directory.EnumerateFiles(_assetsDirectory, "*.kb");
            string stockDesignDirectory = Path.Combine(_assetsDirectory, "Stock Designs");
            if (Directory.Exists(stockDesignDirectory))
                builtInLayouts = builtInLayouts.Concat(
                    Directory.EnumerateFiles(stockDesignDirectory, "*.kb", SearchOption.AllDirectories));
            foreach (string path in builtInLayouts.OrderBy(Path.GetFileName))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                string name = stem.Equals("en_gb", StringComparison.OrdinalIgnoreCase)
                    ? "Parchment" : FriendlyDesignName(stem);
                choices.Add(new KeyboardDesignChoice(name, Path.GetFullPath(path), BuiltIn: true));
            }
            if (preferred is not null)
            {
                KeyboardDesignChoice? matchingBuiltIn = choices.FirstOrDefault(choice =>
                    choice.BuiltIn && KeyboardLayoutFilesEquivalent(choice.Path, preferred));
                if (matchingBuiltIn is not null)
                    preferred = matchingBuiltIn.Path;
            }

            var registered = new List<string>();
            try
            {
                if (File.Exists(DesignRegistryPath))
                    registered.AddRange(File.ReadAllLines(DesignRegistryPath));
                if (Directory.Exists(DesignLibraryDirectory))
                    registered.AddRange(Directory.EnumerateFiles(DesignLibraryDirectory, "*.kb", SearchOption.AllDirectories));
            }
            catch
            {
                // A damaged optional library must never stop Studio opening.
            }

            foreach (string path in registered
                .Select(NormalizeExistingPath)
                .Where(path => path is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(Path.GetFileNameWithoutExtension))
            {
                if (choices.Any(choice => choice.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (choices.Any(choice => choice.BuiltIn && KeyboardLayoutFilesEquivalent(choice.Path, path)))
                    continue;
                string stem = Path.GetFileNameWithoutExtension(path);
                string name = FriendlyDesignName(stem);
                // Bundled designs are authoritative. Older managed copies can have
                // different serialized bytes while still representing the same named
                // design; showing both produced entries such as
                // "Pug Dragon Keyboard — PugDragonKeyboard-...".
                if (choices.Any(choice => choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                choices.Add(new KeyboardDesignChoice(name, path, BuiltIn: false));
            }

            if (preferred is not null && choices.All(choice => !choice.Path.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
                choices.Add(new KeyboardDesignChoice(FriendlyDesignName(Path.GetFileNameWithoutExtension(preferred)), preferred, BuiltIn: false));

            _designs.Clear();
            _designs.AddRange(choices);
            _designCombo.Items.Clear();
            _designCombo.Items.AddRange(_designs.Cast<object>().ToArray());
            int selected = preferred is null ? -1 : _designs.FindIndex(choice => choice.Path.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            _designCombo.SelectedIndex = selected >= 0 ? selected : (_designs.Count > 0 ? 0 : -1);
        }
        finally
        {
            _updatingDesignLibrary = false;
        }
    }

    private string RegisterDesign(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(DesignLibraryDirectory);
        string assetsRoot = Path.GetFullPath(_assetsDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        string libraryRoot = Path.GetFullPath(DesignLibraryDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string registeredPath = fullPath;
        if (!fullPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
        {
            string stem = Path.GetFileNameWithoutExtension(fullPath);
            string safeStem = string.Concat(stem.Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')).Trim('-');
            if (string.IsNullOrWhiteSpace(safeStem))
                safeStem = "Keyboard";
            byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant());
            string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pathBytes))[..8];
            string projectDirectory = Path.Combine(DesignLibraryDirectory, $"{safeStem}-{suffix}");
            Directory.CreateDirectory(projectDirectory);
            registeredPath = Path.Combine(projectDirectory, $"{safeStem}.kb");
            File.WriteAllText(registeredPath, _document.Serialize(), new System.Text.UTF8Encoding(false));
            CopyArtworkBesideLayout(projectDirectory);
        }

        var paths = File.Exists(DesignRegistryPath)
            ? File.ReadAllLines(DesignRegistryPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
            : [];
        if (!paths.Any(existing => existing.Equals(registeredPath, StringComparison.OrdinalIgnoreCase)))
        {
            paths.Add(registeredPath);
            File.WriteAllLines(DesignRegistryPath, paths.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        return registeredPath;
    }

    private static string? NormalizeExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool KeyboardLayoutFilesEquivalent(string left, string right)
    {
        try
        {
            return File.ReadAllText(left).Equals(File.ReadAllText(right), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string FriendlyDesignName(string stem)
    {
        string value = System.Text.RegularExpressions.Regex.Replace(
            stem.Replace('_', ' ').Replace('-', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ").Trim();
        return string.IsNullOrWhiteSpace(value) ? "Unnamed Keyboard" : value;
    }

    private static void SetIniValue(string path, string section, string key, string value)
    {
        List<string> lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        int sectionStart = lines.FindIndex(line => line.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            int sectionEnd = lines.FindIndex(sectionStart + 1, line => line.TrimStart().StartsWith('['));
            if (sectionEnd < 0)
                sectionEnd = lines.Count;
            int keyLine = -1;
            for (int index = sectionStart + 1; index < sectionEnd; index++)
            {
                string trimmed = lines[index].TrimStart();
                int equals = trimmed.IndexOf('=');
                if (equals > 0 && trimmed[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    keyLine = index;
                    break;
                }
            }
            if (keyLine >= 0)
                lines[keyLine] = $"{key}={value}";
            else
                lines.Insert(sectionEnd, $"{key}={value}");
        }
        File.WriteAllLines(path, lines);
    }

    private void CopyArtworkBesideLayout(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        CopyArtwork(_document.BackgroundImagePath, Path.Combine(directory, "OCUKeyboardBackground.png"));
        for (int index = 0; index < _document.Sprites.Count; index++)
            CopyArtwork(_document.Sprites[index].SourcePath,
                Path.Combine(directory, KeyboardDocument.SpriteFileName(index)));
        CopyArtwork(_document.ControlArrowImagePath, Path.Combine(directory, "OCUKeyboardControlArrow.png"));
        CopyFile(_document.CustomFontMetadataPath, Path.Combine(directory, "OCUKeyboardFont.sfn"));
        CopyFile(_document.CustomFontTexturePath, Path.Combine(directory, "OCUKeyboardFont.png"));
    }

    private static void CopyFile(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return;
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return;
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyArtwork(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return;
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return;
        string extension = Path.GetExtension(source);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
            return;
        }
        using var image = new Bitmap(source);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static OpenFileDialog PngArtworkDialog(string title) => new()
    {
        Title = title,
        Filter = "PNG artwork (*.png)|*.png"
    };

    private static OpenFileDialog BackgroundArtworkDialog(string title) => new()
    {
        Title = title,
        Filter = "Recommended PNG background (*.png)|*.png|JPEG background (*.jpg;*.jpeg)|*.jpg;*.jpeg"
    };

    private bool ConfirmDiscard()
    {
        if (!_document.IsDirty)
            return true;
        DialogResult result = MessageBox.Show(this, "Save your keyboard changes first?", "Unsaved keyboard",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result switch
        {
            DialogResult.Yes => SaveLayout(false),
            DialogResult.No => true,
            _ => false
        };
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard())
            e.Cancel = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_capturingAssignedKey)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode != Keys.None)
                CaptureAssignedKey(keyCode);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingAssignedKey)
        {
            CaptureAssignedKey(e.KeyCode);
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }
        if (e.Control && e.KeyCode == Keys.S) { SaveLayout(e.Shift); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.O) { OpenLayout(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.X && ActiveControl is not TextBox) { _canvas.CutSelection(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.C && ActiveControl is not TextBox) { _canvas.CopySelection(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.V && ActiveControl is not TextBox) { _canvas.PasteClipboard(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Delete && ActiveControl is not TextBox)
        {
            if (!_canvas.DeleteSelection()) DeleteKey();
            e.SuppressKeyPress = true;
        }
    }

    private SudoFont LoadSelectedFont()
    {
        if (_fontCombo.SelectedItem is not FontChoice choice)
            choice = _fonts[0];
        return new SudoFont(choice.MetadataPath, choice.TexturePath);
    }

    private void ImportFont()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add a TrueType or OpenType font",
            Filter = "Font files (*.ttf;*.otf)|*.ttf;*.otf|TrueType font (*.ttf)|*.ttf|OpenType font (*.otf)|*.otf"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            FontChoice fallbackChoice = _fonts.First(font => font.ConfigName == "ocu_nordic");
            var fallback = new SudoFont(fallbackChoice.MetadataPath, fallbackChoice.TexturePath);
            FontImportResult result = FontImporter.Import(dialog.FileName, _document, _assetsDirectory, fallback);
            var choice = new FontChoice(result.DisplayName, result.MetadataPath, result.TexturePath,
                result.ConfigName, result.SourceFontPath);
            int index = _fonts.FindIndex(font => font.ConfigName == result.ConfigName);
            if (index >= 0)
            {
                _fonts[index] = choice;
                _fontCombo.Items[index] = choice;
            }
            else
            {
                _fonts.Add(choice);
                _fontCombo.Items.Add(choice);
                index = _fonts.Count - 1;
            }

            PushUndo();
            _document.FontName = result.ConfigName;
            _document.CustomFontMetadataPath = result.MetadataPath;
            _document.CustomFontTexturePath = result.TexturePath;
            _updatingEditor = true;
            try { _fontCombo.SelectedIndex = index; }
            finally { _updatingEditor = false; }
            _renderer!.Font = new SudoFont(result.MetadataPath, result.TexturePath);
            MarkChanged();
            _canvas.RefreshPreview();
            ReportFontCoverage(result, imported: true);
        }
        catch (Exception exception)
        {
            ShowError("Font import failed", exception);
        }
    }

    private void AuditSelectedCustomFont(bool showMessage)
    {
        if (_updatingEditor || _fontCombo.SelectedItem is not FontChoice choice)
            return;
        try
        {
            var current = new SudoFont(choice.MetadataPath, choice.TexturePath);
            char[] absent = _document.RequiredFontCharacters().Where(character => !current.ContainsGlyph(character)).ToArray();
            if (absent.Length == 0)
                return;
            if (!choice.IsCustom)
            {
                if (showMessage)
                    MessageBox.Show(this,
                        $"{choice.Name} cannot draw the new key text: {DescribeCharacters(absent)}\n\nChoose another font or import a TTF/OTF that supports it.",
                        "Unsupported key text", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(choice.SourceFontPath) || !File.Exists(choice.SourceFontPath))
            {
                if (showMessage)
                    MessageBox.Show(this,
                        $"The selected custom font does not contain the new key text: {DescribeCharacters(absent)}\n\nRe-import its TTF/OTF so Studio can rebuild the font and use OCU Nordic where needed.",
                        "Unsupported key text", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            FontChoice fallbackChoice = _fonts.First(font => font.ConfigName == "ocu_nordic");
            var fallback = new SudoFont(fallbackChoice.MetadataPath, fallbackChoice.TexturePath);
            FontImportResult result = FontImporter.Import(choice.SourceFontPath, _document, _assetsDirectory,
                fallback, choice.ConfigName);
            var refreshed = choice with
            {
                Name = result.DisplayName,
                MetadataPath = result.MetadataPath,
                TexturePath = result.TexturePath,
                SourceFontPath = result.SourceFontPath
            };
            int index = _fonts.IndexOf(choice);
            _fonts[index] = refreshed;
            _fontCombo.Items[index] = refreshed;
            _fontCombo.SelectedIndex = index;
            _document.CustomFontMetadataPath = result.MetadataPath;
            _document.CustomFontTexturePath = result.TexturePath;
            _renderer!.Font = new SudoFont(result.MetadataPath, result.TexturePath);
            _canvas.RefreshPreview();
            if (showMessage)
                ReportFontCoverage(result, imported: false);
        }
        catch (Exception exception)
        {
            ShowError("Font coverage check failed", exception);
        }
    }

    private void ReportFontCoverage(FontImportResult result, bool imported)
    {
        if (result.UnsupportedCharacters.Count > 0)
        {
            MessageBox.Show(this,
                $"These characters are used by existing keys but are not supported by either {result.DisplayName} or OCU Nordic:\n\n{DescribeCharacters(result.UnsupportedCharacters)}\n\nThose key characters cannot be drawn.",
                "Unsupported key text", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus($"{result.DisplayName}: unsupported existing-key characters were reported.");
            return;
        }
        if (result.FallbackCharacters.Count > 0)
        {
            MessageBox.Show(this,
                $"{result.DisplayName} does not contain these characters used by existing keys:\n\n{DescribeCharacters(result.FallbackCharacters)}\n\nOCU Nordic will draw only those missing characters.",
                "OCU Nordic fallback", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus($"{result.DisplayName} converted; {result.FallbackCharacters.Count} existing-key glyphs use OCU Nordic.");
            return;
        }
        SetStatus(imported
            ? $"{result.DisplayName} converted from TTF/OTF for the {_document.Keys.Count} existing keys."
            : $"{result.DisplayName} rebuilt for the newly edited key text.");
    }

    private static string DescribeCharacters(IEnumerable<char> characters)
        => string.Join("  ", characters.Distinct().Select(character => character == ' '
            ? "[space]" : $"{character} (U+{(int)character:X4})"));

    private void UpdateTitle()
    {
        string name = Path.GetFileName(_currentPackagePath ?? _document.SourcePath) ?? "Untitled";
        if (_saveButton is not null)
        {
            _saveButton.Enabled = _document.IsDirty;
            _toolTip.SetToolTip(_saveButton, _document.IsDirty
                ? "Save changes to this keyboard project."
                : "Nothing changed. Use Save As to create another copy.");
        }
        Text = $"{(_document.IsDirty ? "● " : "")}OCU Keyboard Studio — {name}";
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void ShowError(string title, Exception exception)
        => MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string DisplayKeyName(char key) => key switch
    {
        '\t' => "Tab",
        '\n' => "Enter / Done",
        '\b' => "Backspace",
        ' ' => "Space",
        '\x01' => "Shift",
        '\x02' => "Caps Lock",
        '\x03' => "Done",
        '\x04' => "Arrow Up",
        '\x05' => "Arrow Down",
        '\x06' => "Arrow Left",
        '\x07' => "Arrow Right",
        '\x0E' => "Escape",
        '\x1D' => "End",
        '\x1E' => "Ctrl",
        '\x1F' => "Print Screen",
        >= '\x10' and <= '\x1B' => $"F{key - '\x10' + 1}",
        _ => key.ToString()
    };

    private static string ThemeConfigName(KeyboardTheme theme) => theme.Name switch
    {
        "Modern Green" => "modern_green",
        "Modern White" => "modern_white",
        "Modern Blue" => "modern_blue",
        "Modern Amber" => "modern_amber",
        "Modern Purple" => "modern_purple",
        "SkyUI Dark" => "skyui",
        _ => theme.Name.ToLowerInvariant()
    };

    private static NumericUpDown NumberBox(decimal minimum, decimal maximum, int decimals, decimal increment)
        => new()
        {
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimals,
            Increment = increment,
            Width = 150,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = StudioTheme.Input,
            ForeColor = TextPrimary
        };

    private static decimal ClampDecimal(decimal value, NumericUpDown control) => Math.Clamp(value, control.Minimum, control.Maximum);

    private static Button ActionButton(string text, EventHandler action, bool compact = false)
    {
        var button = new ModernPillButton
        {
            Text = text,
            AutoSize = true,
            Height = compact ? 30 : 34,
            ForeColor = TextPrimary,
            Padding = compact ? new Padding(7, 1, 7, 1) : new Padding(10, 2, 10, 2),
            Margin = new Padding(3, 1, 3, 1),
            Cursor = Cursors.Hand,
            Destructive = text.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Remove", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Clear", StringComparison.OrdinalIgnoreCase)
        };
        button.Click += action;
        return button;
    }

    private static Button SwatchButton()
    {
        var button = new Button
        {
            Text = "Choose on wheel",
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Edge;
        return button;
    }

    private static void SetSwatch(Button button, Color color)
    {
        button.BackColor = color;
        double luminance = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
        button.ForeColor = luminance > 150 ? Color.Black : Color.White;
        button.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static TabPage InspectorPage(string title) => new(title)
    {
        BackColor = Surface,
        ForeColor = TextPrimary,
        Padding = new Padding(8),
        AutoScroll = true
    };

    private static TableLayoutPanel InspectorTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(2),
            BackColor = Surface
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static Control Spacer(int width) => new Panel { Width = width, Height = 1, Margin = Padding.Empty };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextMuted,
        Margin = new Padding(9, 9, 4, 0)
    };

    private static void StyleCombo(ComboBox combo)
    {
        combo.BackColor = StudioTheme.Input;
        combo.ForeColor = TextPrimary;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Height = 32;
        combo.Margin = new Padding(0, 4, 4, 0);
    }

    private static void StyleCheck(ButtonBase check)
    {
        check.ForeColor = TextPrimary;
        check.BackColor = Color.Transparent;
        check.Margin = new Padding(8, 8, 3, 0);
    }

    private static void AddSection(TableLayoutPanel table, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = AccentBright,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Margin = new Padding(0, 18, 0, 7)
        };
        AddWide(table, label);
    }

    private static void AddHint(TableLayoutPanel table, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            ForeColor = TextMuted,
            Margin = new Padding(0, 7, 0, 3)
        };
        AddWide(table, label);
    }

    private static void AddRow(TableLayoutPanel table, string caption, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = caption, AutoSize = true, ForeColor = TextMuted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 5, 4) };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 3, 0, 3);
        if (control is TextBox or NumericUpDown or ComboBox or ListBox)
            control.BackColor = StudioTheme.Input;
        control.ForeColor = TextPrimary;
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void AddWide(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 3, 0, 3);
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private sealed record FontChoice(string Name, string MetadataPath, string TexturePath, string ConfigName, string? SourceFontPath)
    {
        public bool IsCustom => ConfigName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase);
        public override string ToString() => Name;
    }
}
