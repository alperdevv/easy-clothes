using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.Core.Model;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public enum DesignerAction
{
    None,
    Create,
    Reshape,
    AddColours,
}

// What every garment designer offers the creator window: a name and gender for new garments, a live
// preview and the three ways of finishing (create, take the new shape, or add the colours as a texture).
public interface IGarmentDesigner : INotifyPropertyChanged
{
    string Name { get; set; }
    GenderOption Gender { get; set; }
    bool IsNew { get; }
    bool IsEditing { get; }
    string Title { get; }
    RenderItem? Preview { get; }
    IReadOnlyList<RenderItem> Body { get; }
    string? PreviewError { get; }
    DesignerAction Action { get; }
    IRelayCommand CreateCommand { get; }
    IRelayCommand ReshapeCommand { get; }
    IRelayCommand AddColoursCommand { get; }
    event Action? PreviewChanged;
    event Action? CloseRequested;
}

public enum GarmentKind
{
    Cap,
    Shirt,
    Hoodie,
    Jacket,
    Trousers,
    Shoes,
}

public sealed record GarmentTypeOption(string Label, string Hint, GarmentKind Kind, string Icon, bool Available)
{
    public bool ComingSoon => !Available;

    public static readonly IReadOnlyList<GarmentTypeOption> All =
    [
        new("Camiseta", "Manga corta, larga, sin mangas o crop", GarmentKind.Shirt, "Icon.Shirt", true),
        new("Gorra y gorro", "Gorra, bucket, gorro de lana o visera", GarmentKind.Cap, "Icon.HardHat", true),
        new("Sudadera", "Con capucha y puños", GarmentKind.Hoodie, "Icon.Shirt", false),
        new("Chaqueta", "Abierta o con cremallera", GarmentKind.Jacket, "Icon.Shirt", false),
        new("Pantalón", "Largo o corto", GarmentKind.Trousers, "Icon.Pants", false),
        new("Zapatos", "Zapatillas o botas", GarmentKind.Shoes, "Icon.Footprints", false),
    ];
}

// The creator window's state: which kind of garment is being designed and its designer. A new garment
// can switch kind (keeping name and gender when the user typed them); an existing one keeps its kind.
public sealed partial class GarmentCreatorViewModel : ObservableObject
{
    private readonly Func<GarmentKind, PedGender, IGarmentDesigner>? _designerFor;

    public GarmentCreatorViewModel(IGarmentDesigner designer)
    {
        Current = designer;
        SelectedType = GarmentTypeOption.All.First(t => t.Kind == KindOf(designer));
    }

    public GarmentCreatorViewModel(GarmentKind kind, Func<GarmentKind, PedGender, IGarmentDesigner> designerFor, PedGender gender)
    {
        _designerFor = designerFor;
        Current = designerFor(kind, gender);
        SelectedType = GarmentTypeOption.All.First(t => t.Kind == kind);
    }

    public IReadOnlyList<GarmentTypeOption> Types => GarmentTypeOption.All;
    public bool ShowsTypes => _designerFor is not null;
    public string Title => ShowsTypes ? "Creador de prendas" : Current.Title;

    public event Action? CurrentChanged;

    [ObservableProperty]
    public partial IGarmentDesigner Current { get; private set; }

    [ObservableProperty]
    public partial GarmentTypeOption SelectedType { get; set; }

    partial void OnSelectedTypeChanged(GarmentTypeOption oldValue, GarmentTypeOption newValue)
    {
        if (_designerFor is null || oldValue is null || !newValue.Available || newValue.Kind == KindOf(Current)) return;
        var previous = Current;
        var next = _designerFor(newValue.Kind, previous.Gender.Value);
        if (previous.Name != DefaultName(KindOf(previous))) next.Name = previous.Name;
        Current = next;
        CurrentChanged?.Invoke();
    }

    public static GarmentKind KindOf(IGarmentDesigner designer) => designer switch
    {
        ShirtDesignerViewModel => GarmentKind.Shirt,
        _ => GarmentKind.Cap,
    };

    public static string DefaultName(GarmentKind kind) => kind == GarmentKind.Shirt ? ShirtStyleOption.All[0].GarmentName : CapStyleOption.All[0].GarmentName;
}
