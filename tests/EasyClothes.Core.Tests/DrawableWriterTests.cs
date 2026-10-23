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
    public void Written_component_keeps_its_bone_weights_with_the_game_layout()
    {
        var mesh = SkinnedQuad();

        var file = Roundtrip(DrawableWriter.CreateComponent(Component(mesh)));
        var geometry = Assert.Single(Assert.Single(Assert.Single(Assert.Single(DrawableDictionary.Read(file, includeMeshes: true)).Lods).Models));

        Assert.Equal(72, geometry.VertexStride);
        Assert.Equal(mesh.BlendWeights, geometry.Mesh!.BlendWeights);
        Assert.Equal(mesh.BlendIndices, geometry.Mesh.BlendIndices);
        Assert.Equal(mesh.Positions, geometry.Mesh.Positions);
        Assert.Equal(mesh.TexCoords, geometry.Mesh.TexCoords);

        // Values found on every ped component: skinned binding, model mask and an identity bone table.
        var reader = new ResourceReader(file);
        var drawable = reader.PointerArray(TextureDictionary.DictionaryRoot + TextureDictionary.DictionaryItems)[0];
        var model = reader.PointerArray(reader.UInt64(drawable + 0x50))[0];
        Assert.Equal(0x180u, reader.UInt32(model + 0x28));
        Assert.Equal(0x1FF, reader.UInt16(model + 0x2C));
        var bones = reader.UInt64(geometry.Pointer + DrawableDictionary.GeometryBoneIds);
        Assert.Equal(128, reader.UInt16(geometry.Pointer + DrawableDictionary.GeometryBoneCount));
        Assert.Equal(Enumerable.Range(0, 128), Enumerable.Range(0, 128).Select(i => (int)reader.UInt16(bones + (ulong)(i * 2))));
    }

    [Fact]
    public void Component_needs_bone_weights()
    {
        Assert.Throws<ArgumentException>(() => DrawableWriter.CreateComponent(Component(Quad())));
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

    private static MeshData SkinnedQuad()
    {
        var quad = Quad();
        return new MeshData
        {
            Positions = quad.Positions, Normals = quad.Normals, TexCoords = quad.TexCoords, Indices = quad.Indices,
            BlendWeights = [255, 0, 0, 0, 128, 127, 0, 0, 200, 55, 0, 0, 85, 85, 85, 0],
            BlendIndices = [0, 0, 0, 0, 11, 12, 0, 0, 40, 41, 0, 0, 3, 4, 5, 0],
        };
    }

    private static ComponentDrawable Component(MeshData mesh)
    {
        var flat = TextureEncoder.Encode(new RgbaImage(4, 4, Enumerable.Repeat((byte)128, 64).ToArray()), TextureFormat.Dxt1);
        return new ComponentDrawable("jbib_000_u", mesh, "jbib_diff_000_a_uni", new EmbeddedTexture("jbib_normal_000", flat), new EmbeddedTexture("jbib_spec_000", flat));
    }

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
