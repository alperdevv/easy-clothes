namespace EasyClothes.Core.Modeling;

// Everything the user can change on a generated cap, in friendly units. Saved with the garment
// so the cap can be reshaped later; the UV layout never changes, so painted textures keep fitting.
public sealed record CapDesign
{
    public float Size { get; init; } = 1;              // 0.9 – 1.1, head fit
    public float CrownHeight { get; init; } = 11;      // cm from the band to the top, 8 – 15
    public float CrownShape { get; init; } = 0.4f;     // 0 rounded – 1 structured
    public float VisorLength { get; init; } = 7;       // cm, 0 = no visor
    public float VisorCurve { get; init; } = 0.6f;     // 0 flat – 1 strongly curved
    public float VisorAngle { get; init; } = 8;        // degrees down from horizontal, -10 – 30
    public bool Backwards { get; init; }
    public bool Button { get; init; } = true;

    public CapColours Colours { get; init; } = new();

    public CapDesign Clamped() => this with
    {
        Size = Math.Clamp(Size, 0.9f, 1.1f),
        CrownHeight = Math.Clamp(CrownHeight, 8, 15),
        CrownShape = Math.Clamp(CrownShape, 0, 1),
        VisorLength = Math.Clamp(VisorLength, 0, 10),
        VisorCurve = Math.Clamp(VisorCurve, 0, 1),
        VisorAngle = Math.Clamp(VisorAngle, -10, 30),
    };
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
