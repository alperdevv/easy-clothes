using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace EasyClothes.App.Services;

public sealed class DialogService(TopLevel topLevel) : IDialogService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, string description, string pattern, bool multiple)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = [pattern] }],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
