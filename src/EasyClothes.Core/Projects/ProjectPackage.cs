using System.IO.Compression;

namespace EasyClothes.Core.Projects;

// A .ecpack is the project folder zipped (assets included, backups left out),
// so a project can be moved to another machine or shared in one file.
public static class ProjectPackage
{
    public const string Extension = ".ecpack";
    public const int KeptBackups = 20;

    public static int Export(string projectPath, string packagePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var tmp = packagePath + ".tmp";
        var count = 0;
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                if (relative.StartsWith(ProjectFile.BackupsFolder + "/") || relative.EndsWith(".tmp")) continue;
                if (InNestedProject(dir, file)) continue;
                if (Path.GetFullPath(file) == Path.GetFullPath(packagePath)) continue;
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                count++;
            }
        }
        File.Move(tmp, packagePath, overwrite: true);
        return count;
    }

    // Another project created in a subfolder is packed on its own, not inside this one.
    private static bool InNestedProject(string root, string file)
    {
        for (var d = Path.GetDirectoryName(file); d is not null && d.Length > root.Length; d = Path.GetDirectoryName(d))
            if (ProjectFolder.HasProject(d)) return true;
        return false;
    }

    // Returns the path of the extracted project file.
    public static string Import(string packagePath, string targetDir)
    {
        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
            throw new IOException($"La carpeta {targetDir} no está vacía.");
        var root = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(packagePath);
        var projects = zip.Entries.Where(e => !e.FullName.Contains('/') && e.FullName.EndsWith(ProjectFile.Extension)).ToList();
        if (projects.Count != 1)
            throw new InvalidDataException("El paquete debe contener exactamente un proyecto en su raíz.");

        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException($"Ruta no permitida en el paquete: {entry.FullName}");
            if (entry.FullName.EndsWith('/')) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
        return Path.Combine(root, projects[0].FullName);
    }

    // Copies the current project file aside before it is overwritten, keeping the newest few.
    public static string? BackupProjectFile(string projectPath)
    {
        if (!File.Exists(projectPath)) return null;
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, ProjectFile.BackupsFolder, "project");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(projectPath)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ProjectFile.Extension}");
        File.Copy(projectPath, target, overwrite: true);
        foreach (var old in Directory.GetFiles(dir, "*" + ProjectFile.Extension).OrderDescending().Skip(KeptBackups))
            File.Delete(old);
        return target;
    }
}
