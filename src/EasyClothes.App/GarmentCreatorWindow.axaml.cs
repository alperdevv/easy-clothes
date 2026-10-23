using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyClothes.App.ViewModels;

namespace EasyClothes.App;

public partial class GarmentCreatorWindow : Window
{
    private IGarmentDesigner? _designer;
    private bool _framed;

    public GarmentCreatorWindow() : this(null!)
    {
    }

    public GarmentCreatorWindow(GarmentCreatorViewModel creator)
    {
        InitializeComponent();
        DataContext = creator;
        if (creator is null) return;

        Attach(creator.Current);
        creator.CurrentChanged += () => Attach(creator.Current);
    }

    // A different kind of garment is framed afresh; reshaping the same one keeps the camera.
    private void Attach(IGarmentDesigner designer)
    {
        if (_designer is not null)
        {
            _designer.PreviewChanged -= OnPreviewChanged;
            _designer.CloseRequested -= Close;
        }
        _designer = designer;
        _framed = false;
        designer.PreviewChanged += OnPreviewChanged;
        designer.CloseRequested += Close;
        OnPreviewChanged();
    }

    private void OnPreviewChanged()
    {
        if (_designer?.Preview is not { } preview) return;
        Viewport.ShowScene([preview], _designer.Body, keepCamera: _framed);
        _framed = true;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
