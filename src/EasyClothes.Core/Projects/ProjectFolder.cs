namespace EasyClothes.Core.Projects;

// Each project owns its folder: the .ecproj is named after it and packing zips all of it.
public static class ProjectFolder
{
    public static bool HasProject(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*" + ProjectFile.Extension).Any();

    // "D:\Ropa\Verano.ecproj" picked in a save dialog becomes the folder "D:\Ropa\Verano".
    public static string SubfolderFor(string pickedFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pickedFile))!, Path.GetFileNameWithoutExtension(pickedFile).Trim());
}
