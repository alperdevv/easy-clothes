using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Settings;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Tests;

public sealed class BaseBodyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-body-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Finds_the_mannequin_parts_and_leaves_out_the_worn_slot()
    {
        var male = Directory.CreateDirectory(Path.Combine(_dir, "mp_m_freemode_01")).FullName;
        foreach (var name in new[] { "head_000_r.ydd", "head_diff_000_a_whi.ytd", "uppr_015_r.ydd", "uppr_diff_015_a_whi.ytd", "lowr_000_r.ydd", "feet_001_u.ydd", "feet_diff_001_a_uni.ytd" })
            File.WriteAllBytes(Path.Combine(male, name), []);

        Assert.Equal(_dir, BaseBody.ResolveRoot(_dir));
        Assert.Equal(_dir, BaseBody.ResolveRoot(male));
        Assert.Null(BaseBody.ResolveRoot(Directory.CreateDirectory(Path.Combine(_dir, "otra", "vacia")).FullName));

        var parts = BaseBody.Parts(_dir, PedGender.Male, ComponentSlot.Legs);
        Assert.Equal([ComponentSlot.Head, ComponentSlot.Torso, ComponentSlot.Shoes], parts.Select(p => p.Slot));
        Assert.EndsWith("feet_diff_001_a_uni.ytd", parts[2].TexturePath);
        Assert.Empty(BaseBody.Parts(_dir, PedGender.Female, null));
    }

    [Fact]
    public void Settings_survive_a_round_trip_and_a_corrupt_file_falls_back_to_defaults()
    {
        var path = Path.Combine(_dir, "cfg", "settings.json");
        new AppSettings { BaseBodyFolder = @"D:\gta\modelos", ShowBody = false }.Save(path);

        var loaded = AppSettings.Load(path);
        Assert.Equal((@"D:\gta\modelos", false), (loaded.BaseBodyFolder, loaded.ShowBody));

        File.WriteAllText(path, "{ no es json");
        Assert.Null(AppSettings.Load(path).BaseBodyFolder);
    }

    [Fact]
    public void Software_renderer_draws_textured_geometry_in_front_of_the_background()
    {
        var quad = new MeshData
        {
            Positions = [-0.5f, 0, 1, 0.5f, 0, 1, 0.5f, 0, 0, -0.5f, 0, 0],
            Normals = [0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0],
            TexCoords = [0, 0, 1, 0, 1, 1, 0, 1],
            Indices = [0, 1, 2, 0, 2, 3],
        };
        var red = new RgbaImage(1, 1, [255, 0, 0, 255]);
        var camera = new OrbitCamera();
        camera.Frame(new(-0.5f, 0, 0), new(0.5f, 0, 1));

        var image = SoftwareRenderer.Render([new RenderItem([quad], red)], camera, 64, 64, 0x000000FF);

        var centre = (32 * 64 + 32) * 4;
        Assert.Equal([255, 0, 0, 255], image.Pixels[centre..(centre + 4)]);
        Assert.Equal([0, 0, 0, 255], image.Pixels[..4]);
    }
}
