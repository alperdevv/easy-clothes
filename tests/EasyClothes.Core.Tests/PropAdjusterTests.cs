using System.Buffers.Binary;
using System.Numerics;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public sealed class PropAdjusterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-adjust-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Turning_a_cap_backwards_rotates_vertices_normals_and_bounds_around_the_head()
    {
        var cap = SaveCap();

        PropAdjuster.Apply(_dir, cap, new PropPlacement { Turn = 180 });

        var mesh = Read(cap.ModelPath);
        var centre = ObjProp.HeadCentre;
        // A visor 10 cm in front of the head ends up 10 cm behind it, facing back.
        Assert.Equal(centre.Y - 0.1f, mesh.Positions[1], 4);
        Assert.Equal(-1, mesh.Normals[1], 4);
        var (min, max) = DrawableBounds(cap.ModelPath);
        Assert.Equal(centre.Y - 0.1f, min.Y, 4);
        Assert.Equal(centre.Y - 0.1f, max.Y, 4);
        Assert.True(File.Exists(Path.Combine(_dir, cap.Adjusted!.OriginalModelPath)));
    }

    [Fact]
    public void Every_change_starts_from_the_original_model()
    {
        var cap = SaveCap();
        var before = Read(cap.ModelPath).Positions;

        PropAdjuster.Apply(_dir, cap, new PropPlacement { Turn = 90, Up = 3 });
        PropAdjuster.Apply(_dir, cap, new PropPlacement { Up = 2 });

        var after = Read(cap.ModelPath).Positions;
        for (var i = 0; i < before.Length; i += 3)
        {
            Assert.Equal(before[i] + 0.02f, after[i], 4);
            Assert.Equal(before[i + 1], after[i + 1], 4);
            Assert.Equal(before[i + 2], after[i + 2], 4);
        }
        Assert.Equal(2, cap.Adjusted!.Placement.Up);
    }

    // A small square standing 10 cm in front of the head centre, facing forward (+y in head-bone space).
    private Drawable SaveCap()
    {
        var c = ObjProp.HeadCentre;
        var y = c.Y + 0.1f;
        var mesh = new MeshData
        {
            Positions = [c.X, y, -0.02f, c.X, y, 0.02f, c.X + 0.02f, y, 0.02f, c.X + 0.02f, y, -0.02f],
            Normals = [0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0],
            TexCoords = [0, 1, 1, 1, 1, 0, 0, 0],
            Indices = [0, 1, 2, 0, 2, 3],
        };
        var flat = TextureEncoder.Encode(new RgbaImage(4, 4, Enumerable.Repeat((byte)128, 64).ToArray()), TextureFormat.Dxt1);
        var relative = "assets/female/p_head_000/p_head_000.ydd";
        Directory.CreateDirectory(Path.Combine(_dir, "assets/female/p_head_000"));
        DrawableWriter.CreateProp(new PropDrawable("p_head_000", mesh, "p_head_diff_000_a", new EmbeddedTexture("n", flat), new EmbeddedTexture("s", flat)))
            .Write(Path.Combine(_dir, relative));
        return new Drawable { Name = "p_head_000", Gender = PedGender.Female, Prop = PropSlot.Hat, ModelPath = relative };
    }

    private MeshData Read(string relative) =>
        DrawableDictionary.Read(ResourceFile.Read(Path.Combine(_dir, relative)), includeMeshes: true)[0].Lods[0].Models[0][0].Mesh!;

    private (Vector3 Min, Vector3 Max) DrawableBounds(string relative)
    {
        var reader = new ResourceReader(ResourceFile.Read(Path.Combine(_dir, relative)));
        var drawable = reader.PointerArray(TextureDictionary.DictionaryRoot + TextureDictionary.DictionaryItems)[0];
        Vector3 At(ulong at) => new(
            BinaryPrimitives.ReadSingleLittleEndian(reader.At(at, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(reader.At(at + 4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(reader.At(at + 8, 4)));
        return (At(drawable + 0x30), At(drawable + 0x40));
    }
}
