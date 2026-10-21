namespace EasyClothes.Core.Textures.Editing;

public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    SoftLight,
    Darken,
    Lighten,
    Add,
    Difference,
    Color,
}

public abstract class Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public float Opacity { get; set; } = 1;
    public BlendMode Blend { get; set; }

    // Images are shared with the copy: they are replaced, never mutated once a history state holds them.
    public Layer Clone() => (Layer)MemberwiseClone();
}

// Free-form pixels at document size; the only layer kind the brush paints on.
public sealed class PixelLayer : Layer
{
    public required RgbaImage Image { get; set; }
}

// A placed picture (logo, patch). Position is its centre in UV (0..1); Scale is its width as a fraction of the document width.
public sealed class ImageLayer : Layer
{
    public required RgbaImage Image { get; init; }
    public float X { get; set; } = 0.5f;
    public float Y { get; set; } = 0.5f;
    public float Scale { get; set; } = 0.25f;
    public float Rotation { get; set; }
}

public sealed class FillLayer : Layer
{
    public uint Rgb { get; set; } = 0xFFFFFF;
}

// Recolours everything below it, like an adjustment layer in Photoshop.
public sealed class AdjustmentLayer : Layer
{
    public float Hue { get; set; }
    public float Saturation { get; set; }
    public float Brightness { get; set; }
    public float Contrast { get; set; }
    public uint TintRgb { get; set; } = 0xFFFFFF;
    public float TintStrength { get; set; }
}
