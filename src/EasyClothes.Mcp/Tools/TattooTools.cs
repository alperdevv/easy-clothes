using System.ComponentModel;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record TattooImport(IReadOnlyList<TattooSummary> Added, IReadOnlyList<string> Warnings);

public sealed record TattooDetail(string Id, string Name, string Zone, IReadOnlyList<string> Genders, float UvX, float UvY, float ScaleX, float ScaleY, float Rotation);

[McpServerToolType]
public sealed class TattooTools(Workspace workspace)
{
    private const string ZoneParam = "Zona: Torso, Head, LeftArm, RightArm, LeftLeg o RightLeg.";

    [McpServerTool(Name = "import_tattoos", Destructive = false, OpenWorld = false)]
    [Description("Importa imágenes .png (o .ytd) como tatuajes en una zona del cuerpo. Al generar el recurso se convierten en texturas del juego y una colección de tatuajes.")]
    public async Task<TattooImport> ImportTattoos(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Rutas de las imágenes.")] string[] paths,
        [Description(ZoneParam)] string zone = "Torso",
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var files = paths.Select(workspace.Resolve).ToList();
        if (files.FirstOrDefault(f => !File.Exists(f)) is { } missing) throw new McpException($"No existe {missing}.");
        var result = TattooImporter.Import(loaded.Project, loaded.Dir, files, ParseZone(zone));
        await Workspace.SaveAsync(loaded, ct);
        return new TattooImport(result.Added.Select(Describe.Tattoo).ToList(), result.Warnings);
    }

    [McpServerTool(Name = "update_tattoo", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cambia nombre, zona, sexos o la colocación de un tatuaje (posición UV del centro, escala y rotación en la zona). Lo que no se indique no cambia.")]
    public async Task<TattooDetail> UpdateTattoo(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id del tatuaje, o su nombre.")] string tattoo,
        string? name = null,
        [Description(ZoneParam)] string? zone = null,
        [Description("Sexos que lo pueden llevar: hombre, mujer o ambos.")] string? genders = null,
        float? uvX = null, float? uvY = null, float? scaleX = null, float? scaleY = null,
        [Description("Grados.")] float? rotation = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var t = Find(loaded.Project, tattoo);
        if (name is not null) t.Name = name;
        if (zone is not null) t.Zone = ParseZone(zone);
        if (genders is not null)
            t.Genders = genders.Trim().ToLowerInvariant() is "ambos" or "both" ? [PedGender.Male, PedGender.Female] : [Workspace.ParseGender(genders)];
        t.UvX = uvX ?? t.UvX;
        t.UvY = uvY ?? t.UvY;
        t.ScaleX = scaleX ?? t.ScaleX;
        t.ScaleY = scaleY ?? t.ScaleY;
        t.Rotation = rotation ?? t.Rotation;
        await Workspace.SaveAsync(loaded, ct);
        return new TattooDetail(t.Id.ToString(), t.Name, t.Zone.ToString(), t.Genders.Select(Describe.Gender).ToList(), t.UvX, t.UvY, t.ScaleX, t.ScaleY, t.Rotation);
    }

    [McpServerTool(Name = "remove_tattoo", Destructive = true, OpenWorld = false)]
    [Description("Quita un tatuaje del proyecto; su imagen sigue en la carpeta del proyecto.")]
    public async Task<string> RemoveTattoo(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id del tatuaje, o su nombre.")] string tattoo,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var t = Find(loaded.Project, tattoo);
        loaded.Project.Tattoos.Remove(t);
        await Workspace.SaveAsync(loaded, ct);
        return $"Tatuaje {t.Name} quitado; quedan {loaded.Project.Tattoos.Count}.";
    }

    private static Tattoo Find(ClothingProject project, string tattoo) =>
        (Guid.TryParse(tattoo, out var id)
            ? project.Tattoos.FirstOrDefault(t => t.Id == id)
            : project.Tattoos.FirstOrDefault(t => t.Name.Equals(tattoo, StringComparison.OrdinalIgnoreCase)))
        ?? throw new McpException($"No hay ningún tatuaje '{tattoo}'. get_project lista los tatuajes.");

    private static TattooZone ParseZone(string zone) =>
        Enum.TryParse<TattooZone>(zone.Replace(" ", ""), ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw new McpException($"Zona desconocida '{zone}'; usa Torso, Head, LeftArm, RightArm, LeftLeg o RightLeg.");
}
