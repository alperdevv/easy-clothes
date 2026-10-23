using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Modeling;

// Adds generated shirts to a project and keeps them editable, like caps: the shape can change at any
// time (only the model is rewritten) and each look becomes a texture variation. Shirts are cut from the
// body the user exported, so every change needs it. Printed pictures are copied next to the garment so
// the project carries everything it needs.
public static class ShirtGarment
{
    private const string GraphicsFolder = "estampados";

    public static Drawable Create(ClothingProject project, string projectDir, ShirtDesign design, PedGender gender, SkinnedBody body,
        string name = "Camiseta", IReadOnlyList<uint>? moreColours = null)
    {
        design = design.Clamped();
        name = DrawableImporter.UniqueName(project, gender, name);
        var dir = DrawableImporter.UniqueDir(projectDir, Path.Combine(DrawableImporter.AssetsFolder, gender.ToString().ToLowerInvariant(), name));
        Directory.CreateDirectory(Path.Combine(projectDir, dir));

        var code = Code(design.Layer);
        var drawable = new Drawable
        {
            Name = name,
            Gender = gender,
            Component = design.Layer == ShirtLayer.Undershirt ? ComponentSlot.Undershirt : ComponentSlot.Top,
            ModelPath = Path.Combine(dir, $"{code}_000_u.ydd").Replace('\\', '/'),
        };
        drawable.Shirt = design with { Look = KeepGraphics(projectDir, drawable, design.Look) };
        var shirt = ShirtModel.Build(drawable.Shirt, body);
        Model(drawable.Shirt, shirt).Write(Path.Combine(projectDir, drawable.ModelPath));
        foreach (var look in new[] { drawable.Shirt.Look }.Concat((moreColours ?? []).Select(c => drawable.Shirt.Look.InColour(c))))
            Paint(projectDir, drawable, look, shirt);
        DrawableOrder.PlaceAtEnd(project, drawable);
        project.Drawables.Add(drawable);
        return drawable;
    }

    // Same UV layout whatever the shape, so every texture variation keeps fitting. The relief (fabric
    // and wrinkles) is part of the model, so it takes the look in the design.
    public static string Reshape(string projectDir, Drawable drawable, ShirtDesign design, SkinnedBody body)
    {
        var current = drawable.Shirt ?? throw NotAShirt(drawable);
        var backup = ProjectBackups.Copy(projectDir, drawable.ModelPath);
        drawable.Shirt = design.Clamped() with { Layer = current.Layer, Look = KeepGraphics(projectDir, drawable, design.Look) };
        Model(drawable.Shirt, ShirtModel.Build(drawable.Shirt, body)).Write(Path.Combine(projectDir, drawable.ModelPath));
        return backup;
    }

    // A new texture variation painted on the current shape; returns its index.
    public static int AddColourway(string projectDir, Drawable drawable, ShirtLook look, SkinnedBody body)
    {
        var design = drawable.Shirt ?? throw NotAShirt(drawable);
        look = KeepGraphics(projectDir, drawable, look.Clamped());
        var index = Paint(projectDir, drawable, look, ShirtModel.Build(design, body));
        drawable.Shirt = design with { Look = look };
        return index;
    }

    // Loads a printed picture, relative to the project once saved with a garment.
    public static RgbaImage? LoadGraphic(string projectDir, string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(projectDir, path);
        try
        {
            return File.Exists(full) ? ImageLoader.Load(full) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            // A broken picture is left out of the print instead of stopping the whole garment.
            return null;
        }
    }

    private static int Paint(string projectDir, Drawable drawable, ShirtLook look, ShirtMesh shirt)
    {
        if (drawable.Textures.Count >= Drawable.MaxTextures)
            throw new InvalidOperationException($"{drawable.Name} ya tiene {Drawable.MaxTextures} texturas, el máximo del juego.");
        var name = UniqueTextureName(projectDir, drawable);
        var relative = Path.Combine(Path.GetDirectoryName(drawable.ModelPath) ?? "", name + ".ytd").Replace('\\', '/');
        var design = drawable.Shirt! with { Look = look };
        var diffuse = TextureEncoder.Encode(ShirtTextures.Diffuse(design, shirt, images: path => LoadGraphic(projectDir, path)), TextureFormat.Dxt1);
        TextureDictionaryWriter.Create([(name, diffuse)]).Write(Path.Combine(projectDir, relative));
        drawable.Textures.Add(new TextureVariation { TexturePath = relative });
        return drawable.Textures.Count - 1;
    }

    // Pictures from elsewhere on disk are copied into the garment's folder and referred to from there.
    private static ShirtLook KeepGraphics(string projectDir, Drawable drawable, ShirtLook look)
    {
        var folder = Path.Combine(Path.GetDirectoryName(drawable.ModelPath) ?? "", GraphicsFolder);
        var kept = look.Graphics.Select(g =>
        {
            if (g.Image is not { Length: > 0 } image || !Path.IsPathRooted(image) || !File.Exists(image)) return g;
            Directory.CreateDirectory(Path.Combine(projectDir, folder));
            var relative = Path.Combine(folder, Path.GetFileName(image)).Replace('\\', '/');
            var target = Path.Combine(projectDir, relative);
            if (!string.Equals(Path.GetFullPath(image), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(image, target, overwrite: true);
            return g with { Image = relative };
        }).ToList();
        return look with { Graphics = kept };
    }

    // Removed variations leave their files behind, so the next free name is taken from disk.
    private static string UniqueTextureName(string projectDir, Drawable drawable)
    {
        var dir = Path.Combine(projectDir, Path.GetDirectoryName(drawable.ModelPath) ?? "");
        var code = Code(drawable.Shirt!.Layer);
        for (var i = drawable.Textures.Count; ; i++)
        {
            var name = $"{code}_diff_000_{(i < Drawable.MaxTextures ? TextureVariation.LetterFor(i).ToString() : i.ToString())}_uni";
            if (!File.Exists(Path.Combine(dir, name + ".ytd"))) return name;
        }
    }

    private static string Code(ShirtLayer layer) => PedSlotNames.FileCode(layer == ShirtLayer.Undershirt ? ComponentSlot.Undershirt : ComponentSlot.Top);

    public static ResourceFile Model(ShirtDesign design, ShirtMesh shirt)
    {
        var code = Code(design.Layer);
        return DrawableWriter.CreateComponent(new ComponentDrawable(
            $"{code}_000_u",
            shirt.Mesh,
            $"{code}_diff_000_a_uni",
            new EmbeddedTexture($"{code}_normal_000", TextureEncoder.Encode(ShirtTextures.Normal(design, shirt), TextureFormat.Dxt1)),
            new EmbeddedTexture($"{code}_spec_000", TextureEncoder.Encode(ShirtTextures.Specular(design, shirt), TextureFormat.Dxt1))));
    }

    private static InvalidOperationException NotAShirt(Drawable drawable) =>
        new($"{drawable.Name} no es una camiseta creada con EasyClothes.");
}
