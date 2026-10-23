using System.ComponentModel;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Viewing;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record PlacementInfo(float UpCm, float ForwardCm, float LeftCm, float Turn, float Nod, float Tilt, float Scale);

public sealed record ImportedModelInfo(GarmentSummary Garment, PlacementInfo Placement, int Triangles, IReadOnlyList<string> Warnings);

[McpServerToolType]
public sealed class ModelTools(Workspace workspace)
{
    private const string Axes = "El .obj debe usar los ejes por defecto de Blender al exportar (Y arriba, el frente hacia +Z) y metros.";

    [McpServerTool(Name = "import_model", Destructive = false, OpenWorld = false)]
    [Description("Convierte un modelo 3D propio (.obj, p. ej. de Blender) en accesorio de cabeza: gorra o sombrero, gafas o pendientes. " + Axes +
                 " Si se modeló sobre la cabeza de referencia (export_reference_head) entra en su sitio; si no, usa fit=true y ajusta con place_model. Devuelve la colocación usada; míralo con render_garment.")]
    public async Task<ImportedModelInfo> ImportModel(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Ruta del .obj.")] string model,
        [Description("PNG con la textura. Si no se indica, se usa la del .mtl o una gris para pintarla después.")] string? texture = null,
        [Description("hat (gorra o sombrero), glasses o ears.")] string slot = "hat",
        [Description("hombre o mujer.")] string gender = "hombre",
        [Description("Nombre de la prenda; por defecto, el del archivo.")] string? name = null,
        [Description("Corregir unidades y centrar sobre la cabeza automáticamente.")] bool fit = false,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var file = workspace.Resolve(model);
        var obj = Read(file);
        var prop = ParseSlot(slot);
        var image = texture is null ? obj.TexturePath : workspace.Resolve(texture);
        var placement = fit ? ObjProp.Fit(obj.Mesh) : new PropPlacement();
        var drawable = Guard(() => ObjProp.Create(loaded.Project, loaded.Dir, file, image, prop, Workspace.ParseGender(gender), name ?? Path.GetFileNameWithoutExtension(file), placement));
        await Workspace.SaveAsync(loaded, ct);
        return new ImportedModelInfo(Describe.Garment(loaded.Project, drawable), Info(placement), obj.Mesh.TriangleCount, obj.Warnings);
    }

    [McpServerTool(Name = "place_model", Destructive = false, OpenWorld = false)]
    [Description("Mueve, gira o escala un modelo importado con import_model (el modelo anterior va a .backups). Los valores que no indiques se mantienen. Sube/baja, adelante/atrás e izquierda en cm; giro, inclinación y ladeo en grados.")]
    public async Task<ImportedModelInfo> PlaceModel(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        [Description("cm hacia arriba (negativo, abajo).")] float? upCm = null,
        [Description("cm hacia la cara (negativo, hacia la nuca).")] float? forwardCm = null,
        [Description("cm hacia la izquierda del personaje (negativo, derecha).")] float? leftCm = null,
        [Description("Giro en grados alrededor de la cabeza.")] float? turn = null,
        [Description("Inclinación adelante/atrás en grados.")] float? nod = null,
        [Description("Ladeo a los lados en grados.")] float? tilt = null,
        [Description("Escala uniforme.")] float? scale = null,
        [Description("Volver a encajar automáticamente antes de aplicar lo demás.")] bool fit = false,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var drawable = Workspace.FindGarment(loaded.Project, garment);
        var imported = drawable.Imported ?? throw new McpException($"{drawable.Name} no se importó desde un .obj.");
        var obj = Read(Path.Combine(loaded.Dir, imported.SourcePath));
        var p = fit ? ObjProp.Fit(obj.Mesh) : imported.Placement;
        p = p with
        {
            Up = upCm ?? p.Up, Forward = forwardCm ?? p.Forward, Left = leftCm ?? p.Left,
            Turn = turn ?? p.Turn, Nod = nod ?? p.Nod, Tilt = tilt ?? p.Tilt, Scale = scale ?? p.Scale,
        };
        Guard(() => ObjProp.Replace(loaded.Dir, drawable, p));
        await Workspace.SaveAsync(loaded, ct);
        return new ImportedModelInfo(Describe.Garment(loaded.Project, drawable), Info(p), obj.Mesh.TriangleCount, obj.Warnings);
    }

    [McpServerTool(Name = "export_reference_head", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Guarda la cabeza del personaje como .obj (ejes por defecto de Blender, metros) para modelar encima un accesorio. Sale de los archivos del juego del usuario: solo para modelar, no para publicarla.")]
    public string ExportReferenceHead(
        [Description("Ruta del .obj a crear.")] string output,
        [Description("hombre o mujer.")] string gender = "hombre")
    {
        if (workspace.Options.BodyFolder is not { } folder || BaseBody.ResolveRoot(folder) is not { } root)
            throw new McpException("Falta la carpeta del personaje: configúrala en la app o arranca el servidor con --body.");
        var path = workspace.Resolve(output);
        Guard(() =>
        {
            ObjProp.ExportReferenceHead(root, Workspace.ParseGender(gender), path);
            return 0;
        });
        return $"Cabeza guardada en {output}. " + Axes;
    }

    private static PropSlot ParseSlot(string slot) => slot.Trim().ToLowerInvariant() switch
    {
        "hat" or "p_head" or "gorra" or "sombrero" or "gorro" => PropSlot.Hat,
        "glasses" or "p_eyes" or "gafas" => PropSlot.Glasses,
        "ears" or "p_ears" or "pendientes" => PropSlot.Ears,
        _ => throw new McpException($"Hueco '{slot}' no válido para un modelo importado; usa hat, glasses o ears."),
    };

    private static ObjModel Read(string path)
    {
        try
        {
            return ObjReader.Read(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new McpException($"No se pudo leer {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static PlacementInfo Info(PropPlacement p) => new(p.Up, p.Forward, p.Left, p.Turn, p.Nod, p.Tilt, p.Scale);

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            throw new McpException($"No se pudo guardar el modelo: {ex.Message}");
        }
    }
}
