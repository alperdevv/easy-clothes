using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Analysis;

// Freemode feet rest at z = -1. The game's heels reach about 9 cm lower and use a heel height of 1,
// so a sole further down than that is read as a heel of proportional height.
public static class HeelEstimator
{
    private const float Floor = -1f;
    private const float BaseHeelDrop = 0.09f;
    private const float MinimumDrop = 0.04f;

    public static float? Estimate(string modelPath)
    {
        var drawables = DrawableDictionary.Read(ResourceFile.Read(modelPath), includeMeshes: true);
        var lowest = float.MaxValue;
        foreach (var mesh in drawables.SelectMany(d => d.Lods.Where(l => l.Level == LodLevel.High))
                     .SelectMany(l => l.Models).SelectMany(m => m).Select(g => g.Mesh).OfType<MeshData>())
        {
            for (var v = 2; v < mesh.Positions.Length; v += 3)
                lowest = Math.Min(lowest, mesh.Positions[v]);
        }
        var drop = Floor - lowest;
        return lowest == float.MaxValue || drop < MinimumDrop ? null : MathF.Round(Math.Min(drop / BaseHeelDrop, 3f), 1);
    }
}
