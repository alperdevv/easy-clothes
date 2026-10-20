using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.App.ViewModels;

public sealed record Preview(IReadOnlyList<MeshData> Meshes, RgbaImage? Texture, string Status)
{
    public static Preview Load(string modelPath, string? texturePath)
    {
        var drawables = DrawableDictionary.Read(ResourceFile.Read(modelPath), includeMeshes: true);
        if (drawables.Count == 0 || drawables[0].Lods.Count == 0)
            return new Preview([], null, "El modelo no contiene geometría.");

        var lod = drawables[0].Lods[0];
        var meshes = lod.Models.SelectMany(m => m).Select(g => g.Mesh!).ToList();
        var status = $"{lod.Vertices:N0} vértices · {lod.Triangles:N0} triángulos";

        RgbaImage? texture = null;
        if (texturePath is not null)
        {
            var resource = ResourceFile.Read(texturePath);
            var info = TextureDictionary.Read(resource).FirstOrDefault();
            if (info is not null)
            {
                texture = TextureDictionary.Decode(resource, info);
                status += $" · {info.Width}x{info.Height} {info.Format}";
            }
        }
        return new Preview(meshes, texture, status);
    }
}
