using System.ComponentModel;
using System.Globalization;
using EasyClothes.Core.Model;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;
using EasyClothes.Core.Viewing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record TextureOperation(
    [property: Description("adjust (tono/saturación/brillo/contraste/tinte de todo), recolor (cambia el color conservando sombras y arrugas), fill (capa de color con modo de fusión), image (logo o parche colocado en UV) o png_layer (una imagen del tamaño de la textura encima, p. ej. editada fuera).")]
    string Type,
    [property: Description("adjust: tono en grados (-180..180).")] float? Hue = null,
    [property: Description("adjust: saturación en % (-100..100).")] float? Saturation = null,
    [property: Description("adjust: brillo en % (-100..100).")] float? Brightness = null,
    [property: Description("adjust: contraste en % (-100..100).")] float? Contrast = null,
    [property: Description("Color #RRGGBB: tinte en adjust, color en recolor y fill.")] string? Color = null,
    [property: Description("adjust: fuerza del tinte en % (0..100). recolor: fuerza del cambio en % (por defecto 100).")] float? Strength = null,
    [property: Description("Modo de fusión: normal, multiply, screen, overlay, softlight, darken, lighten, add, difference, color.")] string? Blend = null,
    [property: Description("Opacidad de la capa en % (0..100, por defecto 100).")] float? Opacity = null,
    [property: Description("image y png_layer: ruta del .png/.jpg.")] string? Path = null,
    [property: Description("image: centro X en UV (0 izquierda .. 1 derecha). render_uv_layout muestra dónde cae cada pieza.")] float? X = null,
    [property: Description("image: centro Y en UV (0 arriba .. 1 abajo).")] float? Y = null,
    [property: Description("image: ancho en % del ancho de la textura (por defecto 25).")] float? Size = null,
    [property: Description("image: rotación en grados.")] float? Rotation = null);

[McpServerToolType]
public sealed class TextureTools(Workspace workspace)
{
    private const int MaxSide = 1024;

