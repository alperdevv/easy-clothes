using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyClothes.App.ViewModels;

namespace EasyClothes.App;

public partial class CapDesignerWindow : Window
{
    public CapDesignerWindow() : this(null!)
    {
    }

    public CapDesignerWindow(CapDesignerViewModel designer)
    {
        InitializeComponent();
        DataContext = designer;
        if (designer is null) return;

        designer.PreviewChanged += () =>
        {
            if (designer.Preview is { } preview) Viewport.ShowScene([preview], designer.Body, keepCamera: true);
        };
        designer.CloseRequested += Close;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
