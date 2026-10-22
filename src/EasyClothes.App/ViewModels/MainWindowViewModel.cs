using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.App.Services;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Settings;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;
using EasyClothes.Core.Validation;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App.ViewModels;

public sealed partial class MainWindowViewModel(IDialogService dialogs) : ObservableObject
{
    private readonly AnalysisCache _analysisCache = new();
    private readonly ConcurrentDictionary<(string, string?), RenderItem> _bodyParts = new();
    private int _previewVersion;
    private int _validationVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyFixCommand), nameof(FixAllCommand), nameof(BuildResourceCommand), nameof(PackProjectCommand), nameof(ImportCommand), nameof(ImportFolderCommand), nameof(ImportTattoosCommand), nameof(EditTextureCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueSummary))]
    public partial bool IsAnalyzing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial ClothingProject? Project { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDraft { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyFixCommand), nameof(FixAllCommand), nameof(BuildResourceCommand), nameof(PackProjectCommand))]
    public partial string? ProjectDir { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial TreeNode? SelectedNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(EditTextureCommand))]
    public partial DrawableViewModel? Selected { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTattooSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial TattooViewModel? SelectedTattoo { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditTextureCommand))]
    public partial TextureItem? SelectedTexture { get; set; }

    [ObservableProperty]
    public partial Preview? Preview { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "Crea o abre un proyecto, o importa prendas directamente.";

    public ObservableCollection<TreeNode> Tree { get; } = [];
    public ObservableCollection<IssueViewModel> Issues { get; } = [];

    public string DraftsRoot { get; set; } = ProjectDraft.DefaultRoot;
    public string SettingsPath { get; set; } = AppSettings.DefaultPath;

    public AppSettings Settings
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ShowBody));
            OnPropertyChanged(nameof(BodyTip));
        }
    } = new();

    public bool ShowBody
    {
        get => Settings.ShowBody && Settings.BaseBodyFolder is not null;
        set
        {
            if (value && Settings.BaseBodyFolder is null)
            {
                _ = ChooseBodyFolderAsync();
                return;
            }
            Settings.ShowBody = value;
            SaveSettings();
            OnPropertyChanged();
            _ = LoadPreviewAsync();
        }
    }

    public string BodyTip => Settings.BaseBodyFolder is { } folder
        ? $"Mostrar la prenda puesta en el personaje ({folder})"
        : "Mostrar la prenda puesta en el personaje. Necesita las carpetas mp_m_freemode_01 y mp_f_freemode_01 exportadas del juego con CodeWalker.";

    public bool HasProject => Project is not null;
    private bool CanImport => !IsBusy;
    public bool HasFixes => Issues.Any(i => i.HasFix);
    private bool CanFix => !IsBusy && ProjectDir is not null;
    public bool HasSelection => Selected is not null;
    public bool HasTattooSelection => SelectedTattoo is not null;
    private bool CanRemove => HasSelection || HasTattooSelection;
    public string Title => Project is null ? "EasyClothes" : $"EasyClothes — {(IsDraft ? DraftName : Project.Name)}{(IsDirty ? " *" : "")}";

    private const string DraftName = "Sin guardar";

    public string IssueSummary
    {
        get
        {
            if (IsAnalyzing) return "Analizando…";
            if (Issues.Count == 0) return "Sin problemas";
            return $"{Issues.Count(i => i.IsError)} errores · {Issues.Count(i => i.IsWarning)} avisos · {Issues.Count(i => i.IsInfo)} info";
        }
    }

    public string CollectionName
    {
        get => Project?.CollectionName ?? "";
        set
        {
            if (Project is null || Project.CollectionName == value) return;
            Project.CollectionName = value;
            OnPropertyChanged();
            MarkChanged(rebuildTree: false);
        }
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (await dialogs.PickFolderAsync("Carpeta del nuevo proyecto") is not { } dir) return;
        if (Directory.EnumerateFiles(dir, "*" + ProjectFile.Extension).Any())
        {
            Status = "Esa carpeta ya tiene un proyecto. Usa Abrir.";
            return;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
        var project = new ClothingProject { Name = name, CollectionName = ClothingProject.CollectionNameFrom(name) };
        var file = ProjectPathFor(dir, project);
        await ProjectFile.SaveAsync(project, file);
        LoadProject(project, dir);
        Watch(file);
        Status = $"Proyecto creado en {dir}.";
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var files = await dialogs.PickFilesAsync("Abrir proyecto", "Proyecto EasyClothes", ["*" + ProjectFile.Extension], multiple: false);
        if (files.Count > 0) await OpenProjectFileAsync(files[0]);
    }

    public async Task OpenProjectFileAsync(string path)
    {
        try
        {
            LoadProject(await ProjectFile.LoadAsync(path), Path.GetDirectoryName(Path.GetFullPath(path))!);
            Watch(Path.GetFullPath(path));
            Status = $"Proyecto abierto: {path}";
            SelectedNode = Tree.SelectMany(g => g.Children).SelectMany(s => s.Children).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Status = $"No se pudo abrir el proyecto: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task SaveAsync()
    {
        if (!IsDraft)
        {
            await SaveProjectFileAsync();
            return;
        }
        if (await dialogs.PickFolderAsync("Carpeta donde guardar el proyecto") is { } dir) await SaveDraftAsAsync(dir);
    }

    public async Task<bool> SaveDraftAsAsync(string targetDir)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(targetDir));
        IsBusy = true;
        try
        {
            await Task.Run(() => ProjectDraft.MoveTo(ProjectDir!, targetDir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudo guardar el proyecto: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        Project!.Name = name;
        if (Project.CollectionName == DraftCollection) CollectionName = ClothingProject.CollectionNameFrom(name);
        ProjectDir = targetDir;
        IsDraft = false;
        await SaveProjectFileAsync();
        _ = LoadPreviewAsync();
        _ = ValidateAsync();
        return true;
    }

    private async Task SaveProjectFileAsync()
    {
        var path = _projectFile ?? ProjectPathFor(ProjectDir!, Project!);
        ProjectPackage.BackupProjectFile(path);
        await ProjectFile.SaveAsync(Project!, path);
        IsDirty = false;
        Watch(path);
        Status = $"Proyecto guardado en {ProjectDir}.";
    }

    private string? _projectFile;
    private FileSystemWatcher? _watcher;
    private DateTime _knownWrite;

    // Claude (through the MCP server) or another program may change the project file while it is open.
    private void Watch(string? file)
    {
        _watcher?.Dispose();
        _watcher = null;
        _projectFile = file;
        if (file is null || !File.Exists(file)) return;
        _knownWrite = File.GetLastWriteTimeUtc(file);
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(file)!, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => _ = ReloadIfChangedAsync());
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(() => _ = ReloadIfChangedAsync());
        _watcher.EnableRaisingEvents = true;
    }

    public async Task ReloadIfChangedAsync()
    {
        if (_projectFile is not { } file || !File.Exists(file)) return;
        var stamp = File.GetLastWriteTimeUtc(file);
        if (stamp == _knownWrite) return;
        _knownWrite = stamp;
        if (IsDirty && await dialogs.ChooseAsync("Proyecto cambiado fuera de la app",
                "Otro programa (por ejemplo Claude) ha cambiado el proyecto y tú tienes cambios sin guardar.",
                ["Cargar los cambios de fuera", "Mantener los míos"]) != 0)
            return;

        ClothingProject project;
        try
        {
            project = await ProjectFile.LoadAsync(file);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Status = $"El proyecto cambió fuera de la app pero no se pudo leer: {ex.Message}";
            return;
        }
        var selected = Selected?.Model.Id ?? SelectedTattoo?.Model.Id;
        LoadProject(project, ProjectDir!);
        if (selected is { } id) Select(id);
        Status = "Proyecto actualizado desde fuera de la app (por ejemplo, por Claude).";
    }

    private const string DraftCollection = "pack";

    private void EnsureProject()
    {
        if (Project is not null) return;
        LoadProject(new ClothingProject { Name = DraftName, CollectionName = DraftCollection }, ProjectDraft.Create(DraftsRoot), draft: true);
        Watch(null);
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task PackProjectAsync()
    {
        var target = await dialogs.PickSaveFileAsync("Exportar proyecto", Project!.CollectionName + ProjectPackage.Extension, "Paquete EasyClothes", ProjectPackage.Extension);
        if (target is not null) await PackProjectAsync(target);
    }

    public async Task PackProjectAsync(string packagePath)
    {
        if (IsDraft)
        {
            Status = "Guarda el proyecto antes de exportarlo.";
            return;
        }
        await SaveProjectFileAsync();
        var projectPath = ProjectPathFor(ProjectDir!, Project!);
        IsBusy = true;
        try
        {
            var files = await Task.Run(() => ProjectPackage.Export(projectPath, packagePath));
            Status = $"Proyecto exportado a {packagePath} ({files} archivos).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudo exportar el proyecto: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnpackProjectAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar proyecto", "Paquete EasyClothes", ["*" + ProjectPackage.Extension], multiple: false);
        if (files.Count == 0) return;
        if (await dialogs.PickFolderAsync("Carpeta vacía donde extraer el proyecto") is not { } dir) return;
        await UnpackProjectAsync(files[0], dir);
    }

    public async Task UnpackProjectAsync(string packagePath, string targetDir)
    {
        try
        {
            var projectPath = await Task.Run(() => ProjectPackage.Import(packagePath, targetDir));
            await OpenProjectFileAsync(projectPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = $"No se pudo importar el proyecto: {ex.Message}";
        }
    }

    private bool CanEditTexture => !IsBusy && Selected is not null && SelectedTexture is not null && ProjectDir is not null;

    [RelayCommand(CanExecute = nameof(CanEditTexture))]
    private async Task EditTextureAsync()
    {
        if (await CreateTextureEditorAsync() is not { } editor) return;
        await dialogs.EditTextureAsync(editor);
        ApplyTextureEdit(editor);
    }

    public async Task<TextureEditorViewModel?> CreateTextureEditorAsync()
    {
        var drawable = Selected!;
        var texture = SelectedTexture!;
        try
        {
            var path = Path.Combine(ProjectDir!, texture.RelativePath);
            var (document, note) = await Task.Run(() => TextureDocumentFile.Open(path));
            var preview = Preview;
            return new TextureEditorViewModel(dialogs, ProjectDir!, drawable.Model, texture.Index, document, preview?.Meshes ?? [], preview?.Body ?? [], note);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Status = $"No se pudo abrir la textura: {ex.Message}";
            return null;
        }
    }

    public void ApplyTextureEdit(TextureEditorViewModel editor)
    {
        if (editor.Saved is not { } saved || Selected is not { } drawable) return;
        drawable.RefreshTextures();
        MarkChanged(rebuildTree: false);
        SelectedTexture = drawable.Textures[saved.Index];
        _ = LoadPreviewAsync();
        Status = saved.BackupPath is { } backup
            ? $"Textura {TextureVariation.LetterFor(saved.Index)} actualizada. Copia del original en {backup}."
            : $"Nueva textura {TextureVariation.LetterFor(saved.Index)} creada a partir de la edición.";
    }

    // The MCP server ships next to the app, so Claude can work on projects directly.
    public string McpServerPath { get; set; } = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "EasyClothes.Mcp.exe" : "EasyClothes.Mcp");

    [RelayCommand]
    private async Task ConnectClaudeAsync()
    {
        if (!File.Exists(McpServerPath))
        {
            Status = $"No se encuentra {Path.GetFileName(McpServerPath)} junto a la app; publícalo en la misma carpeta.";
            return;
        }
        var args = ClaudeConnection.ServerArgs(Settings.BaseBodyFolder);
        var command = ClaudeConnection.ClaudeCodeCommand(McpServerPath, args);
        var desktop = ClaudeConnection.DesktopConfigPath;
        string[] options = desktop is null ? ["Copiar comando de Claude Code", "Cancelar"] : ["Añadir a Claude Desktop", "Copiar comando de Claude Code", "Cancelar"];
        var choice = await dialogs.ChooseAsync("Conectar con Claude",
            "Claude podrá abrir tus proyectos, importar y ordenar prendas, revisar problemas, ver las prendas renderizadas, editar texturas y generar el recurso. " +
            "Cada cambio guarda una copia en .backups y esta ventana se recarga sola.", options);
        if (choice is null || options[choice.Value] == "Cancelar") return;

        if (options[choice.Value] == "Copiar comando de Claude Code")
        {
            await dialogs.CopyToClipboardAsync(command);
            Status = $"Comando copiado; pégalo en una terminal: {command}";
            return;
        }
        try
        {
            ClaudeConnection.AddToDesktop(desktop!, McpServerPath, args);
            Status = "EasyClothes añadido a Claude Desktop. Ciérralo del todo (también desde la bandeja) y vuelve a abrirlo.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Status = $"No se pudo modificar la configuración de Claude Desktop ({desktop}): {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChooseBodyFolderAsync()
    {
        if (await dialogs.PickFolderAsync("Carpeta con mp_m_freemode_01 y mp_f_freemode_01 exportadas del juego") is { } folder)
            SetBodyFolder(folder);
        else
            OnPropertyChanged(nameof(ShowBody));
    }

    public bool SetBodyFolder(string folder)
    {
        if (BaseBody.ResolveRoot(folder) is not { } root)
        {
            Status = "Esa carpeta no contiene mp_m_freemode_01 ni mp_f_freemode_01.";
            OnPropertyChanged(nameof(ShowBody));
            return false;
        }
        Settings.BaseBodyFolder = root;
        Settings.ShowBody = true;
        SaveSettings();
        _bodyParts.Clear();
        OnPropertyChanged(nameof(ShowBody));
        OnPropertyChanged(nameof(BodyTip));
        Status = $"Personaje cargado desde {root}.";
        _ = LoadPreviewAsync();
        return true;
    }

    private void SaveSettings()
    {
        try
        {
            Settings.Save(SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudieron guardar los ajustes: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChooseFirstPersonAsync()
    {
        if (Selected is not { } drawable || ProjectDir is not { } dir) return;
        var files = await dialogs.PickFilesAsync("Modelo de primera persona", "Modelo de ropa", ["*.ydd"], multiple: false);
        if (files.Count == 0) return;
        AttachExtra(drawable, () => DrawableImporter.AttachFirstPerson(dir, drawable.Model, files[0]), "Modelo de primera persona añadido.");
    }

    [RelayCommand]
    private void ClearFirstPerson()
    {
        if (Selected is not { } drawable) return;
        AttachExtra(drawable, () => drawable.Model.FirstPersonModelPath = null, "Modelo de primera persona quitado.");
    }

    [RelayCommand]
    private async Task ChooseClothPhysicsAsync()
    {
        if (Selected is not { } drawable || ProjectDir is not { } dir) return;
        var files = await dialogs.PickFilesAsync("Físicas de tela", "Físicas de tela", ["*.yld"], multiple: false);
        if (files.Count == 0) return;
        AttachExtra(drawable, () => DrawableImporter.AttachClothPhysics(dir, drawable.Model, files[0]), "Físicas de tela añadidas.");
    }

    [RelayCommand]
    private void ClearClothPhysics()
    {
        if (Selected is not { } drawable) return;
        AttachExtra(drawable, () => drawable.Model.ClothPhysicsPath = null, "Físicas de tela quitadas.");
    }

    private void AttachExtra(DrawableViewModel drawable, Action change, string done)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudo copiar el archivo: {ex.Message}";
            return;
        }
        drawable.RefreshExtras();
        MarkChanged(rebuildTree: false);
        Status = done;
    }

    [RelayCommand]
    private async Task DetectHeelAsync()
    {
        if (Selected is not { } drawable || ProjectDir is not { } dir) return;
        var model = Path.Combine(dir, drawable.Model.ModelPath);
        float? height;
        try
        {
            height = await Task.Run(() => HeelEstimator.Estimate(model));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Status = $"No se pudo leer el modelo: {ex.Message}";
            return;
        }
        if (height is null)
        {
            Status = $"{drawable.Name}: la suela está a ras de suelo, no parece un tacón.";
            return;
        }
        drawable.Model.Flags.HeelHeight = height;
        drawable.RefreshExtras();
        MarkChanged(rebuildTree: false);
        Status = $"{drawable.Name}: tacón detectado, altura {height:0.0}.";
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar prendas", "Modelos de ropa", ["*.ydd"], multiple: true);
        if (files.Count > 0) await ImportDrawablesAsync(files);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportFolderAsync()
    {
        if (await dialogs.PickFolderAsync("Carpeta con prendas (se busca también en subcarpetas)") is { } dir)
            await ImportDrawablesAsync([dir]);
    }

    public async Task<IReadOnlyList<Drawable>> ImportDrawablesAsync(IReadOnlyList<string> paths)
    {
        EnsureProject();
        var fallback = Selected?.Model.Gender ?? PedGender.Male;
        var wasEmpty = Project!.Drawables.Count == 0;
        // The import runs on a copy so validation and the tree never see the list change under them.
        var staging = new ClothingProject { Name = Project.Name, CollectionName = Project.CollectionName, Drawables = [.. Project.Drawables] };
        var dir = ProjectDir!;
        IsBusy = true;
        Status = "Importando prendas…";
        ResourceImportResult result;
        try
        {
            result = await Task.Run(() => ResourceImporter.ImportAny(staging, dir, paths, fallback));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudo importar: {ex.Message}";
            return [];
        }
        finally
        {
            IsBusy = false;
        }

        Project.Drawables.AddRange(result.Added);
        if (wasEmpty && result.Collection is { } collection)
        {
            Project.CollectionName = collection;
            OnPropertyChanged(nameof(CollectionName));
        }
        MarkChanged(rebuildTree: true);
        Status = $"{result.Added.Count(d => !d.Reserved)} prenda(s) importada(s)." + WarningsSummary(result.Warnings);
        if (result.Added.LastOrDefault() is { } last) Select(last.Id);
        return result.Added;
    }

    [RelayCommand]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveDown() => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        if (Selected is not { } drawable || !DrawableOrder.Move(Project!, drawable.Model, offset)) return;
        MarkChanged(rebuildTree: true);
        Status = $"{drawable.Name} pasa a ser el número {DrawableOrder.NumberOf(Project!, drawable.Model):000}.";
    }

    [RelayCommand]
    private void ReserveSlot()
    {
        if (Selected is not { } drawable) return;
        var reserved = DrawableOrder.Reserve(Project!, drawable.Model.Gender, drawable.Model.Component, drawable.Model.Prop);
        MarkChanged(rebuildTree: true);
        Select(reserved.Id);
        Status = $"Número {DrawableOrder.NumberOf(Project!, reserved):000} reservado; asígnale una prenda cuando la tengas.";
    }

    [RelayCommand]
    private async Task FillReservedAsync()
    {
        if (Selected is not { IsReserved: true } reserved) return;
        var files = await dialogs.PickFilesAsync("Prenda para el hueco reservado", "Modelos de ropa", ["*.ydd"], multiple: false);
        if (files.Count == 0) return;
        if (await ImportDrawablesAsync(files) is not [var garment]) return;
        DrawableOrder.Fill(Project!, reserved.Model, garment);
        MarkChanged(rebuildTree: true);
        Select(garment.Id);
        Status = $"{garment.Name} ocupa el número {DrawableOrder.NumberOf(Project!, garment):000}.";
    }

    // Models and folders win over images, so dropping a whole stream folder's files imports clothes, not tattoos.
    public async Task DropAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy) return;
        if (paths is [var single] && single.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenProjectFileAsync(single);
            return;
        }
        var clothes = paths.Where(p => Directory.Exists(p) || p.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase)).ToList();
        if (clothes.Count > 0)
        {
            await ImportDrawablesAsync(clothes);
            return;
        }
        var images = paths.Where(p => ImageLoader.Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList();
        if (images.Count > 0) ImportTattoos(images);
        else Status = "Arrastra modelos .ydd, carpetas con prendas, imágenes de tatuajes o un proyecto .ecproj.";
    }

    // Big packs can produce hundreds of warnings; the status bar only has room for a few.
    private static string WarningsSummary(IReadOnlyList<string> warnings) => warnings.Count switch
    {
        0 => "",
        <= 3 => " " + string.Join(" ", warnings),
        _ => $" {warnings.Count} avisos, p. ej.: {warnings[0]}",
    };

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task BuildResourceAsync()
    {
        var parent = await dialogs.PickFolderAsync("Carpeta donde crear el recurso de FiveM");
        if (parent is null) return;
        await BuildResourceAsync(Path.Combine(parent, Project!.CollectionName));
    }

    public async Task BuildResourceAsync(string outputDir)
    {
        var snapshot = ProjectFile.Deserialize(ProjectFile.Serialize(Project!));
        var dir = ProjectDir!;
        IsBusy = true;
        Status = "Generando el recurso…";
        try
        {
            var result = await Task.Run(() => ResourceExporter.Export(snapshot, dir, outputDir));
            Status = $"Recurso generado en {result.OutputDir}: {result.Drawables} prenda(s), {result.Files} archivo(s)."
                + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
        }
        catch (ExportException ex)
        {
            Status = $"No se generó el recurso: hay {ex.Errors.Count} error(es) en el panel de problemas.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Status = $"No se generó el recurso: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportTattoosAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar tatuajes", "Imágenes", ImageLoader.Extensions.Select(e => "*" + e).ToList(), multiple: true);
        if (files.Count > 0) ImportTattoos(files);
    }

    public void ImportTattoos(IReadOnlyList<string> files)
    {
        EnsureProject();
        var result = TattooImporter.Import(Project!, ProjectDir!, files, TattooZone.Torso);
        MarkChanged(rebuildTree: true);
        Status = $"{result.Added.Count} tatuaje(s) importado(s)." + WarningsSummary(result.Warnings);
        if (result.Added.LastOrDefault() is { } last) Select(last.Id);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedTattoo is { } tattoo) Project!.Tattoos.Remove(tattoo.Model);
        else Project!.Drawables.Remove(Selected!.Model);
        MarkChanged(rebuildTree: true);
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task ApplyFixAsync(IssueViewModel? issue)
    {
        if (issue?.Issue.Fix is not { } fix) return;
        await RunFixesAsync([fix]);
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task FixAllAsync()
    {
        await RunFixesAsync(IssueFixer.OnePerFile(Issues.Select(i => i.Issue.Fix).OfType<FixAction>()));
    }

    private async Task RunFixesAsync(IReadOnlyList<FixAction> fixes)
    {
        if (fixes.Count == 0) return;
        IsBusy = true;
        try
        {
            var dir = ProjectDir!;
            var messages = new List<string>();
            for (var i = 0; i < fixes.Count; i++)
            {
                Status = $"Corrigiendo {i + 1}/{fixes.Count}: {fixes[i].Label}…";
                var fix = fixes[i];
                try
                {
                    messages.Add(await Task.Run(() => IssueFixer.Apply(fix, dir)));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
                {
                    messages.Add($"{IssueFixer.TargetOf(fix)}: no se pudo corregir ({ex.Message}).");
                }
            }
            Status = fixes.Count == 1 ? messages[0] : $"{fixes.Count} correcciones aplicadas. {messages.Last()}";
        }
        finally
        {
            IsBusy = false;
        }
        await ValidateAsync();
        await LoadPreviewAsync();
    }

    [RelayCommand]
    private void SelectIssue(IssueViewModel? issue)
    {
        if (issue?.Issue.DrawableId is { } id) Select(id);
    }

    partial void OnSelectedNodeChanged(TreeNode? value)
    {
        if (value is null) return;
        var sameDrawable = Selected?.Model.Id == value.Drawable?.Model.Id;
        var textureIndex = sameDrawable ? SelectedTexture?.Index ?? 0 : 0;
        Selected = value.Drawable;
        SelectedTattoo = value.Tattoo;
        SelectedTexture = Selected?.Textures.ElementAtOrDefault(textureIndex) ?? Selected?.Textures.FirstOrDefault();
        if (Selected is null || SelectedTexture is null) _ = LoadPreviewAsync();
    }

    partial void OnSelectedTextureChanged(TextureItem? value) => _ = LoadPreviewAsync();

    private async Task LoadPreviewAsync()
    {
        var version = ++_previewVersion;
        if (SelectedTattoo is { } tattoo && ProjectDir is not null)
        {
            var imagePath = Path.Combine(ProjectDir, tattoo.Model.TexturePath);
            try
            {
                var image = await Task.Run(() => Preview.Image(imagePath));
                if (version == _previewVersion) Preview = image;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                if (version == _previewVersion) Preview = new Preview([], null, $"No se pudo cargar: {ex.Message}");
            }
            return;
        }
        if (Selected is null || ProjectDir is null)
        {
            Preview = null;
            return;
        }

        var modelPath = Path.Combine(ProjectDir, Selected.Model.ModelPath);
        var texturePath = SelectedTexture is null ? null : Path.Combine(ProjectDir, SelectedTexture.RelativePath);
        var body = BodyFor(Selected.Model);
        try
        {
            var preview = await Task.Run(() => Preview.Load(modelPath, texturePath) with { Body = LoadBody(body) });
            if (version == _previewVersion) Preview = preview;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            if (version == _previewVersion) Preview = new Preview([], null, $"No se pudo cargar: {ex.Message}");
        }
    }

    // Props sit on a bone rather than in body space, so they are shown alone until the skeleton is read.
    private IReadOnlyList<BodyPart> BodyFor(Drawable drawable) =>
        ShowBody && drawable.Component is { } slot ? BaseBody.Parts(Settings.BaseBodyFolder!, drawable.Gender, slot) : [];

    private IReadOnlyList<RenderItem> LoadBody(IReadOnlyList<BodyPart> parts)
    {
        var items = new List<RenderItem>();
        foreach (var part in parts)
        {
            try
            {
                items.Add(_bodyParts.GetOrAdd((part.ModelPath, part.TexturePath), key => Preview.LoadPart(key.Item1, key.Item2)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                // A damaged body part only costs that piece of the mannequin, never the garment preview.
            }
        }
        return items;
    }

    private void LoadProject(ClothingProject project, string dir, bool draft = false)
    {
        Project = project;
        ProjectDir = dir;
        IsDraft = draft;
        IsDirty = false;
        OnPropertyChanged(nameof(CollectionName));
        RebuildTree();
        _ = ValidateAsync();
    }

    private void MarkChanged(bool rebuildTree)
    {
        IsDirty = true;
        if (rebuildTree) RebuildTree();
        _ = ValidateAsync();
    }

    private void OnDrawableChanged(DrawableViewModel drawable, bool regroup)
    {
        if (regroup) DrawableOrder.PlaceAtEnd(Project!, drawable.Model);
        if (!regroup && FindNode(drawable.Model.Id) is { } node) node.Title = drawable.Name;
        MarkChanged(rebuildTree: regroup);
    }

    private void OnTattooChanged(TattooViewModel tattoo, bool regroup)
    {
        if (!regroup && FindNode(tattoo.Model.Id) is { } node) node.Title = tattoo.Name;
        MarkChanged(rebuildTree: regroup);
    }

    private void RebuildTree()
    {
        var selectedId = Selected?.Model.Id ?? SelectedTattoo?.Model.Id;
        Tree.Clear();
        if (Project is null) return;

        foreach (var gender in GenderOption.All)
        {
            var genderNode = new TreeNode(gender.Label);
            foreach (var slot in SlotOption.All)
            {
                var drawables = ClothingProject.InGameOrder(Project.DrawablesFor(gender.Value).Where(d => SlotOption.For(d) == slot));
                if (drawables.Count == 0) continue;
                var slotNode = new TreeNode($"{slot.Label} ({drawables.Count})");
                for (var i = 0; i < drawables.Count; i++)
                    slotNode.Children.Add(new TreeNode(drawables[i].Name, new DrawableViewModel(drawables[i], OnDrawableChanged) { Number = i }, number: $"{i:000}"));
                genderNode.Children.Add(slotNode);
            }
            if (genderNode.Children.Count > 0) Tree.Add(genderNode);
        }

        if (Project.Tattoos.Count > 0)
        {
            var tattoosNode = new TreeNode($"Tatuajes ({Project.Tattoos.Count})");
            foreach (var zone in TattooZoneOption.All)
            {
                var tattoos = Project.Tattoos.Where(t => t.Zone == zone.Value).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (tattoos.Count == 0) continue;
                var zoneNode = new TreeNode($"{zone.Label} ({tattoos.Count})");
                foreach (var t in tattoos)
                    zoneNode.Children.Add(new TreeNode(t.Name, tattoo: new TattooViewModel(t, OnTattooChanged)));
                tattoosNode.Children.Add(zoneNode);
            }
            Tree.Add(tattoosNode);
        }

        if (selectedId is { } id && FindNode(id) is { } node)
            SelectedNode = node;
        else
        {
            SelectedNode = null;
            Selected = null;
            SelectedTattoo = null;
            SelectedTexture = null;
        }
    }

    private void Select(Guid id)
    {
        if (FindNode(id) is { } node) SelectedNode = node;
    }

    private TreeNode? FindNode(Guid id) =>
        Tree.SelectMany(g => g.Children).SelectMany(s => s.Children).FirstOrDefault(n => (n.Drawable?.Model.Id ?? n.Tattoo?.Model.Id) == id);

    private async Task ValidateAsync()
    {
        var version = ++_validationVersion;
        if (Project is not { } project || ProjectDir is not { } dir)
        {
            Issues.Clear();
            OnPropertyChanged(nameof(IssueSummary));
            return;
        }

        IsAnalyzing = true;
        var snapshot = ProjectFile.Deserialize(ProjectFile.Serialize(project));
        IReadOnlyList<ValidationIssue> issues;
        try
        {
            issues = await Task.Run(() => ProjectValidator.Validate(snapshot, dir, _analysisCache));
        }
        catch (IOException ex)
        {
            issues = [new ValidationIssue(IssueSeverity.Error, $"No se pudo analizar el proyecto: {ex.Message}")];
        }
        if (version != _validationVersion) return;

        Issues.Clear();
        foreach (var issue in issues.OrderBy(i => i.Severity)) Issues.Add(new IssueViewModel(issue));
        IsAnalyzing = false;
        OnPropertyChanged(nameof(HasFixes));
    }

    private static string ProjectPathFor(string dir, ClothingProject project) =>
        Path.Combine(dir, ClothingProject.CollectionNameFrom(project.Name) + ProjectFile.Extension);
}
