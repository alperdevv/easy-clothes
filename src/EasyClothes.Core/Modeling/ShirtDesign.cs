namespace EasyClothes.Core.Modeling;

public enum ShirtStyle
{
    TShirt,
    LongSleeve,
    Tank,
    Crop,
    Oversized,
    Polo,
    Turtleneck,
    Shirt,
}

public enum Neckline
{
    Crew,
    VNeck,
    Scoop,
}

// A piece added at the neckline: polos and shirts get a folded collar and a button placket.
public enum Collar
{
    None,
    Polo,
    Turtleneck,
    Shirt,
}

public enum HemShape
{
    Straight,
    Curved,
    LongerBack,
}

// Over everything else (jbib) or underneath a jacket (accs, a little tighter).
public enum ShirtLayer
{
    Top,
    Undershirt,
}

// Everything the user can change on a generated shirt's shape, in friendly units. Saved with the garment
// so it can be reshaped later; the UV layout never changes, so painted textures keep fitting.
public sealed record ShirtDesign
{
    public const float MaxLength = 20;

    public ShirtStyle Style { get; init; }
    public float Sleeve { get; init; } = 0.3f;         // 0 sleeveless – 1 down to the wrist
    public float Length { get; init; } = 6;            // cm below the waist, negative for a cropped shirt, -12 – MaxLength
    public float Fit { get; init; } = 0.3f;            // 0 tight – 1 loose
    public Neckline Neckline { get; init; }
    public float NeckDepth { get; init; } = 2;         // cm the neckline drops at the front, 0 – 15
    public Collar Collar { get; init; }
    public HemShape Hem { get; init; }
    public bool RolledCuffs { get; init; }
    public bool Pocket { get; init; }
    public ShirtLayer Layer { get; init; }
    public ShirtLook Look { get; init; } = new();

    public ShirtDesign Clamped() => this with
    {
        Sleeve = Math.Clamp(Sleeve, 0, 1),
        Length = Math.Clamp(Length, -12, MaxLength),
        Fit = Math.Clamp(Fit, 0, 1),
        NeckDepth = Math.Clamp(NeckDepth, 0, 15),
        Look = Look.Clamped(),
    };

    // A style's typical shape; the look and the layer are kept so switching style doesn't lose them.
    public ShirtDesign WithStyle(ShirtStyle style) => (style switch
    {
        ShirtStyle.LongSleeve => new ShirtDesign { Sleeve = 1, Length = 6, Fit = 0.25f },
        ShirtStyle.Tank => new ShirtDesign { Sleeve = 0, Length = 6, Fit = 0.2f, Neckline = Neckline.Scoop, NeckDepth = 8 },
        ShirtStyle.Crop => new ShirtDesign { Sleeve = 0.25f, Length = -8, Fit = 0.15f },
        ShirtStyle.Oversized => new ShirtDesign { Sleeve = 0.42f, Length = 16, Fit = 0.9f },
        ShirtStyle.Polo => new ShirtDesign { Sleeve = 0.28f, Length = 8, Fit = 0.3f, Collar = Collar.Polo, NeckDepth = 0 },
        ShirtStyle.Turtleneck => new ShirtDesign { Sleeve = 1, Length = 6, Fit = 0.15f, Collar = Collar.Turtleneck, NeckDepth = 0 },
        ShirtStyle.Shirt => new ShirtDesign { Sleeve = 1, Length = 14, Fit = 0.35f, Collar = Collar.Shirt, NeckDepth = 0, Hem = HemShape.Curved },
        _ => new ShirtDesign(),
    }) with { Style = style, Look = Look, Layer = Layer };
}

public enum ShirtPatternKind
{
    None,
    Stripes,
    VerticalStripes,
    Checks,
    Camo,
    TieDye,
    Gradient,
    Raglan,
    ColourBlock,
}

public enum Fabric
{
    Cotton,
    Knit,
    Silk,
    Leather,
    Sport,
    Denim,
}

public enum GraphicZone
{
    ChestCentre,
    ChestLeft,
    Back,
    Nape,
    Sleeve,
    Belly,
}

// A printed picture or text. Image is a PNG/YTD path (relative to the project once saved with a garment);
// Text is drawn instead when there is no image. Size is the width in cm.
public sealed record ShirtGraphic
{
    public GraphicZone Zone { get; init; }
    public string? Image { get; init; }
    public string? Text { get; init; }
    public uint TextColour { get; init; } = 0xFFFFFF;
    public float Size { get; init; } = 12;
    public float OffsetX { get; init; }                // cm right of the zone's centre, as seen from outside
    public float OffsetY { get; init; }                // cm above the zone's centre
}

public sealed record ShirtPattern
{
    public ShirtPatternKind Kind { get; init; }
    public uint Colour { get; init; } = 0x1E1F24;
    public uint Colour2 { get; init; } = 0x55606A;     // camo and tie-dye use a third tone
    public float Scale { get; init; } = 4;             // cm: stripe width, check size or blot size, 1 – 30
}

// How a shirt looks: what each texture variation is painted from. A new colourway is a new look.
public sealed record ShirtLook
{
    public uint Body { get; init; } = 0xE6E6E3;
    public uint Sleeves { get; init; } = 0xE6E6E3;
    public uint Trim { get; init; } = 0xE6E6E3;
    public uint Stitch { get; init; } = 0xBDBDB8;
    public uint Buttons { get; init; } = 0xF2F0EA;
    public ShirtPattern Pattern { get; init; } = new();
    public Fabric Fabric { get; init; }
    public float Wrinkles { get; init; } = 0.5f;       // 0 – 1
    public float Dirt { get; init; }                   // 0 – 1
    public float Fade { get; init; }                   // 0 – 1
    public float Distress { get; init; }               // 0 – 1, frayed edges and worn spots
    public IReadOnlyList<ShirtGraphic> Graphics { get; init; } = [];

    public ShirtLook Clamped() => this with
    {
        Pattern = Pattern with { Scale = Math.Clamp(Pattern.Scale, 1, 30) },
        Wrinkles = Math.Clamp(Wrinkles, 0, 1),
        Dirt = Math.Clamp(Dirt, 0, 1),
        Fade = Math.Clamp(Fade, 0, 1),
        Distress = Math.Clamp(Distress, 0, 1),
        Graphics = Graphics.Select(g => g with { Size = Math.Clamp(g.Size, 2, 40) }).ToList(),
    };

    // The same look in another main colour: whatever matched the body follows it.
    public ShirtLook InColour(uint body) => this with
    {
        Body = body,
        Sleeves = Sleeves == Body ? body : Sleeves,
        Trim = Trim == Body ? body : Trim,
    };
}
