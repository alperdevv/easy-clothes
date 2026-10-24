using System.Numerics;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Tests;

public class ViewingTests
{
    [Fact]
    public void Wireframe_lists_each_shared_edge_once()
    {
        // Two triangles sharing an edge: 5 unique edges.
        var quad = new MeshData { Positions = new float[12], Normals = new float[12], TexCoords = new float[8], Indices = [0, 1, 2, 2, 1, 3] };

        var lines = quad.WireframeIndices();

        Assert.Equal(10, lines.Length);
    }

    [Fact]
    public void Transformed_mesh_moves_positions_and_turns_normals()
    {
        var mesh = new MeshData { Positions = [1, 0, 0], Normals = [1, 0, 0], TexCoords = [0, 0], Indices = [] };

        var moved = mesh.Transformed(Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0, 2));

        Assert.Equal([0, 1, 2], moved.Positions.Select(v => MathF.Round(v, 5)));
        Assert.Equal([0, 1, 0], moved.Normals.Select(v => MathF.Round(v, 5)));
        Assert.Equal([1, 0, 0], mesh.Positions);
    }

    [Fact]
    public void Camera_starts_in_front_of_the_target_looking_along_y()
    {
        var camera = new OrbitCamera { Target = new Vector3(0, 0, 1), Distance = 3 };

        Assert.Equal(new Vector3(0, -3, 1), camera.Position);
    }

    [Fact]
    public void Pitch_is_clamped_before_the_poles()
    {
        var camera = new OrbitCamera();
        camera.Rotate(0, 10);
        Assert.True(camera.Position.Z < camera.Distance);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.01f)]
    public void Views_centre_the_garment_with_an_eight_percent_margin_whatever_its_size_and_place(float scale)
    {
        // A wide quad facing the camera, far from the origin like a badge on the chest.
        var (x, z, w, h) = (5f, 3f, 2 * scale, 0.5f * scale);
        var quad = new MeshData
        {
            Positions = [x, 0, z, x + w, 0, z, x, 0, z + h, x + w, 0, z + h],
            Normals = [0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0],
            TexCoords = new float[8],
            Indices = [0, 1, 2, 2, 1, 3],
        };

        var image = GarmentPreview.RenderViews(new RenderItem([quad], null), [], [0], 200, 300, background: 0);

        var (minX, maxX, minY, maxY) = OpaqueBounds(image);
        Assert.Equal(0, image.Pixels[3]);
        Assert.InRange(minX, 14, 18);
        Assert.InRange(200 - 1 - maxX, 14, 18);
        Assert.InRange((minY + maxY) / 2.0, 148, 152);
    }

    [Fact]
    public void Translucent_texels_are_blended_over_what_is_behind_them_even_in_front_of_it_in_draw_order()
    {
        // The glass is drawn first and nearer the camera; the red quad behind still shows through it.
        var glass = new RenderItem([Quad(y: -1)], Solid(0, 0, 255, 128));
        var wall = new RenderItem([Quad(y: 0)], Solid(255, 0, 0, 255));
        var camera = new OrbitCamera { Target = new Vector3(0.5f, 0, 0.5f), Distance = 4 };

        var image = SoftwareRenderer.Render([glass, wall], camera, 20, 20, backgroundRgba: 0);
        var transparent = SoftwareRenderer.Render([glass], camera, 20, 20, backgroundRgba: 0);

        var centre = (10 * 20 + 10) * 4;
        Assert.InRange(image.Pixels[centre], 40, 215);
        Assert.InRange(image.Pixels[centre + 2], 40, 215);
        Assert.Equal(255, image.Pixels[centre + 3]);
        Assert.Equal(0, transparent.Pixels[centre]);
        Assert.InRange(transparent.Pixels[centre + 2], 100, 255);
        Assert.InRange(transparent.Pixels[centre + 3], 120, 136);
    }

    private static MeshData Quad(float y) => new()
    {
        Positions = [0, y, 0, 1, y, 0, 0, y, 1, 1, y, 1],
        Normals = [0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0],
        TexCoords = new float[8],
        Indices = [0, 1, 2, 2, 1, 3],
    };

    private static Textures.RgbaImage Solid(byte r, byte g, byte b, byte a) => new(1, 1, [r, g, b, a]);

    private static (int MinX, int MaxX, int MinY, int MaxY) OpaqueBounds(Textures.RgbaImage image)
    {
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            if (image.Pixels[(y * image.Width + x) * 4 + 3] < 128) continue;
            (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
        }
        return (minX, maxX, minY, maxY);
    }

    [Fact]
    public void Framing_centres_on_the_bounds_and_fits_them()
    {
        var camera = new OrbitCamera();
        camera.Frame(new Vector3(-1, -1, 0), new Vector3(1, 1, 2));

        Assert.Equal(new Vector3(0, 0, 1), camera.Target);
        Assert.True(camera.Distance > MathF.Sqrt(3));
        camera.Zoom(0.0001f);
        Assert.Equal(camera.MinDistance, camera.Distance);
    }

    [Fact]
    public void Panning_moves_the_target_in_the_view_plane()
    {
        var camera = new OrbitCamera { Distance = 2 };
        var before = camera.Position - camera.Target;

        camera.Pan(1, 1);

        Assert.True(camera.Target.X > 0 && camera.Target.Z > 0);
        Assert.Equal(0, camera.Target.Y, 5);
        Assert.Equal(before, camera.Position - camera.Target);
    }
}
