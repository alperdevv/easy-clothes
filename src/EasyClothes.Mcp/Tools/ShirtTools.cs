using System.ComponentModel;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Viewing;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp.Tools;

public sealed record ShirtShape(string Style, float Sleeve, float LengthCm, float Fit, string Neckline, float NeckDepthCm, string Collar, string Hem,
    bool RolledCuffs, bool Pocket, string Layer);

public sealed record ShirtPrint(
    [property: Description("Dónde: pecho, pecho-izquierda, tripa, espalda, nuca o mangas.")] string Zone,
    [property: Description("Texto a imprimir (nombre, dorsal…). Se ignora si das image.")] string? Text = null,
    [property: Description("Imagen PNG o .ytd a imprimir (ruta Windows o WSL).")] string? Image = null,
    [property: Description("Color del texto, #RRGGBB.")] string? Color = null,
    [property: Description("Ancho en cm, 2-40.")] float SizeCm = 12,
    [property: Description("Desplazamiento a la derecha en cm, visto desde fuera.")] float RightCm = 0,
    [property: Description("Desplazamiento hacia arriba en cm.")] float UpCm = 0);

public sealed record ShirtLookInfo(string Body, string Sleeves, string Trim, string Stitch, string Buttons, string Pattern, string PatternColor, string PatternColor2,
    float PatternScaleCm, string Fabric, float Wrinkles, float Dirt, float Fade, float Distress, IReadOnlyList<ShirtPrint> Prints);

public sealed record ShirtInfo(GarmentSummary Garment, ShirtShape Shape, ShirtLookInfo Look, string TextureLayout);

public sealed record ShirtTemplateInfo(string Name, string Style, ShirtLookInfo Look);

[McpServerToolType]
public sealed class ShirtTools(Workspace workspace)
{
    // Shared with the app: templates saved in either show up in both.
    public ShirtTemplates Templates { get; init; } = new(ShirtTemplates.DefaultFolder);

    // Where each part sits in the texture, for edit_texture; prints are easier with the prints parameter.
    private const string Layout = "UV del torso del personaje (0-1, origen arriba a la izquierda). Mitad inferior: el torso, con el frente en el centro " +
        "(u 0.3-0.7; el cuello arriba en v≈0.52-0.62) y la espalda partida a los lados (u 0-0.25 y 0.75-1); la cintura en v≈0.9 y por debajo, " +
        "hasta v 0.99, el bajo alargado. Mitad superior: las mangas (las dos comparten textura). Para logos y textos es más fácil el parámetro prints de create_shirt/update_shirt, " +
        "que los coloca por zonas (pecho, espalda, nuca, mangas…).";

