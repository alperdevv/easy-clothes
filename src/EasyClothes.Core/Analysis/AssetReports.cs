using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Analysis;

public sealed record LodReport(LodLevel Level, int Vertices, int Triangles);

public sealed record ModelReport(
    int SystemSize,
    int GraphicsSize,
    IReadOnlyList<LodReport> Lods,
    int UvOutOfRange,
    int UvInvalid,
    int DegenerateTriangles,
    int Vertices)
{
    public LodReport? High => Lods.FirstOrDefault(l => l.Level == LodLevel.High);
}

public sealed record TextureReport(int SystemSize, int GraphicsSize, IReadOnlyList<TextureInfo> Textures);
