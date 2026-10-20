using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EasyClothes.App.Viewer;
using EasyClothes.Core.Rage;

namespace EasyClothes.App;

public partial class MainWindow : Window
{
    private string _modelStatus = "";
    private string _textureStatus = "";

    public MainWindow()
    {
        InitializeComponent();
        Viewport.RenderFailed += message => SetStatus(model: message);
    }

    public void LoadModel(string path)
    {
        try
        {
            var drawables = DrawableDictionary.Read(ResourceFile.Read(path), includeMeshes: true);
            if (drawables.Count == 0 || drawables[0].Lods.Count == 0)
            {
                SetStatus(model: $"{Path.GetFileName(path)}: no contiene drawables");
                return;
            }
            var lod = drawables[0].Lods[0];
            Viewport.ShowMeshes(lod.Models.SelectMany(m => m).Select(g => g.Mesh!).ToList());
            SetStatus(model: $"{drawables[0].Name} · {lod.Level} · {lod.Vertices:N0} vértices · {lod.Triangles:N0} triángulos");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            SetStatus(model: $"No se pudo abrir {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    public void LoadTexture(string path)
    {
        try
        {
            var resource = ResourceFile.Read(path);
            var textures = TextureDictionary.Read(resource);
            if (textures.Count == 0)
            {
                SetStatus(texture: $"{Path.GetFileName(path)}: no contiene texturas");
                return;
            }
            var texture = textures[0];
            Viewport.ShowTexture(TextureDictionary.Decode(resource, texture));
            SetStatus(texture: $"{texture.Name} · {texture.Width}x{texture.Height} {texture.Format}");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            SetStatus(texture: $"No se pudo abrir {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private async void OnOpenModel(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Modelo de ropa", "*.ydd") is { } path) LoadModel(path);
    }

    private async void OnOpenTexture(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Diccionario de texturas", "*.ytd") is { } path) LoadTexture(path);
    }

    private void OnShadingChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Fires once while the XAML loads, before Viewport is assigned.
        if (Viewport is null) return;
        Viewport.Shading = (ShadingMode)Math.Max(0, ShadingBox.SelectedIndex);
    }

    private void OnResetCamera(object? sender, RoutedEventArgs e) => Viewport.ResetCamera();

    private async Task<string?> PickFile(string description, string pattern)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = [pattern] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void SetStatus(string? model = null, string? texture = null)
    {
        _modelStatus = model ?? _modelStatus;
        _textureStatus = texture ?? _textureStatus;
        StatusText.Text = string.Join("   |   ", new[] { _modelStatus, _textureStatus }.Where(s => s.Length > 0));
    }
}
