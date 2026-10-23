using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace EasyClothes.App.Services;

public sealed class DialogService(TopLevel topLevel) : IDialogService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, string description, IReadOnlyList<string> patterns, bool multiple)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = patterns }],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (topLevel.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    public async Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> options)
    {
        int? choice = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                MaxWidth = 460,
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
            },
        };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var button = new Button { Content = options[i] };
            button.Click += (_, _) =>
            {
                choice = index;
                dialog.Close();
            };
            buttons.Children.Add(button);
        }
        var cancel = new Button { Content = "Cancelar" };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(cancel);

        if (topLevel is Window owner) await dialog.ShowDialog(owner);
        return choice;
    }

    public void OpenExternally(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public async Task EditTextureAsync(ViewModels.TextureEditorViewModel editor)
    {
        var window = new TextureEditorWindow(editor);
        if (topLevel is Window owner) await window.ShowDialog(owner);
    }

    public async Task DesignCapAsync(ViewModels.CapDesignerViewModel designer)
    {
        var window = new CapDesignerWindow(designer);
        if (topLevel is Window owner) await window.ShowDialog(owner);
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string description, string extension)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(description) { Patterns = ["*" + extension] }],
        });
        return file?.TryGetLocalPath();
    }
}
