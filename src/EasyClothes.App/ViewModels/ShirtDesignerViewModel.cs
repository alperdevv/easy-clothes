using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public sealed record ShirtStyleOption(string Label, ShirtStyle Value)
{
    public string GarmentName => Value switch
    {
        ShirtStyle.LongSleeve => "Camiseta manga larga",
        ShirtStyle.Tank => "Camiseta sin mangas",
        ShirtStyle.Crop => "Crop top",
        ShirtStyle.Oversized => "Camiseta oversize",
        ShirtStyle.Polo => "Polo",
        ShirtStyle.Turtleneck => "Jersey de cuello alto",
        ShirtStyle.Shirt => "Camisa",
        _ => "Camiseta",
    };

    public static readonly IReadOnlyList<ShirtStyleOption> All =
    [
        new("Manga corta", ShirtStyle.TShirt),
        new("Manga larga", ShirtStyle.LongSleeve),
        new("Sin mangas", ShirtStyle.Tank),
        new("Crop top", ShirtStyle.Crop),
        new("Oversize", ShirtStyle.Oversized),
        new("Polo", ShirtStyle.Polo),
        new("Cuello alto", ShirtStyle.Turtleneck),
        new("Camisa", ShirtStyle.Shirt),
    ];

    public static ShirtStyleOption For(ShirtStyle style) => All.First(o => o.Value == style);
}

public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public static class ShirtChoices
{
    public static readonly IReadOnlyList<Choice<Neckline>> Necklines = [new("Redondo", Neckline.Crew), new("De pico", Neckline.VNeck), new("Barco (ancho)", Neckline.Scoop)];
    public static readonly IReadOnlyList<Choice<Collar>> Collars = [new("Sin cuello", Collar.None), new("Polo", Collar.Polo), new("Cuello alto", Collar.Turtleneck), new("Camisa", Collar.Shirt)];
    public static readonly IReadOnlyList<Choice<HemShape>> Hems = [new("Recto", HemShape.Straight), new("Curvado (de camisa)", HemShape.Curved), new("Más largo detrás", HemShape.LongerBack)];
    public static readonly IReadOnlyList<Choice<ShirtLayer>> Layers = [new("Encima (parte de arriba)", ShirtLayer.Top), new("Debajo de chaquetas (interior)", ShirtLayer.Undershirt)];
    public static readonly IReadOnlyList<Choice<Fabric>> Fabrics =
        [new("Algodón", Fabric.Cotton), new("Punto", Fabric.Knit), new("Seda", Fabric.Silk), new("Cuero", Fabric.Leather), new("Deportivo (rejilla)", Fabric.Sport), new("Vaquero", Fabric.Denim)];
    public static readonly IReadOnlyList<Choice<ShirtPatternKind>> Patterns =
    [
        new("Liso", ShirtPatternKind.None), new("Rayas", ShirtPatternKind.Stripes), new("Rayas verticales", ShirtPatternKind.VerticalStripes),
        new("Cuadros", ShirtPatternKind.Checks), new("Camuflaje", ShirtPatternKind.Camo), new("Tie-dye", ShirtPatternKind.TieDye),
        new("Degradado", ShirtPatternKind.Gradient), new("Raglán (hombros de color)", ShirtPatternKind.Raglan), new("Dos colores (arriba y abajo)", ShirtPatternKind.ColourBlock),
    ];
    public static readonly IReadOnlyList<Choice<GraphicZone>> Zones =
        [new("Pecho, centro", GraphicZone.ChestCentre), new("Pecho, izquierda", GraphicZone.ChestLeft), new("Tripa", GraphicZone.Belly), new("Espalda", GraphicZone.Back), new("Nuca", GraphicZone.Nape), new("Mangas", GraphicZone.Sleeve)];

    public static Choice<T> For<T>(IReadOnlyList<Choice<T>> choices, T value) => choices.First(c => EqualityComparer<T>.Default.Equals(c.Value, value));
}

public sealed record SleevePresetOption(string Label, double Sleeve)
{
    public static readonly IReadOnlyList<SleevePresetOption> All =
    [
        new("Sin", 0),
        new("Corta", 0.3),
        new("Codo", 0.5),
        new("3/4", 0.75),
        new("Larga", 1),
    ];
}

