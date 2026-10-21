using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyClothes.App.ViewModels;
using EasyClothes.Core.Viewing;

namespace EasyClothes.App;

public partial class TextureEditorWindow : Window
{
    private bool _closeConfirmed;

    public TextureEditorWindow() : this(null!)
    {
    }

    public TextureEditorWindow(TextureEditorViewModel editor)
    {
        InitializeComponent();
        Editor = editor;
        DataContext = editor;
        if (editor is null) return;

        Canvas.Editor = editor;
        Viewport.ShowScene([new RenderItem(editor.Meshes, editor.Composed)], editor.Body);
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TextureEditorViewModel.Composed)) Viewport.ShowItemTexture(editor.Composed);
        };
        editor.CloseRequested += () =>
        {
            _closeConfirmed = true;
            Close();
        };
        Closing += OnClosing;
        Closed += (_, _) => editor.Dispose();
    }

    public TextureEditorViewModel Editor { get; }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !Editor.HasUnsavedChanges) return;
        e.Cancel = true;
        if (!await Editor.ConfirmDiscardAsync()) return;
        _closeConfirmed = true;
        Close();
    }
}
