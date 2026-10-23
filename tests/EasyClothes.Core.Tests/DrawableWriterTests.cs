using System.Numerics;
using EasyClothes.Core.Meshes;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public class DrawableWriterTests
{
    [Fact]
    public void Written_prop_reads_back_with_the_same_mesh()
    {
        var mesh = Quad();

        var file = Roundtrip(DrawableWriter.CreateProp(Prop(mesh)));
        var drawable = Assert.Single(DrawableDictionary.Read(file, includeMeshes: true));

        Assert.Equal("p_head_000", drawable.Name);
        var read = Assert.Single(Assert.Single(Assert.Single(drawable.Lods).Models)).Mesh!;
        Assert.Equal(mesh.Positions, read.Positions);
        Assert.Equal(mesh.Normals, read.Normals);
        Assert.Equal(mesh.TexCoords, read.TexCoords);
        Assert.Equal(mesh.Indices, read.Indices);
        Assert.NotEmpty(file.GraphicsData);
    }

    [Fact]
    public void Prop_must_fit_in_sixteen_bit_indices()
    {
        var empty = new MeshData { Positions = [], Normals = [], TexCoords = [], Indices = [] };

        Assert.Throws<ArgumentException>(() => DrawableWriter.CreateProp(Prop(empty)));
    }

    [Fact]
    public void Tangents_follow_the_u_direction()
    {
        var tangents = MeshTangents.Compute(Quad());

        Assert.All(tangents, t => Assert.Equal(new Vector4(1, 0, 0, 1), t));
    }

    // A unit quad facing +Z with U along +X and V along -Y, like the game's textures.
    private static MeshData Quad() => new()
    {
        Positions = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
        Normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
        TexCoords = [0, 1, 1, 1, 1, 0, 0, 0],
        Indices = [0, 1, 2, 0, 2, 3],
    };

    private static PropDrawable Prop(MeshData mesh)
    {
        var flat = TextureEncoder.Encode(new RgbaImage(4, 4, Enumerable.Repeat((byte)128, 64).ToArray()), TextureFormat.Dxt1);
        return new PropDrawable("p_head_000", mesh, "p_head_diff_000_a", new EmbeddedTexture("p_head_normal_000", flat), new EmbeddedTexture("p_head_spec_000", flat));
    }

    private static ResourceFile Roundtrip(ResourceFile file)
    {
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        return ResourceFile.Read(stream);
    }
}
