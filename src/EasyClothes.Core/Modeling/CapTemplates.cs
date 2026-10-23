using System.Text.Json;
using EasyClothes.Core.Projects;

namespace EasyClothes.Core.Modeling;

public sealed record CapTemplate(string Name, CapDesign Design);

// The user's own cap designs, one JSON file each, shared by the app and the MCP server.
public sealed class CapTemplates(string folder)
{
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyClothes", "plantillas");

    public string Folder { get; } = folder;

    // Unreadable files are skipped: one broken template must not hide the others.
    public IReadOnlyList<CapTemplate> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var templates = new List<CapTemplate>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonSerializer.Deserialize<CapDesign>(File.ReadAllText(file), ProjectFile.Options) is { } design)
                    templates.Add(new CapTemplate(Path.GetFileNameWithoutExtension(file), design.Clamped()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Hand-edited or half-written file; the rest of the list is still useful.
            }
        }
        return templates;
    }

    public CapTemplate? Find(string name) =>
        List().FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public CapTemplate Save(string name, CapDesign design)
    {
        var clean = FileName(name);
        if (clean.Length == 0) throw new ArgumentException("La plantilla necesita un nombre.", nameof(name));
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, clean + ".json"), JsonSerializer.Serialize(design.Clamped(), ProjectFile.Options));
        return new CapTemplate(clean, design.Clamped());
    }

    public bool Delete(string name)
    {
        var path = Path.Combine(Folder, FileName(name) + ".json");
        if (FileName(name).Length == 0 || !File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string FileName(string name) =>
        string.Concat(name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '.'));
}
