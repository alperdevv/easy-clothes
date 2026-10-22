using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;

namespace EasyClothes.Core.Importing;

public sealed record ImportResult<T>(IReadOnlyList<T> Added, IReadOnlyList<string> Warnings);

public static class DrawableImporter
{
    public const string AssetsFolder = "assets";

    // Copies each model and the textures found next to it into the project folder,
    // so a project directory is self-contained and can be zipped or moved as is.
    public static ImportResult<Drawable> Import(ClothingProject project, string projectDir, IEnumerable<string> paths, PedGender fallbackGender)
    {
        var added = new List<Drawable>();
        var warnings = new List<string>();
        var texturesByDir = new Dictionary<string, List<(string Path, TextureName Name)>>();

        var ownAssets = Path.GetFullPath(projectDir) + Path.DirectorySeparatorChar;
        foreach (var modelPath in ExpandModels(paths).Where(p => !p.StartsWith(ownAssets, StringComparison.OrdinalIgnoreCase)))
        {
            if (StreamName.ParseModel(modelPath) is not { } model)
            {
                warnings.Add($"{Path.GetFileName(modelPath)}: el nombre no sigue el formato <tipo>_000.ydd, no se puede saber a qué hueco pertenece.");
                continue;
            }
            if (model.Alternate > 0)
            {
                if (!File.Exists(MainModelOf(modelPath)))
                    warnings.Add($"{Path.GetFileName(modelPath)}: es un modelo de primera persona y no está junto a su prenda; impórtalo desde la carpeta de la prenda.");
                continue;
            }

            var gender = model.Gender ?? StreamName.GenderFromFolders(modelPath) ?? fallbackGender;
            var textures = FindTextures(texturesByDir, modelPath, model, gender);
            if (textures.Count == 0)
                warnings.Add($"{Path.GetFileName(modelPath)}: no se encontraron texturas junto al modelo.");

            var drawable = Create(project, projectDir, modelPath, model, gender, textures, warnings);
            DrawableOrder.PlaceAtEnd(project, drawable);
            project.Drawables.Add(drawable);
            added.Add(drawable);
        }

        return new ImportResult<Drawable>(added, warnings);
    }

    // Copies one model with its textures, first person model and cloth physics; the caller adds it to the project.
    internal static Drawable Create(ClothingProject project, string projectDir, string modelPath, ModelName model, PedGender gender, IReadOnlyList<string> textures, List<string> warnings)
    {
        var name = $"{model.Slot.FileCode}_{model.Index:D3}";
        var targetDir = UniqueDir(projectDir, Path.Combine(AssetsFolder, gender.ToString().ToLowerInvariant(), name));
        var drawable = new Drawable
        {
            Name = UniqueName(project, gender, name),
            Gender = gender,
            Component = model.Slot.Component,
            Prop = model.Slot.Prop,
            Skin = model.Skin,
            ModelPath = CopyInto(projectDir, targetDir, modelPath),
            Textures = textures.Take(Drawable.MaxTextures)
                .Select(t => new TextureVariation { TexturePath = CopyInto(projectDir, targetDir, t) })
                .ToList(),
        };
        var firstPerson = Path.Combine(Path.GetDirectoryName(modelPath)!, Path.GetFileNameWithoutExtension(modelPath) + "_1.ydd");
        if (File.Exists(firstPerson)) drawable.FirstPersonModelPath = CopyInto(projectDir, targetDir, firstPerson);
        var cloth = Path.ChangeExtension(modelPath, ".yld");
        if (File.Exists(cloth)) drawable.ClothPhysicsPath = CopyInto(projectDir, targetDir, cloth);
        if (drawable.Component == ComponentSlot.Shoes) drawable.Flags.HeelHeight = EstimateHeel(modelPath);

        if (textures.Count > Drawable.MaxTextures)
            warnings.Add($"{drawable.Name}: tiene {textures.Count} texturas y el juego admite {Drawable.MaxTextures}; se han ignorado las sobrantes.");

        return drawable;
    }

    // Files chosen by hand are stored next to the model with the names the game expects.
    public static void AttachFirstPerson(string projectDir, Drawable drawable, string source) =>
        drawable.FirstPersonModelPath = CopyBesideModel(projectDir, drawable, source, "_1.ydd");

    public static void AttachClothPhysics(string projectDir, Drawable drawable, string source) =>
        drawable.ClothPhysicsPath = CopyBesideModel(projectDir, drawable, source, ".yld");

    private static string CopyBesideModel(string projectDir, Drawable drawable, string source, string suffix)
    {
        var relative = Path.Combine(Path.GetDirectoryName(drawable.ModelPath) ?? "", Path.GetFileNameWithoutExtension(drawable.ModelPath) + suffix).Replace('\\', '/');
        var target = Path.GetFullPath(Path.Combine(projectDir, relative));
        if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase)) return relative;
        if (File.Exists(target)) ProjectBackups.Copy(projectDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return relative;
    }

    // Folders are searched recursively; a file or folder listed twice is imported once.
    public static IEnumerable<string> ExpandModels(IEnumerable<string> paths) =>
        paths.SelectMany<string, string>(path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.ydd", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
                : [path])
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    internal static List<string> FindTextures(Dictionary<string, List<(string Path, TextureName Name)>> cache, string modelPath, ModelName model, PedGender gender)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
        if (!cache.TryGetValue(dir, out var textures))
        {
            textures = Directory.EnumerateFiles(dir, "*.ytd")
                .Select(path => (Path: path, Name: StreamName.ParseTexture(path)))
                .Where(t => t.Name is not null)
                .Select(t => (t.Path, t.Name!))
                .ToList();
            cache[dir] = textures;
        }
        var folderGender = StreamName.GenderFromFolders(modelPath);
        return textures
            .Where(t => t.Name.Slot == model.Slot && t.Name.Index == model.Index && (t.Name.Gender ?? folderGender ?? gender) == gender)
            .OrderBy(t => t.Name.Variation)
            .Select(t => t.Path)
            .ToList();
    }

    private static string MainModelOf(string alternatePath)
    {
        var stem = Path.GetFileNameWithoutExtension(alternatePath);
        return Path.Combine(Path.GetDirectoryName(alternatePath)!, stem[..stem.LastIndexOf('_')] + ".ydd");
    }

    // An unreadable model is reported by validation; here it only means no heel guess.
    private static float? EstimateHeel(string modelPath)
    {
        try
        {
            return HeelEstimator.Estimate(modelPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static string UniqueDir(string projectDir, string relativeDir)
    {
        var candidate = relativeDir;
        for (var i = 2; Directory.Exists(Path.Combine(projectDir, candidate)); i++)
            candidate = $"{relativeDir}_{i}";
        return candidate;
    }

    private static string CopyInto(string projectDir, string relativeDir, string source)
    {
        var relative = Path.Combine(relativeDir, Path.GetFileName(source));
        var target = Path.Combine(projectDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return relative.Replace('\\', '/');
    }

    private static string UniqueName(ClothingProject project, PedGender gender, string name)
    {
        var candidate = name;
        for (var i = 2; project.DrawablesFor(gender).Any(d => d.Name == candidate); i++)
            candidate = $"{name} ({i})";
        return candidate;
    }
}
