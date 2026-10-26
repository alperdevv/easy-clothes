using EasyClothes.Core.Analysis;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Validation;

public sealed record FolderTextureChange(string File, string Fix, int OldGraphicsSize, int? NewGraphicsSize, string? Result);

public sealed record FolderOptimization(
    int Scanned,
    IReadOnlyList<FolderTextureChange> Changes,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> OversizedModels,
    string? BackupDir);

// Applies the validator's texture fixes to every .ytd of a folder, e.g. a resource that has no project.
public static class FolderOptimizer
{
    public static string DefaultBackupDir(string folder) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyClothes", "backups",
        $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}-{DateTime.Now:yyyyMMdd-HHmmss}");

    // With dryRun nothing is written and NewGraphicsSize stays null. Originals are copied to backupDir
    // with their relative paths before they change; it must be outside the folder so FiveM does not stream it.
    public static FolderOptimization Optimize(string folder, string backupDir, bool dryRun, AnalysisLimits? limits = null,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        limits ??= new AnalysisLimits();
        folder = Path.GetFullPath(folder);
        backupDir = Path.GetFullPath(backupDir);
        if (backupDir.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La copia de seguridad no puede ir dentro de la carpeta que se optimiza.", nameof(backupDir));

        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.')))
            .Where(f => f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var changes = new List<FolderTextureChange>();
        var failed = new List<string>();
        var oversizedModels = new List<string>();
        var done = 0;

        Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            var relative = Path.GetRelativePath(folder, file);
            try
            {
                if (file.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                {
                    var model = AssetAnalyzer.AnalyzeModel(file);
                    if (Math.Max(model.SystemSize, model.GraphicsSize) > AnalysisLimits.FiveMOversizedBytes)
                        lock (oversizedModels) oversizedModels.Add(relative);
                }
                else if (Optimize(file, relative, backupDir, dryRun, limits) is { } change)
                {
                    lock (changes) changes.Add(change);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            {
                lock (failed) failed.Add($"{relative}: {ex.Message}");
            }
            progress?.Report((Interlocked.Increment(ref done), files.Count));
        });

        return new FolderOptimization(files.Count, [.. changes.OrderBy(c => c.File, StringComparer.Ordinal)], [.. failed.Order()], [.. oversizedModels.Order()],
            !dryRun && changes.Count > 0 ? backupDir : null);
    }

    private static FolderTextureChange? Optimize(string file, string relative, string backupDir, bool dryRun, AnalysisLimits limits)
    {
        var report = AssetAnalyzer.AnalyzeTexture(file);
        var fixes = ProjectValidator.TextureIssues(report, relative, limits, Path.GetFileName(file)).Select(i => i.Fix).OfType<FixAction>();
        if (IssueFixer.OnePerFile(fixes).FirstOrDefault() is not { } fix) return null;
        if (dryRun) return new FolderTextureChange(relative, fix.Label, report.GraphicsSize, null, null);

        var backup = Path.Combine(backupDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(file, backup, overwrite: true);
        var result = TextureOptimizer.Optimize(file, IssueFixer.TextureSide(fix));
        return new FolderTextureChange(relative, fix.Label, result.OldGraphicsSize, result.NewGraphicsSize, IssueFixer.Describe(result));
    }
}
