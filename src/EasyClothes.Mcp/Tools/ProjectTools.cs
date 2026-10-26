using System.ComponentModel;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Validation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record ProjectEntry(string File, string Name, string Collection, int Garments, int Tattoos);

public sealed record ProjectSummary(
    string File, string Name, string Collection, string? Author, string? Description,
    IReadOnlyList<GarmentSummary> Garments, IReadOnlyList<TattooSummary> Tattoos, int AlternateSwitches, int Errors, int Warnings, string Note);

public sealed record TextureDetail(string Letter, string File, int? Width, int? Height, string? Format, int? Mips, int? MemoryKiB);

public sealed record GarmentDetail(
    GarmentSummary Summary, string? ModelFile, string? FirstPersonFile, IReadOnlyList<string> AlternateFiles, string? ClothPhysicsFile,
    IReadOnlyList<string> Lods, IReadOnlyList<TextureDetail> Textures, IReadOnlyList<IssueInfo> Issues);

[McpServerToolType]
public sealed class ProjectTools(Workspace workspace)
{
    [McpServerTool(Name = "find_projects", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Busca proyectos de EasyClothes (.ecproj) en una carpeta y sus subcarpetas.")]
    public async Task<IReadOnlyList<ProjectEntry>> FindProjects(
        [Description("Carpeta donde buscar. Acepta rutas de Windows (D:\\...) o de WSL (/mnt/d/...).")] string folder,
        CancellationToken ct)
    {
        var root = workspace.Resolve(folder);
        if (!Directory.Exists(root)) throw new McpException($"No existe la carpeta {folder}.");
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        var found = new List<ProjectEntry>();
        foreach (var file in Directory.EnumerateFiles(root, "*" + ProjectFile.Extension, options))
        {
            if (file.Contains(Path.DirectorySeparatorChar + ProjectFile.BackupsFolder + Path.DirectorySeparatorChar)) continue;
            try
            {
                var project = await ProjectFile.LoadAsync(file, ct);
                found.Add(new ProjectEntry(file, project.Name, project.CollectionName, project.Drawables.Count, project.Tattoos.Count));
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                found.Add(new ProjectEntry(file, $"(no se puede leer: {ex.Message})", "", 0, 0));
            }
        }
        return found;
    }

    [McpServerTool(Name = "get_project", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Resumen de un proyecto: colección, prendas por sexo y hueco con su número en el juego, opciones (tacones, primera persona, físicas, pelo bajo sombrero), origen de cada prenda (paquete y archivo importados), tatuajes, interruptores de pedalternatevariations y número de problemas estructurales.")]
    public async Task<ProjectSummary> GetProject(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        CancellationToken ct)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var p = loaded.Project;
        var issues = ProjectValidator.Validate(p, loaded.Dir);
        return new ProjectSummary(
            loaded.File, p.Name, p.CollectionName, p.Author, p.Description,
            Describe.Garments(p).ToList(), p.Tattoos.Select(Describe.Tattoo).ToList(), p.AlternateVariations.Count,
            issues.Count(i => i.Severity == IssueSeverity.Error), issues.Count(i => i.Severity == IssueSeverity.Warning),
            "Number es el número de la prenda en el juego dentro de su hueco; SourcePackage y SourceFile dicen de qué carpeta y archivo se importó. "
            + "Alternates es el mayor k de sus modelos alternativos <modelo>_k.ydd (numAlternatives del .ymt) y PropMask el propMask del .ymt. "
            + "AlternateSwitches son los interruptores de modelo alternativo (_k.ydd) que build_resource escribe en pedalternatevariations.meta, con los números absolutos del juego del recurso importado. validate analiza además modelos y texturas.");
    }

    [McpServerTool(Name = "get_garment", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Detalle de una prenda: archivos, LODs con triángulos, cada textura (tamaño, formato, mipmaps, memoria) y sus problemas.")]
    public async Task<GarmentDetail> GetGarment(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        CancellationToken ct)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        string? Full(string? relative) => relative is null ? null : Path.Combine(loaded.Dir, relative);

        var lods = new List<string>();
        if (!d.Reserved && File.Exists(Full(d.ModelPath)))
        {
            try
            {
                lods.AddRange(workspace.Cache.Model(Full(d.ModelPath)!).Lods.Select(l => $"{l.Level}: {l.Triangles:N0} triángulos, {l.Vertices:N0} vértices"));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                lods.Add($"no se puede leer: {ex.Message}");
            }
        }

        var textures = d.Textures.Select((t, i) =>
        {
            var file = Full(t.TexturePath)!;
            try
            {
                var report = workspace.Cache.Texture(file);
                var first = report.Textures.FirstOrDefault();
                return new TextureDetail(TextureVariation.LetterFor(i).ToString(), file, first?.Width, first?.Height, first?.Format, first?.MipLevels,
                    Math.Max(report.SystemSize, report.GraphicsSize) / 1024);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                return new TextureDetail(TextureVariation.LetterFor(i).ToString(), file, null, null, null, null, null);
            }
        }).ToList();

        var issues = ProjectValidator.Validate(loaded.Project, loaded.Dir, workspace.Cache)
            .Where(i => i.DrawableId == d.Id).Select(i => Describe.Issue(loaded.Project, i)).ToList();
        return new GarmentDetail(Describe.Garment(loaded.Project, d), d.Reserved ? null : Full(d.ModelPath),
            Full(d.FirstPersonModelPath), d.AlternateModels.Select(a => Full(a.ModelPath)!).ToList(), Full(d.ClothPhysicsPath), lods, textures, issues);
    }

    [McpServerTool(Name = "validate", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Revisa el proyecto como el panel de problemas de la app: archivos que faltan, polígonos, tamaño y formato de texturas, mipmaps, UV, duplicados y límites del juego. Indica qué problemas tienen corrección automática.")]
    public async Task<IReadOnlyList<IssueInfo>> Validate(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Si es false solo revisa la estructura, sin abrir modelos ni texturas (más rápido).")] bool deep = true,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var issues = await Task.Run(() => ProjectValidator.Validate(loaded.Project, loaded.Dir, deep ? workspace.Cache : null), ct);
        return issues.OrderBy(i => i.Severity).Select(i => Describe.Issue(loaded.Project, i)).ToList();
    }
}
