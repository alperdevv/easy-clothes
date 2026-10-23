using System.Globalization;
using System.Text.Json;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using ModelContextProtocol;

namespace EasyClothes.Mcp;

public sealed record LoadedProject(ClothingProject Project, string Dir, string File);

// Shared by every tool: allowed folders, project loading and the asset analysis cache.
public sealed class Workspace(ServerOptions options)
{
    public AnalysisCache Cache { get; } = new();
    public ServerOptions Options => options;

    public string Resolve(string path)
    {
        var full = HostPaths.Normalize(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (options.Roots.Count > 0 && !options.Roots.Any(root => full.Equals(root, comparison) || full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison)))
            throw new McpException($"{path} está fuera de las carpetas permitidas: {string.Join(", ", options.Roots)}.");
        return full;
    }

    // Accepts the .ecproj file or the folder that holds it.
    public async Task<LoadedProject> LoadAsync(string path, CancellationToken ct = default)
    {
        var file = Resolve(path);
        if (Directory.Exists(file))
        {
            var candidates = Directory.GetFiles(file, "*" + ProjectFile.Extension);
            file = candidates.Length == 1
                ? candidates[0]
                : throw new McpException(candidates.Length == 0
                    ? $"No hay ningún proyecto {ProjectFile.Extension} en {path}. Usa find_projects para buscarlos."
                    : $"Hay {candidates.Length} proyectos en {path}; indica el archivo: {string.Join(", ", candidates.Select(Path.GetFileName))}.");
        }
        if (!File.Exists(file)) throw new McpException($"No existe el proyecto {path}.");
        try
        {
            return new LoadedProject(await ProjectFile.LoadAsync(file, ct), Path.GetDirectoryName(file)!, file);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            throw new McpException($"El proyecto {Path.GetFileName(file)} no se puede leer: {ex.Message}");
        }
    }

    // By id, or by name when it is unique in the project.
    public static Drawable FindGarment(ClothingProject project, string garment)
    {
        if (Guid.TryParse(garment, out var id))
            return project.Drawables.FirstOrDefault(d => d.Id == id) ?? throw new McpException($"No hay ninguna prenda con id {garment}.");
        var matches = project.Drawables.Where(d => string.Equals(d.Name, garment, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches switch
        {
            [var one] => one,
            [] => throw new McpException($"No hay ninguna prenda llamada {garment}. get_project lista las prendas."),
            _ => throw new McpException($"Hay {matches.Count} prendas llamadas {garment}; usa su id: {string.Join(", ", matches.Select(d => $"{d.Id} ({Describe.Gender(d.Gender)})"))}."),
        };
    }

    // Keeps the previous version under .backups/project before every write.
    public static async Task SaveAsync(LoadedProject loaded, CancellationToken ct = default)
    {
        ProjectPackage.BackupProjectFile(loaded.File);
        await ProjectFile.SaveAsync(loaded.Project, loaded.File, ct);
    }

    public static PedGender ParseGender(string gender) => gender.Trim().ToLowerInvariant() switch
    {
        "hombre" or "male" or "m" or "mp_m_freemode_01" => PedGender.Male,
        "mujer" or "female" or "f" or "mp_f_freemode_01" => PedGender.Female,
        _ => throw new McpException($"Sexo desconocido '{gender}'; usa hombre o mujer."),
    };

    public static uint ParseColor(string color)
    {
        var hex = color.Trim().TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? rgb
            : throw new McpException($"Color '{color}' no válido; usa #RRGGBB.");
    }

    // A file code ("jbib", "p_head") or the slot name ("Top", "Hat").
    public static (ComponentSlot? Component, PropSlot? Prop) ParseSlot(string slot)
    {
        var text = slot.Trim();
        foreach (var c in Enum.GetValues<ComponentSlot>())
            if (text.Equals(PedSlotNames.FileCode(c), StringComparison.OrdinalIgnoreCase) || text.Equals(c.ToString(), StringComparison.OrdinalIgnoreCase)) return (c, null);
        foreach (var p in Enum.GetValues<PropSlot>())
            if (text.Equals(PedSlotNames.FileCode(p), StringComparison.OrdinalIgnoreCase) || text.Equals(p.ToString(), StringComparison.OrdinalIgnoreCase) || text.Equals("Prop" + p, StringComparison.OrdinalIgnoreCase)) return (null, p);
        var codes = Enum.GetValues<ComponentSlot>().Select(c => PedSlotNames.FileCode(c)).Concat(Enum.GetValues<PropSlot>().Select(p => PedSlotNames.FileCode(p)));
        throw new McpException($"Hueco desconocido '{slot}'; usa uno de: {string.Join(", ", codes)}.");
    }

    public static int TextureIndex(Drawable drawable, string letter)
    {
        var index = letter.Trim().ToLowerInvariant() is [var c] ? c - 'a' : -1;
        if (index < 0 || index >= drawable.Textures.Count)
            throw new McpException(drawable.Textures.Count == 0
                ? $"{drawable.Name} no tiene texturas."
                : $"{drawable.Name} tiene las texturas a-{TextureVariation.LetterFor(drawable.Textures.Count - 1)}; no existe la '{letter}'.");
        return index;
    }
}
