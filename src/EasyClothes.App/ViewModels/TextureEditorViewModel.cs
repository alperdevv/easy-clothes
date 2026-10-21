using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.App.Services;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public enum EditorTool
{
    Move,
    Brush,
    Eraser,
}

public sealed partial class DecalViewModel(Decal model, Action changed) : ObservableObject
{
    public Decal Model { get; } = model;
    public string Name => Model.Name;

    public double Size
    {
        get => Model.Scale * 100;
        set => Set(() => Model.Scale = (float)Math.Clamp(value, 1, 200) / 100);
    }

    public double Rotation
    {
        get => Model.Rotation;
        set => Set(() => Model.Rotation = (float)value);
    }

    public double Opacity
    {
        get => Model.Opacity * 100;
        set => Set(() => Model.Opacity = (float)Math.Clamp(value, 0, 100) / 100);
    }

    public void Move(Vector2 delta)
    {
        Model.X = Math.Clamp(Model.X + delta.X, 0, 1);
        Model.Y = Math.Clamp(Model.Y + delta.Y, 0, 1);
        changed();
    }

    public void Resize(float factor) => Size = Size * factor;

    private void Set(Action apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        apply();
        OnPropertyChanged(property);
        changed();
    }
}

public sealed partial class TextureEditorViewModel : ObservableObject, IDisposable
{
    public const int PreviewSide = 1024;
    private const int MaxUndo = 20;

    private readonly IDialogService _dialogs;
    private readonly string _projectDir;
    private readonly Drawable _drawable;
    private readonly int _textureIndex;
    private readonly Stack<(byte[] Pixels, bool HasPaint)> _undo = new();
    private bool _composing;
    private bool _composeAgain;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _externalDebounce;

    public TextureEditorViewModel(IDialogService dialogs, string projectDir, Drawable drawable, int textureIndex, RgbaImage original,
        IReadOnlyList<MeshData> meshes, IReadOnlyList<RenderItem> body)
    {
        _dialogs = dialogs;
        _projectDir = projectDir;
        _drawable = drawable;
        _textureIndex = textureIndex;
        Edit = new TextureEdit { Original = original };
        Meshes = meshes;
        Body = body;
        Composed = TextureCompositor.Compose(Edit, PreviewSide);
    }

    public TextureEdit Edit { get; }
    public IReadOnlyList<MeshData> Meshes { get; }
    public IReadOnlyList<RenderItem> Body { get; }
    public TextureSaveResult? Saved { get; private set; }
    public string ExternalPath { get; set; } = Path.Combine(Path.GetTempPath(), "EasyClothes", "edit");

    public string Letter => TextureVariation.LetterFor(_textureIndex).ToString();
    public string Title => $"Editor de texturas — {_drawable.Name} · textura {Letter}";
    public string SizeLabel => $"{Edit.Base.Width}x{Edit.Base.Height}";
    public bool HasUnsavedChanges => Saved is null && (!Edit.IsEmpty || _undo.Count > 0);

    public event Action? CloseRequested;

