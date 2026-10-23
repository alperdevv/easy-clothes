using EasyClothes.Core.Export;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Tests;

public sealed class CapTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-cap-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Cap_mesh_sits_on_the_head_with_the_visor_in_front()
    {
        var mesh = CapModel.Build(new CapDesign());
        var (min, max) = MeshBounds.Of([mesh]);

        Assert.All(mesh.Positions, v => Assert.True(float.IsFinite(v)));
        Assert.All(mesh.TexCoords, v => Assert.InRange(v, 0, 1));
        Assert.All(Enumerable.Range(0, mesh.VertexCount), v => Assert.Equal(1, Length(mesh.Normals, v), 3));
        // Freemode head top is 0.174 above the head bone; the face looks along +Y.
        Assert.InRange(max.X, 0.18f, 0.21f);
        Assert.InRange(max.Y, 0.17f, 0.21f);
        Assert.InRange(min.Y, -0.12f, -0.08f);
    }

    [Fact]
    public void Shape_options_change_the_mesh()
    {
        var standard = MeshBounds.Of([CapModel.Build(new CapDesign())]);
        var backwards = MeshBounds.Of([CapModel.Build(new CapDesign { Rotation = 180 })]);
        var left = MeshBounds.Of([CapModel.Build(new CapDesign { Rotation = 90 })]);
        var right = MeshBounds.Of([CapModel.Build(new CapDesign { Rotation = -90 })]);
        var tall = MeshBounds.Of([CapModel.Build(new CapDesign { CrownHeight = 14 })]);
        var noVisor = CapModel.Build(new CapDesign { VisorLength = 0, Button = false });

        Assert.True(backwards.Min.Y < standard.Min.Y - 0.05f);
        // Bone Z points to the wearer's left.
        Assert.True(left.Max.Z > standard.Max.Z + 0.05f);
        Assert.True(right.Min.Z < standard.Min.Z - 0.05f);
        Assert.True(tall.Max.X > standard.Max.X + 0.02f);
        Assert.InRange(MeshBounds.Of([noVisor]).Max.Y, 0.1f, 0.14f);
        Assert.True(noVisor.VertexCount < CapModel.Build(new CapDesign()).VertexCount);
    }

    [Fact]
    public void Created_cap_exports_as_a_hat_and_keeps_its_design()
    {
        var project = new ClothingProject { Name = "test", CollectionName = "gorras" };
        var cap = CapGarment.Create(project, _dir, new CapDesign { Colours = new CapColours { Front = 0xFFFFFF } }, PedGender.Male);

        Assert.Equal((PropSlot.Hat, "Gorra"), (cap.Prop!.Value, cap.Name));
        var drawable = Assert.Single(DrawableDictionary.Read(ResourceFile.Read(Path.Combine(_dir, cap.ModelPath))));
        Assert.True(drawable.Lods[0].Triangles > 1000);
        Assert.Single(TextureDictionary.Read(ResourceFile.Read(Path.Combine(_dir, cap.Textures[0].TexturePath))));

        var reloaded = ProjectFile.Deserialize(ProjectFile.Serialize(project)).Drawables[0];
        Assert.Equal(0xFFFFFFu, reloaded.Cap!.Colours.Front);

        ResourceExporter.Export(project, _dir, Path.Combine(_dir, "out", "gorras"));
        var stream = Path.Combine(_dir, "out", "gorras", "stream", "mp_m_freemode_01");
        Assert.True(File.Exists(Path.Combine(stream, "mp_m_freemode_01_p_gorras^p_head_000.ydd")));
        Assert.True(File.Exists(Path.Combine(stream, "mp_m_freemode_01_p_gorras^p_head_diff_000_a.ytd")));
    }

    [Fact]
    public void Reshaping_rewrites_only_the_model_and_colourways_add_textures()
    {
        var project = new ClothingProject { Name = "test", CollectionName = "gorras" };
        var cap = CapGarment.Create(project, _dir, new CapDesign(), PedGender.Female);
        var texture = File.ReadAllBytes(Path.Combine(_dir, cap.Textures[0].TexturePath));
        var model = File.ReadAllBytes(Path.Combine(_dir, cap.ModelPath));

        var backup = CapGarment.Reshape(_dir, cap, new CapDesign { VisorLength = 9, Colours = new CapColours { Crown = 0xFF0000 } });
        var red = CapGarment.AddColourway(_dir, cap, new CapColours { Crown = 0xFF0000 });

        Assert.Equal(model, File.ReadAllBytes(Path.Combine(_dir, backup)));
        Assert.NotEqual(model, File.ReadAllBytes(Path.Combine(_dir, cap.ModelPath)));
        Assert.Equal(texture, File.ReadAllBytes(Path.Combine(_dir, cap.Textures[0].TexturePath)));
        Assert.Equal((9f, 1), (cap.Cap!.VisorLength, red));
        Assert.EndsWith("p_head_diff_000_b.ytd", cap.Textures[1].TexturePath);
        Assert.Equal(0xFF0000u, cap.Cap.Colours.Crown);
    }

    [Fact]
    public void Styles_keep_rotation_and_colours_and_old_backwards_caps_still_load()
    {
        var mine = new CapDesign { Rotation = 90, Colours = new CapColours { Crown = 0x123456 } };

        var snapback = mine.WithStyle(CapStyle.Snapback);
        var json = """{"name":"t","collectionName":"t","drawables":[{"name":"g","prop":"hat","modelPath":"m.ydd","cap":{"backwards":true,"visorLength":5}}]}""";
        var legacy = ProjectFile.Deserialize(json).Drawables[0].Cap!;

        Assert.Equal((CapStyle.Snapback, 90f, 0x123456u), (snapback.Style, snapback.Rotation, snapback.Colours.Crown));
        Assert.True(snapback.VisorCurve < 0.1f);
        Assert.Equal((180f, 5f), (legacy.Rotation, legacy.VisorLength));
        Assert.DoesNotContain("backwards", ProjectFile.Serialize(new ClothingProject { Name = "t", CollectionName = "t", Drawables = [new Drawable { Name = "g", ModelPath = "m", Cap = legacy }] }));
    }

    [Fact]
    public void Trucker_back_panels_are_mesh()
    {
        var plain = CapTextures.Diffuse(new CapDesign(), 256);
        var trucker = CapTextures.Diffuse(new CapDesign().WithStyle(CapStyle.Trucker), 256);

        static float Dark(RgbaImage image, int x0, int x1) =>
            Enumerable.Range(10, 40).SelectMany(y => Enumerable.Range(x0, x1 - x0).Select(x => image.Pixels[(y * image.Width + x) * 4])).Count(v => v < 15) / (float)(40 * (x1 - x0));
        Assert.True(Dark(trucker, 10, 70) > Dark(plain, 10, 70) + 0.1f);
        Assert.Equal(Dark(plain, 100, 150), Dark(trucker, 100, 150), 2);
    }

    [Theory]
    [InlineData(CapStyle.Baseball)]
    [InlineData(CapStyle.Trucker)]
    [InlineData(CapStyle.Bucket)]
    [InlineData(CapStyle.Beanie)]
    [InlineData(CapStyle.SunVisor)]
    public void Every_style_builds_paints_and_stays_off_the_skull_at_its_tightest(CapStyle style)
    {
        var design = new CapDesign().WithStyle(style) with { Size = 0, CrownHeight = 0, CrownShape = 0, Pompom = true };

        var mesh = CapModel.Build(design);
        var diffuse = CapTextures.Diffuse(design, 128);

        Assert.All(mesh.TexCoords, v => Assert.InRange(v, 0, 1));
        // The freemode skull, roughly: no vertex of the hat may sit inside it.
        var centre = new System.Numerics.Vector3(0.075f, 0.016f, 0);
        var radii = new System.Numerics.Vector3(0.099f, 0.101f, 0.09f);
        for (var v = 0; v < mesh.VertexCount; v++)
        {
            var p = new System.Numerics.Vector3(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);
            Assert.True(((p - centre) / radii).Length() >= 1, $"vertex {v} at {p} is inside the head");
        }
        Assert.True(diffuse.Pixels.Distinct().Count() > 20);
    }

    [Fact]
    public void Beanie_cuff_and_pompom_take_their_colours()
    {
        var design = new CapDesign { Colours = new CapColours { Crown = 0x000000, Front = 0xFF0000, Button = 0x00FF00 } }.WithStyle(CapStyle.Beanie) with { Pompom = true };
        var layout = CapLayout.For(CapKind.Beanie);
        var image = CapTextures.Diffuse(design, 256);

        (int R, int G, int B) At(UvRect r)
        {
            var (x, y) = ((int)((r.U0 + r.U1) / 2 * 256), (int)((r.V0 + r.V1) / 2 * 256));
            var o = (y * 256 + x) * 4;
            return (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2]);
        }
        Assert.True(At(layout.Visor).R > 150 && At(layout.Visor).G < 60);
        Assert.True(At(layout.Extra).G > 120 && At(layout.Extra).R < 80);
        Assert.True(At(layout.Crown).R < 40);
    }

    [Fact]
    public void Templates_are_saved_listed_and_deleted_and_bad_files_are_skipped()
    {
        var store = new CapTemplates(Path.Combine(_dir, "plantillas"));
        Assert.Empty(store.List());

        store.Save("Mi snapback", new CapDesign { Rotation = 90 }.WithStyle(CapStyle.Snapback));
        var odd = store.Save("../fuera", new CapDesign().WithStyle(CapStyle.Beanie));
        File.WriteAllText(Path.Combine(store.Folder, "rota.json"), "{ esto no es json");

        Assert.Equal("fuera", odd.Name);
        Assert.Equal(["fuera", "Mi snapback"], store.List().Select(t => t.Name));
        Assert.Equal((CapStyle.Snapback, 90f), (store.Find("mi SNAPBACK")!.Design.Style, store.Find("mi snapback")!.Design.Rotation));
        Assert.True(store.Delete("fuera"));
        Assert.False(store.Delete("fuera"));
        Assert.Throws<ArgumentException>(() => store.Save("  ", new CapDesign()));
    }

    private static float Length(float[] v, int i) => MathF.Sqrt(v[i * 3] * v[i * 3] + v[i * 3 + 1] * v[i * 3 + 1] + v[i * 3 + 2] * v[i * 3 + 2]);
}
