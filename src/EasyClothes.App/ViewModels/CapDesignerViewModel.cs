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

    public CapDesignerViewModel(CapDesign design, PedGender gender, bool editing, Func<PedGender, CapStage> stageFor)
    {
        _stageFor = stageFor;
        IsEditing = editing;
        Gender = GenderOption.For(gender);
        Load(design.Clamped());
        PropertyChanged += OnChanged;
        SchedulePreview();
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
    public partial bool Backwards { get; set; }

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
        Backwards = Backwards,
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
    private void ResetShape()
    {
        var colours = Design.Colours;
        Load(new CapDesign { Colours = colours });
    }

    private void Finish(CapDesignerAction action)
    {
        Action = action;
        CloseRequested?.Invoke();
    }

    private void Load(CapDesign d)
    {
        Size = d.Size;
        CrownHeight = d.CrownHeight;
        CrownShape = d.CrownShape;
        VisorLength = d.VisorLength;
        VisorCurve = d.VisorCurve;
        VisorAngle = d.VisorAngle;
        Backwards = d.Backwards;
        Button = d.Button;
        CrownColor = Colour(d.Colours.Crown);
        FrontColor = Colour(d.Colours.Front);
        VisorColor = Colour(d.Colours.Visor);
        UnderVisorColor = Colour(d.Colours.UnderVisor);
        ButtonColor = Colour(d.Colours.Button);
        StitchColor = Colour(d.Colours.Stitch);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Name) or nameof(PreviewError)) return;
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
