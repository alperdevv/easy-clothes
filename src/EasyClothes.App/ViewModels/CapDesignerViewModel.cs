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
    public static readonly IReadOnlyList<CapStyleOption> All =
    [
        new("Béisbol", CapStyle.Baseball),
        new("Snapback", CapStyle.Snapback),
        new("Dad hat", CapStyle.DadHat),
        new("Trucker (rejilla detrás)", CapStyle.Trucker),
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

    public CapDesignerViewModel(CapDesign design, PedGender gender, bool editing, Func<PedGender, CapStage> stageFor)
    {
        _stageFor = stageFor;
        IsEditing = editing;
        Gender = GenderOption.For(gender);
        PropertyChanged += OnChanged;
        Load(design.Clamped());
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
    partial void OnStyleChanged(CapStyleOption value)
    {
        if (!_loading) Load(Design.WithStyle(value.Value));
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
        if (_loading || e.PropertyName is nameof(Name) or nameof(PreviewError)) return;
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
