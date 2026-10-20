using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.App.Services;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Validation;

namespace EasyClothes.App.ViewModels;

public sealed partial class MainWindowViewModel(IDialogService dialogs) : ObservableObject
{
    private readonly AnalysisCache _analysisCache = new();
    private int _previewVersion;
    private int _validationVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyFixCommand), nameof(FixAllCommand), nameof(BuildResourceCommand), nameof(PackProjectCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueSummary))]
    public partial bool IsAnalyzing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ImportCommand), nameof(ImportTattoosCommand))]
    public partial ClothingProject? Project { get; private set; }

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
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial DrawableViewModel? Selected { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTattooSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial TattooViewModel? SelectedTattoo { get; private set; }

    [ObservableProperty]
    public partial TextureItem? SelectedTexture { get; set; }

    [ObservableProperty]
    public partial Preview? Preview { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "Crea o abre un proyecto para empezar.";

    public ObservableCollection<TreeNode> Tree { get; } = [];
    public ObservableCollection<IssueViewModel> Issues { get; } = [];

    public bool HasProject => Project is not null;
    public bool HasFixes => Issues.Any(i => i.HasFix);
    private bool CanFix => !IsBusy && ProjectDir is not null;
    public bool HasSelection => Selected is not null;
    public bool HasTattooSelection => SelectedTattoo is not null;
    private bool CanRemove => HasSelection || HasTattooSelection;
    public string Title => Project is null ? "EasyClothes" : $"EasyClothes — {Project.Name}{(IsDirty ? " *" : "")}";

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
        var project = new ClothingProject { Name = name, CollectionName = ToCollectionName(name) };
        await ProjectFile.SaveAsync(project, ProjectPathFor(dir, project));
        LoadProject(project, dir);
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
        var path = ProjectPathFor(ProjectDir!, Project!);
        ProjectPackage.BackupProjectFile(path);
        await ProjectFile.SaveAsync(Project!, path);
        IsDirty = false;
        Status = "Proyecto guardado.";
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task PackProjectAsync()
    {
        var target = await dialogs.PickSaveFileAsync("Exportar proyecto", Project!.CollectionName + ProjectPackage.Extension, "Paquete EasyClothes", ProjectPackage.Extension);
        if (target is not null) await PackProjectAsync(target);
    }

    public async Task PackProjectAsync(string packagePath)
    {
        await SaveAsync();
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

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task ImportAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar prendas", "Modelos de ropa", ["*.ydd"], multiple: true);
        if (files.Count == 0) return;

        var fallback = Selected?.Model.Gender ?? PedGender.Male;
        var result = await Task.Run(() => DrawableImporter.Import(Project!, ProjectDir!, files, fallback));
        MarkChanged(rebuildTree: true);
        Status = $"{result.Added.Count} prenda(s) importada(s)." + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
        if (result.Added.LastOrDefault() is { } last) Select(last.Id);
    }

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

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task ImportTattoosAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar tatuajes", "Imágenes", ImageLoader.Extensions.Select(e => "*" + e).ToList(), multiple: true);
        if (files.Count > 0) ImportTattoos(files);
    }

    public void ImportTattoos(IReadOnlyList<string> files)
    {
        var result = TattooImporter.Import(Project!, ProjectDir!, files, TattooZone.Torso);
        MarkChanged(rebuildTree: true);
        Status = $"{result.Added.Count} tatuaje(s) importado(s)." + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
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
        // A resize also regenerates mips, so one fix per file is enough.
        var fixes = Issues.Select(i => i.Issue.Fix).OfType<FixAction>()
            .GroupBy(TargetOf)
            .Select(g => g.OfType<ResizeTextureFix>().FirstOrDefault() ?? g.First())
            .ToList();
        await RunFixesAsync(fixes);
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
                    messages.Add($"{TargetOf(fix)}: no se pudo corregir ({ex.Message}).");
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

    private static string TargetOf(FixAction fix) => fix switch
    {
        ResizeTextureFix f => f.TexturePath,
        RegenerateMipsFix f => f.TexturePath,
        ReduceMeshFix f => f.ModelPath,
        _ => fix.Label,
    };

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
        try
        {
            var preview = await Task.Run(() => Preview.Load(modelPath, texturePath));
            if (version == _previewVersion) Preview = preview;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            if (version == _previewVersion) Preview = new Preview([], null, $"No se pudo cargar: {ex.Message}");
        }
    }

    private void LoadProject(ClothingProject project, string dir)
    {
        Project = project;
        ProjectDir = dir;
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
                foreach (var d in drawables)
                    slotNode.Children.Add(new TreeNode(d.Name, new DrawableViewModel(d, OnDrawableChanged)));
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
        Path.Combine(dir, ToCollectionName(project.Name) + ProjectFile.Extension);

    private static string ToCollectionName(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return result.Length > 0 ? result : "pack";
    }
}
