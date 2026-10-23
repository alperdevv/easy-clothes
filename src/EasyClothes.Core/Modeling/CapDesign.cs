using System.Text.Json.Serialization;

namespace EasyClothes.Core.Modeling;

public enum CapStyle
{
    Baseball,
    Snapback,
    DadHat,
    Trucker,
    Bucket,
    Beanie,
    SunVisor,
}

// Which geometry a style is built from.
public enum CapKind
{
    Cap,
    Bucket,
    Beanie,
    SunVisor,
}

// Everything the user can change on a generated cap, in friendly units. Saved with the garment
// so the cap can be reshaped later; the UV layout never changes, so painted textures keep fitting.
public sealed record CapDesign
{
    public CapStyle Style { get; init; }
    public float Size { get; init; } = 1;              // 0.95 – 1.1, head fit
    public float CrownHeight { get; init; } = 11;      // cm from the band to the top, MinCrownHeight – 16
    public float CrownShape { get; init; } = 0.4f;     // 0 rounded – 1 structured
    public float VisorLength { get; init; } = 7;       // cm, 0 = no visor
    public float VisorCurve { get; init; } = 0.6f;     // 0 flat – 1 strongly curved
    public float VisorAngle { get; init; } = 8;        // degrees down from horizontal, -10 – 30
    public float VisorWidth { get; init; } = 0.5f;     // 0 narrow – 1 wide
    public float VisorSquare { get; init; }            // 0 rounded – 1 squared off
    public float Rotation { get; init; }               // degrees: 0 front, 90 left, -90 right, 180 back
    public bool Button { get; init; } = true;
    public float BrimWidth { get; init; } = 6;         // cm, bucket hat brim, 3 – 9
    public float BrimDroop { get; init; } = 25;        // degrees down, bucket hat brim, 0 – 45
    public float CuffHeight { get; init; } = 5;        // cm, beanie fold, 0 – 8
    public bool Pompom { get; init; }                  // beanie

    [JsonIgnore]
    public CapKind Kind => Style switch
    {
        CapStyle.Bucket => CapKind.Bucket,
        CapStyle.Beanie => CapKind.Beanie,
        CapStyle.SunVisor => CapKind.SunVisor,
        _ => CapKind.Cap,
    };

    public CapColours Colours { get; init; } = new();

    // Caps saved before Rotation existed only knew "backwards".
    [JsonInclude, JsonPropertyName("backwards"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal bool LegacyBackwards
    {
        get => false;
        init { if (value) Rotation = 180; }
    }

    // Lower crowns would let the top of the head show through; hats with a lower band need more.
    [JsonIgnore]
    public float MinCrownHeight => Kind switch
    {
        CapKind.Bucket => 11.5f,
        CapKind.Beanie => 12.5f,
        _ => 10.5f,
    };

    public CapDesign Clamped() => this with
    {
        Size = Math.Clamp(Size, 0.95f, 1.1f),
        CrownHeight = Math.Clamp(CrownHeight, MinCrownHeight, 16),
        CrownShape = Math.Clamp(CrownShape, 0, 1),
        VisorLength = Math.Clamp(VisorLength, 0, 10),
        VisorCurve = Math.Clamp(VisorCurve, 0, 1),
        VisorAngle = Math.Clamp(VisorAngle, -10, 30),
        VisorWidth = Math.Clamp(VisorWidth, 0, 1),
        VisorSquare = Math.Clamp(VisorSquare, 0, 1),
        Rotation = Math.Clamp(Rotation, -180, 180),
        BrimWidth = Math.Clamp(BrimWidth, 3, 9),
        BrimDroop = Math.Clamp(BrimDroop, 0, 45),
        CuffHeight = Math.Clamp(CuffHeight, 0, 8),
    };

    // A style's typical shape; rotation and colours are kept so switching style doesn't lose them.
    public CapDesign WithStyle(CapStyle style) => (style switch
    {
        CapStyle.Snapback => new CapDesign { CrownHeight = 12, CrownShape = 0.9f, VisorLength = 7.5f, VisorCurve = 0.05f, VisorAngle = 3, VisorWidth = 0.6f, VisorSquare = 0.6f },
        CapStyle.DadHat => new CapDesign { Size = 1.02f, CrownHeight = 10.5f, CrownShape = 0, VisorLength = 6.5f, VisorCurve = 0.8f, VisorAngle = 12, VisorWidth = 0.45f },
        CapStyle.Trucker => new CapDesign { CrownHeight = 12, CrownShape = 0.8f, VisorLength = 7, VisorCurve = 0.5f, VisorAngle = 6, VisorWidth = 0.55f, VisorSquare = 0.2f },
        CapStyle.Bucket => new CapDesign { CrownHeight = 12, CrownShape = 0.7f, Button = false, BrimDroop = 20 },
        CapStyle.Beanie => new CapDesign { CrownHeight = 13.5f, CrownShape = 0.1f, Button = false },
        CapStyle.SunVisor => new CapDesign { VisorLength = 7.5f, VisorCurve = 0.5f, VisorAngle = 10, VisorWidth = 0.6f, Button = false },
        _ => new CapDesign(),
    }) with { Style = style, Rotation = Rotation, Colours = Colours };
}

public sealed record CapColours
{
    public uint Crown { get; init; } = 0x1E1F24;
    public uint Front { get; init; } = 0x1E1F24;
    public uint Visor { get; init; } = 0x1E1F24;
    public uint UnderVisor { get; init; } = 0x3B4A3A;
    public uint Button { get; init; } = 0x1E1F24;
    public uint Stitch { get; init; } = 0x3A3C44;
}
