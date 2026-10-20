namespace EasyClothes.App.Services;

public interface IDialogService
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, string description, string pattern, bool multiple);
    Task<string?> PickFolderAsync(string title);
}
