using System.ComponentModel;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record CapShape(string Style, float Rotation, float Size, float CrownHeightCm, float CrownShape, float VisorLengthCm, float VisorCurve, float VisorAngle, float VisorWidth, float VisorSquare, bool Button);

public sealed record CapColourSet(string Crown, string Front, string Visor, string UnderVisor, string Button, string Stitch);

public sealed record CapInfo(GarmentSummary Garment, CapShape Shape, CapColourSet Colours, string TextureLayout);

[McpServerToolType]
public sealed class CapTools(Workspace workspace)
{
    private const string Layout =
        "Plantilla UV fija (0-1, origen arriba a la izquierda): copa exterior u 0-1, v 0-0.25 (v=0 arriba, v=0.25 la banda; " +
        "el centro de la frente en u=0.5 y los paneles delanteros en u 0.333-0.667); visera por arriba u 0-0.5, v 0.30-0.50 " +
        "(borde delantero arriba); visera por debajo u 0.5-1, v 0.30-0.50; canto de la visera v 0.52-0.54; botón u 0-0.05, v 0.56-0.61; " +
        "interior de la copa v 0.62-0.87. Un logo frontal va bien en edit_texture con x≈0.5, y≈0.15.";

    [McpServerTool(Name = "create_cap", Destructive = false, OpenWorld = false)]
    [Description("Crea una gorra propia desde cero (modelo original de EasyClothes, sin partir de ningún mod) y la añade al proyecto como accesorio de cabeza, con su textura 'a'. Los parámetros que no indiques usan la forma estándar. Después puedes verla con render_garment y añadir logos con edit_texture.")]
    public async Task<CapInfo> CreateCap(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("hombre o mujer.")] string gender = "hombre",
        [Description("Nombre de la prenda.")] string name = "Gorra",
        [Description("Modelo de partida: beisbol, snapback (copa alta y visera plana), dad (baja y blanda) o trucker (rejilla detrás).")] string style = "beisbol",
        [Description("Talla, 0.9-1.1 (1 = ajustada a la cabeza del personaje).")] float? size = null,
        [Description("Alto de la copa en cm, 8-15 (estándar 11).")] float? crownHeightCm = null,
        [Description("Forma de la copa, 0 redondeada - 1 estructurada (estándar 0.4).")] float? crownShape = null,
        [Description("Largo de la visera en cm, 0-10 (0 = sin visera, estándar 7).")] float? visorLengthCm = null,
        [Description("Curva de la visera, 0 plana - 1 muy curvada (estándar 0.6).")] float? visorCurve = null,
        [Description("Inclinación de la visera en grados hacia abajo, -10 a 30 (estándar 8).")] float? visorAngle = null,
        [Description("Ancho de la visera, 0 estrecha - 1 ancha (estándar 0.5).")] float? visorWidth = null,
        [Description("Forma de la visera, 0 redondeada - 1 cuadrada (estándar 0).")] float? visorSquare = null,
        [Description("Giro en grados: 0 delante, 90 de lado a la izquierda, -90 a la derecha, 180 del revés.")] float? rotation = null,
        [Description("Botón en lo alto de la copa.")] bool? button = null,
        [Description("Color de la copa, #RRGGBB.")] string? crownColor = null,
        [Description("Color de los dos paneles delanteros, #RRGGBB.")] string? frontColor = null,
        [Description("Color de la visera por arriba, #RRGGBB.")] string? visorColor = null,
        [Description("Color de la visera por debajo, #RRGGBB.")] string? underVisorColor = null,
        [Description("Color del botón, #RRGGBB.")] string? buttonColor = null,
        [Description("Color de las costuras, #RRGGBB.")] string? stitchColor = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var design = Shaped(new CapDesign().WithStyle(ParseStyle(style)), size, crownHeightCm, crownShape, visorLengthCm, visorCurve, visorAngle, visorWidth, visorSquare, rotation, button);
        design = design with { Colours = Coloured(design.Colours, crownColor, frontColor, visorColor, underVisorColor, buttonColor, stitchColor) };
        var cap = Guard(() => CapGarment.Create(loaded.Project, loaded.Dir, design, Workspace.ParseGender(gender), name));
        await Workspace.SaveAsync(loaded, ct);
        return Info(loaded.Project, cap);
    }

    [McpServerTool(Name = "update_cap", Destructive = false, OpenWorld = false)]
    [Description("Cambia una gorra creada con create_cap. mode=shape cambia la forma (se reescribe el modelo, con copia en .backups; todas las texturas siguen encajando). mode=colours añade una textura nueva (b, c…) con los colores indicados sobre los actuales; las texturas existentes no cambian.")]
    public async Task<CapInfo> UpdateCap(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la gorra, o su nombre si es único.")] string garment,
        [Description("shape o colours.")] string mode,
        [Description("Con mode=shape, cambia al modelo indicado (beisbol, snapback, dad, trucker) y parte de su forma típica.")] string? style = null,
        [Description("Talla, 0.9-1.1.")] float? size = null,
        [Description("Alto de la copa en cm, 8-15.")] float? crownHeightCm = null,
        [Description("Forma de la copa, 0 redondeada - 1 estructurada.")] float? crownShape = null,
        [Description("Largo de la visera en cm, 0-10.")] float? visorLengthCm = null,
        [Description("Curva de la visera, 0-1.")] float? visorCurve = null,
        [Description("Inclinación de la visera en grados, -10 a 30.")] float? visorAngle = null,
        [Description("Ancho de la visera, 0 estrecha - 1 ancha (estándar 0.5).")] float? visorWidth = null,
        [Description("Forma de la visera, 0 redondeada - 1 cuadrada (estándar 0).")] float? visorSquare = null,
        [Description("Giro en grados: 0 delante, 90 de lado a la izquierda, -90 a la derecha, 180 del revés.")] float? rotation = null,
        [Description("Botón en lo alto de la copa.")] bool? button = null,
        [Description("Color de la copa, #RRGGBB.")] string? crownColor = null,
        [Description("Color de los paneles delanteros, #RRGGBB.")] string? frontColor = null,
        [Description("Color de la visera por arriba, #RRGGBB.")] string? visorColor = null,
        [Description("Color de la visera por debajo, #RRGGBB.")] string? underVisorColor = null,
        [Description("Color del botón, #RRGGBB.")] string? buttonColor = null,
        [Description("Color de las costuras, #RRGGBB.")] string? stitchColor = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var cap = Workspace.FindGarment(loaded.Project, garment);
        var design = cap.Cap ?? throw new McpException($"{cap.Name} no es una gorra creada con EasyClothes.");
        switch (mode.Trim().ToLowerInvariant())
        {
            case "shape" or "forma":
                var start = style is null ? design : design.WithStyle(ParseStyle(style));
                Guard(() => CapGarment.Reshape(loaded.Dir, cap, Shaped(start, size, crownHeightCm, crownShape, visorLengthCm, visorCurve, visorAngle, visorWidth, visorSquare, rotation, button)));
                break;
            case "colours" or "colors" or "colores":
                Guard(() => CapGarment.AddColourway(loaded.Dir, cap, Coloured(design.Colours, crownColor, frontColor, visorColor, underVisorColor, buttonColor, stitchColor)));
                break;
            default:
                throw new McpException($"Modo '{mode}' desconocido; usa shape o colours.");
        }
        await Workspace.SaveAsync(loaded, ct);
        return Info(loaded.Project, cap);
    }

    private static CapDesign Shaped(CapDesign d, float? size, float? crownHeight, float? crownShape, float? visorLength, float? visorCurve, float? visorAngle,
        float? visorWidth, float? visorSquare, float? rotation, bool? button) => d with
    {
        Size = size ?? d.Size,
        CrownHeight = crownHeight ?? d.CrownHeight,
        CrownShape = crownShape ?? d.CrownShape,
        VisorLength = visorLength ?? d.VisorLength,
        VisorCurve = visorCurve ?? d.VisorCurve,
        VisorAngle = visorAngle ?? d.VisorAngle,
        VisorWidth = visorWidth ?? d.VisorWidth,
        VisorSquare = visorSquare ?? d.VisorSquare,
        Rotation = rotation ?? d.Rotation,
        Button = button ?? d.Button,
    };

    private static CapColours Coloured(CapColours c, string? crown, string? front, string? visor, string? underVisor, string? button, string? stitch) => c with
    {
        Crown = crown is null ? c.Crown : Workspace.ParseColor(crown),
        Front = front is null ? c.Front : Workspace.ParseColor(front),
        Visor = visor is null ? c.Visor : Workspace.ParseColor(visor),
        UnderVisor = underVisor is null ? c.UnderVisor : Workspace.ParseColor(underVisor),
        Button = button is null ? c.Button : Workspace.ParseColor(button),
        Stitch = stitch is null ? c.Stitch : Workspace.ParseColor(stitch),
    };

    private static CapInfo Info(ClothingProject project, Drawable cap)
    {
        var d = cap.Cap!;
        var c = d.Colours;
        return new CapInfo(
            Describe.Garment(project, cap),
            new CapShape(StyleName(d.Style), d.Rotation, d.Size, d.CrownHeight, d.CrownShape, d.VisorLength, d.VisorCurve, d.VisorAngle, d.VisorWidth, d.VisorSquare, d.Button),
            new CapColourSet(Hex(c.Crown), Hex(c.Front), Hex(c.Visor), Hex(c.UnderVisor), Hex(c.Button), Hex(c.Stitch)),
            Layout);
    }

    private static CapStyle ParseStyle(string style) => style.Trim().ToLowerInvariant() switch
    {
        "beisbol" or "béisbol" or "baseball" => CapStyle.Baseball,
        "snapback" => CapStyle.Snapback,
        "dad" or "dad hat" or "dadhat" => CapStyle.DadHat,
        "trucker" => CapStyle.Trucker,
        _ => throw new McpException($"Modelo '{style}' desconocido; usa beisbol, snapback, dad o trucker."),
    };

    private static string StyleName(CapStyle style) => style switch
    {
        CapStyle.Snapback => "snapback",
        CapStyle.DadHat => "dad",
        CapStyle.Trucker => "trucker",
        _ => "beisbol",
    };

    private static string Hex(uint rgb) => $"#{rgb:X6}";

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new McpException($"No se pudo guardar la gorra: {ex.Message}");
        }
    }
}
