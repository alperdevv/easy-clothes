using System.Numerics;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Viewing;

// Loads garments for display and renders them on the CPU, for the app's preview and for tools without a GPU.
public static class GarmentPreview
{
    public static RenderItem LoadPart(string modelPath, string? texturePath, Matrix4x4? transform = null)
    {
        var drawables = DrawableDictionary.Read(ResourceFile.Read(modelPath), includeMeshes: true);
        var meshes = drawables.Count == 0 || drawables[0].Lods.Count == 0
            ? []
            : drawables[0].Lods[0].Models.SelectMany(m => m).Select(g => transform is { } t ? g.Mesh!.Transformed(t) : g.Mesh!).ToList();
        return new RenderItem(meshes, texturePath is null ? null : LoadTexture(texturePath, out _));
    }

    public static RgbaImage? LoadTexture(string path, out string format)
    {
        var resource = ResourceFile.Read(path);
        var info = TextureDictionary.Read(resource).FirstOrDefault();
        format = info?.Format ?? "";
        return info is null ? null : TextureDictionary.Decode(resource, info);
    }

    public const uint DefaultBackground = 0x202226FF;
    private const float Margin = 0.08f;

    // One view per yaw, side by side, each framed on the garment (8 % free on every side) so the body
    // only gives context. Rendered at twice the size and scaled down to smooth the edges; a background
    // with zero alpha gives a transparent PNG.
    public static RgbaImage RenderViews(RenderItem garment, IReadOnlyList<RenderItem> body, IReadOnlyList<float> yawDegrees, int viewWidth, int viewHeight, uint background = DefaultBackground)
    {
        const int Supersample = 2;
        var points = Points(garment.Meshes);
        var items = body.Append(garment).ToList();
        var sheet = new RgbaImage(viewWidth * yawDegrees.Count, viewHeight, new byte[viewWidth * yawDegrees.Count * viewHeight * 4]);
        for (var v = 0; v < yawDegrees.Count; v++)
        {
            var camera = new OrbitCamera { FieldOfView = 30 * MathF.PI / 180, Yaw = yawDegrees[v] * MathF.PI / 180 };
            var aspect = (float)viewWidth / viewHeight;
            camera.FitTo(points, aspect, Margin);
            if (VisibleArea(SoftwareRenderer.Render([garment], camera, viewWidth, viewHeight)) is { } visible)
            {
                camera.FitTo(Inside(points, camera, visible, viewWidth, viewHeight), aspect, Margin);
                if (VisibleArea(SoftwareRenderer.Render([garment], camera, viewWidth, viewHeight)) is { } settled)
                    Refit(camera, settled, viewWidth, viewHeight, Margin);
            }
            var large = SoftwareRenderer.Render(items, camera, viewWidth * Supersample, viewHeight * Supersample, background, Lighting.Studio);
            var transparent = (background & 0xFF) == 0;
            if (transparent) Premultiply(large);
            var view = ImageResampler.Resize(large, viewWidth, viewHeight);
            if (transparent) Unpremultiply(view);
            for (var y = 0; y < viewHeight; y++)
                Buffer.BlockCopy(view.Pixels, y * viewWidth * 4, sheet.Pixels, (y * sheet.Width + v * viewWidth) * 4, viewWidth * 4);
        }
        return sheet;
    }

    // Geometry hidden by transparent texels still counts in the bounds, so a first render of the garment
    // alone shows what is really visible: the camera is fitted again on the vertices that land there,
    // and a last render corrects what perspective leaves off.
    private readonly record struct Area(int MinX, int MaxX, int MinY, int MaxY);

    private static Area? VisibleArea(RgbaImage probe)
    {
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (var y = 0; y < probe.Height; y++)
        for (var x = 0; x < probe.Width; x++)
        {
            if (probe.Pixels[(y * probe.Width + x) * 4 + 3] == 0) continue;
            (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
        }
        return maxX < 0 ? null : new Area(minX, maxX, minY, maxY);
    }

    private static List<Vector3> Inside(List<Vector3> points, OrbitCamera camera, Area area, int width, int height)
    {
        var viewProjection = camera.ViewMatrix * camera.ProjectionMatrix((float)width / height);
        var inside = points.Where(p =>
        {
            var clip = Vector4.Transform(p, viewProjection);
            if (clip.W <= 0) return false;
            var x = (clip.X / clip.W + 1) * 0.5f * width;
            var y = (1 - clip.Y / clip.W) * 0.5f * height;
            return x >= area.MinX - 1 && x <= area.MaxX + 2 && y >= area.MinY - 1 && y <= area.MaxY + 2;
        }).ToList();
        return inside.Count > 0 ? inside : points;
    }

    private static void Refit(OrbitCamera camera, Area area, int width, int height, float margin)
    {
        var aspect = (float)width / height;
        camera.Pan(((area.MinX + area.MaxX + 1f) / width - 1) * aspect, 1 - (area.MinY + area.MaxY + 1f) / height);
        camera.Distance *= MathF.Max((area.MaxX - area.MinX + 1f) / width, (area.MaxY - area.MinY + 1f) / height) / (1 - 2 * margin);
    }

    private static List<Vector3> Points(IEnumerable<MeshData> meshes)
    {
        var points = new List<Vector3>();
        foreach (var mesh in meshes)
            for (var i = 0; i + 2 < mesh.Positions.Length; i += 3)
                points.Add(new Vector3(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]));
        return points;
    }

    // Scaling down a transparent image has to average colours weighted by their alpha.
    private static void Premultiply(RgbaImage image)
    {
        var p = image.Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            if (p[i + 3] == 255) continue;
            for (var c = 0; c < 3; c++) p[i + c] = (byte)(p[i + c] * p[i + 3] / 255);
        }
    }

    private static void Unpremultiply(RgbaImage image)
    {
        var p = image.Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            if (p[i + 3] is 0 or 255) continue;
            for (var c = 0; c < 3; c++) p[i + c] = (byte)Math.Min(255, p[i + c] * 255 / p[i + 3]);
        }
    }
}
