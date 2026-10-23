using System.Numerics;
using EasyClothes.Core.Export;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Tests;

public sealed class ShirtTests : IDisposable
{
    private const float BodyRadius = 0.15f;
    private const float TrousersRadius = 0.17f;
    private const float TrousersTop = 0.06f;

    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-shirt-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Shirt_is_skinned_and_sits_outside_the_body_and_the_trousers()
    {
        var shirt = ShirtModel.Build(new ShirtDesign(), Body);
        var mesh = shirt.Mesh;

        Assert.True(mesh.IsSkinned);
        Assert.All(mesh.BlendWeights!.Chunk(4), w => Assert.Equal(255, w.Sum(x => x)));
        Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p)));
        Assert.All(mesh.TexCoords, uv => Assert.InRange(uv, 0, 1));
        foreach (var (p, paint) in Vertices(shirt).Where(v => v.Paint.Sleeve < 0.5f))
        {
            var radius = new Vector2(p.X, p.Y).Length();
            Assert.True(radius > BodyRadius, $"{p} is inside the body");
            if (p.Z < TrousersTop) Assert.True(radius > TrousersRadius, $"{p} is inside the trousers");
        }
    }

    [Fact]
    public void Sleeve_and_length_set_where_the_shirt_ends()
    {
        float Reach(ShirtDesign design) => Vertices(ShirtModel.Build(design, Body)).Where(v => v.Position.X > 0).Max(v => v.Position.X);
        float Bottom(ShirtDesign design) => Vertices(ShirtModel.Build(design, Body)).Where(v => v.Paint.Sleeve < 0.5f).Min(v => v.Position.Z);

        Assert.True(Reach(new ShirtDesign { Sleeve = 1 }) > Reach(new ShirtDesign { Sleeve = 0.3f }) + 0.1f);
        Assert.True(Reach(new ShirtDesign { Sleeve = 0 }) < Reach(new ShirtDesign { Sleeve = 0.3f }) - 0.03f);
        Assert.InRange(Bottom(new ShirtDesign { Length = -8 }), 0.07f, 0.1f);
        Assert.InRange(Bottom(new ShirtDesign { Length = 15 }), -0.16f, -0.14f);
    }

    [Fact]
    public void A_v_neck_drops_lower_at_the_front_than_a_crew_neck()
    {
        float FrontNeck(ShirtDesign design) => Vertices(ShirtModel.Build(design, Body))
            .Where(v => MathF.Abs(v.Position.X) < 0.005f && v.Position.Y < 0).Max(v => v.Position.Z);

        Assert.True(FrontNeck(new ShirtDesign { Neckline = Neckline.VNeck, NeckDepth = 12 }) < FrontNeck(new ShirtDesign()) - 0.08f);
    }

    [Fact]
    public void Created_shirt_exports_as_a_top_and_keeps_its_design()
    {
        var project = new ClothingProject { Name = "test", CollectionName = "camis" };
        var shirt = ShirtGarment.Create(project, _dir, new ShirtDesign { Look = new ShirtLook { Body = 0x112233 } }, PedGender.Male, Body);

        Assert.Equal((ComponentSlot.Top, "Camiseta"), (shirt.Component!.Value, shirt.Name));
        var geometry = DrawableDictionary.Read(ResourceFile.Read(Path.Combine(_dir, shirt.ModelPath)), includeMeshes: true)[0].Lods[0].Models[0][0];
        Assert.True(geometry.Mesh!.IsSkinned);
        Assert.Equal(72, geometry.VertexStride);
        Assert.Single(TextureDictionary.Read(ResourceFile.Read(Path.Combine(_dir, shirt.Textures[0].TexturePath))));
        Assert.Equal(0x112233u, ProjectFile.Deserialize(ProjectFile.Serialize(project)).Drawables[0].Shirt!.Look.Body);

        ResourceExporter.Export(project, _dir, Path.Combine(_dir, "out", "camis"));
        var stream = Path.Combine(_dir, "out", "camis", "stream", "mp_m_freemode_01");
        Assert.True(File.Exists(Path.Combine(stream, "mp_m_freemode_01_camis^jbib_000_u.ydd")));
        Assert.True(File.Exists(Path.Combine(stream, "mp_m_freemode_01_camis^jbib_diff_000_a_uni.ytd")));
    }

    [Fact]
    public void Reshaping_rewrites_only_the_model_and_colourways_add_textures()
    {
        var project = new ClothingProject { Name = "test", CollectionName = "camis" };
        var shirt = ShirtGarment.Create(project, _dir, new ShirtDesign(), PedGender.Female, Body);
        var texture = File.ReadAllBytes(Path.Combine(_dir, shirt.Textures[0].TexturePath));
        var model = File.ReadAllBytes(Path.Combine(_dir, shirt.ModelPath));

        var backup = ShirtGarment.Reshape(_dir, shirt, new ShirtDesign { Sleeve = 1, Look = new ShirtLook { Body = 0xFF0000 } }, Body);
        var red = ShirtGarment.AddColourway(_dir, shirt, new ShirtLook { Body = 0xFF0000 }, Body);

        Assert.Equal(model, File.ReadAllBytes(Path.Combine(_dir, backup)));
        Assert.NotEqual(model, File.ReadAllBytes(Path.Combine(_dir, shirt.ModelPath)));
        Assert.Equal(texture, File.ReadAllBytes(Path.Combine(_dir, shirt.Textures[0].TexturePath)));
        Assert.Equal((1f, 1), (shirt.Shirt!.Sleeve, red));
        Assert.EndsWith("jbib_diff_000_b_uni.ytd", shirt.Textures[1].TexturePath);
        Assert.Equal(0xFF0000u, shirt.Shirt.Look.Body);
    }

    [Fact]
    public void A_turtleneck_rises_up_the_neck_and_moves_with_it()
    {
        var plain = ShirtModel.Build(new ShirtDesign().WithStyle(ShirtStyle.LongSleeve), Body);
        var turtleneck = ShirtModel.Build(new ShirtDesign().WithStyle(ShirtStyle.Turtleneck), Body);

        var collar = Vertices(turtleneck).Select((v, i) => (v, i)).Where(x => x.v.Paint.Collar > 0.5f).ToList();
        Assert.NotEmpty(collar);
        Assert.True(collar.Max(c => c.v.Position.Z) > Vertices(plain).Max(v => v.Position.Z) + 0.04f);
        // The top of the collar hangs mostly from the neck bone (index 5 in the stand-in skeleton).
        var top = collar.MaxBy(c => c.v.Position.Z).i;
        Assert.Equal(5, turtleneck.Mesh.BlendIndices![top * 4]);
    }

    [Fact]
    public void A_longer_back_hem_drops_lower_behind()
    {
        var shirt = ShirtModel.Build(new ShirtDesign { Hem = HemShape.LongerBack }, Body);
        var torso = Vertices(shirt).Where(v => v.Paint.Sleeve < 0.5f).ToList();

        var back = torso.Where(v => v.Position.Y > 0.1f).Min(v => v.Position.Z);
        var front = torso.Where(v => v.Position.Y < -0.1f).Min(v => v.Position.Z);
        Assert.True(back < front - 0.04f);
    }

    [Fact]
    public void An_undershirt_is_tighter_and_goes_in_the_undershirt_slot()
    {
        float Radius(ShirtMesh shirt) => Vertices(shirt).Where(v => v.Paint.Sleeve < 0.5f && v.Position.Z is > 0.2f and < 0.4f)
            .Average(v => new Vector2(v.Position.X, v.Position.Y).Length());
        var design = new ShirtDesign { Layer = ShirtLayer.Undershirt };

        Assert.True(Radius(ShirtModel.Build(design, Body)) < Radius(ShirtModel.Build(new ShirtDesign(), Body)));
        var project = new ClothingProject { Name = "test", CollectionName = "camis" };
        var created = ShirtGarment.Create(project, _dir, design, PedGender.Male, Body);
        Assert.Equal(ComponentSlot.Undershirt, created.Component);
        Assert.EndsWith("accs_diff_000_a_uni.ytd", created.Textures[0].TexturePath);
    }

    [Fact]
    public void Prints_patterns_and_extra_colours_end_up_in_the_textures()
    {
        var shirt = ShirtModel.Build(new ShirtDesign(), Body);
        int Red(ShirtLook look) => ShirtTextures.Diffuse(new ShirtDesign { Look = look }, shirt, 256).Pixels.Chunk(4).Count(p => p[0] > 200 && p[1] < 60 && p[2] < 60);

        var plain = new ShirtLook { Body = 0xFFFFFF, Sleeves = 0xFFFFFF, Trim = 0xFFFFFF };
        Assert.Equal(0, Red(plain));
        Assert.True(Red(plain with { Graphics = [new ShirtGraphic { Zone = GraphicZone.ChestCentre, Text = "10", TextColour = 0xFF0000, Size = 20 }] }) > 100);
        Assert.True(Red(plain with { Pattern = new ShirtPattern { Kind = ShirtPatternKind.Stripes, Colour = 0xFF0000 } }) > 1000);

        var project = new ClothingProject { Name = "test", CollectionName = "camis" };
        var created = ShirtGarment.Create(project, _dir, new ShirtDesign { Look = plain }, PedGender.Male, Body, moreColours: [0x112233, 0x445566]);
        Assert.Equal(3, created.Textures.Count);
    }

    [Fact]
    public void Shirt_templates_are_kept_apart_from_cap_templates()
    {
        var root = Path.Combine(_dir, "plantillas");
        var shirts = new ShirtTemplates(Path.Combine(root, "camisetas"));
        var caps = new CapTemplates(root);

        shirts.Save("Rayas", new ShirtDesign { Look = new ShirtLook { Pattern = new ShirtPattern { Kind = ShirtPatternKind.Stripes } } });
        caps.Save("Negra", new CapDesign());

        Assert.Equal(ShirtPatternKind.Stripes, Assert.Single(shirts.List()).Design.Look.Pattern.Kind);
        Assert.Equal("Negra", Assert.Single(caps.List()).Name);
        Assert.True(shirts.Delete("Rayas"));
        Assert.Empty(shirts.List());
    }

    [Theory]
    [InlineData(PedGender.Male)]
    [InlineData(PedGender.Female)]
    public void Every_style_builds_on_the_exported_body(PedGender gender)
    {
        // Set EASYCLOTHES_BODY to the folder with the exported freemode peds.
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_BODY") is not { } root) return;
        var body = BaseBody.Torso(root, gender) ?? throw new InvalidOperationException("No body in EASYCLOTHES_BODY.");

        foreach (var style in Enum.GetValues<ShirtStyle>())
        {
            var shirt = ShirtModel.Build(new ShirtDesign().WithStyle(style), body);
            Assert.InRange(shirt.Mesh.TriangleCount, 500, 6000);
            Assert.All(shirt.Mesh.BlendWeights!.Chunk(4), w => Assert.Equal(255, w.Sum(x => x)));
            Assert.Equal(ShirtTextures.DiffuseSize, ShirtTextures.Diffuse(new ShirtDesign(), shirt).Width);
            Assert.Equal(ShirtTextures.NormalSize, ShirtTextures.Normal(new ShirtDesign().WithStyle(style), shirt).Width);
        }
    }

    private static IEnumerable<(Vector3 Position, ShirtPaint Paint)> Vertices(ShirtMesh shirt) =>
        Enumerable.Range(0, shirt.Mesh.VertexCount).Select(v => (
            new Vector3(shirt.Mesh.Positions[v * 3], shirt.Mesh.Positions[v * 3 + 1], shirt.Mesh.Positions[v * 3 + 2]),
            shirt.Paints[v]));

    // A stand-in for the freemode torso: a cylinder with arms hanging in an A pose, the bones a shirt
    // needs, and a wider cylinder for the trousers. The torso UVs fill the bottom half like the game's.
    private static readonly SkinnedBody Body = MakeBody();

    private static SkinnedBody MakeBody()
    {
        Bone B(string name, Vector3 at) => new(name, 0, -1, Matrix4x4.CreateTranslation(at));
        var bones = new List<Bone>
        {
            B("SKEL_ROOT", Vector3.Zero), B("SKEL_Pelvis", Vector3.Zero),
            B("SKEL_L_Thigh", new(0.09f, 0, -0.09f)), B("SKEL_R_Thigh", new(-0.09f, 0, -0.09f)),
            B("SKEL_Spine3", new(0, 0, 0.28f)), B("SKEL_Neck_1", new(0, 0, 0.53f)),
            B("SKEL_L_UpperArm", new(0.2f, 0, 0.46f)), B("SKEL_L_Forearm", new(0.35f, 0, 0.23f)), B("SKEL_L_Hand", new(0.49f, 0, 0.01f)),
            B("SKEL_R_UpperArm", new(-0.2f, 0, 0.46f)), B("SKEL_R_Forearm", new(-0.35f, 0, 0.23f)), B("SKEL_R_Hand", new(-0.49f, 0, 0.01f)),
        };

        var mesh = new SkinBuilder();
        mesh.Tube(z => new Vector3(0, 0, z), 0, 0.55f, BodyRadius, Vector3.UnitZ, _ => (4, 4), (u, t) => new(u, 0.5f + (1 - t) * 0.49f));
        foreach (var (side, first) in new[] { (1f, 6), (-1f, 9) })
        {
            var shoulder = new Vector3(0.2f * side, 0, 0.46f);
            var elbow = new Vector3(0.35f * side, 0, 0.23f);
            var wrist = new Vector3(0.49f * side, 0, 0.01f);
            Vector3 Along(float s) => s < 0.5f ? Vector3.Lerp(shoulder, elbow, s * 2) : Vector3.Lerp(elbow, wrist, Math.Min(1, s * 2 - 1)) + (s > 1 ? Vector3.Normalize(wrist - elbow) * (s - 1) * 0.5f : Vector3.Zero);
            mesh.Tube(Along, 0, 1.15f, 0.045f, Vector3.Normalize(elbow - shoulder),
                s => s < 0.5f ? (first, s < 0.1f ? 4 : first) : s < 1 ? (first + 1, first + 1) : (first + 2, first + 2),
                (u, t) => new(u, 0.05f + t * 0.4f));
        }
        var legs = new SkinBuilder();
        legs.Tube(z => new Vector3(0, 0, z), -0.4f, TrousersTop, TrousersRadius, Vector3.UnitZ, _ => (1, 1), (u, t) => new(u, t));
        return new SkinnedBody(mesh.Build(), bones, legs.Build());
    }

    private sealed class SkinBuilder
    {
        private const int Around = 24;
        private const int Rings = 16;
        private readonly List<float> _positions = [], _normals = [], _uvs = [];
        private readonly List<byte> _weights = [], _bones = [];
        private readonly List<ushort> _indices = [];

        // A ring per step along a path; the first and last columns share positions like a UV seam.
        public void Tube(Func<float, Vector3> path, float from, float to, float radius, Vector3 axis, Func<float, (int A, int B)> bones, Func<float, float, Vector2> uv)
        {
            var side = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var other = Vector3.Cross(axis, side);
            var start = _positions.Count / 3;
            for (var r = 0; r <= Rings; r++)
            {
                var t = (float)r / Rings;
                var s = from + (to - from) * t;
                var (a, b) = bones(s);
                for (var i = 0; i <= Around; i++)
                {
                    var angle = MathF.Tau * i / Around;
                    var normal = side * MathF.Cos(angle) + other * MathF.Sin(angle);
                    var p = path(s) + normal * radius;
                    _positions.AddRange([p.X, p.Y, p.Z]);
                    _normals.AddRange([normal.X, normal.Y, normal.Z]);
                    var texture = uv((float)i / Around, t);
                    _uvs.AddRange([texture.X, texture.Y]);
                    _weights.AddRange(a == b ? [255, 0, 0, 0] : [128, 127, 0, 0]);
                    _bones.AddRange([(byte)a, (byte)b, 0, 0]);
                }
            }
            for (var r = 0; r < Rings; r++)
            {
                for (var i = 0; i < Around; i++)
                {
                    var v = (ushort)(start + r * (Around + 1) + i);
                    var up = (ushort)(v + Around + 1);
                    _indices.AddRange([v, (ushort)(v + 1), (ushort)(up + 1), v, (ushort)(up + 1), up]);
                }
            }
        }

        public MeshData Build() => new()
        {
            Positions = [.. _positions], Normals = [.. _normals], TexCoords = [.. _uvs], Indices = [.. _indices],
            BlendWeights = [.. _weights], BlendIndices = [.. _bones],
        };
    }
}
