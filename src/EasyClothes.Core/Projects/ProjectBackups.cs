namespace EasyClothes.Core.Projects;

public static class ProjectBackups
{
    // Copies a project file under .backups/<timestamp>/ and returns the backup's project-relative path.
    public static string Copy(string projectDir, string relative)
    {
        var target = Path.Combine(ProjectFile.BackupsFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss"), relative);
        var full = Path.Combine(projectDir, target);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(Path.Combine(projectDir, relative), full, overwrite: true);
        return target.Replace('\\', '/');
    }
}
