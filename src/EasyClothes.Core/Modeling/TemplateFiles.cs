using System.Text.Json;
using EasyClothes.Core.Projects;

namespace EasyClothes.Core.Modeling;

// User templates on disk, one JSON file each, shared by the app and the MCP server.
internal static class TemplateFiles
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyClothes", "plantillas");

    // Unreadable files are skipped: one broken template must not hide the others.
    public static IReadOnlyList<(string Name, T Design)> List<T>(string folder, Func<T, T> clean)
    {
        if (!Directory.Exists(folder)) return [];
        var templates = new List<(string, T)>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(File.ReadAllText(file), ProjectFile.Options) is { } design)
                    templates.Add((Path.GetFileNameWithoutExtension(file), clean(design)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Hand-edited or half-written file; the rest of the list is still useful.
            }
        }
        return templates;
    }

    public static string Save<T>(string folder, string name, T design)
    {
        var clean = FileName(name);
        if (clean.Length == 0) throw new ArgumentException("La plantilla necesita un nombre.", nameof(name));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, clean + ".json"), JsonSerializer.Serialize(design, ProjectFile.Options));
        return clean;
    }

    public static bool Delete(string folder, string name)
    {
        var path = Path.Combine(folder, FileName(name) + ".json");
        if (FileName(name).Length == 0 || !File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string FileName(string name) =>
        string.Concat(name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '.'));
}
