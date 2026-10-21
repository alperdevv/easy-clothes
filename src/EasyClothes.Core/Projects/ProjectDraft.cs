namespace EasyClothes.Core.Projects;

// Imports made before a project exists go to a draft folder; saving moves it to where the user chooses.
public static class ProjectDraft
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyClothes", "drafts");

    public static string Create(string root)
    {
        DeleteStale(root);
        var dir = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Copies first and deletes the draft last, so a failure halfway leaves the draft intact.
    public static void MoveTo(string draftDir, string targetDir)
    {
        var files = Directory.EnumerateFiles(draftDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(draftDir, f))
            .ToList();
        if (Directory.Exists(targetDir) && Directory.EnumerateFiles(targetDir, "*" + ProjectFile.Extension).Any())
            throw new IOException("Esa carpeta ya tiene un proyecto.");
        if (files.FirstOrDefault(f => File.Exists(Path.Combine(targetDir, f))) is { } clash)
            throw new IOException($"Esa carpeta ya contiene {clash}.");

        foreach (var file in files)
        {
            var target = Path.Combine(targetDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(draftDir, file), target);
        }
        try
        {
            Directory.Delete(draftDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover draft is removed later by DeleteStale.
        }
    }

    private static void DeleteStale(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (DateTime.Now - Directory.GetCreationTime(dir) < StaleAfter) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
