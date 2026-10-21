using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyClothes.App.ViewModels;
using EasyClothes.Core.Textures;

namespace EasyClothes.App.Viewer;

// Shows the texture being edited, fitted to the control, and maps pointer input to texture UV.
public sealed class TextureCanvas : Control
{
    private static readonly IBrush CheckerLight = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44));
    private static readonly IBrush CheckerDark = new SolidColorBrush(Color.FromRgb(0x2C, 0x2F, 0x35));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0xB4, 0xFF)), 1.5, DashStyle.Dash);
    private static readonly IPen BrushPen = new Pen(Brushes.White, 1);

    private WriteableBitmap? _bitmap;
    private Vector2? _lastUv;
    private Point? _hover;
    private TextureEditorViewModel? _editor;

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
        }
        using (var frame = _bitmap.Lock())
        {
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(image.Pixels, y * image.Width * 4, frame.Address + y * frame.RowBytes, image.Width * 4);
        }
        InvalidateVisual();
    }

    private Rect ImageRect()
    {
        if (_editor is null) return default;
        var source = _editor.Edit.Base;
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var size = new Size(source.Width * scale, source.Height * scale);
        return new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
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

        const double cell = 12;
        using (context.PushClip(rect))
        {
            context.FillRectangle(CheckerDark, rect);
            for (var y = rect.Top; y < rect.Bottom; y += cell)
            for (var x = rect.Left + ((int)((y - rect.Top) / cell) % 2) * cell; x < rect.Right; x += cell * 2)
                context.FillRectangle(CheckerLight, new Rect(x, y, cell, cell));
            context.DrawImage(_bitmap, rect);
        }

        if (_editor.IsMoveTool && _editor.SelectedDecal?.Model is { } decal)
        {
            var width = decal.Scale * rect.Width;
            var height = width * decal.Image.Height / decal.Image.Width;
            var centre = new Point(rect.X + decal.X * rect.Width, rect.Y + decal.Y * rect.Height);
            var transform = Matrix.CreateRotation(decal.Rotation * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y);
            using (context.PushTransform(transform))
                context.DrawRectangle(null, SelectionPen, new Rect(-width / 2, -height / 2, width, height));
        }
        else if (!_editor.IsMoveTool && _hover is { } hover)
        {
            var radius = _editor.BrushSize / 2 * rect.Width / _editor.Edit.Base.Width;
            context.DrawEllipse(null, BrushPen, hover, radius, radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_editor is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var uv = ToUv(e.GetPosition(this));
        if (_editor.IsMoveTool)
        {
            if (!_editor.SelectDecalAt(uv) && _editor.SelectedDecal is null) return;
        }
        else
        {
            _editor.BeginStroke();
            _editor.StrokeTo(uv, uv);
        }
        _lastUv = uv;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hover = e.GetPosition(this);
        if (_editor is not null && _lastUv is { } last)
        {
            var uv = ToUv(_hover.Value);
            if (_editor.IsMoveTool) _editor.SelectedDecal?.Move(uv - last);
            else _editor.StrokeTo(last, uv);
            _lastUv = uv;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastUv = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    // Wheel resizes the selected logo in move mode and the brush otherwise.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_editor is null) return;
        var factor = (float)Math.Pow(1.1, e.Delta.Y);
        if (_editor.IsMoveTool) _editor.SelectedDecal?.Resize(factor);
        else _editor.BrushSize = Math.Clamp(_editor.BrushSize * factor, 1, 512);
        InvalidateVisual();
        e.Handled = true;
    }
}