// One picture or text printed on the shirt.
public sealed partial class GraphicViewModel : ObservableObject
{
    public GraphicViewModel(ShirtGraphic graphic)
    {
        Zone = ShirtChoices.For(ShirtChoices.Zones, graphic.Zone);
        Image = graphic.Image;
        Text = graphic.Text ?? "";
        TextColor = Color.FromRgb((byte)(graphic.TextColour >> 16), (byte)(graphic.TextColour >> 8), (byte)graphic.TextColour);
        Size = graphic.Size;
        OffsetX = graphic.OffsetX;
        OffsetY = graphic.OffsetY;
    }

    public string? Image { get; }
    public bool IsText => Image is null;
    public string Title => Image is { } image ? Path.GetFileName(image) : "Texto";

    [ObservableProperty]
    public partial Choice<GraphicZone> Zone { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial Color TextColor { get; set; }

    [ObservableProperty]
    public partial double Size { get; set; }

    [ObservableProperty]
    public partial double OffsetX { get; set; }

    [ObservableProperty]
    public partial double OffsetY { get; set; }

    public ShirtGraphic Graphic => new()
    {
        Zone = Zone.Value,
        Image = Image,
        Text = IsText ? Text : null,
        TextColour = (uint)(TextColor.R << 16 | TextColor.G << 8 | TextColor.B),
        Size = (float)Size,
        OffsetX = (float)OffsetX,
        OffsetY = (float)OffsetY,
    };
}

// Another main colour for a new shirt: each one becomes its own texture variation.
public sealed partial class ExtraColourViewModel(Color color) : ObservableObject
{
    [ObservableProperty]
    public partial Color Color { get; set; } = color;
}

// The mannequin shown around the shirt and the bare body it is cut from; Torso is null without the
// exported body, and then no shirt can be made.
public sealed record ShirtStage(IReadOnlyList<RenderItem> Body, SkinnedBody? Torso);

// What the shirt designer needs from the window around it: the body, the user's templates, a file
// picker for pictures and a way to load them (relative paths belong to the project).
public sealed record ShirtDesignerServices(
    Func<PedGender, ShirtStage> StageFor,
    ShirtTemplates Templates,
    Func<Task<string?>> PickImage,
    Func<string, RgbaImage?> LoadImage);

// Edits a shirt's shape and look with a live preview, like the cap designer.
public sealed partial class ShirtDesignerViewModel : ObservableObject, IGarmentDesigner
{
    private const int PreviewTextureSize = 512;

    private readonly ShirtDesignerServices _services;
    private readonly ConcurrentDictionary<PedGender, ShirtStage> _stages = new();
    private readonly ConcurrentDictionary<string, RgbaImage?> _images = new();
    private CancellationTokenSource? _pending;
    private bool _loading;
    private ShirtLayer _layer;

    public ShirtDesignerViewModel(ShirtDesign design, PedGender gender, bool editing, ShirtDesignerServices services)
    {
        _services = services;
        IsEditing = editing;
        Gender = GenderOption.For(gender);
        Name = ShirtStyleOption.For(design.Style).GarmentName;
        Layer = ShirtChoices.For(ShirtChoices.Layers, design.Layer);
        PropertyChanged += OnChanged;
        Graphics.CollectionChanged += OnListChanged;
        ExtraColours.CollectionChanged += OnListChanged;
        Load(design.Clamped());
        RefreshTemplates();
    }

    public bool IsEditing { get; }
    public bool IsNew => !IsEditing;
    public string Title => IsEditing ? "Editar camiseta" : "Nueva camiseta";

    public DesignerAction Action { get; private set; }
    public event Action? CloseRequested;
    public event Action? PreviewChanged;

    public RenderItem? Preview { get; private set; }
    public IReadOnlyList<RenderItem> Body { get; private set; } = [];

    // The body the current shirt was cut from, for creating it with exactly what the preview shows.
    public SkinnedBody? Torso { get; private set; }

    public ObservableCollection<GraphicViewModel> Graphics { get; } = [];
    public ObservableCollection<ExtraColourViewModel> ExtraColours { get; } = [];
    public ObservableCollection<ShirtTemplate> Templates { get; } = [];