    [ObservableProperty]
    public partial RgbaImage Composed { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "Arrastra en la textura para mover logos o pintar; la prenda se actualiza al momento.";

    public double Hue { get => Edit.Hue; set => SetAdjustment(() => Edit.Hue = (float)value); }
    public double Saturation { get => Edit.Saturation * 100; set => SetAdjustment(() => Edit.Saturation = (float)value / 100); }
    public double Brightness { get => Edit.Brightness * 100; set => SetAdjustment(() => Edit.Brightness = (float)value / 100); }
    public double Contrast { get => Edit.Contrast * 100; set => SetAdjustment(() => Edit.Contrast = (float)value / 100); }
    public double TintStrength { get => Edit.TintStrength * 100; set => SetAdjustment(() => Edit.TintStrength = (float)value / 100); }

    public Color TintColor
    {
        get => Color.FromUInt32(0xFF000000 | Edit.TintRgb);
        set => SetAdjustment(() => Edit.TintRgb = value.ToUInt32() & 0xFFFFFF);
    }

    public ObservableCollection<DecalViewModel> Decals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDecal))]
    [NotifyCanExecuteChangedFor(nameof(RemoveDecalCommand))]
    public partial DecalViewModel? SelectedDecal { get; set; }

    public bool HasSelectedDecal => SelectedDecal is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMoveTool), nameof(IsBrushTool), nameof(IsEraserTool))]
    public partial EditorTool Tool { get; set; } = EditorTool.Move;

    // Clicking the active tool again must not leave it looking unselected.
    public bool IsMoveTool { get => Tool == EditorTool.Move; set => SetTool(EditorTool.Move, value); }
    public bool IsBrushTool { get => Tool == EditorTool.Brush; set => SetTool(EditorTool.Brush, value); }
    public bool IsEraserTool { get => Tool == EditorTool.Eraser; set => SetTool(EditorTool.Eraser, value); }

    private void SetTool(EditorTool tool, bool selected, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (selected) Tool = tool;
        else OnPropertyChanged(property);
    }

    [ObservableProperty]
    public partial Color BrushColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial double BrushSize { get; set; } = 24;

    [ObservableProperty]
    public partial double BrushHardness { get; set; } = 70;

    [ObservableProperty]
    public partial double BrushOpacity { get; set; } = 100;

    private void SetAdjustment(Action apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        apply();
        OnPropertyChanged(property);
        RequestCompose();
    }

    // At most one compose runs at a time; changes made meanwhile are folded into one more pass.
    public void RequestCompose()
    {
        if (_composing)
        {
            _composeAgain = true;
            return;
        }
        _ = ComposeLoopAsync();
    }

    private async Task ComposeLoopAsync()
    {
        _composing = true;
        try
        {
            do
            {
                _composeAgain = false;
                var snapshot = Edit.Snapshot();
                Composed = await Task.Run(() => TextureCompositor.Compose(snapshot, PreviewSide));
            } while (_composeAgain);
        }
        finally
        {
            _composing = false;
        }
    }

    public Task WaitForComposeAsync() => WaitAsync(() => !_composing);

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++) await Task.Delay(10);
    }

    [RelayCommand]
    private async Task AddDecalAsync()
    {
        var files = await _dialogs.PickFilesAsync("Añadir logo o parche", "Imágenes", ImageLoader.Extensions.Select(e => "*" + e).ToList(), multiple: true);
        foreach (var file in files) AddDecal(file);
    }

    public void AddDecal(string path)
    {
        try
        {
            var decal = new Decal { Name = Path.GetFileNameWithoutExtension(path), Image = ImageLoader.Load(path) };
            Edit.Decals.Add(decal);
            var item = new DecalViewModel(decal, RequestCompose);
            Decals.Add(item);
            SelectedDecal = item;
            Tool = EditorTool.Move;
            RequestCompose();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Status = $"No se pudo cargar {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDecal))]
    private void RemoveDecal()
    {
        Edit.Decals.Remove(SelectedDecal!.Model);
        Decals.Remove(SelectedDecal);
        SelectedDecal = Decals.LastOrDefault();
        RequestCompose();
    }

    // The topmost decal under the point wins, matching the drawing order.
    public bool SelectDecalAt(Vector2 uv)
    {
        var size = new Vector2(Edit.Base.Width, Edit.Base.Height);
        for (var i = Decals.Count - 1; i >= 0; i--)
        {
            var d = Decals[i].Model;
            var width = d.Scale * size.X;
            var height = width * d.Image.Height / d.Image.Width;
            var offset = (uv - new Vector2(d.X, d.Y)) * size;
            var angle = d.Rotation * MathF.PI / 180;
            var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
            var local = new Vector2(offset.X * cos + offset.Y * sin, -offset.X * sin + offset.Y * cos);
            if (MathF.Abs(local.X) <= width / 2 && MathF.Abs(local.Y) <= height / 2)
            {
                SelectedDecal = Decals[i];
                return true;
            }
        }
        return false;
    }

    public void BeginStroke()
    {
        Edit.EnsurePaintLayer();
        _undo.Push(Edit.SavePaint());
        while (_undo.Count > MaxUndo)
        {
            var kept = _undo.Reverse().Skip(1).ToList();
            _undo.Clear();
            foreach (var entry in kept) _undo.Push(entry);
        }
        UndoCommand.NotifyCanExecuteChanged();
    }

    public void StrokeTo(Vector2 fromUv, Vector2 toUv)
    {
        var size = new Vector2(Edit.Base.Width, Edit.Base.Height);
        var colour = BrushColor.ToUInt32() & 0xFFFFFF;
        var brush = new BrushSettings(colour, (float)Math.Max(1, BrushSize / 2), (float)BrushHardness / 100, (float)BrushOpacity / 100, Tool == EditorTool.Eraser);
        Core.Textures.Editing.Brush.Stroke(Edit, fromUv * size, toUv * size, brush);
        RequestCompose();
    }

    private bool CanUndo => _undo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Edit.RestorePaint(_undo.Pop());
        UndoCommand.NotifyCanExecuteChanged();
        RequestCompose();
    }

    [RelayCommand]
    private void ResetAll()
    {
        Edit.ResetAdjustments();
        Edit.Decals.Clear();
        Decals.Clear();
        SelectedDecal = null;
        Edit.External = null;
        Edit.EnsurePaintLayer();
        Edit.ClearPaint();
        _undo.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        NotifyAdjustments();
        OnPropertyChanged(nameof(SizeLabel));
        Status = "Textura restablecida al original.";
        RequestCompose();
    }

    private void NotifyAdjustments()
    {
        foreach (var name in new[] { nameof(Hue), nameof(Saturation), nameof(Brightness), nameof(Contrast), nameof(TintStrength), nameof(TintColor) })
            OnPropertyChanged(name);
    }

    // Exports what is on screen at full size; when the file comes back, it becomes the new base
    // and the layers already baked into it are cleared.
    [RelayCommand]
    private async Task OpenExternalAsync()
    {
        Directory.CreateDirectory(ExternalPath);
        var path = Path.Combine(ExternalPath, $"{Sanitize(_drawable.Name)}_{Letter}.png");
        var image = await Task.Run(() => TextureCompositor.Compose(Edit.Snapshot()));
        await using (var file = File.Create(path)) PngWriter.Write(image, file);
        Watch(path);
        try
        {
            _dialogs.OpenExternally(path);
            Status = $"Editando {path}. Guarda en tu programa y la prenda se actualizará sola.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = $"No hay un programa asociado a .png. Abre a mano {path}; los cambios se recargarán al guardar.";
        }
    }

    private void Watch(string path)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(() => ScheduleReload(path));
        _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(() => ScheduleReload(path));
    }

    // Editors write in several steps; wait for the file to settle before reading it.
    private void ScheduleReload(string path)
    {
        _externalDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _externalDebounce.Stop();
        _externalDebounce.Tick -= OnDebounce;
        _externalDebounce.Tick += OnDebounce;
        _externalDebounce.Start();

        async void OnDebounce(object? sender, EventArgs e)
        {
            _externalDebounce!.Stop();
            await ReloadExternalAsync(path);
        }
    }

    public async Task ReloadExternalAsync(string path)
    {
        try
        {
            var image = await Task.Run(() => PngReader.Read(path));
            Edit.External = image;
            Edit.ResetAdjustments();
            Edit.Decals.Clear();
            Decals.Clear();
            SelectedDecal = null;
            Edit.EnsurePaintLayer();
            Edit.ClearPaint();
            _undo.Clear();
            UndoCommand.NotifyCanExecuteChanged();
            NotifyAdjustments();
            OnPropertyChanged(nameof(SizeLabel));
            Status = $"Cambios recargados desde {Path.GetFileName(path)} ({image.Width}x{image.Height}).";
            RequestCompose();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Status = $"No se pudo leer {Path.GetFileName(path)} todavía: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var canAdd = _drawable.Textures.Count < Drawable.MaxTextures;
        var next = canAdd ? TextureVariation.LetterFor(_drawable.Textures.Count).ToString() : "";
        var options = canAdd
            ? new[] { $"Crear variante {next}", $"Sobrescribir textura {Letter}" }
            : [$"Sobrescribir textura {Letter}"];
        var message = canAdd
            ? $"Puedes guardar el resultado como una textura nueva ({next}) dejando la original intacta, o reemplazar la textura {Letter} (se guarda una copia en .backups)."
            : $"La prenda ya tiene {Drawable.MaxTextures} texturas. Se reemplazará la textura {Letter} y se guardará una copia en .backups.";
        if (await _dialogs.ChooseAsync("Aplicar cambios", message, options) is not { } choice) return;
        var mode = canAdd && choice == 0 ? TextureSaveMode.NewVariation : TextureSaveMode.Overwrite;
        await SaveAsync(mode);
    }

    public async Task SaveAsync(TextureSaveMode mode)
    {
        Status = "Guardando y comprimiendo la textura…";
        try
        {
            var snapshot = Edit.Snapshot();
            Saved = await Task.Run(() => TextureEditSaver.Save(_projectDir, _drawable, _textureIndex, snapshot, mode));
            CloseRequested?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            Status = $"No se pudo guardar: {ex.Message}";
        }
    }

    public async Task<bool> ConfirmDiscardAsync() =>
        !HasUnsavedChanges || await _dialogs.ChooseAsync("Descartar cambios", "Hay cambios sin aplicar en la textura. ¿Cerrar y descartarlos?", ["Descartar"]) == 0;

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

    public void Dispose()
    {
        _watcher?.Dispose();
        _externalDebounce?.Stop();
    }
}
