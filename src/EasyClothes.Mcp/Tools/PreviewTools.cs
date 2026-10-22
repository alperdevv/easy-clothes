using System.ComponentModel;
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
    [Description("Renderiza una prenda con una de sus texturas y devuelve la imagen, con una vista por ángulo una al lado de otra. Si está configurado el personaje base, la ropa (no los accesorios) se muestra puesta sobre él.")]
    public async Task<IEnumerable<ContentBlock>> RenderGarment(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        [Description("Letra de la textura (a, b, c…).")] string texture = "a",
        [Description("Vistas separadas por comas: front, left, back, right, o ángulos en grados.")] string views = "front,left,back",
        [Description("Mostrar la prenda puesta en el personaje base si está disponible.")] bool withBody = true,
        [Description("Alto en píxeles de cada vista (256-1024).")] int size = 640,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        if (d.Reserved) throw new McpException($"{d.Name} es un número reservado sin prenda.");
        var textureFile = d.Textures.Count == 0 ? null : Path.Combine(loaded.Dir, d.Textures[Workspace.TextureIndex(d, texture)].TexturePath);
        var yaws = views.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseView).ToList();
        if (yaws.Count is 0 or > 6) throw new McpException("Indica entre 1 y 6 vistas.");
        size = Math.Clamp(size, 256, 1024);

        var body = withBody && d.Component is { } slot && workspace.Options.BodyFolder is { } folder && BaseBody.ResolveRoot(folder) is { } root
            ? BaseBody.Parts(root, d.Gender, slot)
            : [];
        var png = await Task.Run(() =>
        {
            try
            {
                var item = GarmentPreview.LoadPart(Path.Combine(loaded.Dir, d.ModelPath), textureFile);
                if (item.Meshes.Count == 0) throw new McpException($"El modelo de {d.Name} no tiene geometría.");
                var parts = body.Select(p => GarmentPreview.LoadPart(p.ModelPath, p.TexturePath)).ToList();
                return Png(GarmentPreview.RenderViews(item, parts, yaws, size * 2 / 3, size));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                throw new McpException($"No se pudo renderizar {d.Name}: {ex.Message}");
            }
        }, ct);

        var bodyNote = body.Count > 0 ? "sobre el personaje base"
            : d.IsProp ? "los accesorios se muestran sin personaje"
            : workspace.Options.BodyFolder is null ? "sin personaje: configura la carpeta del personaje en la app o arranca el servidor con --body" : "sin personaje";
        return
        [
            ImageContentBlock.FromBytes(png, "image/png"),
            new TextContentBlock { Text = $"{d.Name}, textura {texture}, vistas {views} ({bodyNote})." },
        ];
    }

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

    private static float ParseView(string view) =>
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
