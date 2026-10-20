using System.Numerics;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Tests;

public class ViewingTests
{
    [Fact]
    public void Wireframe_lists_each_shared_edge_once()
    {
        // Two triangles sharing the 1-2 edge: a quad has 5 unique edges.
        var quad = new MeshData { Positions = new float[12], Normals = new float[12], TexCoords = new float[8], Indices = [0, 1, 2, 2, 1, 3] };

        var lines = quad.WireframeIndices();

        Assert.Equal(10, lines.Length);
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
}
