using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OCUKeyboardStudio;

internal enum CanvasSelectionKind
{
    None, KeyContent, KeyPlate, Sprite, Background,
    Control, ControlUpArrow, ControlLabel, ControlValue, ControlDownArrow,
    TopTextBar, TopMode, TopLock
}
internal enum CanvasDragOperation
{
    None, Move, ResizeLeft, ResizeRight, ResizeTop, ResizeBottom,
    ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, Rotate
}

internal readonly record struct CanvasSelectionState(
    CanvasSelectionKind Kind,
    int KeyId,
    Guid? SpriteId,
    KeyboardRuntimeControl Control,
    KeyboardControlPart ControlPart);

internal sealed class KeyboardCanvas : Control
{
    private KeyboardDocument? _document;
    private KeyboardRenderer? _renderer;
    private Bitmap? _preview;
    private int _selectedKeyId = -1;
    private Guid? _selectedSpriteId;
    private CanvasSelectionKind _selectionKind;
    private CanvasDragOperation _dragOperation;
    private bool _dragging;
    private PointF _dragStartTexture;
    private KeyboardKey? _dragStartKey;
    private RectangleF _dragStartRectangle;
    private float _dragStartControlX;
    private float _dragStartControlY;
    private float _dragStartTopX;
    private float _dragStartTopY;
    private float _dragStartRotation;
    private float _dragStartAngle;
    private KeyboardControlDesign? _dragStartControlDesign;
    private KeyboardSprite? _spriteClipboard;
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 50 };
    private readonly System.Diagnostics.Stopwatch _animationClock = System.Diagnostics.Stopwatch.StartNew();

    public event EventHandler? SelectionChanged;
    public event EventHandler? EditStarted;
    public event EventHandler? DocumentChanged;
    public event EventHandler? EditCompleted;
    public event EventHandler? ChooseControlArrowRequested;
    public event EventHandler? UseBuiltInControlArrowRequested;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public KeyboardDocument? Document
    {
        get => _document;
        set => ReplaceDocument(value, preserveSelection: false);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public KeyboardRenderer? Renderer { get => _renderer; set { _renderer = value; RefreshPreview(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SnapToTenth { get; set; } = true;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public KeyboardRuntimeControl SelectedControl { get; set; } = KeyboardRuntimeControl.Size;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public KeyboardControlPart SelectedControlPart { get; private set; } = KeyboardControlPart.Group;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CanvasSelectionKind SelectionKind => _selectionKind;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public KeyboardTopElement? SelectedTopElement => _selectionKind switch
    {
        CanvasSelectionKind.TopTextBar => KeyboardTopElement.TextBar,
        CanvasSelectionKind.TopMode => KeyboardTopElement.Mode,
        CanvasSelectionKind.TopLock => KeyboardTopElement.Lock,
        _ => null
    };

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedKeyId
    {
        get => _selectedKeyId;
        set
        {
            int normalized = value;
            if (_document is not null && !_document.Keys.Any(key => key.Id == normalized)) normalized = -1;
            bool changed = _selectedKeyId != normalized;
            _selectedKeyId = normalized;
            if (_renderer is not null) _renderer.SelectedKeyId = normalized;
            if (normalized >= 0 && _selectionKind is not (CanvasSelectionKind.KeyContent or CanvasSelectionKind.KeyPlate))
                _selectionKind = CanvasSelectionKind.KeyContent;
            if (changed) { RefreshPreview(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Guid? SelectedSpriteId
    {
        get => _selectedSpriteId;
        set
        {
            Guid? normalized = value is Guid id && _document?.Sprites.Any(sprite => sprite.Id == id) == true ? id : null;
            bool changed = _selectedSpriteId != normalized || (normalized is not null && _selectionKind != CanvasSelectionKind.Sprite);
            _selectedSpriteId = normalized;
            if (normalized is not null) _selectionKind = CanvasSelectionKind.Sprite;
            if (changed) { RefreshPreview(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
        }
    }

    public KeyboardKey? SelectedKey => _document?.Keys.FirstOrDefault(key => key.Id == _selectedKeyId);
    public KeyboardSprite? SelectedSprite => _document?.Sprites.FirstOrDefault(sprite => sprite.Id == _selectedSpriteId);

    public CanvasSelectionState CaptureSelection() => new(
        _selectionKind, _selectedKeyId, _selectedSpriteId, SelectedControl, SelectedControlPart);

    public void ReplaceDocument(KeyboardDocument? value, bool preserveSelection)
    {
        CanvasSelectionState previous = CaptureSelection();
        _document = value;

        if (preserveSelection && value is not null)
        {
            _selectedKeyId = value.Keys.Any(key => key.Id == previous.KeyId) ? previous.KeyId : value.Keys.FirstOrDefault()?.Id ?? -1;
            _selectedSpriteId = previous.SpriteId is Guid spriteId && value.Sprites.Any(sprite => sprite.Id == spriteId)
                ? spriteId : value.Sprites.FirstOrDefault()?.Id;
            SelectedControl = previous.Control;
            SelectedControlPart = previous.ControlPart;
            _selectionKind = NormalizeSelectionKind(previous.Kind);
        }
        else
        {
            _selectedSpriteId = value?.Sprites.FirstOrDefault()?.Id;
            _selectedKeyId = value?.Keys.FirstOrDefault()?.Id ?? -1;
            _selectionKind = _selectedKeyId >= 0 ? CanvasSelectionKind.KeyContent : CanvasSelectionKind.None;
        }

        if (_renderer is not null) _renderer.SelectedKeyId = _selectedKeyId;
        RefreshPreview();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private CanvasSelectionKind NormalizeSelectionKind(CanvasSelectionKind requested)
    {
        if (_document is null)
            return CanvasSelectionKind.None;
        if (requested is CanvasSelectionKind.KeyContent or CanvasSelectionKind.KeyPlate)
            return _selectedKeyId >= 0 ? requested : CanvasSelectionKind.None;
        if (requested == CanvasSelectionKind.Sprite)
            return _selectedSpriteId is not null ? requested : CanvasSelectionKind.None;
        if (requested == CanvasSelectionKind.Background)
            return !string.IsNullOrWhiteSpace(_document.BackgroundImagePath)
                || !string.IsNullOrWhiteSpace(_document.BackgroundFileName)
                ? requested : CanvasSelectionKind.None;
        return requested;
    }

    public KeyboardCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(9, 11, 15);
        TabStop = true;
        AllowDrop = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        _animationTimer.Tick += (_, _) => { if (Visible && _document?.HasBreathingEffects == true) RefreshPreview(); };
        _animationTimer.Start();
    }

    public void SelectSprite(KeyboardSprite? sprite) => SelectedSpriteId = sprite?.Id;
    public void SelectBackground()
    {
        if (_document is not null && !string.IsNullOrWhiteSpace(_document.BackgroundImagePath)) SetSelection(CanvasSelectionKind.Background);
    }
    public void SelectControl(KeyboardRuntimeControl control, KeyboardControlPart part = KeyboardControlPart.Group)
    {
        CanvasSelectionKind kind = CanvasKindForControlPart(part);
        bool selectionEventWillFire = _selectionKind != kind;
        SelectedControl = control;
        SelectedControlPart = part;
        SetSelection(kind);
        if (!selectionEventWillFire) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshPreview()
    {
        if (_renderer is not null) _renderer.AnimationTimeSeconds = _animationClock.Elapsed.TotalSeconds;
        _preview?.Dispose();
        _preview = _document is not null && _renderer is not null ? _renderer.Render(_document) : null;
        Invalidate();
    }

    public Bitmap RenderExact()
    {
        if (_document is null || _renderer is null) throw new InvalidOperationException("No keyboard is loaded.");
        return _renderer.Render(_document);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _animationTimer.Stop(); _animationTimer.Dispose(); _preview?.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_preview is null)
        {
            using var brush = new SolidBrush(Color.FromArgb(140, 160, 170));
            e.Graphics.DrawString("Open an OCU .kb layout", Font, brush, ClientRectangle,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return;
        }
        RectangleF destination = PreviewBounds();
        using (var shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            e.Graphics.FillRoundedRectangle(shadow, RectangleF.Inflate(destination, 7, 7), 18);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_preview, destination);

        if (SelectionRectangle() is not RectangleF selectedTexture) return;
        RectangleF selected = TextureToClient(selectedTexture);
        using var pen = new Pen(Color.FromArgb(255, 132, 242, 158), 2f) { DashStyle = DashStyle.Dash };
        e.Graphics.DrawRectangle(pen, selected.X, selected.Y, selected.Width, selected.Height);
        if (CanResizeSelection())
        {
            using var brush = new SolidBrush(Color.FromArgb(255, 132, 242, 158));
            foreach (RectangleF handle in ResizeHandleRectangles(selected).Values) e.Graphics.FillRectangle(brush, handle);
        }
        if (CanRotateSelection())
        {
            PointF rotate = new(selected.Left + selected.Width / 2f, selected.Top - 25f);
            using var connector = new Pen(Color.FromArgb(210, 132, 242, 158), 1.5f);
            e.Graphics.DrawLine(connector, selected.Left + selected.Width / 2f, selected.Top, rotate.X, rotate.Y + 6);
            using var brush = new SolidBrush(Color.FromArgb(255, 132, 242, 158));
            e.Graphics.FillEllipse(brush, rotate.X - 7, rotate.Y - 7, 14, 14);
        }
    }

    private RectangleF? SelectionRectangle()
    {
        if (_document is null || _renderer is null) return null;
        return _selectionKind switch
        {
            CanvasSelectionKind.KeyContent when SelectedKey is KeyboardKey key => _renderer.KeyContentRectangle(_document, key),
            CanvasSelectionKind.KeyPlate when SelectedKey is KeyboardKey key => _renderer.KeyRectangle(_document, key),
            CanvasSelectionKind.Sprite when SelectedSprite is KeyboardSprite sprite => SpriteRectangle(sprite),
            CanvasSelectionKind.Background when !string.IsNullOrWhiteSpace(_document.BackgroundImagePath) => BackgroundRectangle(),
            CanvasSelectionKind.Control => _renderer.RuntimeControlRectangle(_document, SelectedControl),
            CanvasSelectionKind.ControlUpArrow => _renderer.RuntimeControlPartRectangle(_document, SelectedControl, KeyboardControlPart.UpArrow),
            CanvasSelectionKind.ControlLabel => _renderer.RuntimeControlPartRectangle(_document, SelectedControl, KeyboardControlPart.Label),
            CanvasSelectionKind.ControlValue => _renderer.RuntimeControlPartRectangle(_document, SelectedControl, KeyboardControlPart.Value),
            CanvasSelectionKind.ControlDownArrow => _renderer.RuntimeControlPartRectangle(_document, SelectedControl, KeyboardControlPart.DownArrow),
            CanvasSelectionKind.TopTextBar => _renderer.TopElementRectangle(_document, KeyboardTopElement.TextBar),
            CanvasSelectionKind.TopMode => _renderer.TopElementRectangle(_document, KeyboardTopElement.Mode),
            CanvasSelectionKind.TopLock => _renderer.TopElementRectangle(_document, KeyboardTopElement.Lock),
            _ => null
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_document is null || _renderer is null) return;
        PointF texturePoint = ClientToTexture(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            SelectAt(texturePoint, true);
            ShowCanvasMenu(e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        CanvasDragOperation operation = HitSelectionHandle(e.Location);
        if (operation != CanvasDragOperation.None) { BeginDrag(texturePoint, operation); return; }
        if (SelectAt(texturePoint, false)) BeginDrag(texturePoint, CanvasDragOperation.Move);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _document is null || _renderer is null)
        {
            Cursor = CursorFor(HitSelectionHandle(e.Location));
            if (Cursor == Cursors.Default && HitTest(ClientToTexture(e.Location)) != CanvasSelectionKind.None) Cursor = Cursors.SizeAll;
            return;
        }
        PointF point = ClientToTexture(e.Location);
        ApplyDrag(point, point.X - _dragStartTexture.X, point.Y - _dragStartTexture.Y);
        _document.IsDirty = true;
        RefreshPreview();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        _dragOperation = CanvasDragOperation.None;
        _dragStartKey = null;
        _dragStartControlDesign = null;
        Capture = false;
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_document is null || e.Delta == 0) return;
        float factor = e.Delta > 0 ? 1.05f : 0.95f;
        bool handled = _selectionKind is CanvasSelectionKind.KeyContent or CanvasSelectionKind.Sprite or CanvasSelectionKind.Background
            or CanvasSelectionKind.ControlLabel or CanvasSelectionKind.ControlValue;
        if (!handled) return;
        PerformEdit(() =>
        {
            if (_selectionKind == CanvasSelectionKind.KeyContent && SelectedKey is KeyboardKey key)
                key.LabelScale = MathF.Round(Math.Clamp(key.LabelScale * factor, 0.25f, 3f) * 20f) / 20f;
            else if (_selectionKind == CanvasSelectionKind.Sprite && SelectedSprite is KeyboardSprite sprite)
                ScaleAroundCenter(sprite, factor);
            else if (_selectionKind == CanvasSelectionKind.Background)
                ScaleBackgroundAroundCenter(factor);
            else if (_selectionKind is CanvasSelectionKind.ControlLabel or CanvasSelectionKind.ControlValue)
            {
                KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
                if (_selectionKind == CanvasSelectionKind.ControlLabel)
                    design.LabelScale = MathF.Round(Math.Clamp(design.LabelScale * factor, 0.2f, 3f) * 100f) / 100f;
                else
                    design.ValueScale = MathF.Round(Math.Clamp(design.ValueScale * factor, 0.2f, 3f) * 100f) / 100f;
            }
        });
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = ContainsPngFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (_document is null || e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        string[] pngs = files.Where(IsPngFile).ToArray();
        if (pngs.Length == 0) return;
        PointF drop = ClientToTexture(PointToClient(new Point(e.X, e.Y)));
        PerformEdit(() => { foreach (string file in pngs) AddSpriteFromPath(file, drop); });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CopySelection()
    {
        if (SelectedSprite is KeyboardSprite sprite)
        {
            _spriteClipboard = sprite.Clone();
            TryCopyImage(sprite.SourcePath);
            return true;
        }
        if (IsParchmentRibbonKey(SelectedKey) && _renderer is not null)
        {
            _spriteClipboard = null;
            return TryCopyImage(Path.Combine(_renderer.AssetsDirectory, "spacebar.png"));
        }
        return false;
    }

    public bool CutSelection()
    {
        if (IsParchmentRibbonKey(SelectedKey))
        {
            if (!CopySelection())
                return false;
            return DeleteSelection();
        }
        if (SelectedSprite is not KeyboardSprite sprite || _document is null) return false;
        CopySelection();
        PerformEdit(() =>
        {
            _document.Sprites.Remove(sprite);
            _selectedSpriteId = _document.Sprites.LastOrDefault()?.Id;
            _selectionKind = _selectedSpriteId is null ? CanvasSelectionKind.None : CanvasSelectionKind.Sprite;
        });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool PasteClipboard()
    {
        if (_document is null) return false;
        KeyboardSprite? pasted;
        if (_spriteClipboard is not null)
        {
            pasted = _spriteClipboard.Clone();
            pasted.Id = Guid.NewGuid();
            pasted.X += 16;
            pasted.Y += 16;
        }
        else
        {
            string? path = ClipboardPngPath();
            pasted = path is null ? null : CreateSprite(path, new PointF(KeyboardRenderer.TextureWidth / 2f, KeyboardRenderer.TextureHeight / 2f));
        }
        if (pasted is null) return false;
        PerformEdit(() => { _document.Sprites.Add(pasted); _selectedSpriteId = pasted.Id; _selectionKind = CanvasSelectionKind.Sprite; });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool DeleteSelection()
    {
        if (_document is null) return false;
        if (_selectionKind == CanvasSelectionKind.KeyContent && IsParchmentRibbonKey(SelectedKey))
        {
            PerformEdit(() =>
            {
                _document.ParchmentRibbonEnabled = false;
                _selectionKind = CanvasSelectionKind.KeyPlate;
            });
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        if (_selectionKind == CanvasSelectionKind.Sprite && SelectedSprite is KeyboardSprite sprite)
        {
            PerformEdit(() =>
            {
                _document.Sprites.Remove(sprite);
                _selectedSpriteId = _document.Sprites.LastOrDefault()?.Id;
                _selectionKind = _selectedSpriteId is null ? CanvasSelectionKind.None : CanvasSelectionKind.Sprite;
            });
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        if (_selectionKind == CanvasSelectionKind.Background)
        {
            PerformEdit(() =>
            {
                _document.BackgroundImagePath = null;
                _document.BackgroundFileName = null;
                _document.BackgroundBreatheEnabled = false;
                _selectionKind = CanvasSelectionKind.None;
            });
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private void BeginDrag(PointF point, CanvasDragOperation operation)
    {
        if (SelectionRectangle() is not RectangleF rectangle) return;
        _dragging = true;
        _dragOperation = operation;
        _dragStartTexture = point;
        _dragStartRectangle = rectangle;
        _dragStartKey = SelectedKey?.Clone();
        GetControlOffsets(SelectedControl, out _dragStartControlX, out _dragStartControlY);
        GetTopElementOffsets(SelectedTopElement, out _dragStartTopX, out _dragStartTopY);
        _dragStartControlDesign = IsControlSelection(_selectionKind) && _document is not null
            ? _document.GetControlDesign(SelectedControl).Clone() : null;
        _dragStartRotation = _selectionKind == CanvasSelectionKind.Sprite ? SelectedSprite?.Rotation ?? 0
            : _selectionKind == CanvasSelectionKind.Background ? _document?.BackgroundRotation ?? 0 : 0;
        PointF center = Center(rectangle);
        _dragStartAngle = MathF.Atan2(point.Y - center.Y, point.X - center.X) * 180f / MathF.PI;
        Capture = true;
        EditStarted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDrag(PointF point, float dx, float dy)
    {
        if (_document is null || _renderer is null) return;
        if (_dragOperation == CanvasDragOperation.Rotate)
        {
            PointF center = Center(_dragStartRectangle);
            float angle = MathF.Atan2(point.Y - center.Y, point.X - center.X) * 180f / MathF.PI;
            float rotation = NormalizeRotation(_dragStartRotation + angle - _dragStartAngle);
            if (_selectionKind == CanvasSelectionKind.Sprite && SelectedSprite is KeyboardSprite rotationSprite) rotationSprite.Rotation = rotation;
            else if (_selectionKind == CanvasSelectionKind.Background) _document.BackgroundRotation = rotation;
            return;
        }
        if (_dragOperation == CanvasDragOperation.Move)
        {
            switch (_selectionKind)
            {
                case CanvasSelectionKind.KeyContent when SelectedKey is KeyboardKey contentKey && _dragStartKey is not null:
                    contentKey.LabelOffsetX = RoundPixel(_dragStartKey.LabelOffsetX + dx); contentKey.LabelOffsetY = RoundPixel(_dragStartKey.LabelOffsetY + dy); return;
                case CanvasSelectionKind.KeyPlate when SelectedKey is KeyboardKey plateKey && _dragStartKey is not null:
                    MoveKey(plateKey, dx, dy); return;
                case CanvasSelectionKind.Sprite when SelectedSprite is KeyboardSprite movingSprite:
                    movingSprite.X = MathF.Round(_dragStartRectangle.X + dx); movingSprite.Y = MathF.Round(_dragStartRectangle.Y + dy); return;
                case CanvasSelectionKind.Background:
                    _document.BackgroundX = MathF.Round(_dragStartRectangle.X + dx); _document.BackgroundY = MathF.Round(_dragStartRectangle.Y + dy); return;
                case CanvasSelectionKind.Control:
                    SetControlOffsets(SelectedControl, MathF.Round(_dragStartControlX + dx), MathF.Round(_dragStartControlY + dy)); return;
                case CanvasSelectionKind.TopTextBar:
                case CanvasSelectionKind.TopMode:
                case CanvasSelectionKind.TopLock:
                    SetTopElementOffsets(SelectedTopElement, MathF.Round(_dragStartTopX + dx), MathF.Round(_dragStartTopY + dy)); return;
                case CanvasSelectionKind.ControlUpArrow when _dragStartControlDesign is not null:
                {
                    KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
                    design.UpOffsetX = _dragStartControlDesign.UpOffsetX + dx;
                    design.UpOffsetY = _dragStartControlDesign.UpOffsetY + dy;
                    return;
                }
                case CanvasSelectionKind.ControlDownArrow when _dragStartControlDesign is not null:
                {
                    KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
                    design.DownOffsetX = _dragStartControlDesign.DownOffsetX + dx;
                    design.DownOffsetY = _dragStartControlDesign.DownOffsetY + dy;
                    return;
                }
                case CanvasSelectionKind.ControlLabel when _dragStartControlDesign is not null:
                {
                    KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
                    design.LabelOffsetX = _dragStartControlDesign.LabelOffsetX + dx;
                    design.LabelOffsetY = _dragStartControlDesign.LabelOffsetY + dy;
                    return;
                }
                case CanvasSelectionKind.ControlValue when _dragStartControlDesign is not null:
                {
                    KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
                    design.ValueOffsetX = _dragStartControlDesign.ValueOffsetX + dx;
                    design.ValueOffsetY = _dragStartControlDesign.ValueOffsetY + dy;
                    return;
                }
            }
        }
        if (_selectionKind == CanvasSelectionKind.KeyPlate && SelectedKey is KeyboardKey key && _dragStartKey is not null)
        {
            ResizeKey(key, dx, dy);
            return;
        }
        RectangleF resized = ResizeRectangle(_dragStartRectangle, dx, dy, _dragOperation, 8, 8);
        if (_selectionKind == CanvasSelectionKind.Sprite && SelectedSprite is KeyboardSprite resizingSprite) SetSpriteRectangle(resizingSprite, resized);
        else if (_selectionKind == CanvasSelectionKind.Background)
        {
            _document.BackgroundX = resized.X; _document.BackgroundY = resized.Y;
            _document.BackgroundWidth = resized.Width; _document.BackgroundHeight = resized.Height;
        }
        else if (_selectionKind == CanvasSelectionKind.Control)
            ResizeControlGroup(resized);
        else if (_selectionKind is CanvasSelectionKind.ControlUpArrow or CanvasSelectionKind.ControlDownArrow)
            ResizeControlArrow(resized, _selectionKind == CanvasSelectionKind.ControlUpArrow);
        else if (_selectionKind == CanvasSelectionKind.KeyContent && SelectedKey is KeyboardKey visualKey
            && IsResizableKeyContent(visualKey) && _dragStartKey is not null)
            ResizeVisualKeyContent(visualKey, resized);
    }

    private bool SelectAt(PointF point, bool preferSelectedArtwork)
    {
        if (_document is null || _renderer is null) return false;
        if (preferSelectedArtwork && _selectionKind == CanvasSelectionKind.Sprite
            && SelectionRectangle() is RectangleF selectedSprite && selectedSprite.Contains(point))
            return true;
        if (preferSelectedArtwork)
        {
            KeyboardSprite? contextSprite = _document.Sprites.AsEnumerable().Reverse()
                .FirstOrDefault(item => SpriteRectangle(item).Contains(point));
            if (contextSprite is not null)
            {
                SelectedSpriteId = contextSprite.Id;
                return true;
            }
        }
        foreach (KeyboardTopElement element in Enum.GetValues<KeyboardTopElement>().Reverse())
        {
            if (_renderer.TopElementRectangle(_document, element).Contains(point))
            {
                SetSelection(CanvasKindForTopElement(element));
                return true;
            }
        }
        foreach (KeyboardRuntimeControl control in Enum.GetValues<KeyboardRuntimeControl>().Reverse())
        {
            if (_renderer.RuntimeControlPartRectangle(_document, control, KeyboardControlPart.DownArrow).Contains(point))
            { SelectControl(control, KeyboardControlPart.DownArrow); return true; }
            if (_renderer.IsPointOnRuntimeControlText(_document, control, label: false, point))
            { SelectControl(control, KeyboardControlPart.Value); return true; }
            if (_renderer.IsPointOnRuntimeControlText(_document, control, label: true, point))
            { SelectControl(control, KeyboardControlPart.Label); return true; }
            if (_renderer.RuntimeControlPartRectangle(_document, control, KeyboardControlPart.UpArrow).Contains(point))
            { SelectControl(control, KeyboardControlPart.UpArrow); return true; }
            if (_renderer.RuntimeControlRectangle(_document, control).Contains(point))
            { SelectControl(control, KeyboardControlPart.Group); return true; }
        }
        KeyboardKey? content = _document.Keys.AsEnumerable().Reverse().FirstOrDefault(key => _renderer.IsPointOnKeyContent(_document, key, point));
        if (content is not null) { SetSelection(CanvasSelectionKind.KeyContent, content); return true; }
        KeyboardKey? plate = _document.Keys.AsEnumerable().Reverse().FirstOrDefault(key => _renderer.KeyRectangle(_document, key).Contains(point));
        if (plate is not null) { SetSelection(CanvasSelectionKind.KeyPlate, plate); return true; }
        KeyboardSprite? sprite = _document.Sprites.AsEnumerable().Reverse().FirstOrDefault(item => SpriteRectangle(item).Contains(point));
        if (sprite is not null) { SelectedSpriteId = sprite.Id; return true; }
        if (!string.IsNullOrWhiteSpace(_document.BackgroundImagePath) && BackgroundRectangle().Contains(point)) { SetSelection(CanvasSelectionKind.Background); return true; }
        SetSelection(CanvasSelectionKind.None);
        return false;
    }

    private CanvasSelectionKind HitTest(PointF point)
    {
        if (_document is null || _renderer is null) return CanvasSelectionKind.None;
        foreach (KeyboardTopElement element in Enum.GetValues<KeyboardTopElement>())
            if (_renderer.TopElementRectangle(_document, element).Contains(point)) return CanvasKindForTopElement(element);
        foreach (KeyboardRuntimeControl control in Enum.GetValues<KeyboardRuntimeControl>())
        {
            if (_renderer.RuntimeControlPartRectangle(_document, control, KeyboardControlPart.UpArrow).Contains(point)) return CanvasSelectionKind.ControlUpArrow;
            if (_renderer.RuntimeControlPartRectangle(_document, control, KeyboardControlPart.DownArrow).Contains(point)) return CanvasSelectionKind.ControlDownArrow;
            if (_renderer.IsPointOnRuntimeControlText(_document, control, true, point)) return CanvasSelectionKind.ControlLabel;
            if (_renderer.IsPointOnRuntimeControlText(_document, control, false, point)) return CanvasSelectionKind.ControlValue;
            if (_renderer.RuntimeControlRectangle(_document, control).Contains(point)) return CanvasSelectionKind.Control;
        }
        if (_document.Keys.Any(key => _renderer.KeyRectangle(_document, key).Contains(point))) return CanvasSelectionKind.KeyPlate;
        if (_document.Sprites.Any(sprite => SpriteRectangle(sprite).Contains(point))) return CanvasSelectionKind.Sprite;
        return !string.IsNullOrWhiteSpace(_document.BackgroundImagePath) && BackgroundRectangle().Contains(point) ? CanvasSelectionKind.Background : CanvasSelectionKind.None;
    }

    private void SetSelection(CanvasSelectionKind kind, KeyboardKey? key = null)
    {
        bool changed = _selectionKind != kind;
        _selectionKind = kind;
        if (key is not null)
        {
            changed |= _selectedKeyId != key.Id;
            _selectedKeyId = key.Id;
            if (_renderer is not null) _renderer.SelectedKeyId = key.Id;
        }
        RefreshPreview();
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private CanvasDragOperation HitSelectionHandle(Point point)
    {
        if (SelectionRectangle() is not RectangleF texture) return CanvasDragOperation.None;
        RectangleF client = TextureToClient(texture);
        if (CanRotateSelection())
        {
            PointF rotate = new(client.Left + client.Width / 2f, client.Top - 25f);
            if (DistanceSquared(point, rotate) <= 121) return CanvasDragOperation.Rotate;
        }
        if (!CanResizeSelection()) return CanvasDragOperation.None;
        foreach ((CanvasDragOperation operation, RectangleF rectangle) in ResizeHandleRectangles(client))
            if (rectangle.Contains(point)) return operation;
        return CanvasDragOperation.None;
    }

    private static Dictionary<CanvasDragOperation, RectangleF> ResizeHandleRectangles(RectangleF rectangle)
    {
        const float size = 10;
        RectangleF At(float x, float y) => new(x - size / 2, y - size / 2, size, size);
        return new()
        {
            [CanvasDragOperation.ResizeTopLeft] = At(rectangle.Left, rectangle.Top),
            [CanvasDragOperation.ResizeTop] = At(rectangle.Left + rectangle.Width / 2, rectangle.Top),
            [CanvasDragOperation.ResizeTopRight] = At(rectangle.Right, rectangle.Top),
            [CanvasDragOperation.ResizeRight] = At(rectangle.Right, rectangle.Top + rectangle.Height / 2),
            [CanvasDragOperation.ResizeBottomRight] = At(rectangle.Right, rectangle.Bottom),
            [CanvasDragOperation.ResizeBottom] = At(rectangle.Left + rectangle.Width / 2, rectangle.Bottom),
            [CanvasDragOperation.ResizeBottomLeft] = At(rectangle.Left, rectangle.Bottom),
            [CanvasDragOperation.ResizeLeft] = At(rectangle.Left, rectangle.Top + rectangle.Height / 2)
        };
    }

    private bool CanResizeSelection() => _selectionKind is CanvasSelectionKind.KeyPlate or CanvasSelectionKind.Sprite or CanvasSelectionKind.Background
        or CanvasSelectionKind.Control or CanvasSelectionKind.ControlUpArrow or CanvasSelectionKind.ControlDownArrow
        || (_selectionKind == CanvasSelectionKind.KeyContent && IsResizableKeyContent(SelectedKey));
    private bool CanRotateSelection() => _selectionKind is CanvasSelectionKind.Sprite or CanvasSelectionKind.Background;
    private static Cursor CursorFor(CanvasDragOperation operation) => operation switch
    {
        CanvasDragOperation.ResizeLeft or CanvasDragOperation.ResizeRight => Cursors.SizeWE,
        CanvasDragOperation.ResizeTop or CanvasDragOperation.ResizeBottom => Cursors.SizeNS,
        CanvasDragOperation.ResizeTopLeft or CanvasDragOperation.ResizeBottomRight => Cursors.SizeNWSE,
        CanvasDragOperation.ResizeTopRight or CanvasDragOperation.ResizeBottomLeft => Cursors.SizeNESW,
        CanvasDragOperation.Rotate => Cursors.Hand,
        _ => Cursors.Default
    };

    private void MoveKey(KeyboardKey key, float dx, float dy)
    {
        if (_document is null || _renderer is null || _dragStartKey is null) return;
        int pitch = _renderer.KeySize(_document) + KeyboardRenderer.Padding;
        key.X = Math.Max(0, _dragStartKey.X + dx / pitch); key.Y = Math.Max(0, _dragStartKey.Y + dy / pitch);
        if (SnapToTenth) { key.X = MathF.Round(key.X * 10f) / 10f; key.Y = MathF.Round(key.Y * 10f) / 10f; }
    }

    private void ResizeKey(KeyboardKey key, float dx, float dy)
    {
        if (_document is null || _renderer is null || _dragStartKey is null) return;
        int size = _renderer.KeySize(_document), pitch = size + KeyboardRenderer.Padding;
        RectangleF precise = ResizeRectangle(_dragStartRectangle, dx, dy, _dragOperation, size * 0.25f, size * 0.25f);
        key.X = Math.Max(0, _dragStartKey.X + (precise.Left - _dragStartRectangle.Left) / pitch);
        key.Y = Math.Max(0, _dragStartKey.Y + (precise.Top - _dragStartRectangle.Top) / pitch);
        key.Width = Math.Max(0.25f, precise.Width / size);
        key.Height = Math.Max(0.25f, precise.Height / size);
        key.SpansToRight = false;
    }

    private void ResizeControlGroup(RectangleF rectangle)
    {
        if (_document is null || _dragStartControlDesign is null)
            return;
        KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
        design.Width = Math.Max(24, rectangle.Width);
        design.Height = Math.Max(40, rectangle.Height);
        SetControlOffsets(SelectedControl,
            _dragStartControlX + rectangle.Left - _dragStartRectangle.Left,
            _dragStartControlY + rectangle.Top - _dragStartRectangle.Top);
    }

    private void ResizeControlArrow(RectangleF rectangle, bool up)
    {
        if (_document is null)
            return;
        RectangleF group = _renderer!.RuntimeControlRectangle(_document, SelectedControl);
        KeyboardControlDesign design = _document.GetControlDesign(SelectedControl);
        if (up)
        {
            design.UpWidth = Math.Max(4, rectangle.Width);
            design.UpHeight = Math.Max(4, rectangle.Height);
            design.UpOffsetX = rectangle.Left + rectangle.Width / 2f - (group.Left + group.Width / 2f);
            design.UpOffsetY = rectangle.Top - group.Top;
        }
        else
        {
            design.DownWidth = Math.Max(4, rectangle.Width);
            design.DownHeight = Math.Max(4, rectangle.Height);
            design.DownOffsetX = rectangle.Left + rectangle.Width / 2f - (group.Left + group.Width / 2f);
            design.DownOffsetY = rectangle.Bottom - group.Bottom;
        }
    }

    private void ResizeVisualKeyContent(KeyboardKey key, RectangleF rectangle)
    {
        if (_document is null || _renderer is null || _dragStartKey is null)
            return;
        RectangleF plate = _renderer.KeyRectangle(_document, key);
        float baseWidth = IsArrowKey(key) ? 20f : plate.Width;
        float baseHeight = IsArrowKey(key) ? 16f : plate.Height;
        float scaleX = rectangle.Width / Math.Max(1f, baseWidth);
        float scaleY = rectangle.Height / Math.Max(1f, baseHeight);
        float originalScale = _dragStartKey.LabelScale;
        float scale = Math.Abs(scaleX - originalScale) >= Math.Abs(scaleY - originalScale) ? scaleX : scaleY;
        key.LabelScale = MathF.Round(Math.Clamp(scale, 0.25f, 3f) * 20f) / 20f;

        float hoverOffset = key.Id == _renderer.SelectedKeyId ? (_renderer.Pressed ? 2 : -2) : 0;
        key.LabelOffsetX = RoundPixel(rectangle.Left + rectangle.Width / 2f - (plate.Left + plate.Width / 2f));
        key.LabelOffsetY = RoundPixel(rectangle.Top + rectangle.Height / 2f - (plate.Top + plate.Height / 2f) - hoverOffset);
    }

    private static bool IsArrowKey(KeyboardKey? key)
        => key?.Character is '\x04' or '\x05' or '\x06' or '\x07';

    private bool IsParchmentRibbonKey(KeyboardKey? key)
        => _document?.ParchmentRibbonEnabled == true
            && _document.BaseTheme.Equals("parchment", StringComparison.OrdinalIgnoreCase)
            && key?.Character == ' ' && string.IsNullOrEmpty(key.Label);

    private bool IsResizableKeyContent(KeyboardKey? key)
        => IsArrowKey(key) || IsParchmentRibbonKey(key);

    private static RectangleF ResizeRectangle(RectangleF start, float dx, float dy, CanvasDragOperation operation, float minW, float minH)
    {
        float left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        if (operation is CanvasDragOperation.ResizeLeft or CanvasDragOperation.ResizeTopLeft or CanvasDragOperation.ResizeBottomLeft) left += dx;
        if (operation is CanvasDragOperation.ResizeRight or CanvasDragOperation.ResizeTopRight or CanvasDragOperation.ResizeBottomRight) right += dx;
        if (operation is CanvasDragOperation.ResizeTop or CanvasDragOperation.ResizeTopLeft or CanvasDragOperation.ResizeTopRight) top += dy;
        if (operation is CanvasDragOperation.ResizeBottom or CanvasDragOperation.ResizeBottomLeft or CanvasDragOperation.ResizeBottomRight) bottom += dy;
        if (right - left < minW) { if (operation is CanvasDragOperation.ResizeLeft or CanvasDragOperation.ResizeTopLeft or CanvasDragOperation.ResizeBottomLeft) left = right - minW; else right = left + minW; }
        if (bottom - top < minH) { if (operation is CanvasDragOperation.ResizeTop or CanvasDragOperation.ResizeTopLeft or CanvasDragOperation.ResizeTopRight) top = bottom - minH; else bottom = top + minH; }
        return new(left, top, right - left, bottom - top);
    }

    private static void SetSpriteRectangle(KeyboardSprite sprite, RectangleF rectangle)
    { sprite.X = rectangle.X; sprite.Y = rectangle.Y; sprite.Width = rectangle.Width; sprite.Height = rectangle.Height; }
    private static void ScaleAroundCenter(KeyboardSprite sprite, float factor)
    {
        float x = sprite.X + sprite.Width / 2f, y = sprite.Y + sprite.Height / 2f;
        sprite.Width = Math.Max(1, MathF.Round(sprite.Width * factor)); sprite.Height = Math.Max(1, MathF.Round(sprite.Height * factor));
        sprite.X = MathF.Round(x - sprite.Width / 2f); sprite.Y = MathF.Round(y - sprite.Height / 2f);
    }
    private void ScaleBackgroundAroundCenter(float factor)
    {
        if (_document is null) return;
        float x = _document.BackgroundX + _document.BackgroundWidth / 2f, y = _document.BackgroundY + _document.BackgroundHeight / 2f;
        _document.BackgroundWidth = Math.Max(1, MathF.Round(_document.BackgroundWidth * factor));
        _document.BackgroundHeight = Math.Max(1, MathF.Round(_document.BackgroundHeight * factor));
        _document.BackgroundX = MathF.Round(x - _document.BackgroundWidth / 2f); _document.BackgroundY = MathF.Round(y - _document.BackgroundHeight / 2f);
    }

    private void ShowCanvasMenu(Point location)
    {
        var menu = new ContextMenuStrip();
        bool spriteSelected = _selectionKind == CanvasSelectionKind.Sprite && SelectedSprite is not null;
        bool ribbonSelected = _selectionKind == CanvasSelectionKind.KeyContent && IsParchmentRibbonKey(SelectedKey);
        bool copyableArtworkSelected = spriteSelected || ribbonSelected;
        menu.Items.Add(new ToolStripMenuItem("Cut", null, (_, _) => CutSelection()) { Enabled = copyableArtworkSelected });
        menu.Items.Add(new ToolStripMenuItem("Copy", null, (_, _) => CopySelection()) { Enabled = copyableArtworkSelected });
        menu.Items.Add(new ToolStripMenuItem("Paste PNG", null, (_, _) => PasteClipboard()) { Enabled = CanPaste() });
        menu.Items.Add(new ToolStripSeparator());
        if (_selectionKind == CanvasSelectionKind.Sprite)
        {
            menu.Items.Add("Duplicate", null, (_, _) => DuplicateSelectedSprite());
            menu.Items.Add("Delete", null, (_, _) => DeleteSelection());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Bring to front", null, (_, _) => MoveSelectedSpriteTo(true));
            menu.Items.Add("Send to back", null, (_, _) => MoveSelectedSpriteTo(false));
        }
        else if (_selectionKind == CanvasSelectionKind.Background) menu.Items.Add("Remove background", null, (_, _) => DeleteSelection());
        else if (_selectionKind == CanvasSelectionKind.KeyContent && IsParchmentRibbonKey(SelectedKey))
        {
            menu.Items.Add("Remove Parchment ribbon", null, (_, _) => DeleteSelection());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Duplicate key", null, (_, _) => DuplicateSelectedKey());
            menu.Items.Add("Delete key", null, (_, _) => DeleteSelectedKey());
        }
        else if (_selectionKind is CanvasSelectionKind.KeyContent or CanvasSelectionKind.KeyPlate)
        {
            menu.Items.Add("Duplicate key", null, (_, _) => DuplicateSelectedKey());
            menu.Items.Add("Delete key", null, (_, _) => DeleteSelectedKey());
        }
        else if (IsControlSelection(_selectionKind))
        {
            menu.Items.Add("Replace control triangles with PNG...", null, (_, _) => ChooseControlArrowRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add("Use built-in triangles", null, (_, _) => UseBuiltInControlArrowRequested?.Invoke(this, EventArgs.Empty));
        }
        else if (SelectedTopElement is KeyboardTopElement topElement)
            menu.Items.Add("Reset position", null, (_, _) => PerformEdit(() => SetTopElementOffsets(topElement, 0, 0)));
        menu.Show(this, location);
    }

    private void DuplicateSelectedSprite()
    {
        if (_document is null || SelectedSprite is not KeyboardSprite sprite) return;
        KeyboardSprite copy = sprite.Clone(); copy.Id = Guid.NewGuid(); copy.X += 16; copy.Y += 16;
        PerformEdit(() => { _document.Sprites.Add(copy); _selectedSpriteId = copy.Id; _selectionKind = CanvasSelectionKind.Sprite; });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void DuplicateSelectedKey()
    {
        if (_document is null || SelectedKey is not KeyboardKey key) return;
        KeyboardKey copy = key.Clone();
        copy.Id = _document.Keys.Count == 0 ? 0 : _document.Keys.Max(item => item.Id) + 1;
        copy.X += 0.2f;
        copy.Y += 0.2f;
        PerformEdit(() => { _document.Keys.Add(copy); _selectedKeyId = copy.Id; _selectionKind = CanvasSelectionKind.KeyPlate; if (_renderer is not null) _renderer.SelectedKeyId = copy.Id; });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void DeleteSelectedKey()
    {
        if (_document is null || SelectedKey is not KeyboardKey key) return;
        PerformEdit(() =>
        {
            int index = _document.Keys.IndexOf(key);
            _document.Keys.Remove(key);
            KeyboardKey? next = _document.Keys.Count == 0 ? null : _document.Keys[Math.Clamp(index, 0, _document.Keys.Count - 1)];
            _selectedKeyId = next?.Id ?? -1;
            _selectionKind = next is null ? CanvasSelectionKind.None : CanvasSelectionKind.KeyPlate;
            if (_renderer is not null) _renderer.SelectedKeyId = _selectedKeyId;
        });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void MoveSelectedSpriteTo(bool front)
    {
        if (_document is null || SelectedSprite is not KeyboardSprite sprite) return;
        PerformEdit(() => { _document.Sprites.Remove(sprite); _document.Sprites.Insert(front ? _document.Sprites.Count : 0, sprite); });
    }

    private bool CanPaste()
    {
        if (_spriteClipboard is not null) return true;
        try { return Clipboard.ContainsImage() || (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().Any(IsPngFile)); }
        catch { return false; }
    }
    private string? ClipboardPngPath()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                string? png = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(IsPngFile);
                if (png is not null) return png;
            }
            if (!Clipboard.ContainsImage()) return null;
            using Image? image = Clipboard.GetImage();
            if (image is null) return null;
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCompositeUnleashed", "KeyboardStudio", "Clipboard");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"pasted-{Guid.NewGuid():N}.png");
            image.Save(path, ImageFormat.Png);
            return path;
        }
        catch { return null; }
    }
    private static bool TryCopyImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var source = new Bitmap(path);
            Clipboard.SetImage(new Bitmap(source));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void AddSpriteFromPath(string path, PointF center)
    {
        if (_document is null) return;
        KeyboardSprite sprite = CreateSprite(path, center);
        _document.Sprites.Add(sprite); _selectedSpriteId = sprite.Id; _selectionKind = CanvasSelectionKind.Sprite;
    }
    private static KeyboardSprite CreateSprite(string path, PointF center)
    {
        using var image = new Bitmap(path);
        float width = Math.Min(480, image.Width), height = Math.Max(1, width * image.Height / Math.Max(1f, image.Width));
        if (height > 280) { height = 280; width = Math.Max(1, height * image.Width / Math.Max(1f, image.Height)); }
        return new() { SourcePath = path, X = MathF.Round(center.X - width / 2f), Y = MathF.Round(center.Y - height / 2f), Width = width, Height = height };
    }
    private void PerformEdit(Action action)
    {
        if (_document is null) return;
        EditStarted?.Invoke(this, EventArgs.Empty); action(); _document.IsDirty = true; RefreshPreview();
        DocumentChanged?.Invoke(this, EventArgs.Empty); EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static bool ContainsPngFiles(IDataObject? data) => data?.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsPngFile);
    private static bool IsPngFile(string file) => Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(file);
    private static RectangleF SpriteRectangle(KeyboardSprite sprite) => new(sprite.X, sprite.Y, Math.Max(1, sprite.Width), Math.Max(1, sprite.Height));
    private RectangleF BackgroundRectangle() => _document is null ? RectangleF.Empty : new(_document.BackgroundX, _document.BackgroundY, Math.Max(1, _document.BackgroundWidth), Math.Max(1, _document.BackgroundHeight));

    private void GetControlOffsets(KeyboardRuntimeControl control, out float x, out float y)
    {
        x = y = 0; if (_document is null) return;
        switch (control)
        {
            case KeyboardRuntimeControl.Size: x = _document.SizeControlOffsetX; y = _document.SizeControlOffsetY; break;
            case KeyboardRuntimeControl.Opacity: x = _document.OpacityControlOffsetX; y = _document.OpacityControlOffsetY; break;
            case KeyboardRuntimeControl.Tilt: x = _document.TiltControlOffsetX; y = _document.TiltControlOffsetY; break;
        }
    }
    private void SetControlOffsets(KeyboardRuntimeControl control, float x, float y)
    {
        if (_document is null) return;
        switch (control)
        {
            case KeyboardRuntimeControl.Size: _document.SizeControlOffsetX = x; _document.SizeControlOffsetY = y; break;
            case KeyboardRuntimeControl.Opacity: _document.OpacityControlOffsetX = x; _document.OpacityControlOffsetY = y; break;
            case KeyboardRuntimeControl.Tilt: _document.TiltControlOffsetX = x; _document.TiltControlOffsetY = y; break;
        }
    }
    private void GetTopElementOffsets(KeyboardTopElement? element, out float x, out float y)
    {
        x = y = 0;
        if (_document is null || element is null) return;
        switch (element.Value)
        {
            case KeyboardTopElement.TextBar: x = _document.TextBarOffsetX; y = _document.TextBarOffsetY; break;
            case KeyboardTopElement.Mode: x = _document.ModeButtonOffsetX; y = _document.ModeButtonOffsetY; break;
            case KeyboardTopElement.Lock: x = _document.LockButtonOffsetX; y = _document.LockButtonOffsetY; break;
        }
    }
    private void SetTopElementOffsets(KeyboardTopElement? element, float x, float y)
    {
        if (_document is null || element is null) return;
        switch (element.Value)
        {
            case KeyboardTopElement.TextBar: _document.TextBarOffsetX = x; _document.TextBarOffsetY = y; break;
            case KeyboardTopElement.Mode: _document.ModeButtonOffsetX = x; _document.ModeButtonOffsetY = y; break;
            case KeyboardTopElement.Lock: _document.LockButtonOffsetX = x; _document.LockButtonOffsetY = y; break;
        }
    }

    private RectangleF PreviewBounds()
    {
        const float ratio = KeyboardRenderer.TextureWidth / (float)KeyboardRenderer.TextureHeight;
        float availableWidth = Math.Max(1, ClientSize.Width - 40), availableHeight = Math.Max(1, ClientSize.Height - 40);
        float width = availableWidth, height = width / ratio;
        if (height > availableHeight) { height = availableHeight; width = height * ratio; }
        return new((ClientSize.Width - width) / 2f, (ClientSize.Height - height) / 2f, width, height);
    }
    private PointF ClientToTexture(Point point)
    {
        RectangleF bounds = PreviewBounds();
        return new((point.X - bounds.Left) * KeyboardRenderer.TextureWidth / bounds.Width, (point.Y - bounds.Top) * KeyboardRenderer.TextureHeight / bounds.Height);
    }
    private RectangleF TextureToClient(RectangleF rectangle)
    {
        RectangleF bounds = PreviewBounds();
        return new(bounds.Left + rectangle.Left * bounds.Width / KeyboardRenderer.TextureWidth,
            bounds.Top + rectangle.Top * bounds.Height / KeyboardRenderer.TextureHeight,
            rectangle.Width * bounds.Width / KeyboardRenderer.TextureWidth, rectangle.Height * bounds.Height / KeyboardRenderer.TextureHeight);
    }
    private float RoundPixel(float value) => SnapToTenth ? MathF.Round(value) : value;
    private static PointF Center(RectangleF rectangle) => new(rectangle.Left + rectangle.Width / 2f, rectangle.Top + rectangle.Height / 2f);
    private static float DistanceSquared(Point point, PointF target) => (point.X - target.X) * (point.X - target.X) + (point.Y - target.Y) * (point.Y - target.Y);
    private static float NormalizeRotation(float degrees) { float value = degrees % 360f; return value < 0 ? value + 360f : value; }
    private static bool IsControlSelection(CanvasSelectionKind kind) => kind is CanvasSelectionKind.Control
        or CanvasSelectionKind.ControlUpArrow or CanvasSelectionKind.ControlLabel
        or CanvasSelectionKind.ControlValue or CanvasSelectionKind.ControlDownArrow;
    private static CanvasSelectionKind CanvasKindForControlPart(KeyboardControlPart part) => part switch
    {
        KeyboardControlPart.UpArrow => CanvasSelectionKind.ControlUpArrow,
        KeyboardControlPart.Label => CanvasSelectionKind.ControlLabel,
        KeyboardControlPart.Value => CanvasSelectionKind.ControlValue,
        KeyboardControlPart.DownArrow => CanvasSelectionKind.ControlDownArrow,
        _ => CanvasSelectionKind.Control
    };
    private static CanvasSelectionKind CanvasKindForTopElement(KeyboardTopElement element) => element switch
    {
        KeyboardTopElement.TextBar => CanvasSelectionKind.TopTextBar,
        KeyboardTopElement.Mode => CanvasSelectionKind.TopMode,
        _ => CanvasSelectionKind.TopLock
    };
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF rectangle, float radius)
    {
        float diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
