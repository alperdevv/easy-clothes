using System.ComponentModel;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public sealed record CapStyleOption(string Label, CapStyle Value)
{
    public string GarmentName => Value switch
    {
        CapStyle.Snapback => "Snapback",
        CapStyle.DadHat => "Dad hat",
        CapStyle.Trucker => "Trucker",
        CapStyle.Bucket => "Bucket",
        CapStyle.Beanie => "Gorro de lana",
        CapStyle.SunVisor => "Visera",
        _ => "Gorra",
    };

    public static readonly IReadOnlyList<CapStyleOption> All =
    [
        new("Béisbol", CapStyle.Baseball),
        new("Snapback", CapStyle.Snapback),
        new("Dad hat", CapStyle.DadHat),
        new("Trucker (rejilla detrás)", CapStyle.Trucker),
        new("Bucket (gorro de pescador)", CapStyle.Bucket),
        new("Gorro de lana", CapStyle.Beanie),
        new("Visera sola", CapStyle.SunVisor),
    ];

    public static CapStyleOption For(CapStyle style) => All.First(o => o.Value == style);
}

// Quick starting points for the visor; the sliders fine-tune from there.
public sealed record VisorPresetOption(string Label, Func<CapDesign, CapDesign> Apply)
{
    public static readonly IReadOnlyList<VisorPresetOption> All =
    [
        new("Curva", d => d with { VisorLength = 7, VisorCurve = 0.7f, VisorAngle = 8, VisorWidth = 0.5f, VisorSquare = 0 }),
        new("Plana", d => d with { VisorLength = 7.5f, VisorCurve = 0.05f, VisorAngle = 3, VisorWidth = 0.6f, VisorSquare = 0.6f }),
        new("Corta", d => d with { VisorLength = 4.5f, VisorCurve = 0.5f, VisorAngle = 10, VisorWidth = 0.45f, VisorSquare = 0.1f }),
        new("Larga", d => d with { VisorLength = 9.5f, VisorCurve = 0.6f, VisorAngle = 10, VisorWidth = 0.55f, VisorSquare = 0.1f }),
        new("Ancha", d => d with { VisorLength = 7, VisorCurve = 0.4f, VisorAngle = 6, VisorWidth = 1, VisorSquare = 0.4f }),
    ];
}

public enum CapDesignerAction
{
    None,
    Create,
    Reshape,
    AddColours,
}

// The mannequin and the head bone placing the cap on it, for one gender.
public sealed record CapStage(IReadOnlyList<RenderItem> Body, Matrix4x4? Anchor);

// Edits a cap's shape and colours with a live preview. A new cap is created from it; an existing one
// either takes the new shape (its textures keep fitting) or gets the colours as another texture.
public sealed partial class CapDesignerViewModel : ObservableObject
{
    private const int PreviewTextureSize = 512;

    private readonly Func<PedGender, CapStage> _stageFor;
    private CancellationTokenSource? _pending;
    private bool _loading;
    private readonly CapKind _kind;
    private readonly CapTemplates _templates;

    public CapDesignerViewModel(CapDesign design, PedGender gender, bool editing, Func<PedGender, CapStage> stageFor, CapTemplates templates)
    {
        _stageFor = stageFor;
        _templates = templates;
        IsEditing = editing;
        _kind = design.Kind;
        Gender = GenderOption.For(gender);
        PropertyChanged += OnChanged;
        Load(design.Clamped());
        RefreshTemplates();
    }

    public bool IsEditing { get; }
    public bool IsNew => !IsEditing;
    public string Title => IsEditing ? "Editar gorra" : "Nueva gorra";

    public CapDesignerAction Action { get; private set; }
    public event Action? CloseRequested;
    public event Action? PreviewChanged;

