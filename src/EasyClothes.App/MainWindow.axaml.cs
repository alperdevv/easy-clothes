using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EasyClothes.App.Services;
using EasyClothes.App.Viewer;
using EasyClothes.App.ViewModels;

namespace EasyClothes.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(new DialogService(this));
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelChanged;
        Viewport.RenderFailed += message => PreviewStatus.Text = message;
    }

    public MainWindowViewModel ViewModel { get; }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Preview)) return;
        var preview = ViewModel.Preview;
        Viewport.ShowMeshes(preview?.Meshes ?? []);
        Viewport.ShowTexture(preview?.Texture);
        PreviewStatus.Text = preview?.Status ?? "";
    }

    private void OnShadingChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Fires once while the XAML loads, before Viewport is assigned.
        if (Viewport is null) return;
        Viewport.Shading = (ShadingMode)Math.Max(0, ShadingBox.SelectedIndex);
    }

    private void OnResetCamera(object? sender, RoutedEventArgs e) => Viewport.ResetCamera();

    private void OnIssueActivated(object? sender, TappedEventArgs e) =>
        ViewModel.SelectIssueCommand.Execute(IssuesList.SelectedItem as IssueViewModel);
}
