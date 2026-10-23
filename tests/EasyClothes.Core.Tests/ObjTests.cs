using System.Globalization;
using System.Numerics;
using System.Text;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Tests;

public sealed class ObjTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-obj-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_quads_negative_indices_and_flips_v()
    {
        var path = Write("quad.obj", """
            mtllib quad.mtl
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            f -4/-4 -3/-3 -2/-2 -1/-1
            """);
        Write("quad.mtl", "newmtl a\nmap_Kd tela.png\n");
        File.WriteAllBytes(Path.Combine(_dir, "tela.png"), []);

        var model = ObjReader.Read(path);

        Assert.Equal((4, 2), (model.Mesh.VertexCount, model.Mesh.TriangleCount));
        Assert.Equal([0f, 1f], model.Mesh.TexCoords[..2]);
        Assert.Equal(1, model.Mesh.Normals[2], 3);
        Assert.EndsWith("tela.png", model.TexturePath);
        Assert.Empty(model.Warnings);
    }

    [Fact]
    public void Bad_files_explain_what_is_wrong()
    {
        Assert.Contains("caras", Assert.Throws<InvalidDataException>(() => ObjReader.Read(Write("vacio.obj", "v 0 0 0\n"))).Message);
        Assert.Contains("índice", Assert.Throws<InvalidDataException>(() => ObjReader.Read(Write("roto.obj", "v 0 0 0\nf 1 2 3\n"))).Message);
        Assert.Contains("UV", ObjReader.Read(Write("sinuv.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n")).Warnings.Single());
    }

    [Fact]
    public void A_model_made_on_the_reference_head_lands_where_it_was_made_and_others_are_fitted()
    {
        var cap = CapModel.Build(new CapDesign());
        var inMetres = ObjReader.Read(Write("gorra.obj", Obj(cap, 1, Vector3.Zero))).Mesh;
        var inCentimetres = ObjReader.Read(Write("gorra_cm.obj", Obj(cap, 100, new Vector3(20, 300, 5)))).Mesh;

        var placed = MeshBounds.Of([ObjProp.Place(inMetres, new PropPlacement())]);
        var fitted = ObjProp.Fit(inCentimetres);
        var refitted = MeshBounds.Of([ObjProp.Place(inCentimetres, fitted)]);

        Assert.Equal(MeshBounds.Of([cap]).Max.X, placed.Max.X, 4);
        Assert.Equal(0.01f, fitted.Scale);
        Assert.Equal(MeshBounds.Of([cap]).Max.X, refitted.Max.X, 1);
        Assert.InRange(refitted.Min.Z + refitted.Max.Z, -0.01f, 0.01f);
    }

    [Fact]
    public void Imported_model_becomes_a_prop_that_can_be_moved_later()
    {
        var project = new ClothingProject { Name = "t", CollectionName = "t" };
        var obj = Write("gafas.obj", Obj(CapModel.Build(new CapDesign()), 1, Vector3.Zero));

        var glasses = ObjProp.Create(project, _dir, obj, null, PropSlot.Glasses, PedGender.Female, "gafas", new PropPlacement());
        var before = Top(glasses);
        var backup = ObjProp.Replace(_dir, glasses, new PropPlacement { Up = 2 });

        Assert.Equal((PropSlot.Glasses, 1), (glasses.Prop!.Value, glasses.Textures.Count));
        Assert.EndsWith("p_eyes_000.ydd", glasses.ModelPath);
        Assert.True(File.Exists(Path.Combine(_dir, glasses.Imported!.SourcePath)));
        Assert.True(File.Exists(Path.Combine(_dir, backup)));
        Assert.Equal(before + 0.02f, Top(glasses), 3);
    }

    private float Top(Drawable d) =>
        MeshBounds.Of(DrawableDictionary.Read(ResourceFile.Read(Path.Combine(_dir, d.ModelPath)), includeMeshes: true)[0].Lods[0].Models[0].Select(g => g.Mesh!)).Max.X;

    // A mesh in head-bone space written as Blender would export it.
    private static string Obj(MeshData mesh, float scale, Vector3 shift)
    {
        var text = new StringBuilder();
        for (var v = 0; v < mesh.VertexCount; v++)
        {
            var o = new Vector3(mesh.Positions[v * 3 + 2], mesh.Positions[v * 3], mesh.Positions[v * 3 + 1]) * scale + shift;
            text.Append(CultureInfo.InvariantCulture, $"v {o.X} {o.Y} {o.Z}\nvt {mesh.TexCoords[v * 2]} {1 - mesh.TexCoords[v * 2 + 1]}\n");
        }
        for (var t = 0; t < mesh.Indices.Length; t += 3)
            text.Append($"f {mesh.Indices[t] + 1}/{mesh.Indices[t] + 1} {mesh.Indices[t + 1] + 1}/{mesh.Indices[t + 1] + 1} {mesh.Indices[t + 2] + 1}/{mesh.Indices[t + 2] + 1}\n");
        return text.ToString();
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }
}
