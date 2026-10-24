using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.App.Services;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public sealed record PropSlotOption(string Label, PropSlot Value)
{
    public static readonly IReadOnlyList<PropSlotOption> All =
    [
        new("Gorra, gorro o sombrero", PropSlot.Hat),
        new("Gafas", PropSlot.Glasses),
        new("Pendientes", PropSlot.Ears),
    ];

    // Wrist props can be placed but are not offered for new models.
    public static PropSlotOption For(PropSlot slot) => All.FirstOrDefault(o => o.Value == slot) ?? new(slot == PropSlot.Watch ? "Reloj" : "Pulsera", slot);
}

// Places a prop on the body with a live preview: the user's own .obj (new models also pick their slot,
// gender, name and texture) or a prop already in the project, which can only be moved.
public sealed partial class ModelPlacementViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Func<PedGender, PropSlot, CapStage> _stageFor;
    private readonly MeshData _model;
    private readonly Func<MeshData, PropPlacement, PropSlot, MeshData> _place;
    private readonly Func<MeshData, PropPlacement>? _fit;
    private RgbaImage? _texture;
    private CancellationTokenSource? _pending;

    public ModelPlacementViewModel(IDialogService dialogs, ObjModel model, string sourceName, PropPlacement placement, PedGender gender, PropSlot slot,
        bool editing, Func<PedGender, PropSlot, CapStage> stageFor, RgbaImage? texture = null)
        : this(dialogs, model.Mesh, Path.GetFileNameWithoutExtension(sourceName), editing ? "Colocar modelo importado" : "Importar modelo 3D",
            placement, gender, slot, editing, stageFor, (mesh, p, _) => ObjProp.Place(mesh, p), ObjProp.Fit, texture)
    {
        Notes = string.Join(" ", model.Warnings);
        if (!editing && model.TexturePath is { } fromMaterial) TexturePath = fromMaterial;
    }

    public ModelPlacementViewModel(IDialogService dialogs, MeshData model, string name, string title, PropPlacement placement, PedGender gender, PropSlot slot,
        bool editing, Func<PedGender, PropSlot, CapStage> stageFor, Func<MeshData, PropPlacement, PropSlot, MeshData> place,
        Func<MeshData, PropPlacement>? fit, RgbaImage? texture)
    {
        _dialogs = dialogs;
        _stageFor = stageFor;
        _model = model;
        _place = place;
        _fit = fit;
        _texture = texture;
        IsEditing = editing;
        Title = title;
        Name = name;
        Gender = GenderOption.For(gender);
        Slot = PropSlotOption.For(slot);
        Summary = $"{model.VertexCount:N0} vértices · {model.TriangleCount:N0} triángulos";
        Load(placement);
        PropertyChanged += OnChanged;
        SchedulePreview();
    }

    public bool IsEditing { get; }
    public bool IsNew => !IsEditing;
    public string Title { get; }
    public string Summary { get; }

    // Fitting and the Blender reference head only make sense for the user's own .obj.
    public bool IsObj => _fit is not null;

    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;
    public event Action? PreviewChanged;

    public RenderItem? Preview { get; private set; }
    public IReadOnlyList<RenderItem> Body { get; private set; } = [];

    // Set by the app when the exported body folder is known, to save the reference head.
    public Func<PedGender, string, Task<string?>>? ExportReferenceHead { get; init; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial GenderOption Gender { get; set; }

    [ObservableProperty]
    public partial PropSlotOption Slot { get; set; }

    [ObservableProperty]
    public partial string? TexturePath { get; set; }

    [ObservableProperty]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    public partial double Up { get; set; }

    [ObservableProperty]
    public partial double Forward { get; set; }

    [ObservableProperty]
    public partial double Left { get; set; }

    [ObservableProperty]
    public partial double Turn { get; set; }

    [ObservableProperty]
    public partial double Nod { get; set; }

    [ObservableProperty]
    public partial double Tilt { get; set; }

    [ObservableProperty]
    public partial double Scale { get; set; }

    public string TextureLabel => TexturePath is null ? "Sin textura (gris, para pintarla luego)" : Path.GetFileName(TexturePath);

    public PropPlacement Placement => new()
    {
        Up = (float)Up,
        Forward = (float)Forward,
        Left = (float)Left,
        Turn = (float)Turn,
        Nod = (float)Nod,
        Tilt = (float)Tilt,
        Scale = (float)Scale,
    };

    [RelayCommand]
    private void Fit()
    {
        if (_fit is not null) Load(_fit(_model));
    }

    [RelayCommand]
    private void Reset() => Load(new PropPlacement());

    [RelayCommand]
    private async Task ChooseTextureAsync()
    {
        var files = await _dialogs.PickFilesAsync("Textura del modelo", "Imágenes", ["*.png"], multiple: false);
        if (files.Count == 0) return;
        try
        {
            _texture = ImageLoader.Load(files[0]);
            TexturePath = files[0];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Notes = $"No se pudo leer la imagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveReferenceHeadAsync()
    {
        if (ExportReferenceHead is null)
        {
            Notes = "Configura la carpeta del personaje (Mostrar cuerpo) para poder exportar la cabeza de referencia.";
            return;
        }
        Notes = await ExportReferenceHead(Gender.Value, Name);
    }

    [RelayCommand]
    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    partial void OnTexturePathChanged(string? value) => OnPropertyChanged(nameof(TextureLabel));

    private bool _loading;

    private void Load(PropPlacement p)
    {
        _loading = true;
        (Up, Forward, Left, Turn, Nod, Tilt, Scale) = (p.Up, p.Forward, p.Left, p.Turn, p.Nod, p.Tilt, p.Scale);
        _loading = false;
        SchedulePreview();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName is nameof(Name) or nameof(Notes) or nameof(TextureLabel)) return;
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        var (placement, gender, slot, texture) = (Placement, Gender.Value, Slot.Value, _texture);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60, cts.Token);
                var stage = _stageFor(gender, slot);
                var placed = _place(_model, placement, slot);
                var item = new RenderItem([stage.Anchor is { } anchor ? placed.Transformed(anchor) : placed], texture);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    (Preview, Body) = (item, stage.Body);
                    PreviewChanged?.Invoke();
                });
            }
            catch (OperationCanceledException)
            {
                // A newer change replaced this preview.
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Notes = $"No se pudo mostrar la vista previa: {ex.Message}");
            }
        });
    }
}
