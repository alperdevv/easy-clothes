using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyClothes.Core.Model;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

[McpServerToolType]
public sealed class PreviewTools(Workspace workspace)
{
    private const int MaxPreviewSide = 1024;

    private static readonly Dictionary<string, float> Views = new(StringComparer.OrdinalIgnoreCase)
    {
        ["front"] = 0, ["frente"] = 0, ["left"] = 90, ["izquierda"] = 90,
        ["back"] = 180, ["espalda"] = 180, ["right"] = 270, ["derecha"] = 270,
    };

    [McpServerTool(Name = "render_garment", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Renderiza una prenda con una de sus texturas y devuelve la imagen, con una vista por ángulo una al lado de otra, encuadrada en la prenda. Si está configurado el personaje base, la ropa se muestra puesta sobre él y los accesorios en su sitio; con withBody=false sale la prenda sola (los accesorios se orientan igual con el esqueleto), útil para fotos de catálogo con transparent=true.")]
    public async Task<IEnumerable<ContentBlock>> RenderGarment(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        [Description("Letra de la textura (a, b, c…).")] string texture = "a",
        [Description("Vistas separadas por comas: front, left, back, right, o ángulos en grados.")] string views = "front,left,back",
        [Description("Mostrar la prenda puesta en el personaje base si está disponible.")] bool withBody = true,
        [Description("Alto en píxeles de cada vista (256-1024).")] int size = 640,
        [Description("Fondo transparente (PNG con alfa).")] bool transparent = false,
        [Description("Color de fondo #RRGGBB si no es transparente.")] string? background = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        if (d.Reserved) throw new McpException($"{d.Name} es un número reservado sin prenda.");
        var yaws = ParseViews(views);
        var options = new RenderOptions(Math.Clamp(size, 256, 1024), withBody, BackgroundFrom(transparent, background));
        var (png, note) = await Task.Run(() => Render(loaded, d, texture, yaws, options, new BodyCache()), ct);
        return
        [
            ImageContentBlock.FromBytes(png, "image/png"),
            new TextContentBlock { Text = $"{d.Name}, textura {texture}, vistas {views} ({note})." },
        ];
    }

    [McpServerTool(Name = "render_all", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Renderiza todas las prendas del proyecto (o las que pasen el filtro) y guarda un PNG por prenda en outDir con el nombre {sexo}_{hueco}_{número}.png, p. ej. mujer_jbib_010.png. Por defecto prenda sola, vista frontal. Devuelve solo el resumen, sin imágenes.")]
    public async Task<string> RenderAll(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Carpeta donde guardar los PNG; se crea si no existe.")] string outDir,
        [Description("Patrones separados por comas con * y ?, comparados con el nombre del archivo (mujer_p_head_*) o de la prenda (jbib_01?). Vacío = todas.")] string? filter = null,
        [Description("Letra de la textura (a, b, c…); las prendas sin esa letra usan la a.")] string texture = "a",
        [Description("Vistas separadas por comas: front, left, back, right, o ángulos en grados.")] string views = "front",
        [Description("Vistas para huecos concretos, que sustituyen a views en ellos, p. ej. {\"p_lwrist\": \"left\", \"p_rwrist\": \"right\"} para ver los relojes de frente.")] Dictionary<string, string>? viewsBySlot = null,
        [Description("Mostrar la prenda puesta en el personaje base.")] bool withBody = false,
        [Description("Alto en píxeles de cada vista (256-1024).")] int size = 640,
        [Description("Fondo transparente (PNG con alfa).")] bool transparent = false,
        [Description("Color de fondo #RRGGBB si no es transparente.")] string? background = null,
        [Description("No volver a renderizar las que ya tienen PNG en outDir (para continuar una tanda cortada).")] bool skipExisting = false,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var target = workspace.Resolve(outDir);
        var yaws = ParseViews(views);
        var slotYaws = (viewsBySlot ?? []).ToDictionary(e => SlotCode(Workspace.ParseSlot(e.Key)), e => ParseViews(e.Value));
        var options = new RenderOptions(Math.Clamp(size, 256, 1024), withBody, BackgroundFrom(transparent, background));
        var textureIndex = texture.Trim().ToLowerInvariant() is [>= 'a' and <= 'z' and var c] ? c - 'a' : throw new McpException($"Textura '{texture}' no válida; usa una letra.");
        var patterns = (filter ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Wildcard).ToList();

        var jobs = loaded.Project.Drawables
            .Where(d => !d.Reserved)
            .Select(d => (Garment: d, File: CatalogName(loaded.Project, d)))
            .Where(j => patterns.Count == 0 || patterns.Any(p => p.IsMatch(j.File) || p.IsMatch(j.Garment.Name)))
            .OrderBy(j => j.File, StringComparer.Ordinal)
            .ToList();
        Directory.CreateDirectory(target);

        var bodies = new BodyCache();
        var failures = new ConcurrentBag<string>();
        int written = 0, skipped = 0;
        var clock = Stopwatch.StartNew();
        await Parallel.ForEachAsync(jobs, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, async (job, token) =>
        {
            var file = Path.Combine(target, job.File + ".png");
            if (skipExisting && File.Exists(file))
            {
                Interlocked.Increment(ref skipped);
                return;
            }
            try
            {
                var letter = job.Garment.Textures.Count > textureIndex ? texture : "a";
                var (png, _) = Render(loaded, job.Garment, letter, slotYaws.GetValueOrDefault(SlotCode((job.Garment.Component, job.Garment.Prop)), yaws), options, bodies);
                await File.WriteAllBytesAsync(file, png, token);
                Interlocked.Increment(ref written);
            }
            catch (McpException ex)
            {
                failures.Add($"{job.File}: {ex.Message}");
            }
        });

        var failed = failures.Order(StringComparer.Ordinal).ToList();
        var summary = $"{written} PNG guardados en {target} en {clock.Elapsed.TotalSeconds:0} s ({jobs.Count} prendas{(patterns.Count > 0 ? " con el filtro" : "")}"
            + (skipped > 0 ? $", {skipped} ya existían" : "") + ").";
        return failed.Count == 0 ? summary
            : $"{summary}\n{failed.Count} fallaron:\n" + string.Join("\n", failed.Take(50)) + (failed.Count > 50 ? $"\n… y {failed.Count - 50} más." : "");
    }

    private sealed record RenderOptions(int Size, bool WithBody, uint Background);

    // Body parts loaded once per call, shared by every garment of a batch.
    private sealed class BodyCache
    {
        private readonly ConcurrentDictionary<string, Lazy<RenderItem>> _parts = new();

        public RenderItem Get(BodyPart part) =>
            _parts.GetOrAdd(part.ModelPath, _ => new Lazy<RenderItem>(() => GarmentPreview.LoadPart(part.ModelPath, part.TexturePath))).Value;
    }

    private (byte[] Png, string Note) Render(LoadedProject loaded, Drawable d, string texture, IReadOnlyList<float> yaws, RenderOptions options, BodyCache bodies)
    {
        var textureFile = d.Textures.Count == 0 ? null : Path.Combine(loaded.Dir, d.Textures[Workspace.TextureIndex(d, texture)].TexturePath);
        var root = workspace.Options.BodyFolder is { } folder ? BaseBody.ResolveRoot(folder) : null;
        // Without the body a prop still needs its bone to stand the way it is worn.
        var body = root is null ? BodyFit.None
            : options.WithBody ? BaseBody.Fit(root, d.Gender, d.Component, d.Prop)
            : new BodyFit([], d.Prop is { } prop ? BaseBody.PropAnchor(root, d.Gender, prop) : null);
        try
        {
            var item = GarmentPreview.LoadPart(Path.Combine(loaded.Dir, d.ModelPath), textureFile, body.Anchor);
            if (item.Meshes.Count == 0) throw new McpException($"El modelo de {d.Name} no tiene geometría.");
            var parts = body.Parts.Select(bodies.Get).ToList();
            var png = Png(GarmentPreview.RenderViews(item, parts, yaws, options.Size * 2 / 3, options.Size, options.Background));
            return (png, Note(d, root, body, options.WithBody));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            throw new McpException($"No se pudo renderizar {d.Name}: {ex.Message}");
        }
    }

    private string Note(Drawable d, string? root, BodyFit body, bool withBody)
    {
        if (body.Parts.Count > 0) return "sobre el personaje base";
        if (root is null)
            return workspace.Options.BodyFolder is null
                ? "sin personaje: configura la carpeta del personaje en la app o arranca el servidor con --body"
                : $"sin personaje: {workspace.Options.BodyFolder} no contiene mp_m_freemode_01 ni mp_f_freemode_01";
        if (d.IsProp && body.Anchor is null)
            return $"accesorio sin orientar: no se pudo leer {PedSlotNames.PedModel(d.Gender)}.yft en {root}";
        return withBody ? "sin personaje" : "prenda sola";
    }

    // {sexo}_{hueco}_{número}, with the in-game number.
    internal static string CatalogName(ClothingProject project, Drawable d) =>
        $"{Describe.Gender(d.Gender)}_{SlotCode((d.Component, d.Prop))}_{DrawableOrder.NumberOf(project, d):D3}";

    private static string SlotCode((ComponentSlot? Component, PropSlot? Prop) slot) =>
        slot.Component is { } c ? PedSlotNames.FileCode(c) : PedSlotNames.FileCode(slot.Prop!.Value);

    private static Regex Wildcard(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<float> ParseViews(string views)
    {
        var yaws = views.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseView).ToList();
        return yaws.Count is 0 or > 6 ? throw new McpException("Indica entre 1 y 6 vistas.") : yaws;
    }

    private static uint BackgroundFrom(bool transparent, string? color) =>
        transparent ? 0 : color is null ? GarmentPreview.DefaultBackground : Workspace.ParseColor(color) << 8 | 0xFF;

    [McpServerTool(Name = "export_texture_png", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Guarda una textura de una prenda como PNG (para revisarla o editarla fuera) y devuelve una vista reducida de la imagen.")]
    public async Task<IEnumerable<ContentBlock>> ExportTexturePng(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        [Description("Letra de la textura (a, b, c…).")] string texture = "a",
        [Description("Ruta del PNG. Por defecto, exports/<prenda>_<letra>.png dentro del proyecto.")] string? output = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        var index = Workspace.TextureIndex(d, texture);
        var target = workspace.Resolve(output ?? Path.Combine(loaded.Dir, "exports", $"{Safe(d.Name)}_{TextureVariation.LetterFor(index)}.png"));

        var (image, format) = await Task.Run(() =>
        {
            try
            {
                var decoded = GarmentPreview.LoadTexture(Path.Combine(loaded.Dir, d.Textures[index].TexturePath), out var f)
                    ?? throw new McpException($"La textura {texture} de {d.Name} está vacía.");
                return (decoded, f);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                throw new McpException($"No se pudo leer la textura: {ex.Message}");
            }
        }, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, Png(image), ct);

        var scale = Math.Min(1.0, (double)MaxPreviewSide / Math.Max(image.Width, image.Height));
        var preview = scale < 1 ? ImageResampler.Resize(image, Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))) : image;
        return
        [
            ImageContentBlock.FromBytes(Png(preview), "image/png"),
            new TextContentBlock { Text = $"Guardada en {target} ({image.Width}x{image.Height}, {format})." },
        ];
    }

    internal static float ParseView(string view) =>
        Views.TryGetValue(view, out var yaw) ? yaw
        : float.TryParse(view, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var degrees) ? degrees
        : throw new McpException($"Vista desconocida '{view}'; usa front, left, back, right o grados.");

    private static byte[] Png(RgbaImage image)
    {
        using var stream = new MemoryStream();
        PngWriter.Write(image, stream);
        return stream.ToArray();
    }

    private static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
