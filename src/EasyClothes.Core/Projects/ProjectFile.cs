using System.Text.Json;
using System.Text.Json.Serialization;
using EasyClothes.Core.Model;

namespace EasyClothes.Core.Projects;

public static class ProjectFile
{
    public const string Extension = ".ecproj";
    public const string BackupsFolder = ".backups";

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(ClothingProject project) =>
        JsonSerializer.Serialize(project, Options);

    public static ClothingProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<ClothingProject>(json, Options)
            ?? throw new InvalidDataException("Project file is empty.");
        if (project.SchemaVersion > ClothingProject.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Project schema v{project.SchemaVersion} is newer than supported v{ClothingProject.CurrentSchemaVersion}.");
        }
        return project;
    }

    public static async Task SaveAsync(ClothingProject project, string path, CancellationToken ct = default)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, Serialize(project), ct);
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task<ClothingProject> LoadAsync(string path, CancellationToken ct = default) =>
        Deserialize(await File.ReadAllTextAsync(path, ct));
}
