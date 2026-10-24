using Avalonia.Controls;
using Avalonia.Input;
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
        AddHandler(KeyDownEvent, OnShortcutDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnShortcutUp, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) => editor.Dispose();
    }

    public TextureEditorViewModel Editor { get; }

    // Single-key shortcuts must not fire while typing a layer name or a colour code.
    private bool Typing => FocusManager?.GetFocusedElement() is TextBox;

    private void OnShortcutDown(object? sender, KeyEventArgs e)
    {
        if (Typing) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Space:
                Canvas.SpaceHeld = true;
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                Canvas.FitToView();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                Canvas.ActualPixels();
                break;
            case Key.OemPlus or Key.Add when ctrl:
                Canvas.ZoomBy(1.25);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                Canvas.ZoomBy(0.8);
                break;
            case Key.V when !ctrl:
                Editor.Tool = EditorTool.Move;
                break;
            case Key.B when !ctrl:
                Editor.Tool = EditorTool.Brush;
                break;
            case Key.E when !ctrl:
                Editor.Tool = EditorTool.Eraser;
                break;
            case Key.H when !ctrl:
                Editor.Tool = EditorTool.Hand;
                break;
            case Key.S when !ctrl:
                Editor.Tool = EditorTool.EraseMark;
                break;
            case Key.OemOpenBrackets:
                Editor.BrushSize = Math.Max(1, Editor.BrushSize / 1.2);
                break;
            case Key.OemCloseBrackets:
                Editor.BrushSize = Math.Min(1024, Editor.BrushSize * 1.2);
                break;
            case Key.Delete when Editor.DeleteLayerCommand.CanExecute(null):
                Editor.DeleteLayerCommand.Execute(null);
                break;
            default:
                e.Handled = false;
                break;
        }
        if (e.Handled) Canvas.InvalidateVisual();
    }

    private void OnShortcutUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        Canvas.SpaceHeld = false;
        Canvas.InvalidateVisual();
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Canvas.ZoomBy(1.25);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Canvas.ZoomBy(0.8);
    private void OnFit(object? sender, RoutedEventArgs e) => Canvas.FitToView();
    private void OnActualPixels(object? sender, RoutedEventArgs e) => Canvas.ActualPixels();

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
