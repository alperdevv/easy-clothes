using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public sealed record Preview(IReadOnlyList<MeshData> Meshes, RgbaImage? Texture, string Status)
{
    public IReadOnlyList<RenderItem> Body { get; init; } = [];

    public static Preview Load(string modelPath, string? texturePath)
    {
        var drawables = DrawableDictionary.Read(ResourceFile.Read(modelPath), includeMeshes: true);
        if (drawables.Count == 0 || drawables[0].Lods.Count == 0)
            return new Preview([], null, "El modelo no contiene geometría.");

        var lod = drawables[0].Lods[0];
        var meshes = lod.Models.SelectMany(m => m).Select(g => g.Mesh!).ToList();
        var status = $"{lod.Vertices:N0} vértices · {lod.Triangles:N0} triángulos";

        RgbaImage? texture = null;
        if (texturePath is not null && LoadTexture(texturePath, out var format) is { } image)
        {
            texture = image;
            status += $" · {image.Width}x{image.Height} {format}";
        }
        return new Preview(meshes, texture, status);
    }

    public static RenderItem LoadPart(string modelPath, string? texturePath) => GarmentPreview.LoadPart(modelPath, texturePath);

    private static RgbaImage? LoadTexture(string path, out string format) => GarmentPreview.LoadTexture(path, out format);

    // Shows a flat image (a tattoo) on an upright quad facing the default camera.
    public static Preview Image(string path)
    {
        var image = ImageLoader.Load(path);
        var w = (float)image.Width / Math.Max(image.Width, image.Height);
        var h = (float)image.Height / Math.Max(image.Width, image.Height);
        var quad = new MeshData
        {
            Positions = [-w / 2, 0, h, w / 2, 0, h, w / 2, 0, 0, -w / 2, 0, 0],
            Normals = [0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0],
            TexCoords = [0, 0, 1, 0, 1, 1, 0, 1],
            Indices = [0, 1, 2, 0, 2, 3],
        };
        return new Preview([quad], image, $"{image.Width}x{image.Height}");
    }
}
