using EasyClothes.Core.Model;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Importing;

public static class TattooImporter
{
    public static ImportResult<Tattoo> Import(ClothingProject project, string projectDir, IEnumerable<string> imagePaths, TattooZone zone)
    {
        var added = new List<Tattoo>();
        var warnings = new List<string>();
        foreach (var path in imagePaths)
        {
            if (!ImageLoader.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                warnings.Add($"{Path.GetFileName(path)}: solo se admiten imágenes .png o .ytd.");
                continue;
            }
            var name = UniqueName(project, Path.GetFileNameWithoutExtension(path));
            var relative = $"{DrawableImporter.AssetsFolder}/tattoos/{name}{Path.GetExtension(path).ToLowerInvariant()}";
            var target = Path.Combine(projectDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);

            var tattoo = new Tattoo { Name = name, Zone = zone, TexturePath = relative };
            project.Tattoos.Add(tattoo);
            added.Add(tattoo);
        }
        return new ImportResult<Tattoo>(added, warnings);
    }

    private static string UniqueName(ClothingProject project, string name)
    {
        var candidate = name;
        for (var i = 2; project.Tattoos.Any(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name}_{i}";
        return candidate;
    }
}
