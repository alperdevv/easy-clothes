using System.ComponentModel;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Validation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record ImportSummary(IReadOnlyList<GarmentSummary> Added, IReadOnlyList<string> Warnings, string Collection);

public sealed record SlotOrder(string Gender, string Slot, IReadOnlyList<string> Garments);

public sealed record FixReport(IReadOnlyList<string> Applied, int RemainingErrors, int RemainingWarnings);

public sealed record BuildReport(string Output, int Garments, int Tattoos, int Files, IReadOnlyList<string> Warnings, string Install);

// Every change saves the project at once (the previous version goes to .backups/project) and the app reloads it.
[McpServerToolType]
public sealed class EditTools(Workspace workspace)
{
    private const string ProjectParam = "Archivo .ecproj o la carpeta que lo contiene.";
    private const string GarmentParam = "Id de la prenda, o su nombre si es único.";

    [McpServerTool(Name = "create_project", Destructive = false, OpenWorld = false)]
    [Description("Crea un proyecto vacío en una carpeta (se crea si no existe).")]
    public async Task<ProjectSummary> CreateProject(
        [Description("Carpeta del proyecto.")] string folder,
        [Description("Nombre del proyecto, p. ej. 'Ropa de policía'.")] string name,
        [Description("Nombre de colección para el juego (minúsculas, números y _). Por defecto, a partir del nombre.")] string? collection = null,
        CancellationToken ct = default)
    {
        var dir = workspace.Resolve(folder);
        if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*" + ProjectFile.Extension).Any())
            throw new McpException($"Ya hay un proyecto en {folder}.");
        Directory.CreateDirectory(dir);
        var project = new ClothingProject { Name = name, CollectionName = collection ?? ClothingProject.CollectionNameFrom(name) };
        var file = Path.Combine(dir, ClothingProject.CollectionNameFrom(name) + ProjectFile.Extension);
        await ProjectFile.SaveAsync(project, file, ct);
        return await new ProjectTools(workspace).GetProject(file, ct);
    }

