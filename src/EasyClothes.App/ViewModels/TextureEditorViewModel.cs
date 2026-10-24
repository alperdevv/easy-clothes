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
    Hand,
    EraseMark,
}

public sealed partial class TextureEditorViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly string _projectDir;
    private readonly Drawable _drawable;
    private readonly int _textureIndex;
    private bool _composing;
    private bool _composeAgain;
    private bool _restoring;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _externalDebounce;
    private Guid? _externalLayer;
    private (PixelLayer Layer, RgbaImage Start)? _pixelMove;
    private Vector2 _moveTotal;

    public TextureEditorViewModel(IDialogService dialogs, string projectDir, Drawable drawable, int textureIndex, TextureDocument document,
        IReadOnlyList<MeshData> meshes, IReadOnlyList<RenderItem> body, string? note = null)
    {
        _dialogs = dialogs;
        _projectDir = projectDir;
        _drawable = drawable;
        _textureIndex = textureIndex;
        Document = document;
        History = new EditHistory(document, "Abrir textura");
        Meshes = meshes;
        Body = body;
        Composed = TextureCompositor.Compose(document);
        RebuildLayers(document.Layers.OfType<PixelLayer>().LastOrDefault()?.Id);
        RefreshHistory();
        if (note is not null) Status = note;
    }

    public TextureDocument Document { get; }
    public EditHistory History { get; }
    public IReadOnlyList<MeshData> Meshes { get; }
    public IReadOnlyList<RenderItem> Body { get; }
    public TextureSaveResult? Saved { get; private set; }
    public string ExternalPath { get; set; } = Path.Combine(Path.GetTempPath(), "EasyClothes", "edit");

    public string Letter => TextureVariation.LetterFor(_textureIndex).ToString();
    public string Title => $"Editor de texturas — {_drawable.Name} · textura {Letter}";
    public string SizeLabel => $"{Document.Width}x{Document.Height}";
    public bool HasUnsavedChanges => Saved is null && History.IsModified;
    public bool HasOriginal => Document.Original is not null;

    public event Action? CloseRequested;

    [ObservableProperty]
    public partial RgbaImage Composed { get; private set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Pinta en una capa o añade imágenes, rellenos y ajustes; la prenda se actualiza al momento.";

    [ObservableProperty]
    public partial double ZoomPercent { get; set; } = 100;

    public bool KeepOriginalAlpha
    {
        get => Document.KeepOriginalAlpha;
        set
        {
            Document.KeepOriginalAlpha = value;
            OnPropertyChanged();
            Commit(value ? "Mantener recorte original" : "Usar transparencia de las capas");
        }
    }

    // Shown top first, like the layers panel of an image editor; the document keeps them bottom first.
    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    public partial LayerViewModel? SelectedLayer { get; set; }

    public bool HasSelectedLayer => SelectedLayer is not null;

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    public int HistoryIndex
    {
        get => History.Index;
        set
        {
            if (value < 0 || value == History.Index || _restoring) return;
            Restore(History.JumpTo(value));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMoveTool), nameof(IsBrushTool), nameof(IsEraserTool), nameof(IsHandTool), nameof(IsPaintTool), nameof(IsEraseMarkTool))]
    public partial EditorTool Tool { get; set; } = EditorTool.Brush;

    // Clicking the active tool again must not leave it looking unselected.
    public bool IsMoveTool { get => Tool == EditorTool.Move; set => SetTool(EditorTool.Move, value); }
    public bool IsBrushTool { get => Tool == EditorTool.Brush; set => SetTool(EditorTool.Brush, value); }
    public bool IsEraserTool { get => Tool == EditorTool.Eraser; set => SetTool(EditorTool.Eraser, value); }
    public bool IsHandTool { get => Tool == EditorTool.Hand; set => SetTool(EditorTool.Hand, value); }
    public bool IsEraseMarkTool { get => Tool == EditorTool.EraseMark; set => SetTool(EditorTool.EraseMark, value); }
    public bool IsPaintTool => Tool is EditorTool.Brush or EditorTool.Eraser;

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

    public void Commit(string label, string? mergeKey = null)
    {
        if (_restoring) return;
        History.Commit(Document, label, string.IsNullOrEmpty(mergeKey) ? null : mergeKey);
        RefreshHistory();
        RefreshLayerCommands();
        RequestCompose();
    }

    private void Restore(TextureDocument state)
    {
        _restoring = true;
        try
        {
            Document.Restore(state);
            RebuildLayers(SelectedLayer?.Id);
            OnPropertyChanged(nameof(KeepOriginalAlpha));
            OnPropertyChanged(nameof(SizeLabel));
            RefreshHistory();
        }
        finally
        {
            _restoring = false;
        }
        RequestCompose();
    }

    private void RefreshHistory()
    {
        HistoryItems.Clear();
        for (var i = 0; i < History.Entries.Count; i++)
            HistoryItems.Add(new HistoryItemViewModel(History.Entries[i].Label, i > History.Index));
        OnPropertyChanged(nameof(HistoryIndex));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RebuildLayers(Guid? select)
    {
        Layers.Clear();
        for (var i = Document.Layers.Count - 1; i >= 0; i--)
            Layers.Add(new LayerViewModel(Document.Layers[i], (label, key) => Commit(label, key)));
        SelectedLayer = Layers.FirstOrDefault(l => l.Id == select) ?? Layers.FirstOrDefault();
    }

    partial void OnSelectedLayerChanged(LayerViewModel? value) => RefreshLayerCommands();

    private void RefreshLayerCommands()
    {
        foreach (var command in new IRelayCommand[] { DuplicateLayerCommand, DeleteLayerCommand, MoveLayerUpCommand, MoveLayerDownCommand, MergeDownCommand, RasterizeLayerCommand })
            command.NotifyCanExecuteChanged();
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
                var snapshot = Document.Snapshot();
                Composed = await Task.Run(() => TextureCompositor.Compose(snapshot));
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

    // New layers go right above the selected one, as in Photoshop.
    private void Insert(Layer layer, string label)
    {
        var index = SelectedLayer is { } selected ? Document.Layers.IndexOf(selected.Model) + 1 : Document.Layers.Count;
        Document.Layers.Insert(index, layer);
        RebuildLayers(layer.Id);
        Commit(label);
    }

    private string NextName(string prefix)
    {
        for (var i = 1; ; i++)
            if (Document.Layers.All(l => l.Name != $"{prefix} {i}")) return $"{prefix} {i}";
    }

    [RelayCommand]
    private void NewLayer() => Insert(Document.NewPixelLayer(NextName("Capa")), "Nueva capa");

    [RelayCommand]
    private void NewFillLayer() =>
        Insert(new FillLayer { Name = NextName("Relleno"), Rgb = BrushColor.ToUInt32() & 0xFFFFFF }, "Nueva capa de relleno");

    [RelayCommand]
    private void NewAdjustmentLayer() => Insert(new AdjustmentLayer { Name = NextName("Ajuste de color") }, "Nueva capa de ajuste");

    [RelayCommand]
    private async Task AddImageAsync()
    {
        var files = await _dialogs.PickFilesAsync("Añadir imagen como capa", "Imágenes", ImageLoader.Extensions.Select(e => "*" + e).ToList(), multiple: true);
        foreach (var file in files) AddImage(file);
    }

    public void AddImage(string path)
    {
        try
        {
            Insert(new ImageLayer { Name = Path.GetFileNameWithoutExtension(path), Image = ImageLoader.Load(path) }, "Añadir imagen");
            Tool = EditorTool.Move;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Status = $"No se pudo cargar {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void DuplicateLayer()
    {
        var copy = SelectedLayer!.Model.Clone();
        Layer duplicate = copy switch
        {
            PixelLayer p => new PixelLayer { Name = p.Name, Image = p.Image },
            ImageLayer i => new ImageLayer { Name = i.Name, Image = i.Image, X = i.X, Y = i.Y, Scale = i.Scale, Rotation = i.Rotation },
            FillLayer f => new FillLayer { Name = f.Name, Rgb = f.Rgb },
            AdjustmentLayer a => new AdjustmentLayer
            {
                Name = a.Name, Hue = a.Hue, Saturation = a.Saturation, Brightness = a.Brightness, Contrast = a.Contrast,
                TintRgb = a.TintRgb, TintStrength = a.TintStrength,
            },
            _ => throw new NotSupportedException(copy.GetType().Name),
        };
        (duplicate.Name, duplicate.Visible, duplicate.Opacity, duplicate.Blend) = ($"{copy.Name} copia", copy.Visible, copy.Opacity, copy.Blend);
        Insert(duplicate, "Duplicar capa");
    }

    private bool CanDeleteLayer => SelectedLayer is not null && Document.Layers.Count > 1;

    [RelayCommand(CanExecute = nameof(CanDeleteLayer))]
    private void DeleteLayer()
    {
        var index = Document.Layers.IndexOf(SelectedLayer!.Model);
        Document.Layers.RemoveAt(index);
        RebuildLayers(Document.Layers[Math.Max(0, index - 1)].Id);
        Commit("Eliminar capa");
    }

    private bool CanMoveLayerUp => SelectedLayer is not null && Document.Layers.IndexOf(SelectedLayer.Model) < Document.Layers.Count - 1;
    private bool CanMoveLayerDown => SelectedLayer is not null && Document.Layers.IndexOf(SelectedLayer.Model) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveLayerUp))]
    private void MoveLayerUp() => MoveLayer(+1);

    [RelayCommand(CanExecute = nameof(CanMoveLayerDown))]
    private void MoveLayerDown() => MoveLayer(-1);

    private void MoveLayer(int step)
    {
        var layer = SelectedLayer!.Model;
        var index = Document.Layers.IndexOf(layer);
        Document.Layers.RemoveAt(index);
        Document.Layers.Insert(index + step, layer);
        RebuildLayers(layer.Id);
        Commit(step > 0 ? "Subir capa" : "Bajar capa");
    }

    [RelayCommand(CanExecute = nameof(CanMoveLayerDown))]
    private void MergeDown()
    {
        var merged = Document.MergeDown(SelectedLayer!.Model);
        RebuildLayers(merged.Id);
        Commit("Combinar hacia abajo");
    }

    private bool CanRasterize => SelectedLayer?.CanRasterize == true;

    [RelayCommand(CanExecute = nameof(CanRasterize))]
    private void RasterizeLayer()
    {
        var layer = Document.Rasterize(SelectedLayer!.Model);
        RebuildLayers(layer.Id);
        Commit("Rasterizar capa");
    }

    // Painting needs a visible, unlocked pixel layer; anything else explains itself instead.
    private PixelLayer? PaintTarget()
    {
        var reason = SelectedLayer?.Model switch
        {
            null => "Selecciona una capa para pintar.",
            PixelLayer { Locked: true } => "La capa está bloqueada.",
            PixelLayer { Visible: false } => "La capa está oculta.",
            PixelLayer => null,
            _ => "Solo se puede pintar en capas de píxeles. Crea una capa nueva o rasteriza esta.",
        };
        if (reason is null) return (PixelLayer)SelectedLayer!.Model;
        Status = reason;
        return null;
    }

    // History states share pixel buffers, so a stroke always works on a fresh copy of the layer.
    public bool BeginStroke()
    {
        if (PaintTarget() is not { } layer) return false;
        layer.Image = layer.Image with { Pixels = (byte[])layer.Image.Pixels.Clone() };
        return true;
    }

    public void StrokeTo(Vector2 fromUv, Vector2 toUv)
    {
        if (SelectedLayer?.Model is not PixelLayer layer) return;
        var size = new Vector2(Document.Width, Document.Height);
        var brush = new BrushSettings(BrushColor.ToUInt32() & 0xFFFFFF, (float)Math.Max(0.5, BrushSize / 2),
            (float)BrushHardness / 100, (float)BrushOpacity / 100, Tool == EditorTool.Eraser);
        Core.Textures.Editing.Brush.Stroke(layer.Image, fromUv * size, toUv * size, brush);
        RequestCompose();
    }

    public void EndStroke() => Commit(Tool == EditorTool.Eraser ? "Borrador" : "Pincel");

    // The last mark erased, so "Otra zona" can cover it again from the next best source.
    private (PixelRect Hole, IReadOnlyList<PatchSource> Sources, int Index, Guid Layer, RgbaImage Image)? _lastErase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TryAnotherSourceCommand))]
    public partial bool IsErasing { get; private set; }

    // Covers the rectangle with the area of the visible texture that best continues its surroundings,
    // as a new layer on top so it can be hidden, faded or undone like any other.
    public async Task EraseMarkAsync(Vector2 fromUv, Vector2 toUv)
    {
        var (min, max) = (Vector2.Clamp(Vector2.Min(fromUv, toUv), Vector2.Zero, Vector2.One), Vector2.Clamp(Vector2.Max(fromUv, toUv), Vector2.Zero, Vector2.One));
        var hole = new PixelRect((int)(min.X * Document.Width), (int)(min.Y * Document.Height),
            (int)MathF.Ceiling((max.X - min.X) * Document.Width), (int)MathF.Ceiling((max.Y - min.Y) * Document.Height));
        if (hole.Width < 2 || hole.Height < 2)
        {
            Status = "Arrastra un rectángulo que cubra la marca entera, con un poco de margen.";
            return;
        }

        await WaitForComposeAsync();
        var image = Composed;
        IsErasing = true;
        Status = "Buscando una zona de la prenda que encaje…";
        var (sources, patch) = await Task.Run(() =>
        {
            var found = MarkEraser.FindSources(image, hole);
            return (found, found.Count == 0 ? null : MarkEraser.Fill(image, hole, found[0]));
        });
        IsErasing = false;
        if (patch is null)
        {
            Status = "No hay otra zona de la textura de ese tamaño; prueba con un rectángulo más pequeño.";
            return;
        }

        var layer = new PixelLayer { Name = NextName("Marca borrada"), Image = patch };
        Document.Layers.Add(layer);
        RebuildLayers(layer.Id);
        Commit("Borrar marca");
        _lastErase = (hole, sources, 0, layer.Id, image);
        TryAnotherSourceCommand.NotifyCanExecuteChanged();
        Status = sources.Count > 1
            ? "Marca tapada con otra zona de la prenda. Si no encaja, pulsa «Otra zona»."
            : "Marca tapada con otra zona de la prenda.";
    }

    private bool CanTryAnotherSource => !IsErasing && _lastErase is { Sources.Count: > 1 } erase && Document.Find(erase.Layer) is PixelLayer;

    [RelayCommand(CanExecute = nameof(CanTryAnotherSource))]
    private void TryAnotherSource()
    {
        if (_lastErase is not { } erase || Document.Find(erase.Layer) is not PixelLayer layer) return;
        var next = (erase.Index + 1) % erase.Sources.Count;
        layer.Image = MarkEraser.Fill(erase.Image, erase.Hole, erase.Sources[next]);
        _lastErase = erase with { Index = next };
        RequestCompose();
        Commit("Borrar marca: otra zona");
        Status = $"Zona {next + 1} de {erase.Sources.Count}.";
    }

    // A click on a placed image selects it; otherwise the selected layer is the one that moves.
    public bool BeginMove(Vector2 uv)
    {
        SelectImageLayerAt(uv);
        _moveTotal = Vector2.Zero;
        _pixelMove = null;
        switch (SelectedLayer?.Model)
        {
            case { Locked: true }:
                Status = "La capa está bloqueada.";
                return false;
            case ImageLayer:
                return true;
            case PixelLayer pixels:
                _pixelMove = (pixels, pixels.Image);
                return true;
            default:
                Status = "Los rellenos y ajustes ocupan toda la textura; no se pueden mover.";
                return false;
        }
    }

    public void MoveBy(Vector2 deltaUv)
    {
        _moveTotal += deltaUv;
        if (SelectedLayer?.Model is ImageLayer image)
        {
            image.X = Math.Clamp(image.X + deltaUv.X, -0.5f, 1.5f);
            image.Y = Math.Clamp(image.Y + deltaUv.Y, -0.5f, 1.5f);
        }
        else if (_pixelMove is { } move)
        {
            move.Layer.Image = Shift(move.Start, (int)MathF.Round(_moveTotal.X * Document.Width), (int)MathF.Round(_moveTotal.Y * Document.Height));
        }
        RequestCompose();
    }

    public void EndMove()
    {
        _pixelMove = null;
        if (_moveTotal != Vector2.Zero) Commit("Mover capa");
    }

    private static RgbaImage Shift(RgbaImage source, int dx, int dy)
    {
        var pixels = new byte[source.Pixels.Length];
        var row = source.Width * 4;
        var start = Math.Max(0, dx);
        var end = Math.Min(source.Width, source.Width + dx);
        for (var y = 0; y < source.Height && end > start; y++)
        {
            var from = y - dy;
            if (from < 0 || from >= source.Height) continue;
            Array.Copy(source.Pixels, from * row + (start - dx) * 4, pixels, y * row + start * 4, (end - start) * 4);
        }
        return source with { Pixels = pixels };
    }

    // The topmost visible image under the point wins, matching the drawing order.
    public bool SelectImageLayerAt(Vector2 uv)
    {
        var size = new Vector2(Document.Width, Document.Height);
        foreach (var item in Layers)
        {
            if (item.Model is not ImageLayer { Visible: true } d) continue;
            var width = d.Scale * size.X;
            var height = width * d.Image.Height / d.Image.Width;
            var offset = (uv - new Vector2(d.X, d.Y)) * size;
            var angle = d.Rotation * MathF.PI / 180;
            var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
            var local = new Vector2(offset.X * cos + offset.Y * sin, -offset.X * sin + offset.Y * cos);
            if (MathF.Abs(local.X) <= width / 2 && MathF.Abs(local.Y) <= height / 2)
            {
                SelectedLayer = item;
                return true;
            }
        }
        return false;
    }

    public void SampleColour(Vector2 uv)
    {
        var x = Math.Clamp((int)(uv.X * Composed.Width), 0, Composed.Width - 1);
        var y = Math.Clamp((int)(uv.Y * Composed.Height), 0, Composed.Height - 1);
        var o = (y * Composed.Width + x) * 4;
        BrushColor = Color.FromRgb(Composed.Pixels[o], Composed.Pixels[o + 1], Composed.Pixels[o + 2]);
    }

    private bool CanUndo => History.CanUndo;
    private bool CanRedo => History.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Restore(History.Undo());

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Restore(History.Redo());

    // Goes back to how the texture was opened, as one more step that can itself be undone.
    [RelayCommand]
    private void ResetAll()
    {
        Document.Restore(History.Entries[0].State);
        RebuildLayers(SelectedLayer?.Id);
        OnPropertyChanged(nameof(KeepOriginalAlpha));
        OnPropertyChanged(nameof(SizeLabel));
        Commit("Restablecer");
        Status = "Textura restablecida a como se abrió. Ctrl+Z lo deshace.";
    }

    // Exports what is on screen at full size; when the file comes back it lands as a layer on top
    // and the layers already baked into it are hidden, so nothing is lost.
    [RelayCommand]
    private async Task OpenExternalAsync()
    {
        Directory.CreateDirectory(ExternalPath);
        var path = Path.Combine(ExternalPath, $"{Sanitize(_drawable.Name)}_{Letter}.png");
        var snapshot = Document.Snapshot();
        var image = await Task.Run(() => TextureCompositor.Compose(snapshot));
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
            Document.Resize(image.Width, image.Height);
            if (_externalLayer is { } id && Document.Find(id) is PixelLayer existing)
            {
                existing.Image = image;
                existing.Visible = true;
            }
            else
            {
                foreach (var layer in Document.Layers) layer.Visible = false;
                var external = new PixelLayer { Name = "Editado fuera", Image = image };
                Document.Layers.Add(external);
                _externalLayer = external.Id;
            }
            RebuildLayers(_externalLayer);
            OnPropertyChanged(nameof(SizeLabel));
            Commit("Cambios del editor externo");
            Status = $"Cambios recargados desde {Path.GetFileName(path)} ({image.Width}x{image.Height}). Las capas anteriores quedan ocultas debajo.";
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
            ? $"Puedes guardar el resultado como una textura nueva ({next}) dejando la original intacta, o reemplazar la textura {Letter} (se guarda una copia en .backups). Las capas se guardan junto a la textura para seguir editando otro día."
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
            var snapshot = Document.Snapshot();
            Saved = await Task.Run(() => TextureEditSaver.Save(_projectDir, _drawable, _textureIndex, snapshot, mode));
            History.MarkSaved();
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
