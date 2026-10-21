using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EasyClothes.App.Services;
using EasyClothes.App.Viewer;
using EasyClothes.App.ViewModels;
using EasyClothes.Core.Settings;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(new DialogService(this)) { Settings = AppSettings.Load(AppSettings.DefaultPath) };
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelChanged;
        Viewport.RenderFailed += message => PreviewStatus.Text = message;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public MainWindowViewModel ViewModel { get; }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Preview)) return;
        var preview = ViewModel.Preview;
        Viewport.ShowScene(preview is null ? [] : [new RenderItem(preview.Meshes, preview.Texture)], preview?.Body ?? []);
        PreviewStatus.Text = preview?.Status ?? "";
    }

    private void OnShadingChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Fires once while the XAML loads, before Viewport is assigned.
        if (Viewport is null) return;
        Viewport.Shading = (ShadingMode)Math.Max(0, ShadingBox.SelectedIndex);
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is { Count: > 0 }) await ViewModel.DropAsync(paths);
    }

    private void OnResetCamera(object? sender, RoutedEventArgs e) => Viewport.ResetCamera();

    private void OnIssueActivated(object? sender, TappedEventArgs e) =>
        ViewModel.SelectIssueCommand.Execute(IssuesList.SelectedItem as IssueViewModel);
}
