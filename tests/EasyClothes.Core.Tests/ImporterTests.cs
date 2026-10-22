using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Validation;

namespace EasyClothes.Core.Tests;

public sealed class ImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easyclothes-").FullName;
    private string Source => Path.Combine(_root, "source");
    private string ProjectDir => Path.Combine(_root, "project");

    public ImporterTests()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(ProjectDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Imports_a_model_with_its_textures_in_letter_order()
    {
        var model = Touch("mp_m_freemode_01_mp_m_eup^jbib_040_u.ydd");
        Touch("mp_m_freemode_01_mp_m_eup^jbib_diff_040_b_uni.ytd");
        Touch("mp_m_freemode_01_mp_m_eup^jbib_diff_040_a_uni.ytd");
        Touch("mp_m_freemode_01_mp_m_eup^jbib_diff_041_a_uni.ytd");
        Touch("mp_f_freemode_01_mp_f_eup^jbib_diff_040_a_uni.ytd");
        var project = NewProject();

        var result = DrawableImporter.Import(project, ProjectDir, [model], PedGender.Female);

        var drawable = Assert.Single(result.Added);
        Assert.Empty(result.Warnings);
        Assert.Equal(("jbib_040", PedGender.Male, ComponentSlot.Top), (drawable.Name, drawable.Gender, drawable.Component!.Value));
        Assert.Equal(["a_uni", "b_uni"], drawable.Textures.Select(t => Path.GetFileNameWithoutExtension(t.TexturePath)[^5..]));
        Assert.Empty(ProjectValidator.Validate(project, ProjectDir));
    }

    [Fact]
    public void Uses_the_fallback_gender_and_dedupes_names()
    {
        var model = Touch("p_eyes_003.ydd");
        Touch("p_eyes_diff_003_a.ytd");
        var project = NewProject();

        DrawableImporter.Import(project, ProjectDir, [model], PedGender.Female);
        var second = DrawableImporter.Import(project, ProjectDir, [model], PedGender.Female);

        Assert.Equal("p_eyes_003 (2)", second.Added[0].Name);
        Assert.Equal(PropSlot.Glasses, second.Added[0].Prop);
    }

    [Fact]
    public void First_person_models_and_cloth_physics_come_along_with_their_garment()
    {
        Touch("mp_f_freemode_01_mine^lowr_002_u.ydd");
        Touch("mp_f_freemode_01_mine^lowr_diff_002_a_uni.ytd");
        Touch("mp_f_freemode_01_mine^lowr_002_u_1.ydd");
        Touch("mp_f_freemode_01_mine^lowr_002_u.yld");
        Touch("other/jbib_005_u_1.ydd");
        var project = NewProject();

        var result = DrawableImporter.Import(project, ProjectDir, [Source], PedGender.Male);

        var skirt = Assert.Single(result.Added);
        Assert.EndsWith("lowr_002_u_1.ydd", skirt.FirstPersonModelPath);
        Assert.EndsWith("lowr_002_u.yld", skirt.ClothPhysicsPath);
        Assert.True(File.Exists(Path.Combine(ProjectDir, skirt.ClothPhysicsPath!)));
        Assert.Contains(result.Warnings, w => w.Contains("jbib_005_u_1.ydd") && w.Contains("primera persona"));
        Assert.DoesNotContain(ProjectValidator.Validate(project, ProjectDir), i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Warns_about_unrecognised_names_and_missing_textures()
    {
        var unknown = Touch("jacket.ydd");
        var lonely = Touch("feet_001_u.ydd");
        var project = NewProject();

        var result = DrawableImporter.Import(project, ProjectDir, [unknown, lonely], PedGender.Male);

        Assert.Single(result.Added);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(ProjectValidator.Validate(project, ProjectDir), i => i.Message.Contains("no tiene texturas"));
    }

    [Fact]
    public void Imports_whole_folders_taking_the_gender_from_the_folder_name()
    {
        var model = Touch("pack_a/stream/mp_f_freemode_01_mp_f_mine/jbib_000_u.ydd");
        Touch("pack_a/stream/mp_f_freemode_01_mp_f_mine/jbib_diff_000_a_uni.ytd");
        var other = Touch("pack_b/jbib_000_u.ydd");
        Touch("pack_b/jbib_diff_000_a_uni.ytd");
        Touch("pack_b/jbib_diff_000_b_uni.ytd");
        File.WriteAllText(other, "b");
        var project = NewProject();

        var result = DrawableImporter.Import(project, ProjectDir, [Path.Combine(Source, "pack_a"), Path.Combine(Source, "pack_b"), model], PedGender.Male);

        Assert.Equal([(PedGender.Male, 2), (PedGender.Female, 1)], result.Added.Select(d => (d.Gender, d.Textures.Count)).Order());
        Assert.Equal(2, result.Added.Count);
        Assert.Empty(ProjectValidator.Validate(project, ProjectDir));
    }

    [Fact]
    public void Same_named_models_from_different_packs_keep_their_own_files()
    {
        var first = Touch("a/lowr_001_u.ydd");
        var second = Touch("b/lowr_001_u.ydd");
        File.WriteAllText(second, "b");
        var project = NewProject();

        DrawableImporter.Import(project, ProjectDir, [first, second], PedGender.Male);
        DrawableImporter.Import(project, ProjectDir, [ProjectDir], PedGender.Male);

        Assert.Equal(2, project.Drawables.Count);
        Assert.NotEqual(project.Drawables[0].ModelPath, project.Drawables[1].ModelPath);
        Assert.Equal("b", File.ReadAllText(Path.Combine(ProjectDir, project.Drawables[1].ModelPath)));
    }

    [Fact]
    public void Validator_reports_missing_files_and_bad_collection_names()
    {
        var project = new ClothingProject
        {
            Name = "x",
            CollectionName = "Mi Pack",
            Drawables = [new Drawable { Name = "a", Component = ComponentSlot.Top, ModelPath = "nope.ydd", Textures = [new() { TexturePath = "nope.ytd" }] }],
        };

        var issues = ProjectValidator.Validate(project, ProjectDir);

        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal(IssueSeverity.Error, i.Severity));
    }

    private ClothingProject NewProject() => new() { Name = "Test", CollectionName = "test" };

    private string Touch(string name)
    {
        var path = Path.Combine(Source, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
