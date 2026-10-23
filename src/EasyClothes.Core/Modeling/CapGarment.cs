using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Modeling;

// Adds generated caps to a project and keeps them editable: the shape can change at any time
// (only the model is rewritten) and each set of colours becomes a texture variation.
public static class CapGarment
{
    private const string ModelFile = "p_head_000.ydd";

    public static Drawable Create(ClothingProject project, string projectDir, CapDesign design, PedGender gender, string name = "Gorra")
    {
        design = design.Clamped();
        name = DrawableImporter.UniqueName(project, gender, name);
        var dir = DrawableImporter.UniqueDir(projectDir, Path.Combine(DrawableImporter.AssetsFolder, gender.ToString().ToLowerInvariant(), name));
        Directory.CreateDirectory(Path.Combine(projectDir, dir));

        var drawable = new Drawable
        {
            Name = name,
            Gender = gender,
            Prop = PropSlot.Hat,
            ModelPath = Path.Combine(dir, ModelFile).Replace('\\', '/'),
            Cap = design,
        };
        WriteModel(projectDir, drawable, design);
        AddColourway(projectDir, drawable, design.Colours);
        DrawableOrder.PlaceAtEnd(project, drawable);
        project.Drawables.Add(drawable);
        return drawable;
    }

    // Same UV layout whatever the shape, so every texture variation keeps fitting.
    public static string Reshape(string projectDir, Drawable drawable, CapDesign design)
    {
        if (drawable.Cap is null) throw new InvalidOperationException($"{drawable.Name} no es una gorra creada con EasyClothes.");
        var backup = ProjectBackups.Copy(projectDir, drawable.ModelPath);
        drawable.Cap = design.Clamped() with { Colours = drawable.Cap.Colours };
        WriteModel(projectDir, drawable, drawable.Cap);
        return backup;
    }

    public static int AddColourway(string projectDir, Drawable drawable, CapColours colours)
    {
        var cap = drawable.Cap ?? throw new InvalidOperationException($"{drawable.Name} no es una gorra creada con EasyClothes.");
        if (drawable.Textures.Count >= Drawable.MaxTextures)
            throw new InvalidOperationException($"{drawable.Name} ya tiene {Drawable.MaxTextures} texturas, el máximo del juego.");
        drawable.Cap = cap with { Colours = colours };
        var name = UniqueTextureName(projectDir, drawable);
        var relative = Path.Combine(Path.GetDirectoryName(drawable.ModelPath) ?? "", name + ".ytd").Replace('\\', '/');
        var diffuse = TextureEncoder.Encode(CapTextures.Diffuse(drawable.Cap), TextureFormat.Dxt1);
        TextureDictionaryWriter.Create([(name, diffuse)]).Write(Path.Combine(projectDir, relative));
        drawable.Textures.Add(new TextureVariation { TexturePath = relative });
        return drawable.Textures.Count - 1;
    }

    // Removed variations leave their files behind, so the next free name is taken from disk.
    private static string UniqueTextureName(string projectDir, Drawable drawable)
    {
        var dir = Path.Combine(projectDir, Path.GetDirectoryName(drawable.ModelPath) ?? "");
        for (var i = drawable.Textures.Count; ; i++)
        {
            var name = $"p_head_diff_000_{(i < Drawable.MaxTextures ? TextureVariation.LetterFor(i).ToString() : i.ToString())}";
            if (!File.Exists(Path.Combine(dir, name + ".ytd"))) return name;
        }
    }

    public static ResourceFile Model(CapDesign design) => DrawableWriter.CreateProp(new PropDrawable(
        "p_head_000",
        CapModel.Build(design),
        "p_head_diff_000_a",
        new EmbeddedTexture("p_head_normal_000", TextureEncoder.Encode(CapTextures.Normal(design), TextureFormat.Dxt1)),
        new EmbeddedTexture("p_head_spec_000", TextureEncoder.Encode(CapTextures.Specular(), TextureFormat.Dxt1))));

    private static void WriteModel(string projectDir, Drawable drawable, CapDesign design) =>
        Model(design).Write(Path.Combine(projectDir, drawable.ModelPath));
}