    public RenderItem? Preview { get; private set; }
    public IReadOnlyList<RenderItem> Body { get; private set; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = "Gorra";

    [ObservableProperty]
    public partial string? PreviewError { get; set; }

    public System.Collections.ObjectModel.ObservableCollection<CapTemplate> Templates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseTemplateCommand), nameof(DeleteTemplateCommand))]
    public partial CapTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand))]
    public partial string TemplateName { get; set; } = "";

    [ObservableProperty]
    public partial string? TemplateNote { get; set; }

    [ObservableProperty]
    public partial GenderOption Gender { get; set; }

    [ObservableProperty]
    public partial double Size { get; set; }

    [ObservableProperty]
    public partial double CrownHeight { get; set; }

    [ObservableProperty]
    public partial double CrownShape { get; set; }

    [ObservableProperty]
    public partial double VisorLength { get; set; }

    [ObservableProperty]
    public partial double VisorCurve { get; set; }

    [ObservableProperty]
    public partial double VisorAngle { get; set; }

    [ObservableProperty]
    public partial CapStyleOption Style { get; set; }

    [ObservableProperty]
    public partial double VisorWidth { get; set; }

    [ObservableProperty]
    public partial double VisorSquare { get; set; }

    [ObservableProperty]
    public partial double Rotation { get; set; }

    [ObservableProperty]
    public partial double BrimWidth { get; set; }

    [ObservableProperty]
    public partial double BrimDroop { get; set; }

    [ObservableProperty]
    public partial double CuffHeight { get; set; }

    [ObservableProperty]
    public partial bool Pompom { get; set; }

    // An existing garment keeps its texture layout, so it can only switch between styles that share it.
    public IReadOnlyList<CapStyleOption> StyleOptions => IsEditing
        ? CapStyleOption.All.Where(o => new CapDesign { Style = o.Value }.Kind == _kind).ToList()
        : CapStyleOption.All;

    private CapKind Kind => Design.Kind;
    public bool HasVisor => Kind is CapKind.Cap or CapKind.SunVisor;
    public bool HasCrown => Kind != CapKind.SunVisor;
    public bool IsCap => Kind == CapKind.Cap;
    public bool IsBucket => Kind == CapKind.Bucket;
    public bool IsBeanie => Kind == CapKind.Beanie;
    public bool HasFrontColour => Kind != CapKind.Bucket;
    public bool HasButtonColour => Kind is CapKind.Cap or CapKind.Beanie;
    public bool HasStitchColour => Kind != CapKind.Beanie;
    public double MinCrownHeight => Design.MinCrownHeight;

    public string CrownLabel => Kind switch { CapKind.Beanie => "Gorro", CapKind.SunVisor => "Banda", _ => "Copa" };
    public string FrontLabel => Kind switch { CapKind.Beanie => "Doblez", CapKind.SunVisor => "Banda (delante)", _ => "Paneles delanteros" };
    public string VisorLabel => Kind == CapKind.Bucket ? "Ala" : "Visera";
    public string UnderVisorLabel => Kind == CapKind.Bucket ? "Debajo del ala" : "Debajo de la visera";
    public string ButtonLabel => Kind == CapKind.Beanie ? "Pompón" : "Botón";

    [ObservableProperty]
    public partial bool Button { get; set; }

    [ObservableProperty]
    public partial Color CrownColor { get; set; }

    [ObservableProperty]
    public partial Color FrontColor { get; set; }

    [ObservableProperty]
    public partial Color VisorColor { get; set; }

    [ObservableProperty]
    public partial Color UnderVisorColor { get; set; }

    [ObservableProperty]
    public partial Color ButtonColor { get; set; }

    [ObservableProperty]
    public partial Color StitchColor { get; set; }

    public CapDesign Design => new()
    {
        Size = (float)Size,
        CrownHeight = (float)CrownHeight,
        CrownShape = (float)CrownShape,
        VisorLength = (float)VisorLength,
        VisorCurve = (float)VisorCurve,
        VisorAngle = (float)VisorAngle,
        VisorWidth = (float)VisorWidth,
        VisorSquare = (float)VisorSquare,
        Rotation = (float)Rotation,
        Style = Style.Value,
        BrimWidth = (float)BrimWidth,
        BrimDroop = (float)BrimDroop,
        CuffHeight = (float)CuffHeight,
        Pompom = Pompom,
        Button = Button,
        Colours = new CapColours
        {
            Crown = Rgb(CrownColor),
            Front = Rgb(FrontColor),
            Visor = Rgb(VisorColor),
            UnderVisor = Rgb(UnderVisorColor),
            Button = Rgb(ButtonColor),
            Stitch = Rgb(StitchColor),
        },
    };

    [RelayCommand]
    private void Create() => Finish(CapDesignerAction.Create);

    [RelayCommand]
    private void Reshape() => Finish(CapDesignerAction.Reshape);

    [RelayCommand]
    private void AddColours() => Finish(CapDesignerAction.AddColours);

    [RelayCommand]
    private void ResetShape() => Load(Design.WithStyle(Style.Value));

    // 0 front, 90 the wearer's left, -90 right, 180 back.
    [RelayCommand]
    private void Turn(string degrees) => Rotation = double.Parse(degrees, System.Globalization.CultureInfo.InvariantCulture);

    [RelayCommand]
    private void VisorPreset(VisorPresetOption preset) => Load(preset.Apply(Design));

    // Picking a style loads its typical shape; the user's rotation and colours stay.
    partial void OnStyleChanged(CapStyleOption oldValue, CapStyleOption newValue)
    {
        if (IsNew && Name == (oldValue?.GarmentName ?? "Gorra")) Name = newValue.GarmentName;
        if (!_loading) Load(Design.WithStyle(newValue.Value));
        foreach (var name in (string[])[nameof(HasVisor), nameof(HasCrown), nameof(IsCap), nameof(IsBucket), nameof(IsBeanie), nameof(HasFrontColour),
                     nameof(HasButtonColour), nameof(HasStitchColour), nameof(MinCrownHeight), nameof(CrownLabel), nameof(FrontLabel), nameof(VisorLabel),
                     nameof(UnderVisorLabel), nameof(ButtonLabel)])
            OnPropertyChanged(name);
    }

    private bool HasTemplate => SelectedTemplate is not null;
    private bool CanSaveTemplate => !string.IsNullOrWhiteSpace(TemplateName);

    // Takes shape and colours from the template; an existing garment keeps its gender and name.
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
            var saved = _templates.Save(TemplateName, Design);
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
            _templates.Delete(name);
            RefreshTemplates();
            TemplateNote = $"Plantilla «{name}» borrada.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TemplateNote = $"No se pudo borrar la plantilla: {ex.Message}";
        }
    }

    // While editing, only templates with the same texture layout fit the garment.
    private void RefreshTemplates()
    {
        Templates.Clear();
        foreach (var template in _templates.List().Where(t => IsNew || t.Design.Kind == _kind))
            Templates.Add(template);
        SelectedTemplate = Templates.FirstOrDefault();
    }

    private void Finish(CapDesignerAction action)
    {
        Action = action;
        CloseRequested?.Invoke();
    }

    private void Load(CapDesign d)
    {
        _loading = true;
        Style = CapStyleOption.For(d.Style);
        VisorWidth = d.VisorWidth;
        VisorSquare = d.VisorSquare;
        Rotation = d.Rotation;
        BrimWidth = d.BrimWidth;
        BrimDroop = d.BrimDroop;
        CuffHeight = d.CuffHeight;
        Pompom = d.Pompom;
        Size = d.Size;
        CrownHeight = d.CrownHeight;
        CrownShape = d.CrownShape;
        VisorLength = d.VisorLength;
        VisorCurve = d.VisorCurve;
        VisorAngle = d.VisorAngle;
        Button = d.Button;
        CrownColor = Colour(d.Colours.Crown);
        FrontColor = Colour(d.Colours.Front);
        VisorColor = Colour(d.Colours.Visor);
        UnderVisorColor = Colour(d.Colours.UnderVisor);
        ButtonColor = Colour(d.Colours.Button);
        StitchColor = Colour(d.Colours.Stitch);
        _loading = false;
        SchedulePreview();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName is nameof(Name) or nameof(PreviewError) or nameof(SelectedTemplate) or nameof(TemplateName) or nameof(TemplateNote)) return;
        SchedulePreview();
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
                var stage = _stageFor(gender);
                var mesh = CapModel.Build(design);
                var item = new RenderItem([stage.Anchor is { } anchor ? mesh.Transformed(anchor) : mesh], CapTextures.Diffuse(design, PreviewTextureSize));
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    Preview = item;
                    Body = stage.Body;
                    PreviewError = null;
                    PreviewChanged?.Invoke();
                });
            }
            catch (OperationCanceledException)
            {
                // A newer change replaced this preview.
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => PreviewError = $"No se pudo mostrar la vista previa: {ex.Message}");
            }
        });
    }

    private static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    private static Color Colour(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
