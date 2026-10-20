using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyClothes.App.Services;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Validation;

namespace EasyClothes.App.ViewModels;

public sealed partial class MainWindowViewModel(IDialogService dialogs) : ObservableObject
{
    private int _previewVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ImportCommand))]
    public partial ClothingProject? Project { get; private set; }

    [ObservableProperty]
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
    public partial TextureItem? SelectedTexture { get; set; }

    [ObservableProperty]
    public partial Preview? Preview { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "Crea o abre un proyecto para empezar.";

    public ObservableCollection<TreeNode> Tree { get; } = [];
    public ObservableCollection<IssueViewModel> Issues { get; } = [];

    public bool HasProject => Project is not null;
    public bool HasSelection => Selected is not null;
    public string Title => Project is null ? "EasyClothes" : $"EasyClothes — {Project.Name}{(IsDirty ? " *" : "")}";

    public string IssueSummary
    {
        get
        {
            var errors = Issues.Count(i => i.IsError);
            return Issues.Count == 0 ? "Sin problemas" : $"{errors} errores · {Issues.Count - errors} avisos";
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
        var files = await dialogs.PickFilesAsync("Abrir proyecto", "Proyecto EasyClothes", "*" + ProjectFile.Extension, multiple: false);
        if (files.Count > 0) await OpenProjectFileAsync(files[0]);
    }

    public async Task OpenProjectFileAsync(string path)
    {
        try
        {
            LoadProject(await ProjectFile.LoadAsync(path), Path.GetDirectoryName(Path.GetFullPath(path))!);
            Status = $"Proyecto abierto: {path}";
            if (Project!.Drawables.FirstOrDefault() is { } first) Select(first.Id);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Status = $"No se pudo abrir el proyecto: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task SaveAsync()
    {
        await ProjectFile.SaveAsync(Project!, ProjectPathFor(ProjectDir!, Project!));
        IsDirty = false;
        Status = "Proyecto guardado.";
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task ImportAsync()
    {
        var files = await dialogs.PickFilesAsync("Importar prendas", "Modelos de ropa", "*.ydd", multiple: true);
        if (files.Count == 0) return;

        var fallback = Selected?.Model.Gender ?? PedGender.Male;
        var result = await Task.Run(() => DrawableImporter.Import(Project!, ProjectDir!, files, fallback));
        MarkChanged(rebuildTree: true);
        Status = $"{result.Added.Count} prenda(s) importada(s)." + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
        if (result.Added.LastOrDefault() is { } last) Select(last.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        Project!.Drawables.Remove(Selected!.Model);
        MarkChanged(rebuildTree: true);
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
        SelectedTexture = Selected?.Textures.ElementAtOrDefault(textureIndex) ?? Selected?.Textures.FirstOrDefault();
        if (Selected is null || SelectedTexture is null) _ = LoadPreviewAsync();
    }

    partial void OnSelectedTextureChanged(TextureItem? value) => _ = LoadPreviewAsync();

    private async Task LoadPreviewAsync()
    {
        var version = ++_previewVersion;
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
        Validate();
    }

    private void MarkChanged(bool rebuildTree)
    {
        IsDirty = true;
        if (rebuildTree) RebuildTree();
        Validate();
    }

    private void OnDrawableChanged(DrawableViewModel drawable, bool regroup)
    {
        if (!regroup && FindNode(drawable.Model.Id) is { } node) node.Title = drawable.Name;
        MarkChanged(rebuildTree: regroup);
    }

    private void RebuildTree()
    {
        var selectedId = Selected?.Model.Id;
        Tree.Clear();
        if (Project is null) return;

        foreach (var gender in GenderOption.All)
        {
            var genderNode = new TreeNode(gender.Label);
            foreach (var slot in SlotOption.All)
            {
                var drawables = Project.DrawablesFor(gender.Value).Where(d => SlotOption.For(d) == slot).ToList();
                if (drawables.Count == 0) continue;
                var slotNode = new TreeNode($"{slot.Label} ({drawables.Count})");
                foreach (var d in drawables)
                    slotNode.Children.Add(new TreeNode(d.Name, new DrawableViewModel(d, OnDrawableChanged)));
                genderNode.Children.Add(slotNode);
            }
            if (genderNode.Children.Count > 0) Tree.Add(genderNode);
        }

        if (selectedId is { } id && FindNode(id) is { } node)
            SelectedNode = node;
        else
        {
            SelectedNode = null;
            Selected = null;
            SelectedTexture = null;
        }
    }

    private void Select(Guid id)
    {
        if (FindNode(id) is { } node) SelectedNode = node;
    }

    private TreeNode? FindNode(Guid id) =>
        Tree.SelectMany(g => g.Children).SelectMany(s => s.Children).FirstOrDefault(n => n.Drawable?.Model.Id == id);

    private void Validate()
    {
        Issues.Clear();
        if (Project is not null)
        {
            foreach (var issue in ProjectValidator.Validate(Project, ProjectDir!).OrderBy(i => i.Severity))
                Issues.Add(new IssueViewModel(issue));
        }
        OnPropertyChanged(nameof(IssueSummary));
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