    [McpServerTool(Name = "create_shirt", Destructive = false, OpenWorld = false)]
    [Description("Crea una camiseta, polo, camisa o jersey de cuello alto desde cero, cortada del cuerpo del personaje exportado del juego (se anima como la ropa del juego, sin Blender), y la añade al proyecto con su textura 'a' (y b, c… si das extraColors). Necesita la carpeta del personaje (la de la app o --body). Después puedes verla con render_garment.")]
    public async Task<ShirtInfo> CreateShirt(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("hombre o mujer.")] string gender = "hombre",
        [Description("Nombre de la prenda.")] string name = "Camiseta",
        [Description("Plantilla del usuario de la que partir (ver list_shirt_templates); sus valores se usan salvo los que indiques. Si la das, se ignora style.")] string? template = null,
        [Description("Modelo de partida: corta, larga, tirantes, crop, oversize, polo, cuello-alto o camisa.")] string style = "corta",
        [Description("encima (parte de arriba) o interior (para llevar bajo chaquetas).")] string layer = "encima",
        [Description("Manga, 0 sin manga - 1 hasta la muñeca (corta 0.3, codo 0.5, tres cuartos 0.75).")] float? sleeve = null,
        [Description("Largo en cm respecto a la cintura, -12 (crop) a 20; estándar 6.")] float? lengthCm = null,
        [Description("Holgura, 0 ajustada - 1 ancha (estándar 0.3).")] float? fit = null,
        [Description("Escote (sin pieza de cuello): redondo, pico o barco.")] string? neckline = null,
        [Description("Cuánto baja el escote por delante en cm, 0-15.")] float? neckDepthCm = null,
        [Description("Pieza de cuello: ninguno, polo, alto o camisa.")] string? collar = null,
        [Description("Bajo: recto, curvado o espalda (más largo detrás).")] string? hem = null,
        [Description("Puños vueltos.")] bool? rolledCuffs = null,
        [Description("Bolsillo en el pecho.")] bool? pocket = null,
        [Description("Color del cuerpo, #RRGGBB.")] string? bodyColor = null,
        [Description("Color de las mangas, #RRGGBB (por defecto como el cuerpo).")] string? sleevesColor = null,
        [Description("Color del cuello, los puños y el bajo, #RRGGBB (por defecto como el cuerpo).")] string? trimColor = null,
        [Description("Color de las costuras, #RRGGBB.")] string? stitchColor = null,
        [Description("Color de los botones (polo y camisa), #RRGGBB.")] string? buttonsColor = null,
        [Description("Estampado: liso, rayas, rayas-verticales, cuadros, camuflaje, tie-dye, degradado, raglan o dos-colores.")] string? pattern = null,
        [Description("Color del estampado, #RRGGBB.")] string? patternColor = null,
        [Description("Segundo color (camuflaje y tie-dye), #RRGGBB.")] string? patternColor2 = null,
        [Description("Tamaño del estampado en cm (ancho de raya, cuadro o mancha), 1-30.")] float? patternScaleCm = null,
        [Description("Tela: algodon, punto, seda, cuero, deportivo o vaquero.")] string? fabric = null,
        [Description("Arrugas, 0-1 (estándar 0.5).")] float? wrinkles = null,
        [Description("Suciedad, 0-1.")] float? dirt = null,
        [Description("Decolorado, 0-1.")] float? fade = null,
        [Description("Rozaduras y bordes deshilachados, 0-1.")] float? distress = null,
        [Description("Logos y textos a imprimir, colocados por zonas.")] IReadOnlyList<ShirtPrint>? prints = null,
        [Description("Más colores del cuerpo, #RRGGBB: cada uno crea otra textura (b, c…) con el mismo diseño.")] IReadOnlyList<string>? extraColors = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var pedGender = Workspace.ParseGender(gender);
        var start = template is null ? new ShirtDesign().WithStyle(ParseStyle(style))
            : Templates.Find(template)?.Design ?? throw new McpException($"No hay ninguna plantilla '{template}'. Usa list_shirt_templates.");
        var design = Shaped(start, sleeve, lengthCm, fit, neckline, neckDepthCm, collar, hem, rolledCuffs, pocket) with { Layer = ParseLayer(layer) };
        var look = Looked(design.Look, bodyColor, sleevesColor, trimColor, stitchColor, buttonsColor, pattern, patternColor, patternColor2, patternScaleCm,
            fabric, wrinkles, dirt, fade, distress, prints);
        if (bodyColor is not null && sleevesColor is null) look = look with { Sleeves = look.Body };
        if (bodyColor is not null && trimColor is null) look = look with { Trim = look.Body };
        var more = (extraColors ?? []).Select(Workspace.ParseColor).ToList();
        var body = Body(pedGender);
        var shirt = Guard(() => ShirtGarment.Create(loaded.Project, loaded.Dir, design with { Look = look }, pedGender, body, name, more));
        await Workspace.SaveAsync(loaded, ct);
        return Info(loaded.Project, shirt);
    }

    [McpServerTool(Name = "update_shirt", Destructive = false, OpenWorld = false)]
    [Description("Cambia una camiseta creada con create_shirt. mode=shape cambia la forma, la tela y las arrugas (se reescribe el modelo, con copia en .backups; todas las texturas siguen encajando). mode=colours añade una textura nueva (b, c…) con el aspecto indicado (colores, estampado, logos y textos, desgaste) sobre el actual; las texturas existentes no cambian. prints sustituye los logos y textos; pasa una lista vacía para quitarlos.")]
    public async Task<ShirtInfo> UpdateShirt(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la camiseta, o su nombre si es único.")] string garment,
        [Description("shape o colours.")] string mode,
        [Description("Con mode=shape, parte de la forma típica de este modelo: corta, larga, tirantes, crop, oversize, polo, cuello-alto o camisa.")] string? style = null,
        [Description("Manga, 0 sin manga - 1 hasta la muñeca.")] float? sleeve = null,
        [Description("Largo en cm respecto a la cintura, -12 a 20.")] float? lengthCm = null,
        [Description("Holgura, 0 ajustada - 1 ancha.")] float? fit = null,
        [Description("Escote: redondo, pico o barco.")] string? neckline = null,
        [Description("Cuánto baja el escote por delante en cm, 0-15.")] float? neckDepthCm = null,
        [Description("Pieza de cuello: ninguno, polo, alto o camisa.")] string? collar = null,
        [Description("Bajo: recto, curvado o espalda.")] string? hem = null,
        [Description("Puños vueltos.")] bool? rolledCuffs = null,
        [Description("Bolsillo en el pecho.")] bool? pocket = null,
        [Description("Color del cuerpo, #RRGGBB.")] string? bodyColor = null,
        [Description("Color de las mangas, #RRGGBB.")] string? sleevesColor = null,
        [Description("Color del cuello, los puños y el bajo, #RRGGBB.")] string? trimColor = null,
        [Description("Color de las costuras, #RRGGBB.")] string? stitchColor = null,
        [Description("Color de los botones, #RRGGBB.")] string? buttonsColor = null,
        [Description("Estampado: liso, rayas, rayas-verticales, cuadros, camuflaje, tie-dye, degradado, raglan o dos-colores.")] string? pattern = null,
        [Description("Color del estampado, #RRGGBB.")] string? patternColor = null,
        [Description("Segundo color del estampado, #RRGGBB.")] string? patternColor2 = null,
        [Description("Tamaño del estampado en cm, 1-30.")] float? patternScaleCm = null,
        [Description("Tela: algodon, punto, seda, cuero, deportivo o vaquero.")] string? fabric = null,
        [Description("Arrugas, 0-1.")] float? wrinkles = null,
        [Description("Suciedad, 0-1.")] float? dirt = null,
        [Description("Decolorado, 0-1.")] float? fade = null,
        [Description("Rozaduras, 0-1.")] float? distress = null,
        [Description("Logos y textos (sustituyen a los actuales).")] IReadOnlyList<ShirtPrint>? prints = null,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var shirt = Workspace.FindGarment(loaded.Project, garment);
        var design = shirt.Shirt ?? throw new McpException($"{shirt.Name} no es una camiseta creada con EasyClothes.");
        var body = Body(shirt.Gender);
        var look = Looked(design.Look, bodyColor, sleevesColor, trimColor, stitchColor, buttonsColor, pattern, patternColor, patternColor2, patternScaleCm,
            fabric, wrinkles, dirt, fade, distress, prints);
        switch (mode.Trim().ToLowerInvariant())
        {
            case "shape" or "forma":
                var start = style is null ? design : design.WithStyle(ParseStyle(style));
                var shaped = Shaped(start, sleeve, lengthCm, fit, neckline, neckDepthCm, collar, hem, rolledCuffs, pocket) with { Look = look };
                Guard(() => ShirtGarment.Reshape(loaded.Dir, shirt, shaped, body));
                break;
            case "colours" or "colors" or "colores":
                Guard(() => ShirtGarment.AddColourway(loaded.Dir, shirt, look, body));
                break;
            default:
                throw new McpException($"Modo '{mode}' desconocido; usa shape o colours.");
        }
        await Workspace.SaveAsync(loaded, ct);
        return Info(loaded.Project, shirt);
    }

    [McpServerTool(Name = "list_shirt_templates", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lista las plantillas de camiseta que el usuario ha guardado (desde la app o con save_shirt_template), para crear prendas con create_shirt template=...")]
    public IReadOnlyList<ShirtTemplateInfo> ListShirtTemplates() =>
        Templates.List().Select(t => new ShirtTemplateInfo(t.Name, StyleName(t.Design.Style), LookInfo(t.Design.Look))).ToList();

    [McpServerTool(Name = "save_shirt_template", Destructive = false, OpenWorld = false)]
    [Description("Guarda la forma y el aspecto de una camiseta del proyecto como plantilla del usuario, para reutilizarla en otras prendas y en la app. Si ya existe una con ese nombre, se sustituye.")]
    public async Task<ShirtTemplateInfo> SaveShirtTemplate(
        [Description("Archivo .ecproj o la carpeta que lo contiene.")] string project,
        [Description("Id de la camiseta, o su nombre si es único.")] string garment,
        [Description("Nombre de la plantilla.")] string name,
        CancellationToken ct = default)
    {
        var loaded = await workspace.LoadAsync(project, ct);
        var shirt = Workspace.FindGarment(loaded.Project, garment);
        var design = shirt.Shirt ?? throw new McpException($"{shirt.Name} no es una camiseta creada con EasyClothes.");
        // Pictures are stored relative to the project; the template keeps them findable from anywhere.
        design = design with { Look = design.Look with { Graphics = design.Look.Graphics.Select(g => g.Image is { } image && !Path.IsPathRooted(image)
            ? g with { Image = Path.Combine(loaded.Dir, image) } : g).ToList() } };
        try
        {
            var saved = Templates.Save(name, design);
            return new ShirtTemplateInfo(saved.Name, StyleName(saved.Design.Style), LookInfo(saved.Design.Look));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new McpException($"No se pudo guardar la plantilla: {ex.Message}");
        }
    }

    private SkinnedBody Body(PedGender gender) =>
        workspace.Options.BodyFolder is { } folder && BaseBody.ResolveRoot(folder) is { } root && BaseBody.Torso(root, gender) is { } body
            ? body
            : throw new McpException("Para crear ropa hace falta el personaje exportado del juego (mp_m_freemode_01 y mp_f_freemode_01 con su .yft). Elige su carpeta en la app (botón de carpeta junto a «Personaje») o arranca el servidor con --body.");

    private static ShirtDesign Shaped(ShirtDesign d, float? sleeve, float? length, float? fit, string? neckline, float? neckDepth, string? collar, string? hem,
        bool? rolledCuffs, bool? pocket) => d with
    {
        Sleeve = sleeve ?? d.Sleeve,
        Length = length ?? d.Length,
        Fit = fit ?? d.Fit,
        Neckline = neckline is null ? d.Neckline : Parse(neckline, Necklines, "Escote"),
        NeckDepth = neckDepth ?? d.NeckDepth,
        Collar = collar is null ? d.Collar : Parse(collar, Collars, "Cuello"),
        Hem = hem is null ? d.Hem : Parse(hem, Hems, "Bajo"),
        RolledCuffs = rolledCuffs ?? d.RolledCuffs,
        Pocket = pocket ?? d.Pocket,
    };

    private static ShirtLook Looked(ShirtLook l, string? body, string? sleeves, string? trim, string? stitch, string? buttons, string? pattern,
        string? patternColour, string? patternColour2, float? patternScale, string? fabric, float? wrinkles, float? dirt, float? fade, float? distress,
        IReadOnlyList<ShirtPrint>? prints) => l with
    {
        Body = body is null ? l.Body : Workspace.ParseColor(body),
        Sleeves = sleeves is null ? l.Sleeves : Workspace.ParseColor(sleeves),
        Trim = trim is null ? l.Trim : Workspace.ParseColor(trim),
        Stitch = stitch is null ? l.Stitch : Workspace.ParseColor(stitch),
        Buttons = buttons is null ? l.Buttons : Workspace.ParseColor(buttons),
        Pattern = l.Pattern with
        {
            Kind = pattern is null ? l.Pattern.Kind : Parse(pattern, Patterns, "Estampado"),
            Colour = patternColour is null ? l.Pattern.Colour : Workspace.ParseColor(patternColour),
            Colour2 = patternColour2 is null ? l.Pattern.Colour2 : Workspace.ParseColor(patternColour2),
            Scale = patternScale ?? l.Pattern.Scale,
        },
        Fabric = fabric is null ? l.Fabric : Parse(fabric, Fabrics, "Tela"),
        Wrinkles = wrinkles ?? l.Wrinkles,
        Dirt = dirt ?? l.Dirt,
        Fade = fade ?? l.Fade,
        Distress = distress ?? l.Distress,
        Graphics = prints is null ? l.Graphics : prints.Select(Graphic).ToList(),
    };

    private static ShirtGraphic Graphic(ShirtPrint print)
    {
        string? image = null;
        if (print.Image is { Length: > 0 } path)
        {
            image = HostPaths.Normalize(path);
            if (!File.Exists(image)) throw new McpException($"No existe la imagen {path}.");
        }
        else if (string.IsNullOrWhiteSpace(print.Text))
        {
            throw new McpException("Cada estampado necesita un texto o una imagen.");
        }
        return new ShirtGraphic
        {
            Zone = Parse(print.Zone, Zones, "Zona"),
            Image = image,
            Text = image is null ? print.Text : null,
            TextColour = print.Color is null ? 0xFFFFFF : Workspace.ParseColor(print.Color),
            Size = print.SizeCm,
            OffsetX = print.RightCm,
            OffsetY = print.UpCm,
        };
    }

    private static ShirtInfo Info(ClothingProject project, Drawable shirt)
    {
        var d = shirt.Shirt!;
        return new ShirtInfo(
            Describe.Garment(project, shirt),
            new ShirtShape(StyleName(d.Style), d.Sleeve, d.Length, d.Fit, Name(Necklines, d.Neckline), d.NeckDepth, Name(Collars, d.Collar), Name(Hems, d.Hem),
                d.RolledCuffs, d.Pocket, d.Layer == ShirtLayer.Undershirt ? "interior" : "encima"),
            LookInfo(d.Look),
            Layout);
    }

    private static ShirtLookInfo LookInfo(ShirtLook l) => new(
        Hex(l.Body), Hex(l.Sleeves), Hex(l.Trim), Hex(l.Stitch), Hex(l.Buttons), Name(Patterns, l.Pattern.Kind), Hex(l.Pattern.Colour), Hex(l.Pattern.Colour2),
        l.Pattern.Scale, Name(Fabrics, l.Fabric), l.Wrinkles, l.Dirt, l.Fade, l.Distress,
        l.Graphics.Select(g => new ShirtPrint(Name(Zones, g.Zone), g.Text, g.Image, Hex(g.TextColour), g.Size, g.OffsetX, g.OffsetY)).ToList());

    private static readonly (string Name, ShirtStyle Value)[] Styles =
    [
        ("corta", ShirtStyle.TShirt), ("larga", ShirtStyle.LongSleeve), ("tirantes", ShirtStyle.Tank), ("crop", ShirtStyle.Crop),
        ("oversize", ShirtStyle.Oversized), ("polo", ShirtStyle.Polo), ("cuello-alto", ShirtStyle.Turtleneck), ("camisa", ShirtStyle.Shirt),
    ];
    private static readonly (string Name, Neckline Value)[] Necklines = [("redondo", Neckline.Crew), ("pico", Neckline.VNeck), ("barco", Neckline.Scoop)];
    private static readonly (string Name, Collar Value)[] Collars = [("ninguno", Collar.None), ("polo", Collar.Polo), ("alto", Collar.Turtleneck), ("camisa", Collar.Shirt)];
    private static readonly (string Name, HemShape Value)[] Hems = [("recto", HemShape.Straight), ("curvado", HemShape.Curved), ("espalda", HemShape.LongerBack)];
    private static readonly (string Name, ShirtPatternKind Value)[] Patterns =
    [
        ("liso", ShirtPatternKind.None), ("rayas", ShirtPatternKind.Stripes), ("rayas-verticales", ShirtPatternKind.VerticalStripes), ("cuadros", ShirtPatternKind.Checks),
        ("camuflaje", ShirtPatternKind.Camo), ("tie-dye", ShirtPatternKind.TieDye), ("degradado", ShirtPatternKind.Gradient), ("raglan", ShirtPatternKind.Raglan),
        ("dos-colores", ShirtPatternKind.ColourBlock),
    ];
    private static readonly (string Name, Fabric Value)[] Fabrics =
        [("algodon", Fabric.Cotton), ("punto", Fabric.Knit), ("seda", Fabric.Silk), ("cuero", Fabric.Leather), ("deportivo", Fabric.Sport), ("vaquero", Fabric.Denim)];
    private static readonly (string Name, GraphicZone Value)[] Zones =
    [
        ("pecho", GraphicZone.ChestCentre), ("pecho-izquierda", GraphicZone.ChestLeft), ("tripa", GraphicZone.Belly), ("espalda", GraphicZone.Back),
        ("nuca", GraphicZone.Nape), ("mangas", GraphicZone.Sleeve),
    ];

    private static ShirtStyle ParseStyle(string style) => Parse(style, Styles, "Modelo");

    private static ShirtLayer ParseLayer(string layer) => layer.Trim().ToLowerInvariant() switch
    {
        "encima" or "top" => ShirtLayer.Top,
        "interior" or "debajo" or "undershirt" => ShirtLayer.Undershirt,
        _ => throw new McpException($"Capa '{layer}' desconocida; usa encima o interior."),
    };

    private static string StyleName(ShirtStyle style) => Name(Styles, style);

    // Accents, spaces and underscores don't matter: "cuello alto", "cuello_alto" and "cuello-alto" all match.
    private static T Parse<T>(string value, (string Name, T Value)[] options, string what)
    {
        var key = Key(value);
        foreach (var (name, option) in options)
            if (Key(name) == key) return option;
        throw new McpException($"{what} '{value}' desconocido; usa {string.Join(", ", options.Select(o => o.Name))}.");
    }

    private static string Key(string value) =>
        new(value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsLetterOrDigit(c)).ToArray());

    private static string Name<T>((string Name, T Value)[] options, T value) =>
        options.First(o => EqualityComparer<T>.Default.Equals(o.Value, value)).Name;

    private static string Hex(uint rgb) => $"#{rgb:X6}";

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            throw new McpException($"No se pudo guardar la camiseta: {ex.Message}");
        }
    }
}