    [McpServerTool(Name = "render_uv_layout", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Muestra la textura con las piezas del modelo (islas UV) dibujadas encima en azul y líneas rojas en 0.25/0.5/0.75. Sirve para saber en qué coordenadas UV colocar un logo con edit_texture.")]
    public async Task<IEnumerable<ContentBlock>> RenderUvLayout(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        [Description("Letra de la textura (a, b, c…).")] string texture = "a",
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        var index = Workspace.TextureIndex(d, texture);
        var image = await Task.Run(() => Guard(d, () =>
        {
            var item = GarmentPreview.LoadPart(Path.Combine(loaded.Dir, d.ModelPath), Path.Combine(loaded.Dir, d.Textures[index].TexturePath));
            return UvLayout.Render(item.Texture, item.Meshes, MaxSide);
        }), ct);
        return
        [
            ImageContentBlock.FromBytes(Png(image), "image/png"),
            new TextContentBlock { Text = $"Imagen de {image.Width}x{image.Height}: un punto en el píxel (px, py) es UV ({1.0 / image.Width:0.#####}·px, {1.0 / image.Height:0.#####}·py)." },
        ];
    }

    [McpServerTool(Name = "edit_texture", Destructive = false, OpenWorld = false)]
    [Description("Edita una textura con capas, como el editor de la app: ajustes de color, recolorear conservando sombras, rellenos con modo de fusión, logos colocados en UV o una imagen encima. Las capas se apilan en orden sobre las que ya tuviera la textura. Por defecto crea una variación nueva y no toca la original; con save=false solo devuelve la vista previa. Devuelve un render de la prenda con el resultado.")]
    public async Task<IEnumerable<ContentBlock>> EditTexture(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la prenda, o su nombre si es único.")] string garment,
        [Description("Operaciones, de abajo arriba.")] TextureOperation[] operations,
        [Description("Letra de la textura de partida (a, b, c…).")] string texture = "a",
        [Description("new: guardar como variación nueva (recomendado); replace: sobrescribir la textura (con copia en .backups).")] string saveAs = "new",
        [Description("false para ver el resultado sin guardar.")] bool save = true,
        [Description("Vistas del render: front, left, back, right o grados.")] string views = "front,back",
        CancellationToken ct = default)
    {
        if (operations.Length == 0) throw new McpException("Indica al menos una operación.");
        var mode = saveAs.Trim().ToLowerInvariant() switch
        {
            "new" => TextureSaveMode.NewVariation,
            "replace" => TextureSaveMode.Overwrite,
            _ => throw new McpException("saveAs debe ser new o replace."),
        };
        var loaded = await workspace.LoadAsync(project, ct);
        var d = Workspace.FindGarment(loaded.Project, garment);
        var index = Workspace.TextureIndex(d, texture);
        var texturePath = Path.Combine(loaded.Dir, d.Textures[index].TexturePath);

        var (document, note) = await Task.Run(() => Guard(d, () => TextureDocumentFile.Open(texturePath)), ct);
        foreach (var operation in operations) document.Layers.Add(ToLayer(operation, document, workspace));
        var composed = await Task.Run(() => TextureCompositor.Compose(document), ct);

        string saved;
        if (save)
        {
            if (mode == TextureSaveMode.NewVariation && d.Textures.Count >= Drawable.MaxTextures)
                throw new McpException($"{d.Name} ya tiene {Drawable.MaxTextures} texturas, el máximo del juego; usa saveAs=replace.");
            var result = await Task.Run(() => Guard(d, () => TextureEditSaver.Save(loaded.Dir, d, index, document, mode)), ct);
            await Workspace.SaveAsync(loaded, ct);
            saved = mode == TextureSaveMode.NewVariation
                ? $"Guardada como textura nueva '{TextureVariation.LetterFor(result.Index)}' de {d.Name}."
                : $"Textura '{texture}' de {d.Name} sobrescrita; el original está en {result.BackupPath}.";
        }
        else saved = "Vista previa sin guardar.";

        var render = await Task.Run(() => Guard(d, () =>
        {
            var item = GarmentPreview.LoadPart(Path.Combine(loaded.Dir, d.ModelPath), null);
            var yaws = views.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(PreviewTools.ParseView).ToList();
            return GarmentPreview.RenderViews(item with { Texture = composed }, [], yaws, 400, 600);
        }), ct);
        return
        [
            ImageContentBlock.FromBytes(Png(render), "image/png"),
            new TextContentBlock { Text = $"{saved} Capas: {string.Join(", ", document.Layers.Select(l => l.Name))}.{(note is null ? "" : " " + note)}" },
        ];
    }

    private static Layer ToLayer(TextureOperation op, TextureDocument document, Workspace workspace)
    {
        var opacity = Percent(op.Opacity ?? 100);
        var blend = ParseBlend(op.Blend);
        switch (op.Type.Trim().ToLowerInvariant())
        {
            case "adjust":
                return new AdjustmentLayer
                {
                    Name = "Ajuste (Claude)",
                    Hue = Math.Clamp(op.Hue ?? 0, -180, 180),
                    Saturation = Math.Clamp((op.Saturation ?? 0) / 100, -1, 1),
                    Brightness = Math.Clamp((op.Brightness ?? 0) / 100, -1, 1),
                    Contrast = Math.Clamp((op.Contrast ?? 0) / 100, -1, 1),
                    TintRgb = op.Color is null ? 0xFFFFFF : ParseColor(op.Color),
                    TintStrength = op.Color is null ? 0 : Percent(op.Strength ?? 50),
                    Opacity = opacity,
                };
            case "recolor":
                return new FillLayer { Name = $"Color {op.Color} (Claude)", Rgb = ParseColor(op.Color ?? throw new McpException("recolor necesita color.")), Blend = BlendMode.Color, Opacity = Percent(op.Strength ?? 100) };
            case "fill":
                return new FillLayer { Name = $"Relleno {op.Color} (Claude)", Rgb = ParseColor(op.Color ?? throw new McpException("fill necesita color.")), Blend = blend, Opacity = opacity };
            case "image":
                return new ImageLayer
                {
                    Name = $"{System.IO.Path.GetFileNameWithoutExtension(op.Path)} (Claude)",
                    Image = LoadImage(op.Path, workspace),
                    X = Math.Clamp(op.X ?? 0.5f, 0, 1),
                    Y = Math.Clamp(op.Y ?? 0.5f, 0, 1),
                    Scale = Math.Clamp((op.Size ?? 25) / 100, 0.005f, 4),
                    Rotation = op.Rotation ?? 0,
                    Blend = blend,
                    Opacity = opacity,
                };
            case "png_layer":
                var image = LoadImage(op.Path, workspace);
                if (image.Width != document.Width || image.Height != document.Height)
                    image = TextureCompositor.Resample(image, document.Width, document.Height);
                return new PixelLayer { Name = $"{System.IO.Path.GetFileNameWithoutExtension(op.Path)} (Claude)", Image = image, Blend = blend, Opacity = opacity };
            default:
                throw new McpException($"Operación desconocida '{op.Type}'; usa adjust, recolor, fill, image o png_layer.");
        }
    }

    private static RgbaImage LoadImage(string? path, Workspace workspace)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new McpException("Esta operación necesita path con la imagen.");
        var file = workspace.Resolve(path);
        if (!File.Exists(file)) throw new McpException($"No existe {path}.");
        try
        {
            return ImageLoader.Load(file);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            throw new McpException($"No se puede leer {path}: {ex.Message}");
        }
    }

    private static float Percent(float value) => Math.Clamp(value / 100, 0, 1);

    private static BlendMode ParseBlend(string? blend) =>
        blend is null ? BlendMode.Normal
        : Enum.TryParse<BlendMode>(blend.Replace("_", "").Replace("-", ""), ignoreCase: true, out var mode) ? mode
        : throw new McpException($"Modo de fusión desconocido '{blend}'.");

    private static uint ParseColor(string color)
    {
        var hex = color.Trim().TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? rgb
            : throw new McpException($"Color '{color}' no válido; usa #RRGGBB.");
    }

    private static T Guard<T>(Drawable d, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            throw new McpException($"{d.Name}: {ex.Message}");
        }
    }

    private static byte[] Png(RgbaImage image)
    {
        using var stream = new MemoryStream();
        PngWriter.Write(image, stream);
        return stream.ToArray();
    }
}
