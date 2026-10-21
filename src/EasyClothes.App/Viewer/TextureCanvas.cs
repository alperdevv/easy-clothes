using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyClothes.App.ViewModels;
using EasyClothes.Core.Textures.Editing;

namespace EasyClothes.App.Viewer;

// Shows the composed texture with zoom and pan, and turns pointer input into editor actions in texture UV.
public sealed class TextureCanvas : Control
{
    private const double MinScale = 0.05;
    private const double MaxScale = 32;
    private static readonly IBrush CheckerLight = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44));
    private static readonly IBrush CheckerDark = new SolidColorBrush(Color.FromRgb(0x2C, 0x2F, 0x35));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0xB4, 0xFF)), 1.5, DashStyle.Dash);
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1);
    private static readonly IPen BrushPen = new Pen(Brushes.White, 1);
    private static readonly IPen BrushShadowPen = new Pen(Brushes.Black, 1);

    private enum Drag { None, Paint, Move, Pan }

    private WriteableBitmap? _bitmap;
    private TextureEditorViewModel? _editor;
    private Drag _drag;
    private Vector2 _lastUv;
    private Point _lastPoint;
    private Point? _hover;
    private double _scale = 1;
    private Point _origin;
    private bool _fitted = true;

    public TextureEditorViewModel? Editor
    {
        get => _editor;
        set
        {
            if (_editor is not null) _editor.PropertyChanged -= OnEditorChanged;
            _editor = value;
            if (_editor is not null) _editor.PropertyChanged += OnEditorChanged;
            UpdateBitmap();
        }
    }

    // Held space pans with the left button, as in Photoshop.
    public bool SpaceHeld { get; set; }

    public double Scale => _scale;

    private void OnEditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextureEditorViewModel.Composed)) UpdateBitmap();
        else InvalidateVisual();
    }

    private void UpdateBitmap()
    {
        if (_editor?.Composed is not { } image) return;
        if (_bitmap is null || _bitmap.PixelSize.Width != image.Width || _bitmap.PixelSize.Height != image.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            if (_fitted) FitToView();
        }
        using (var frame = _bitmap.Lock())
        {
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(image.Pixels, y * image.Width * 4, frame.Address + y * frame.RowBytes, image.Width * 4);
        }
        InvalidateVisual();
    }

    private Size DocumentSize => _editor is null ? default : new Size(_editor.Document.Width, _editor.Document.Height);

    private Rect ImageRect() => new(_origin, DocumentSize * _scale);

    public void FitToView()
    {
        var size = DocumentSize;
        if (size.Width <= 0 || Bounds.Width <= 0) return;
        _scale = Math.Clamp(Math.Min((Bounds.Width - 24) / size.Width, (Bounds.Height - 24) / size.Height), MinScale, MaxScale);
        _origin = new Point((Bounds.Width - size.Width * _scale) / 2, (Bounds.Height - size.Height * _scale) / 2);
        _fitted = true;
        ReportZoom();
    }

    public void ActualPixels() => ZoomAt(1, new Point(Bounds.Width / 2, Bounds.Height / 2), absolute: true);

    public void ZoomBy(double factor) => ZoomAt(factor, new Point(Bounds.Width / 2, Bounds.Height / 2));

    private void ZoomAt(double factor, Point anchor, bool absolute = false)
    {
        var scale = Math.Clamp(absolute ? factor : _scale * factor, MinScale, MaxScale);
        var texel = (anchor - _origin) / _scale;
        _scale = scale;
        _origin = anchor - texel * _scale;
        _fitted = false;
        ReportZoom();
    }

    private void ReportZoom()
    {
        if (_editor is not null) _editor.ZoomPercent = Math.Round(_scale * 100);
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitted) FitToView();
        else _origin += new Avalonia.Vector(e.NewSize.Width - e.PreviousSize.Width, e.NewSize.Height - e.PreviousSize.Height) / 2;
    }

    private Vector2 ToUv(Point point)
    {
        var rect = ImageRect();
        return new Vector2((float)((point.X - rect.X) / rect.Width), (float)((point.Y - rect.Y) / rect.Height));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var rect = ImageRect();
        if (rect.Width <= 0 || _bitmap is null || _editor is null) return;

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            var visible = rect.Intersect(new Rect(Bounds.Size));
            const double cell = 10;
            context.FillRectangle(CheckerDark, visible);
            var startX = rect.X + Math.Floor((visible.X - rect.X) / cell) * cell;
            var startY = rect.Y + Math.Floor((visible.Y - rect.Y) / cell) * cell;
            using (context.PushClip(visible))
            {
                for (var y = startY; y < visible.Bottom; y += cell)
                for (var x = startX + (((int)Math.Round((y - rect.Y) / cell) + (int)Math.Round((startX - rect.X) / cell)) % 2) * cell; x < visible.Right; x += cell * 2)
                    context.FillRectangle(CheckerLight, new Rect(x, y, cell, cell));
            }

            // Zoomed in, show crisp texels like an image editor; zoomed out, filter.
            var mode = _scale >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
                context.DrawImage(_bitmap, rect);
            context.DrawRectangle(null, BorderPen, rect);

            if (_editor.IsMoveTool && _editor.SelectedLayer?.Model is ImageLayer image) DrawImageOutline(context, rect, image);
            else if (_editor.IsPaintTool && _hover is { } hover && _drag != Drag.Pan && !SpaceHeld)
            {
                var radius = Math.Max(1, _editor.BrushSize / 2 * _scale);
                context.DrawEllipse(null, BrushShadowPen, hover, radius + 1, radius + 1);
                context.DrawEllipse(null, BrushPen, hover, radius, radius);
            }
        }
    }

    private static void DrawImageOutline(DrawingContext context, Rect rect, ImageLayer image)
    {
        var width = image.Scale * rect.Width;
        var height = width * image.Image.Height / image.Image.Width;
        var centre = new Point(rect.X + image.X * rect.Width, rect.Y + image.Y * rect.Height);
        var transform = Matrix.CreateRotation(image.Rotation * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y);
        using (context.PushTransform(transform))
            context.DrawRectangle(null, SelectionPen, new Rect(-width / 2, -height / 2, width, height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_editor is null) return;
        var point = e.GetCurrentPoint(this);
        var uv = ToUv(point.Position);
        _drag = Drag.None;

        if (point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && (SpaceHeld || _editor.IsHandTool)))
            _drag = Drag.Pan;
        else if (!point.Properties.IsLeftButtonPressed)
            return;
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && _editor.IsPaintTool)
            _editor.SampleColour(uv);
        else if (_editor.IsPaintTool && _editor.BeginStroke())
        {
            _drag = Drag.Paint;
            _editor.StrokeTo(uv, uv);
        }
        else if (_editor.IsMoveTool && _editor.BeginMove(uv))
            _drag = Drag.Move;

        if (_drag == Drag.None) return;
        _lastUv = uv;
        _lastPoint = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        _hover = position;
        if (_editor is not null)
        {
            var uv = ToUv(position);
            switch (_drag)
            {
                case Drag.Paint:
                    _editor.StrokeTo(_lastUv, uv);
                    break;
                case Drag.Move:
                    _editor.MoveBy(uv - _lastUv);
                    break;
                case Drag.Pan:
                    _origin += position - _lastPoint;
                    _fitted = false;
                    break;
            }
            _lastUv = uv;
            _lastPoint = position;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var drag = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        if (drag == Drag.Paint) _editor?.EndStroke();
        else if (drag == Drag.Move) _editor?.EndMove();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        var drag = _drag;
        _drag = Drag.None;
        if (drag == Drag.Paint) _editor?.EndStroke();
        else if (drag == Drag.Move) _editor?.EndMove();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    // Wheel zooms around the cursor; Ctrl+wheel resizes the brush or the selected image instead.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_editor is null) return;
        var factor = Math.Pow(1.15, e.Delta.Y);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_editor.IsMoveTool && _editor.SelectedLayer is { IsImage: true } layer) layer.Size *= factor;
            else _editor.BrushSize = Math.Clamp(_editor.BrushSize * factor, 1, 1024);
        }
        else ZoomAt(factor, e.GetPosition(this));
        InvalidateVisual();
        e.Handled = true;
    }
}
