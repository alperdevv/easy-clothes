using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.App.Services;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Modeling;
using EasyClothes.Core.Rage;
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
    [NotifyCanExecuteChangedFor(nameof(ApplyFixCommand), nameof(FixAllCommand), nameof(BuildResourceCommand), nameof(PackProjectCommand), nameof(ImportCommand), nameof(ImportFolderCommand), nameof(ImportTattoosCommand), nameof(EditTextureCommand), nameof(OptimizeFolderCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueSummary), nameof(IsClean))]
    public partial bool IsAnalyzing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(Title), nameof(IsClean))]
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
    public bool IsClean => HasProject && !IsAnalyzing && Issues.Count == 0;
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
        if (await dialogs.PickFolderAsync("Carpeta del nuevo proyecto") is not { } picked) return;
        if (await FreeProjectDirAsync(picked) is { } dir) await CreateProjectAsync(dir);
    }

    // A folder holds a single project; if it already has one, the new project goes in a subfolder named by the user.
    private async Task<string?> FreeProjectDirAsync(string dir)
    {
        if (!ProjectFolder.HasProject(dir)) return dir;
        var file = await dialogs.PickSaveFileAsync("Esa carpeta ya tiene un proyecto: nombre del nuevo", "Nuevo proyecto", "Proyecto EasyClothes", ProjectFile.Extension, dir);
        if (file is null) return null;
        var sub = ProjectFolder.SubfolderFor(file);
        if (!ProjectFolder.HasProject(sub)) return sub;
        Status = $"{sub} ya tiene un proyecto. Usa Abrir.";
        return null;
    }

    public async Task CreateProjectAsync(string dir)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
        var project = new ClothingProject { Name = name, CollectionName = ClothingProject.CollectionNameFrom(name) };
        var file = ProjectPathFor(dir, project);
        Directory.CreateDirectory(dir);
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
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
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
            await RefreshResourceAsync();
            return;
        }
        if (await dialogs.PickFolderAsync("Carpeta donde guardar el proyecto") is { } picked && await FreeProjectDirAsync(picked) is { } dir)
            await SaveDraftAsAsync(dir);
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
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
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

    public CapTemplates CapTemplates { get; set; } = new(CapTemplates.DefaultFolder);
    public ShirtTemplates ShirtTemplates { get; set; } = new(ShirtTemplates.DefaultFolder);

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
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
        if (ImportProblem(result) is var (title, message)) await dialogs.ChooseAsync(title, message, ["Entendido"]);
        return result.Added;
    }

    // The status bar is easy to miss, so names the importer cannot read and empty imports get a window.
    private static (string Title, string Message)? ImportProblem(ResourceImportResult result)
    {
        if (result.UnrecognizedNames.Count > 0)
        {
            var names = result.UnrecognizedNames.Select(Path.GetFileName).ToList();
            var list = string.Join("\n", names.Take(5).Select(n => "• " + n)) + (names.Count > 5 ? $"\n• … y {names.Count - 5} más" : "");
            var imported = result.Added.Count(d => !d.Reserved);
            return ("Nombre de archivo no reconocido",
                $"Estos modelos no se han importado porque su nombre no dice a qué hueco pertenecen:\n{list}\n\n"
                + "El nombre tiene que seguir el formato del juego, <hueco>_<número de 3 cifras>_u.ydd, por ejemplo "
                + "jbib_000_u.ydd (parte de arriba), lowr_000_u.ydd (pantalón), feet_000_u.ydd (calzado) o p_head_000.ydd (gorro). "
                + "Sus texturas van al lado con el mismo número: jbib_diff_000_a_uni.ytd, jbib_diff_000_b_uni.ytd…\n\n"
                + "Renombra los archivos e impórtalos otra vez."
                + (imported > 0 ? $" El resto ({imported} prenda(s)) sí se ha importado." : ""));
        }
        if (result.Added.Any(d => !d.Reserved)) return null;
        return ("No se ha importado ninguna prenda",
            result.Warnings.Count == 0
                ? "No se ha encontrado ningún modelo .ydd en lo que has elegido."
                : string.Join("\n", result.Warnings.Take(5).Select(w => "• " + w)) + (result.Warnings.Count > 5 ? $"\n• … y {result.Warnings.Count - 5} avisos más" : ""));
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // Set when a newer release exists, so the top bar can point at it.
    [ObservableProperty]
    public partial string UpdateLabel { get; private set; } = "Actualizar";

    [ObservableProperty]
    public partial bool UpdateAvailable { get; private set; }

    // The window closes when the installer takes over.
    public event Action? ExitRequested;

    // At start: only changes the button when there is something new, never interrupts.
    public async Task CheckForUpdatesQuietlyAsync()
    {
        try
        {
            ShowUpdate(await Updater.CheckAsync(Http, Updater.CurrentVersion));
        }
        catch (Exception ex) when (IsUpdateFailure(ex))
        {
            // No connection or GitHub unavailable: the button still lets the user try later.
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        var current = Updater.CurrentVersion;
        Status = "Buscando una versión nueva…";
        UpdateInfo? update;
        try
        {
            update = await Updater.CheckAsync(Http, current);
        }
        catch (Exception ex) when (IsUpdateFailure(ex))
        {
            Status = $"No se pudo comprobar si hay una versión nueva: {ex.Message}";
            return;
        }
        ShowUpdate(update);
        if (update is null)
        {
            Status = $"EasyClothes {current} es la última versión.";
            await dialogs.ChooseAsync("EasyClothes está al día", $"Tienes la última versión ({current}).", ["Entendido"]);
            return;
        }

        var notes = update.Notes.Length == 0 ? "" : "\n\nNovedades:\n" + (update.Notes.Length > 800 ? update.Notes[..800] + "…" : update.Notes);
        var message = $"Hay una versión nueva de EasyClothes: la {update.Version} (tienes la {current}). Se descargará, la app se cerrará, "
            + "se instalará y se volverá a abrir sola. Tus proyectos no se tocan." + notes;
        if (await dialogs.ChooseAsync("Versión nueva", message, [IsDirty ? "Guardar y actualizar" : "Actualizar ahora", "Más tarde"]) != 0)
        {
            Status = "";
            return;
        }
        if (IsDirty)
        {
            await SaveAsync();
            if (IsDirty) return;
        }

        IsBusy = true;
        Status = $"Descargando EasyClothes {update.Version}…";
        string installer;
        try
        {
            installer = await Updater.DownloadAsync(Http, update, Path.Combine(Path.GetTempPath(), "EasyClothes-update"));
        }
        catch (Exception ex) when (IsUpdateFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            Status = $"No se pudo descargar la actualización: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }
        if (!OperatingSystem.IsWindows())
        {
            Status = $"Instalador descargado en {installer}; ejecútalo en Windows.";
            return;
        }
        // /RELAUNCH is ours: the installer script opens the app again when it finishes.
        Process.Start(new ProcessStartInfo(installer, "/SILENT /CLOSEAPPLICATIONS /RELAUNCH") { UseShellExecute = true });
        ExitRequested?.Invoke();
    }

    private void ShowUpdate(UpdateInfo? update)
    {
        UpdateAvailable = update is not null;
        UpdateLabel = update is null ? "Actualizar" : $"Actualizar a {update.Version}";
    }

    private static bool IsUpdateFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException;

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

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task NewGarmentAsync()
    {
        var gender = Selected?.Model.Gender ?? PedGender.Male;
        var creator = new GarmentCreatorViewModel(GarmentKind.Shirt, NewDesigner, gender);
        await dialogs.CreateGarmentAsync(creator);
        var designer = creator.Current;
        if (designer.Action != DesignerAction.Create) return;

        EnsureProject();
        var name = string.IsNullOrWhiteSpace(designer.Name) ? GarmentCreatorViewModel.DefaultName(GarmentCreatorViewModel.KindOf(designer)) : designer.Name.Trim();
        var garment = designer switch
        {
            ShirtDesignerViewModel { Torso: { } torso } shirt =>
                await RunGarmentJobAsync(() => ShirtGarment.Create(Project!, ProjectDir!, shirt.Design, shirt.Gender.Value, torso, name, shirt.MoreColours)),
            CapDesignerViewModel cap => await RunGarmentJobAsync(() => CapGarment.Create(Project!, ProjectDir!, cap.Design, cap.Gender.Value, name)),
            _ => null,
        };
        if (garment is null) return;
        MarkChanged(rebuildTree: true);
        Select(garment.Id);
        Status = $"{garment.Name}: creada. Edita su textura para añadir logos, o pulsa «Editar prenda» para cambiar forma y colores.";
    }

    private IGarmentDesigner NewDesigner(GarmentKind kind, PedGender gender) => kind switch
    {
        GarmentKind.Shirt => new ShirtDesignerViewModel(new ShirtDesign(), gender, editing: false, ShirtServices()),
        _ => new CapDesignerViewModel(new CapDesign(), gender, editing: false, CapStageFor, CapTemplates),
    };

    // Pictures picked in the designer are absolute; those already in a garment are relative to the project.
    private ShirtDesignerServices ShirtServices() => new(
        ShirtStageFor,
        ShirtTemplates,
        async () => (await dialogs.PickFilesAsync("Imagen para estampar", "Imágenes", ["*.png", "*.ytd"], multiple: false)).FirstOrDefault(),
        path => ShirtGarment.LoadGraphic(ProjectDir ?? "", path));

    [RelayCommand]
    private async Task EditGarmentAsync()
    {
        if (Selected is not { } selected) return;
        var drawable = selected.Model;
        IGarmentDesigner? designer = drawable switch
        {
            { Cap: { } cap } => new CapDesignerViewModel(cap, drawable.Gender, editing: true, CapStageFor, CapTemplates),
            { Shirt: { } shirt } => new ShirtDesignerViewModel(shirt, drawable.Gender, editing: true, ShirtServices()),
            _ => null,
        };
        if (designer is null) return;
        await dialogs.CreateGarmentAsync(new GarmentCreatorViewModel(designer));

        switch (designer.Action)
        {
            case DesignerAction.Reshape:
                var backup = designer switch
                {
                    ShirtDesignerViewModel { Torso: { } torso } shirt => await RunGarmentJobAsync(() => ShirtGarment.Reshape(ProjectDir!, drawable, shirt.Design, torso)),
                    CapDesignerViewModel cap => await RunGarmentJobAsync(() => CapGarment.Reshape(ProjectDir!, drawable, cap.Design)),
                    _ => null,
                };
                if (backup is null) return;
                Status = $"Forma de {drawable.Name} actualizada; el modelo anterior está en {backup}.";
                break;
            case DesignerAction.AddColours:
                var index = designer switch
                {
                    ShirtDesignerViewModel { Torso: { } torso } shirt =>
                        await RunGarmentJobAsync(() => (int?)ShirtGarment.AddColourway(ProjectDir!, drawable, shirt.Look, torso)),
                    CapDesignerViewModel cap => await RunGarmentJobAsync(() => (int?)CapGarment.AddColourway(ProjectDir!, drawable, cap.Design.Colours)),
                    _ => null,
                };
                if (index is not { } added) return;
                selected.RefreshTextures();
                SelectedTexture = selected.Textures[added];
                Status = $"Textura {TextureVariation.LetterFor(added)} añadida a {drawable.Name}.";
                break;
            default:
                return;
        }
        MarkChanged(rebuildTree: false);
        _ = LoadPreviewAsync();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportModelAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar modelo 3D", "Modelos .obj", ["*.obj"], multiple: false);
        if (files.Count == 0) return;
        if (await ReadObjAsync(files[0]) is not { } model) return;

        var gender = Selected?.Model.Gender ?? PedGender.Male;
        var placement = new ModelPlacementViewModel(dialogs, model, files[0], StartingPlacement(model.Mesh), gender, PropSlot.Hat, editing: false, ModelStageFor)
        {
            ExportReferenceHead = ExportReferenceHeadAsync,
        };
        await dialogs.PlaceModelAsync(placement);
        if (!placement.Confirmed) return;

        EnsureProject();
        var name = string.IsNullOrWhiteSpace(placement.Name) ? "Modelo" : placement.Name.Trim();
        var source = files[0];
        if (await RunGarmentJobAsync(() => ObjProp.Create(Project!, ProjectDir!, source, placement.TexturePath, placement.Slot.Value, placement.Gender.Value, name, placement.Placement)) is not { } prop) return;
        MarkChanged(rebuildTree: true);
        Select(prop.Id);
        Status = string.Join(" ", new[] { $"{prop.Name} importado como accesorio." }.Concat(model.Warnings));
    }

    [RelayCommand]
    private async Task PlaceImportedModelAsync()
    {
        if (Selected is not { Model.Imported: { } imported } selected) return;
        if (await ReadObjAsync(Path.Combine(ProjectDir!, imported.SourcePath)) is not { } model) return;
        var texture = SelectedTexture is null ? null : Preview?.Texture;
        var placement = new ModelPlacementViewModel(dialogs, model, imported.SourcePath, imported.Placement, selected.Model.Gender, selected.Model.Prop ?? PropSlot.Hat,
            editing: true, ModelStageFor, texture) { ExportReferenceHead = ExportReferenceHeadAsync };
        await dialogs.PlaceModelAsync(placement);
        if (!placement.Confirmed) return;
        var drawable = selected.Model;
        if (await RunGarmentJobAsync(() => ObjProp.Replace(ProjectDir!, drawable, placement.Placement)) is not { } backup) return;
        MarkChanged(rebuildTree: false);
        _ = LoadPreviewAsync();
        Status = $"{drawable.Name} recolocado; el modelo anterior está en {backup}.";
    }

    // Moves a prop from a pack in place; every change starts from the model as it was imported.
    [RelayCommand]
    private async Task AdjustPropAsync()
    {
        if (Selected is not { Model: { Prop: { } slot, Reserved: false } drawable }) return;
        MeshData original;
        try
        {
            original = await Task.Run(() => PropAdjuster.LoadOriginal(ProjectDir!, drawable));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Status = $"No se pudo leer el modelo de {drawable.Name}: {ex.Message}";
            return;
        }
        var placement = new ModelPlacementViewModel(dialogs, original, drawable.Name, $"Colocar {drawable.Name}", drawable.Adjusted?.Placement ?? new PropPlacement(),
            drawable.Gender, slot, editing: true, ModelStageFor, (mesh, p, s) => PropAdjuster.Place(mesh, s, p), fit: null, Preview?.Texture);
        await dialogs.PlaceModelAsync(placement);
        if (!placement.Confirmed) return;
        if (await RunGarmentJobAsync(() => PropAdjuster.Apply(ProjectDir!, drawable, placement.Placement)) is not { } backup) return;
        MarkChanged(rebuildTree: false);
        _ = LoadPreviewAsync();
        Status = $"{drawable.Name} recolocado; el modelo anterior está en {backup}.";
    }

    private async Task<ObjModel?> ReadObjAsync(string path)
    {
        try
        {
            return await Task.Run(() => ObjReader.Read(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = $"No se pudo leer {Path.GetFileName(path)}: {ex.Message}";
            return null;
        }
    }

    // Models made on the reference head are already in place; anything else starts auto-fitted.
    private static PropPlacement StartingPlacement(MeshData obj)
    {
        var (min, max) = MeshBounds.Of([ObjProp.Place(obj, new PropPlacement())]);
        var size = (max - min).Length();
        var centre = (min + max) / 2;
        return size is > 0.05f and < 0.8f && centre.Length() < 0.4f ? new PropPlacement() : ObjProp.Fit(obj);
    }

    private CapStage ModelStageFor(PedGender gender, PropSlot slot)
    {
        if (!ShowBody) return new CapStage([], null);
        var fit = BaseBody.Fit(Settings.BaseBodyFolder!, gender, null, slot);
        return new CapStage(LoadBody(fit.Parts), fit.Anchor);
    }

    private async Task<string?> ExportReferenceHeadAsync(PedGender gender, string name)
    {
        if (Settings.BaseBodyFolder is not { } folder || BaseBody.ResolveRoot(folder) is not { } root)
            return "Configura la carpeta del personaje (Mostrar cuerpo) para poder exportar la cabeza de referencia.";
        if (await dialogs.PickSaveFileAsync("Guardar cabeza de referencia", "cabeza_referencia.obj", "Modelo 3D", ".obj") is not { } path) return null;
        try
        {
            await Task.Run(() => ObjProp.ExportReferenceHead(root, gender, path));
            return $"Cabeza guardada en {path}. Es del juego: úsala solo para modelar, no la publiques.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            return $"No se pudo exportar la cabeza: {ex.Message}";
        }
    }

    private async Task<T?> RunGarmentJobAsync<T>(Func<T> job)
    {
        IsBusy = true;
        try
        {
            return await Task.Run(job);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            Status = $"No se pudo guardar la prenda: {ex.Message}";
            return default;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Runs off the UI thread while the designer previews. A shirt is cut from the body, so the body is
    // loaded even when the mannequin is hidden.
    private ShirtStage ShirtStageFor(PedGender gender)
    {
        if (Settings.BaseBodyFolder is not { } folder || BaseBody.ResolveRoot(folder) is not { } root) return new ShirtStage([], null);
        var mannequin = ShowBody ? LoadBody(BaseBody.Parts(root, gender, ComponentSlot.Top)) : [];
        return new ShirtStage(mannequin, BaseBody.Torso(root, gender));
    }

    // Runs off the UI thread while the designer previews.
    private CapStage CapStageFor(PedGender gender)
    {
        if (!ShowBody) return new CapStage([], null);
        var fit = BaseBody.Fit(Settings.BaseBodyFolder!, gender, null, PropSlot.Hat);
        return new CapStage(LoadBody(fit.Parts), fit.Anchor);
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

    // After Guardar, the generated resource is rebuilt so its stream files match what the app shows.
    private async Task RefreshResourceAsync()
    {
        if (_projectFile is null || !Settings.ResourceFolders.TryGetValue(_projectFile, out var outputDir)) return;
        if (!Directory.Exists(Path.GetDirectoryName(outputDir)))
        {
            Status += $" El recurso no se ha actualizado: no existe {Path.GetDirectoryName(outputDir)}.";
            return;
        }
        await BuildResourceAsync(outputDir);
        Status = Status.StartsWith("Recurso generado")
            ? $"Proyecto guardado y recurso actualizado en {outputDir}."
            : $"Proyecto guardado. {Status}";
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
            if (_projectFile is not null && Settings.ResourceFolders.GetValueOrDefault(_projectFile) != outputDir)
            {
                Settings.ResourceFolders[_projectFile] = outputDir;
                SaveSettings();
            }
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

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task OptimizeFolderAsync()
    {
        if (await dialogs.PickFolderAsync("Recurso o carpeta con texturas (.ytd) a optimizar") is { } folder) await OptimizeFolderAsync(folder);
    }

    // Lists what would change first and asks, since a built resource has no project history to undo it.
    public async Task OptimizeFolderAsync(string folder)
    {
        var backup = FolderOptimizer.DefaultBackupDir(folder);
        var progress = new Progress<(int Done, int Total)>(p => Status = $"Revisando {p.Done}/{p.Total}…");
        IsBusy = true;
        try
        {
            var plan = await Task.Run(() => FolderOptimizer.Optimize(folder, backup, dryRun: true, progress: progress));
            var models = plan.OversizedModels.Count > 0 ? $" {plan.OversizedModels.Count} modelo(s) .ydd pasan de 16 MiB y no se tocan." : "";
            if (plan.Changes.Count == 0)
            {
                Status = $"No hay texturas que optimizar en {plan.Scanned} archivo(s).{models}";
                return;
            }

            var before = plan.Changes.Sum(c => (long)c.OldGraphicsSize) / 1048576;
            var choice = await dialogs.ChooseAsync("Optimizar recurso",
                $"Se van a comprimir o reducir {plan.Changes.Count} textura(s), que ahora ocupan {before:N0} MiB de memoria.{models}\n\nLos originales se copian a {backup}.",
                ["Optimizar", "Cancelar"]);
            if (choice != 0)
            {
                Status = "Optimización cancelada.";
                return;
            }

            progress = new Progress<(int Done, int Total)>(p => Status = $"Optimizando {p.Done}/{p.Total}…");
            var result = await Task.Run(() => FolderOptimizer.Optimize(folder, backup, dryRun: false, progress: progress));
            var after = result.Changes.Sum(c => (long)(c.NewGraphicsSize ?? 0)) / 1048576;
            var failed = result.Failed.Count > 0 ? $" {result.Failed.Count} no se pudieron leer, p. ej.: {result.Failed[0]}" : "";
            Status = $"{result.Changes.Count} textura(s) optimizadas: {before:N0} → {after:N0} MiB. Originales en {result.BackupDir}.{failed}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"No se pudo optimizar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
            var preview = await Task.Run(() => Preview.Load(modelPath, texturePath, body.Anchor) with { Body = LoadBody(body.Parts) });
            if (version == _previewVersion) Preview = preview;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            if (version == _previewVersion) Preview = new Preview([], null, $"No se pudo cargar: {ex.Message}");
        }
    }

    private BodyFit BodyFor(Drawable drawable) =>
        ShowBody ? BaseBody.Fit(Settings.BaseBodyFolder!, drawable.Gender, drawable.Component, drawable.Prop) : BodyFit.None;

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
            var genderNode = new TreeNode(gender.Label, icon: gender.Value == PedGender.Male ? "Icon.Mars" : "Icon.Venus");
            foreach (var slot in SlotOption.All)
            {
                var drawables = ClothingProject.InGameOrder(Project.DrawablesFor(gender.Value).Where(d => SlotOption.For(d) == slot));
                if (drawables.Count == 0) continue;
                var slotNode = new TreeNode(slot.Label, icon: slot.Icon, count: drawables.Count);
                for (var i = 0; i < drawables.Count; i++)
                    slotNode.Children.Add(new TreeNode(drawables[i].Name, new DrawableViewModel(drawables[i], OnDrawableChanged) { Number = i }, number: $"{i:000}"));
                genderNode.Children.Add(slotNode);
            }
            if (genderNode.Children.Count > 0) Tree.Add(genderNode);
        }

        if (Project.Tattoos.Count > 0)
        {
            var tattoosNode = new TreeNode("Tatuajes", icon: "Icon.Stamp", count: Project.Tattoos.Count);
            foreach (var zone in TattooZoneOption.All)
            {
                var tattoos = Project.Tattoos.Where(t => t.Zone == zone.Value).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (tattoos.Count == 0) continue;
                var zoneNode = new TreeNode(zone.Label, count: tattoos.Count);
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
            OnPropertyChanged(nameof(IsClean));
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
