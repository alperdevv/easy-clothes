using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;

namespace EasyClothes.Core.Tests;

public class ProjectFileTests
{
    [Fact]
    public async Task Project_survives_a_save_and_load_round_trip()
    {
        var project = new ClothingProject
        {
            Name = "Summer pack",
            CollectionName = "summer_pack",
            Drawables =
            [
                new Drawable
                {
                    Name = "Hoodie",
                    Gender = PedGender.Female,
                    Component = ComponentSlot.Top,
                    ModelPath = "models/hoodie.ydd",
                    Textures = [new() { TexturePath = "tex/hoodie_a.ytd" }, new() { TexturePath = "tex/hoodie_b.ytd" }],
                },
                new Drawable { Name = "Cap", Prop = PropSlot.Hat, ModelPath = "models/cap.ydd", Flags = { HatHair = HatHair.Remove } },
            ],
            Tattoos = [new Tattoo { Name = "Rose", Zone = TattooZone.LeftArm, TexturePath = "tattoos/rose.ytd" }],
        };
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ProjectFile.Extension}");

        try
        {
            await ProjectFile.SaveAsync(project, path);
            var loaded = await ProjectFile.LoadAsync(path);

            Assert.Equal(ProjectFile.Serialize(project), ProjectFile.Serialize(loaded));
            Assert.Equal(ComponentSlot.Top, loaded.Drawables[0].Component);
            Assert.True(loaded.Drawables[1].IsProp);
            Assert.Equal(HatHair.Remove, loaded.Drawables[1].Flags.HatHair);
            Assert.Null(loaded.Drawables[0].Flags.HeelHeight);
            Assert.Contains("\"component\": \"top\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Refuses_projects_from_a_newer_schema()
    {
        var json = """{ "schemaVersion": 99, "name": "x", "collectionName": "x" }""";
        Assert.Throws<InvalidDataException>(() => ProjectFile.Deserialize(json));
    }
}