    [McpServerTool(Name = "update_project", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cambia el nombre, la colección, el autor o la descripción del proyecto. Lo que no se indique no cambia.")]
    public async Task<ProjectSummary> UpdateProject(
        [Description(ProjectParam)] string project,
        string? name = null,
        [Description("Nombre de colección para el juego: minúsculas, números y _.")] string? collection = null,
        string? author = null,
        string? description = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var p = loaded.Project;
        if (collection is not null && (collection.Length == 0 || collection.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_')))
            throw new McpException("La colección solo puede tener minúsculas, números y guiones bajos.");
        p.Name = name ?? p.Name;
        p.CollectionName = collection ?? p.CollectionName;
        p.Author = author ?? p.Author;
        p.Description = description ?? p.Description;
        await Workspace.SaveAsync(loaded, ct);
        return await new ProjectTools(workspace).GetProject(loaded.File, ct);
    }

    [McpServerTool(Name = "import_garments", Destructive = false, OpenWorld = false)]
    [Description("Importa prendas: archivos .ydd sueltos (con sus texturas, modelo de primera persona _1.ydd y físicas .yld al lado) o carpetas. Una carpeta de recurso ya generado (con su .ymt) conserva el número y las opciones de cada prenda. Los zapatos con tacón se detectan solos.")]
    public async Task<ImportSummary> ImportGarments(
        [Description(ProjectParam)] string project,
        [Description("Rutas de archivos .ydd o de carpetas.")] string[] paths,
        [Description("Sexo para los archivos cuyo nombre o carpeta no lo indica: hombre o mujer.")] string fallbackGender = "hombre",
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var sources = paths.Select(workspace.Resolve).ToList();
        if (sources.FirstOrDefault(p => !File.Exists(p) && !Directory.Exists(p)) is { } missing) throw new McpException($"No existe {missing}.");
        var wasEmpty = loaded.Project.Drawables.Count == 0;
        var result = await Task.Run(() => ResourceImporter.ImportAny(loaded.Project, loaded.Dir, sources, Workspace.ParseGender(fallbackGender)), ct);
        if (wasEmpty && result.Collection is { } collection) loaded.Project.CollectionName = collection;
        await Workspace.SaveAsync(loaded, ct);
        return new ImportSummary(result.Added.Select(d => Describe.Garment(loaded.Project, d)).ToList(), result.Warnings, loaded.Project.CollectionName);
    }

    [McpServerTool(Name = "update_garment", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cambia el nombre, sexo, hueco o las opciones de una prenda. Al cambiar de sexo o de hueco pasa al final de su nuevo hueco. Lo que no se indique no cambia.")]
    public async Task<GarmentSummary> UpdateGarment(
        [Description(ProjectParam)] string project,
        [Description(GarmentParam)] string garment,
        string? name = null,
        [Description("hombre o mujer.")] string? gender = null,
        [Description("Código del hueco (jbib, lowr, feet, p_head…) o su nombre (Top, Legs, Shoes, Hat…).")] string? slot = null,
        [Description("Solo calzado: altura del tacón (1 = tacones del juego, unos 9 cm). 0 los quita.")] float? heelHeight = null,
        [Description("Solo sombreros: pelo debajo, keep (entero), cut (recortado) o remove (oculto).")] string? hatHair = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        var regroup = false;
        if (name is not null) d.Name = name;
        if (gender is not null && Workspace.ParseGender(gender) is var g && g != d.Gender)
        {
            d.Gender = g;
            regroup = true;
        }
        if (slot is not null && Workspace.ParseSlot(slot) is var s && (s.Component != d.Component || s.Prop != d.Prop))
        {
            (d.Component, d.Prop) = s;
            regroup = true;
        }
        if (regroup) DrawableOrder.PlaceAtEnd(loaded.Project, d);
        if (heelHeight is { } height)
        {
            if (d.Component != ComponentSlot.Shoes) throw new McpException($"{d.Name} no es calzado; los tacones solo se aplican a feet.");
            d.Flags.HeelHeight = height <= 0 ? null : Math.Clamp(height, 0.1f, 3f);
        }
        if (hatHair is not null)
        {
            if (d.Prop != PropSlot.Hat) throw new McpException($"{d.Name} no es un sombrero (p_head).");
            d.Flags.HatHair = Enum.TryParse<HatHair>(hatHair, ignoreCase: true, out var hair) ? hair : throw new McpException("hatHair debe ser keep, cut o remove.");
        }
        await Workspace.SaveAsync(loaded, ct);
        return Describe.Garment(loaded.Project, d);
    }

    [McpServerTool(Name = "move_garment", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Pone una prenda en otro número del juego dentro de su hueco; las demás se desplazan.")]
    public async Task<SlotOrder> MoveGarment(
        [Description(ProjectParam)] string project,
        [Description(GarmentParam)] string garment,
        [Description("Número de destino (0 es el primero del hueco).")] int number,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        var count = loaded.Project.SameSlot(d).Count();
        if (number < 0 || number >= count) throw new McpException($"El hueco tiene {count} prendas; el número debe estar entre 0 y {count - 1}.");
        DrawableOrder.Move(loaded.Project, d, number - DrawableOrder.NumberOf(loaded.Project, d));
        await Workspace.SaveAsync(loaded, ct);
        return Order(loaded.Project, d);
    }

    [McpServerTool(Name = "reserve_numbers", Destructive = false, OpenWorld = false)]
    [Description("Reserva números al final de un hueco para prendas futuras, así los números de las demás no cambian cuando lleguen.")]
    public async Task<SlotOrder> ReserveNumbers(
        [Description(ProjectParam)] string project,
        [Description("hombre o mujer.")] string gender,
        [Description("Código del hueco (jbib, lowr, feet, p_head…) o su nombre.")] string slot,
        [Description("Cuántos números reservar (1-50).")] int count = 1,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var (component, prop) = Workspace.ParseSlot(slot);
        Drawable? last = null;
        for (var i = 0; i < Math.Clamp(count, 1, 50); i++)
            last = DrawableOrder.Reserve(loaded.Project, Workspace.ParseGender(gender), component, prop);
        await Workspace.SaveAsync(loaded, ct);
        return Order(loaded.Project, last!);
    }

    [McpServerTool(Name = "fill_reserved", Destructive = false, OpenWorld = false)]
    [Description("Asigna un modelo .ydd (con sus texturas al lado) a un número reservado; la prenda ocupa ese número exacto.")]
    public async Task<GarmentSummary> FillReserved(
        [Description(ProjectParam)] string project,
        [Description("Id o nombre del número reservado (p. ej. 'reservado 1').")] string reserved,
        [Description("Ruta del .ydd.")] string model,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var slot = Workspace.FindGarment(loaded.Project, reserved);
        if (!slot.Reserved) throw new McpException($"{slot.Name} no es un número reservado.");
        var file = workspace.Resolve(model);
        if (!File.Exists(file)) throw new McpException($"No existe {model}.");
        var result = DrawableImporter.Import(loaded.Project, loaded.Dir, [file], slot.Gender);
        if (result.Added is not [var garment]) throw new McpException($"No se pudo importar {model}: {string.Join(" ", result.Warnings)}");
        DrawableOrder.Fill(loaded.Project, slot, garment);
        await Workspace.SaveAsync(loaded, ct);
        return Describe.Garment(loaded.Project, garment);
    }

    [McpServerTool(Name = "set_garment_file", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Pone o quita el modelo de primera persona (_1.ydd) o las físicas de tela (.yld) de una prenda. Sin ruta, lo quita.")]
    public async Task<GarmentSummary> SetGarmentFile(
        [Description(ProjectParam)] string project,
        [Description(GarmentParam)] string garment,
        [Description("first_person o cloth_physics.")] string kind,
        [Description("Ruta del archivo; vacío para quitarlo.")] string? path = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        if (d.IsProp || d.Reserved) throw new McpException($"{d.Name} no admite modelo de primera persona ni físicas de tela.");
        var source = string.IsNullOrWhiteSpace(path) ? null : workspace.Resolve(path);
        if (source is not null && !File.Exists(source)) throw new McpException($"No existe {path}.");
        switch (kind.Trim().ToLowerInvariant())
        {
            case "first_person":
                if (source is null) d.FirstPersonModelPath = null;
                else DrawableImporter.AttachFirstPerson(loaded.Dir, d, source);
                break;
            case "cloth_physics":
                if (source is null) d.ClothPhysicsPath = null;
                else DrawableImporter.AttachClothPhysics(loaded.Dir, d, source);
                break;
            default:
                throw new McpException("kind debe ser first_person o cloth_physics.");
        }
        await Workspace.SaveAsync(loaded, ct);
        return Describe.Garment(loaded.Project, d);
    }

    [McpServerTool(Name = "remove_garment", Destructive = true, OpenWorld = false)]
    [Description("Quita una prenda (o un número reservado) del proyecto. Sus archivos siguen en la carpeta del proyecto. Las prendas detrás de ella en su hueco bajan un número.")]
    public async Task<SlotOrder> RemoveGarment(
        [Description(ProjectParam)] string project,
        [Description(GarmentParam)] string garment,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        loaded.Project.Drawables.Remove(d);
        await Workspace.SaveAsync(loaded, ct);
        return Order(loaded.Project, d);
    }

    [McpServerTool(Name = "apply_fixes", Destructive = true, OpenWorld = false)]
    [Description("Aplica las correcciones automáticas que propone validate (reducir texturas, generar mipmaps, reducir polígonos), de todo el proyecto o de una prenda. Cada archivo original se copia antes a .backups.")]
    public async Task<FixReport> ApplyFixes(
        [Description(ProjectParam)] string project,
        [Description("Opcional: solo esta prenda (id o nombre).")] string? garment = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var only = garment is null ? null : Workspace.FindGarment(loaded.Project, garment);
        var issues = await Task.Run(() => ProjectValidator.Validate(loaded.Project, loaded.Dir, workspace.Cache), ct);
        var fixes = IssueFixer.OnePerFile(issues.Where(i => only is null || i.DrawableId == only.Id).Select(i => i.Fix).OfType<FixAction>());
        var applied = new List<string>();
        foreach (var fix in fixes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                applied.Add(await Task.Run(() => IssueFixer.Apply(fix, loaded.Dir), ct));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                applied.Add($"{IssueFixer.TargetOf(fix)}: no se pudo corregir ({ex.Message}).");
            }
        }
        var remaining = ProjectValidator.Validate(loaded.Project, loaded.Dir, workspace.Cache);
        return new FixReport(applied, remaining.Count(i => i.Severity == IssueSeverity.Error), remaining.Count(i => i.Severity == IssueSeverity.Warning));
    }

    [McpServerTool(Name = "build_resource", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Genera el recurso de FiveM (fxmanifest.lua, stream con .ymt y archivos renombrados, .meta de tienda, tacones y sombreros, primera persona, tatuajes). Solo sobrescribe carpetas vacías o generadas por EasyClothes. Falla con la lista de errores si el proyecto tiene alguno.")]
    public async Task<BuildReport> BuildResource(
        [Description(ProjectParam)] string project,
        [Description("Carpeta de salida; su nombre será el del recurso. Por defecto, build/<colección> dentro del proyecto.")] string? output = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var target = workspace.Resolve(output ?? Path.Combine(loaded.Dir, "build", loaded.Project.CollectionName));
        try
        {
            var result = await Task.Run(() => ResourceExporter.Export(loaded.Project, loaded.Dir, target), ct);
            return new BuildReport(result.OutputDir, result.Drawables, result.Tattoos, result.Files, result.Warnings,
                $"Copia la carpeta a resources/ del servidor y añade 'ensure {Path.GetFileName(result.OutputDir)}' a server.cfg.");
        }
        catch (ExportException ex)
        {
            throw new McpException("No se puede generar porque el proyecto tiene errores:\n" + string.Join("\n", ex.Errors));
        }
        catch (IOException ex)
        {
            throw new McpException($"No se pudo generar: {ex.Message}");
        }
    }

    [McpServerTool(Name = "pack_project", Destructive = false, OpenWorld = false)]
    [Description("Empaqueta el proyecto con todos sus archivos en un .ecpack para moverlo o compartirlo.")]
    public async Task<string> PackProject(
        [Description(ProjectParam)] string project,
        [Description("Ruta del .ecpack. Por defecto, junto al proyecto.")] string? output = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var target = workspace.Resolve(output ?? Path.Combine(Path.GetDirectoryName(loaded.Dir) ?? loaded.Dir, loaded.Project.CollectionName + ProjectPackage.Extension));
        var files = await Task.Run(() => ProjectPackage.Export(loaded.File, target), ct);
        return $"{files} archivos empaquetados en {target}.";
    }

    private static SlotOrder Order(ClothingProject project, Drawable d) => new(
        Describe.Gender(d.Gender),
        d.Component is { } c ? PedSlotNames.FileCode(c) : PedSlotNames.FileCode(d.Prop!.Value),
        ClothingProject.InGameOrder(project.SameSlot(d)).Select((x, i) => $"{i:000} {x.Name}").ToList());
}
