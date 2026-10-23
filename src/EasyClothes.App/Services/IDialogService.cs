namespace EasyClothes.App.Services;

public interface IDialogService
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, string description, IReadOnlyList<string> patterns, bool multiple);
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string description, string extension);
    Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> options);
    void OpenExternally(string path);
    Task CopyToClipboardAsync(string text);
    Task EditTextureAsync(ViewModels.TextureEditorViewModel editor);
    Task DesignCapAsync(ViewModels.CapDesignerViewModel designer);
}
