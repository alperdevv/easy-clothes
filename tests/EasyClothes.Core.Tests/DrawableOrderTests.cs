using EasyClothes.Core.Analysis;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Validation;

namespace EasyClothes.Core.Tests;

public sealed class DrawableOrderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easyclothes-order-").FullName;
    private string Dir => Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;
    private string Source => Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void New_imports_go_last_so_existing_numbers_never_shift()
    {
        var project = Project();
        Import(project, "jbib_005_u.ydd");
        Import(project, "jbib_001_u.ydd");

        Assert.Equal(["jbib_005", "jbib_001"], Order(project));
    }

    [Fact]
    public void Moving_a_garment_swaps_it_with_its_neighbour_within_the_slot()
    {
        var project = Project();
        Import(project, "jbib_000_u.ydd", "jbib_001_u.ydd", "jbib_002_u.ydd", "lowr_000_u.ydd");
        var last = project.Drawables.Single(d => d.Name == "jbib_002");

        Assert.True(DrawableOrder.Move(project, last, -1));
        Assert.False(DrawableOrder.Move(project, project.Drawables.Single(d => d.Name == "lowr_000"), 1));

        Assert.Equal(["jbib_000", "jbib_002", "jbib_001"], Order(project));
        Assert.Equal(1, DrawableOrder.NumberOf(project, last));
    }

    [Fact]
    public void Reserved_numbers_export_as_empty_drawables_and_can_be_filled_later()
    {
        var project = Project();
        Import(project, "jbib_000_u.ydd");
        var reserved = DrawableOrder.Reserve(project, PedGender.Male, ComponentSlot.Top, null);
        Import(project, "jbib_007_u.ydd");

        Assert.Equal(["jbib_000", "reservado 1", "jbib_007"], Order(project));
        Assert.DoesNotContain(ProjectValidator.Validate(project, Dir), i => i.Severity == IssueSeverity.Error);
        var output = Path.Combine(Dir, "out");
        ResourceExporter.Export(project, Dir, output);
        var stream = Path.Combine(output, "stream", "mp_m_freemode_01");
        Assert.Equal(["mp_m_freemode_01_ropa^jbib_000_u.ydd", "mp_m_freemode_01_ropa^jbib_002_u.ydd"],
            Directory.GetFiles(stream, "*.ydd").Select(Path.GetFileName).Order());
        Assert.Equal(3, PedVariation.Read(ResourceFile.Read(Path.Combine(output, "stream", "mp_m_freemode_01_ropa.ymt"))).Components[ComponentSlot.Top].Count);

        var garment = Import(project, "lowr_003_u.ydd").Single();
        DrawableOrder.Fill(project, reserved, garment);

        Assert.Equal(["jbib_000", "lowr_003", "jbib_007"], Order(project));
        Assert.Equal(ComponentSlot.Top, garment.Component);
        Assert.DoesNotContain(reserved, project.Drawables);
    }

    [Fact]
    public void Duplicated_models_and_textures_are_reported()
    {
        var project = Project();
        Import(project, "jbib_000_u.ydd", "jbib_001_u.ydd");
        File.WriteAllText(Path.Combine(Source, "jbib_diff_002_a_uni.ytd"), "same");
        File.WriteAllText(Path.Combine(Source, "jbib_diff_002_b_uni.ytd"), "same");
        File.WriteAllText(Path.Combine(Source, "jbib_002_u.ydd"), "unique");
        DrawableImporter.Import(project, Dir, [Path.Combine(Source, "jbib_002_u.ydd")], PedGender.Male);

        var issues = ProjectValidator.Validate(project, Dir, new AnalysisCache());

        Assert.Contains(issues, i => i.Message.StartsWith("jbib_000, jbib_001: usan el mismo modelo"));
        Assert.Contains(issues, i => i.Message == "jbib_002: las texturas a, b son idénticas.");
    }

    private static ClothingProject Project() => new() { Name = "Ropa", CollectionName = "ropa" };

    private static List<string> Order(ClothingProject project) =>
        ClothingProject.InGameOrder(project.Drawables.Where(d => d.Component == ComponentSlot.Top)).Select(d => d.Name).ToList();

    // Every model gets the same bytes, so models (not textures) are duplicates unless written otherwise.
    private IReadOnlyList<Drawable> Import(ClothingProject project, params string[] models)
    {
        var source = Source;
        var paths = new List<string>();
        foreach (var model in models)
        {
            var path = Path.Combine(source, model);
            File.WriteAllText(path, "model");
            var stem = Path.GetFileNameWithoutExtension(model);
            File.WriteAllText(Path.Combine(source, $"{stem[..4]}_diff_{stem[5..8]}_a_uni.ytd"), stem);
            paths.Add(path);
        }
        return DrawableImporter.Import(project, Dir, paths, PedGender.Male).Added;
    }
}
