using System.Numerics;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Viewing;

public static class MeshBounds
{
    public static (Vector3 Min, Vector3 Max) Of(IEnumerable<MeshData> meshes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < mesh.Positions.Length; i += 3)
            {
                var p = new Vector3(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        return min.X > max.X ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }
}
