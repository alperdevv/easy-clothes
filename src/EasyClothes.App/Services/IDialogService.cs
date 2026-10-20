namespace EasyClothes.App.Services;

public interface IDialogService
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, string description, IReadOnlyList<string> patterns, bool multiple);
    Task<string?> PickFolderAsync(string title);
}
