using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyClothes.App.ViewModels;

namespace EasyClothes.App;

public partial class ModelPlacementWindow : Window
{
    public ModelPlacementWindow() : this(null!)
    {
    }

    public ModelPlacementWindow(ModelPlacementViewModel placement)
    {
        InitializeComponent();
        DataContext = placement;
        if (placement is null) return;

        placement.PreviewChanged += () =>
        {
            if (placement.Preview is { } preview) Viewport.ShowScene([preview], placement.Body, keepCamera: true);
        };
        placement.CloseRequested += Close;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
