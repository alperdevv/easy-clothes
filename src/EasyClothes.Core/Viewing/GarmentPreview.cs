using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Viewing;

// Loads garments for display and renders them on the CPU, for the app's preview and for tools without a GPU.
public static class GarmentPreview
{
    public static RenderItem LoadPart(string modelPath, string? texturePath)
    {
        var drawables = DrawableDictionary.Read(ResourceFile.Read(modelPath), includeMeshes: true);
        var meshes = drawables.Count == 0 || drawables[0].Lods.Count == 0
            ? []
            : drawables[0].Lods[0].Models.SelectMany(m => m).Select(g => g.Mesh!).ToList();
        return new RenderItem(meshes, texturePath is null ? null : LoadTexture(texturePath, out _));
    }

    public static RgbaImage? LoadTexture(string path, out string format)
    {
        var resource = ResourceFile.Read(path);
        var info = TextureDictionary.Read(resource).FirstOrDefault();
        format = info?.Format ?? "";
        return info is null ? null : TextureDictionary.Decode(resource, info);
    }

    // One view per yaw, side by side, all framed on the garment so the body only gives context.
    public static RgbaImage RenderViews(RenderItem garment, IReadOnlyList<RenderItem> body, IReadOnlyList<float> yawDegrees, int viewWidth, int viewHeight, uint background = 0x202226FF)
    {
        var (min, max) = MeshBounds.Of(garment.Meshes);
        var items = body.Append(garment).ToList();
        var sheet = new RgbaImage(viewWidth * yawDegrees.Count, viewHeight, new byte[viewWidth * yawDegrees.Count * viewHeight * 4]);
        for (var v = 0; v < yawDegrees.Count; v++)
        {
            var camera = new OrbitCamera();
            camera.Frame(min, max);
            camera.Yaw = yawDegrees[v] * MathF.PI / 180;
            var view = SoftwareRenderer.Render(items, camera, viewWidth, viewHeight, background);
            for (var y = 0; y < viewHeight; y++)
                Buffer.BlockCopy(view.Pixels, y * viewWidth * 4, sheet.Pixels, (y * sheet.Width + v * viewWidth) * 4, viewWidth * 4);
        }
        return sheet;
    }
}
