using EasyClothes.Core.Meshes;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Validation;

public static class IssueFixer
{
    public static string Apply(FixAction fix, string projectDir)
    {
        var relative = fix switch
        {
            ResizeTextureFix f => f.TexturePath,
            RegenerateMipsFix f => f.TexturePath,
            ReduceMeshFix f => f.ModelPath,
            _ => throw new NotSupportedException(fix.GetType().Name),
        };
        var path = Path.Combine(projectDir, relative);
        var backup = Backup(projectDir, relative);

        var summary = fix switch
        {
            ResizeTextureFix f => Describe(TextureOptimizer.Optimize(path, f.MaxSide)),
            RegenerateMipsFix => Describe(TextureOptimizer.Optimize(path, int.MaxValue)),
            ReduceMeshFix f => Describe(MeshReducer.Reduce(path, f.TargetTriangles)),
            _ => throw new NotSupportedException(fix.GetType().Name),
        };
        return $"{Path.GetFileName(relative)}: {summary} Copia del original en {backup}.";
    }

    private static string Backup(string projectDir, string relative)
    {
        var target = Path.Combine(ProjectFile.BackupsFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss"), relative);
        var full = Path.Combine(projectDir, target);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(Path.Combine(projectDir, relative), full, overwrite: true);
        return target.Replace('\\', '/');
    }

    private static string Describe(MeshReduction result) =>
        $"{result.OldTriangles:N0} → {result.NewTriangles:N0} triángulos, {result.OldVertices:N0} → {result.NewVertices:N0} vértices.";

    private static string Describe(TextureOptimization result) =>
        string.Join(", ", result.Changes.Select(c => $"{c.OldWidth}x{c.OldHeight} {c.OldFormat} → {c.NewWidth}x{c.NewHeight} {c.NewFormat.ToString().ToUpperInvariant()} ({c.Mips} mips)"))
        + $"; memoria {result.OldGraphicsSize / 1024:N0} → {result.NewGraphicsSize / 1024:N0} KiB.";
}