    public IReadOnlyList<uint> MoreColours => ExtraColours.Select(c => Rgb(c.Color)).ToList();

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string? PreviewError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(ReshapeCommand), nameof(AddColoursCommand))]
    public partial bool HasBody { get; private set; } = true;

    [ObservableProperty]
    public partial GenderOption Gender { get; set; }

    [ObservableProperty]
    public partial ShirtStyleOption Style { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleeveLabel))]
    public partial double Sleeve { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LengthLabel))]
    public partial double Length { get; set; }

    [ObservableProperty]
    public partial double Fit { get; set; }

    [ObservableProperty]
    public partial Choice<Neckline> Neckline { get; set; }

    [ObservableProperty]
    public partial double NeckDepth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNeckline), nameof(HasButtons))]
    public partial Choice<Collar> Collar { get; set; }

    [ObservableProperty]
    public partial Choice<HemShape> Hem { get; set; }

    [ObservableProperty]
    public partial bool RolledCuffs { get; set; }

    [ObservableProperty]
    public partial bool Pocket { get; set; }

    [ObservableProperty]
    public partial Choice<ShirtLayer> Layer { get; set; }

    [ObservableProperty]
    public partial Color BodyColor { get; set; }

    [ObservableProperty]
    public partial Color SleevesColor { get; set; }

    [ObservableProperty]
    public partial Color TrimColor { get; set; }

    [ObservableProperty]
    public partial Color StitchColor { get; set; }

    [ObservableProperty]
    public partial Color ButtonsColor { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPattern), nameof(HasSecondPatternColour), nameof(HasPatternScale))]
    public partial Choice<ShirtPatternKind> Pattern { get; set; }

    [ObservableProperty]
    public partial Color PatternColor { get; set; }

    [ObservableProperty]
    public partial Color PatternColor2 { get; set; }

    [ObservableProperty]
    public partial double PatternScale { get; set; }

    [ObservableProperty]
    public partial Choice<Fabric> Fabric { get; set; }

    [ObservableProperty]
    public partial double Wrinkles { get; set; }

    [ObservableProperty]
    public partial double Dirt { get; set; }

    [ObservableProperty]
    public partial double Fade { get; set; }

    [ObservableProperty]
    public partial double Distress { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseTemplateCommand), nameof(DeleteTemplateCommand))]
    public partial ShirtTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand))]
    public partial string TemplateName { get; set; } = "";

    [ObservableProperty]
    public partial string? TemplateNote { get; set; }

    public bool HasNeckline => Collar.Value == Core.Modeling.Collar.None;
    public bool HasButtons => Collar.Value is Core.Modeling.Collar.Polo or Core.Modeling.Collar.Shirt;
    public bool HasPattern => Pattern.Value != ShirtPatternKind.None;
    public bool HasSecondPatternColour => Pattern.Value is ShirtPatternKind.Camo or ShirtPatternKind.TieDye;
    public bool HasPatternScale => Pattern.Value is ShirtPatternKind.Stripes or ShirtPatternKind.VerticalStripes or ShirtPatternKind.Checks
        or ShirtPatternKind.Camo or ShirtPatternKind.TieDye;

    public string SleeveLabel => Sleeve switch
    {
        < 0.05 => "sin manga",
        < 0.4 => "corta",
        < 0.6 => "por el codo",
        < 0.9 => "tres cuartos",
        _ => "larga",
    };

    public string LengthLabel => Length switch
    {
        < -0.5 => $"{-Length:0} cm sobre la cintura",
        > 0.5 => $"{Length:0} cm bajo la cintura",
        _ => "por la cintura",
    };

    public ShirtLook Look => new()
    {
        Body = Rgb(BodyColor),
        Sleeves = Rgb(SleevesColor),
        Trim = Rgb(TrimColor),
        Stitch = Rgb(StitchColor),
        Buttons = Rgb(ButtonsColor),
        Pattern = new ShirtPattern { Kind = Pattern.Value, Colour = Rgb(PatternColor), Colour2 = Rgb(PatternColor2), Scale = (float)PatternScale },
        Fabric = Fabric.Value,
        Wrinkles = (float)Wrinkles,
        Dirt = (float)Dirt,
        Fade = (float)Fade,
        Distress = (float)Distress,
        Graphics = Graphics.Select(g => g.Graphic).ToList(),
    };

    public ShirtDesign Design => new()
    {
        Style = Style.Value,
        Sleeve = (float)Sleeve,
        Length = (float)Length,
        Fit = (float)Fit,
        Neckline = Neckline.Value,
        NeckDepth = (float)NeckDepth,
        Collar = Collar.Value,
        Hem = Hem.Value,
        RolledCuffs = RolledCuffs,
        Pocket = Pocket,
        Layer = IsNew ? Layer.Value : _layer,
        Look = Look,
    };

    [RelayCommand(CanExecute = nameof(HasBody))]
    private void Create() => Finish(DesignerAction.Create);

    [RelayCommand(CanExecute = nameof(HasBody))]
    private void Reshape() => Finish(DesignerAction.Reshape);

    [RelayCommand(CanExecute = nameof(HasBody))]
    private void AddColours() => Finish(DesignerAction.AddColours);

    [RelayCommand]
    private void ResetShape() => Load(Design.WithStyle(Style.Value));

    [RelayCommand]
    private void SleevePreset(SleevePresetOption preset) => Sleeve = preset.Sleeve;

    // The same colour everywhere, the quickest start for a plain shirt.
    [RelayCommand]
    private void MatchColours()
    {
        _loading = true;
        SleevesColor = BodyColor;
        TrimColor = BodyColor;
        _loading = false;
        SchedulePreview();
    }

    [RelayCommand]
    private async Task AddImage()
    {
        if (await _services.PickImage() is not { } path) return;
        Graphics.Add(new GraphicViewModel(new ShirtGraphic { Zone = GraphicZone.ChestCentre, Image = path, Size = 14 }));
    }

    [RelayCommand]
    private void AddText() =>
        Graphics.Add(new GraphicViewModel(new ShirtGraphic { Zone = GraphicZone.Back, Text = "10", Size = 18, TextColour = Rgb(TrimColor) }));

    [RelayCommand]
    private void RemoveGraphic(GraphicViewModel graphic) => Graphics.Remove(graphic);

    // A new colour starts as the current one turned round the colour wheel, so it's visibly different.
    [RelayCommand]
    private void AddExtraColour()
    {
        var hsv = (ExtraColours.LastOrDefault()?.Color ?? BodyColor).ToHsv();
        ExtraColours.Add(new ExtraColourViewModel(new HsvColor(1, (hsv.H + 60) % 360, Math.Max(0.35, hsv.S), Math.Max(0.35, hsv.V)).ToRgb()));
    }

    [RelayCommand]
    private void RemoveExtraColour(ExtraColourViewModel colour) => ExtraColours.Remove(colour);

    private bool HasTemplate => SelectedTemplate is not null;
    private bool CanSaveTemplate => !string.IsNullOrWhiteSpace(TemplateName);

    // Takes shape and look from the template; an existing garment keeps its gender, name and layer.
    [RelayCommand(CanExecute = nameof(HasTemplate))]
    private void UseTemplate()
    {
        Load(SelectedTemplate!.Design);
        TemplateNote = $"Plantilla «{SelectedTemplate.Name}» cargada.";
    }

    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private void SaveTemplate()
    {
        try
        {
            var saved = _services.Templates.Save(TemplateName, Design);
            RefreshTemplates();
            SelectedTemplate = Templates.FirstOrDefault(t => t.Name == saved.Name);
            TemplateNote = $"Guardada como «{saved.Name}». La tendrás en Mis plantillas al crear otra.";
            TemplateName = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TemplateNote = $"No se pudo guardar la plantilla: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasTemplate))]
    private void DeleteTemplate()
    {
        var name = SelectedTemplate!.Name;
        try
        {
            _services.Templates.Delete(name);
            RefreshTemplates();
            TemplateNote = $"Plantilla «{name}» borrada.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TemplateNote = $"No se pudo borrar la plantilla: {ex.Message}";
        }
    }

    private void RefreshTemplates()
    {
        Templates.Clear();
        foreach (var template in _services.Templates.List()) Templates.Add(template);
        SelectedTemplate = Templates.FirstOrDefault();
    }

    // Picking a style loads its typical shape; the look stays.
    partial void OnStyleChanged(ShirtStyleOption oldValue, ShirtStyleOption newValue)
    {
        if (IsNew && Name == (oldValue?.GarmentName ?? "Camiseta")) Name = newValue.GarmentName;
        if (!_loading) Load(Design.WithStyle(newValue.Value));
    }

    private void Finish(DesignerAction action)
    {
        Action = action;
        CloseRequested?.Invoke();
    }

    private void Load(ShirtDesign d)
    {
        _loading = true;
        _layer = d.Layer;
        Style = ShirtStyleOption.For(d.Style);
        Sleeve = d.Sleeve;
        Length = d.Length;
        Fit = d.Fit;
        Neckline = ShirtChoices.For(ShirtChoices.Necklines, d.Neckline);
        NeckDepth = d.NeckDepth;
        Collar = ShirtChoices.For(ShirtChoices.Collars, d.Collar);
        Hem = ShirtChoices.For(ShirtChoices.Hems, d.Hem);
        RolledCuffs = d.RolledCuffs;
        Pocket = d.Pocket;
        var look = d.Look;
        BodyColor = Colour(look.Body);
        SleevesColor = Colour(look.Sleeves);
        TrimColor = Colour(look.Trim);
        StitchColor = Colour(look.Stitch);
        ButtonsColor = Colour(look.Buttons);
        Pattern = ShirtChoices.For(ShirtChoices.Patterns, look.Pattern.Kind);
        PatternColor = Colour(look.Pattern.Colour);
        PatternColor2 = Colour(look.Pattern.Colour2);
        PatternScale = look.Pattern.Scale;
        Fabric = ShirtChoices.For(ShirtChoices.Fabrics, look.Fabric);
        Wrinkles = look.Wrinkles;
        Dirt = look.Dirt;
        Fade = look.Fade;
        Distress = look.Distress;
        foreach (var graphic in Graphics) graphic.PropertyChanged -= OnGraphicChanged;
        Graphics.Clear();
        foreach (var graphic in look.Graphics) Graphics.Add(new GraphicViewModel(graphic));
        _loading = false;
        SchedulePreview();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName is nameof(Name) or nameof(PreviewError) or nameof(HasBody) or nameof(SleeveLabel) or nameof(LengthLabel)
            or nameof(SelectedTemplate) or nameof(TemplateName) or nameof(TemplateNote)) return;
        SchedulePreview();
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<INotifyPropertyChanged>() ?? []) item.PropertyChanged += OnGraphicChanged;
        foreach (var item in e.OldItems?.OfType<INotifyPropertyChanged>() ?? []) item.PropertyChanged -= OnGraphicChanged;
        if (!_loading && sender == Graphics) SchedulePreview();
    }

    private void OnGraphicChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading && sender is GraphicViewModel) SchedulePreview();
    }

    // Slider drags fire many changes; only the last one within a short pause is built.
    private void SchedulePreview()
    {
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        var design = Design;
        var gender = Gender.Value;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80, cts.Token);
                var stage = _stages.GetOrAdd(gender, _services.StageFor);
                if (stage.Torso is not { } torso)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        HasBody = false;
                        PreviewError = "Para crear ropa hace falta el personaje exportado del juego: elige su carpeta en la ventana principal (botón de carpeta junto a «Personaje»).";
                    });
                    return;
                }
                var shirt = ShirtModel.Build(design, torso);
                cts.Token.ThrowIfCancellationRequested();
                var texture = ShirtTextures.Diffuse(design, shirt, PreviewTextureSize, path => _images.GetOrAdd(path, _services.LoadImage));
                var item = new RenderItem([shirt.Mesh], texture);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    Preview = item;
                    Body = stage.Body;
                    Torso = torso;
                    HasBody = true;
                    PreviewError = null;
                    PreviewChanged?.Invoke();
                });
            }
            catch (OperationCanceledException)
            {
                // A newer change replaced this preview.
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => PreviewError = $"No se pudo mostrar la vista previa: {ex.Message}");
            }
        });
    }

    private static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    private static Color Colour(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
