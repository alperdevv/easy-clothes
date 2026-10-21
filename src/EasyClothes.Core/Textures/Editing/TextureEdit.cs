namespace EasyClothes.Core.Textures.Editing;

// A non-destructive stack over one texture: base (or an external replacement) → colour adjustments →
// decals → paint. Decals and paint come after the adjustments so logos keep their own colours.
public sealed class TextureEdit
{
    public required RgbaImage Original { get; init; }
    public RgbaImage? External { get; set; }

    public float Hue { get; set; }
    public float Saturation { get; set; }
    public float Brightness { get; set; }
    public float Contrast { get; set; }
    public uint TintRgb { get; set; } = 0xFFFFFF;
    public float TintStrength { get; set; }

    public List<Decal> Decals { get; } = [];

    // Same size as the base; straight alpha, painted over everything else.
    public RgbaImage Paint { get; private set; } = new(1, 1, new byte[4]);

    public RgbaImage Base => External ?? Original;

    public bool HasAdjustments => Hue != 0 || Saturation != 0 || Brightness != 0 || Contrast != 0 || TintStrength > 0;

    public bool IsEmpty => External is null && !HasAdjustments && Decals.Count == 0 && !HasPaint;

    public bool HasPaint { get; private set; }

    public void EnsurePaintLayer()
    {
        if (Paint.Width == Base.Width && Paint.Height == Base.Height) return;
        Paint = new RgbaImage(Base.Width, Base.Height, new byte[Base.Width * Base.Height * 4]);
        HasPaint = false;
    }

    public void MarkPainted() => HasPaint = true;

    public void ClearPaint()
    {
        Array.Clear(Paint.Pixels);
        HasPaint = false;
    }

    public void ResetAdjustments()
    {
        Hue = Saturation = Brightness = Contrast = TintStrength = 0;
        TintRgb = 0xFFFFFF;
    }
}

// Position is the decal centre in texture UV (0..1); Scale is its width as a fraction of the texture width.
public sealed class Decal
{
    public required string Name { get; set; }
    public required RgbaImage Image { get; init; }
    public float X { get; set; } = 0.5f;
    public float Y { get; set; } = 0.5f;
    public float Scale { get; set; } = 0.25f;
    public float Rotation { get; set; }
    public float Opacity { get; set; } = 1;
}
