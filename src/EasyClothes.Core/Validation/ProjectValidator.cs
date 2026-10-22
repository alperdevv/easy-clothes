using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Validation;

public static class ProjectValidator
{
    // Without a cache only the project structure is checked; with one, every asset is read and analysed.
    public static IReadOnlyList<ValidationIssue> Validate(ClothingProject project, string projectDir, AnalysisCache? cache = null, AnalysisLimits? limits = null)
    {
        limits ??= new AnalysisLimits();
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(project.CollectionName) || project.CollectionName.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_'))
            issues.Add(new(IssueSeverity.Error, "El nombre de colección solo puede tener minúsculas, números y guiones bajos."));

        foreach (var d in project.Drawables)
        {
            var modelFile = Path.Combine(projectDir, d.ModelPath);
            if (!File.Exists(modelFile))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta el modelo {d.ModelPath}.", d.Id));
            else if (cache is not null)
                AnalyzeModel(issues, d, d.ModelPath, modelFile, cache, limits);
            if (d.FirstPersonModelPath is { } firstPerson && !File.Exists(Path.Combine(projectDir, firstPerson)))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta el modelo de primera persona {firstPerson}.", d.Id));
            if (d.ClothPhysicsPath is { } cloth && !File.Exists(Path.Combine(projectDir, cloth)))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: faltan las físicas de tela {cloth}.", d.Id));
            if (d.Textures.Count == 0)
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: no tiene texturas.", d.Id));
            if (d.Textures.Count > Drawable.MaxTextures)
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: {d.Textures.Count} texturas, el máximo es {Drawable.MaxTextures}.", d.Id));
            foreach (var t in d.Textures)
            {
                var textureFile = Path.Combine(projectDir, t.TexturePath);
                if (!File.Exists(textureFile))
                    issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta la textura {t.TexturePath}.", d.Id));
                else if (cache is not null)
                    AnalyzeTexture(issues, d, t.TexturePath, textureFile, cache, limits);
            }
            if (d.Component is null == d.Prop is null)
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: debe ser una componente o un prop.", d.Id));
        }

        foreach (var group in project.Drawables.GroupBy(d => (d.Gender, d.Name)).Where(g => g.Count() > 1))
            issues.Add(new(IssueSeverity.Warning, $"Hay {group.Count()} prendas llamadas {group.Key.Name} ({group.Key.Gender})."));

        foreach (var t in project.Tattoos)
        {
            if (!File.Exists(Path.Combine(projectDir, t.TexturePath)))
                issues.Add(new(IssueSeverity.Error, $"Tatuaje {t.Name}: falta la imagen {t.TexturePath}."));
            if (t.Genders.Count == 0)
                issues.Add(new(IssueSeverity.Error, $"Tatuaje {t.Name}: elige al menos un sexo."));
        }

        foreach (var group in project.Drawables.GroupBy(d => (d.Gender, d.Component, d.Prop)).Where(g => g.Count() > PedVariation.MaxDrawablesPerSlot))
            issues.Add(new(IssueSeverity.Error, $"{group.Count()} prendas en el mismo hueco ({group.Key.Gender}); el máximo es {PedVariation.MaxDrawablesPerSlot}."));
        if (project.Drawables.Where(d => d.IsProp).GroupBy(d => d.Gender).FirstOrDefault(g => g.Count() > PedVariation.MaxDrawablesPerSlot) is { } props)
            issues.Add(new(IssueSeverity.Error, $"{props.Count()} props ({props.Key}); el máximo por sexo es {PedVariation.MaxDrawablesPerSlot}."));

        return issues;
    }

    private static void AnalyzeModel(List<ValidationIssue> issues, Drawable d, string relative, string file, AnalysisCache cache, AnalysisLimits limits)
    {
        ModelReport report;
        try
        {
            report = cache.Model(file);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            issues.Add(new(IssueSeverity.Error, $"{d.Name}: el modelo no se puede leer ({ex.Message}).", d.Id));
            return;
        }

        CheckOversized(issues, d, relative, report.SystemSize, report.GraphicsSize);
        if (report.High is { } high && high.Triangles > limits.MaxHighLodTriangles)
            issues.Add(new(IssueSeverity.Warning, $"{d.Name}: {high.Triangles:N0} triángulos en el LOD alto (recomendado ≤ {limits.MaxHighLodTriangles:N0}).", d.Id,
                new ReduceMeshFix(relative, limits.MaxHighLodTriangles)));
        if (report.UvInvalid > 0)
            issues.Add(new(IssueSeverity.Error, $"{d.Name}: {report.UvInvalid} vértices con UV no válidas (NaN o infinito).", d.Id));
        if (report.Vertices > 0 && report.UvOutOfRange > report.Vertices * limits.UvOutOfRangeRatio)
            issues.Add(new(IssueSeverity.Info, $"{d.Name}: {report.UvOutOfRange * 100 / report.Vertices}% de las UV fuera de 0..1 (la textura se repetirá).", d.Id));
        if (report.DegenerateTriangles > 0)
            issues.Add(new(IssueSeverity.Info, $"{d.Name}: {report.DegenerateTriangles} triángulos degenerados.", d.Id));
        if (report.Lods.All(l => l.Level != Rage.LodLevel.Medium))
            issues.Add(new(IssueSeverity.Info, $"{d.Name}: no tiene LOD medio; se verá con todo el detalle a cualquier distancia.", d.Id));
    }

    private static void AnalyzeTexture(List<ValidationIssue> issues, Drawable d, string relative, string file, AnalysisCache cache, AnalysisLimits limits)
    {
        TextureReport report;
        try
        {
            report = cache.Texture(file);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            issues.Add(new(IssueSeverity.Error, $"{d.Name}: la textura {ShortName(relative)} no se puede leer ({ex.Message}).", d.Id));
            return;
        }

        CheckOversized(issues, d, relative, report.SystemSize, report.GraphicsSize);
        var name = ShortName(relative);
        foreach (var t in report.Textures)
        {
            var side = Math.Max(t.Width, t.Height);
            if (side > limits.MaxTextureSide)
                issues.Add(new(IssueSeverity.Warning, $"{d.Name}: {name} mide {t.Width}x{t.Height} (recomendado ≤ {limits.MaxTextureSide}).", d.Id,
                    new ResizeTextureFix(relative, limits.MaxTextureSide)));
            if (!int.IsPow2(t.Width) || !int.IsPow2(t.Height))
                issues.Add(new(IssueSeverity.Warning, $"{d.Name}: {name} mide {t.Width}x{t.Height}, que no es potencia de dos.", d.Id,
                    new ResizeTextureFix(relative, Math.Clamp(RoundDownPow2(side), 4, limits.MaxTextureSide))));
            if (t.MipLevels <= 1 && side > 64)
                issues.Add(new(IssueSeverity.Warning, $"{d.Name}: {name} no tiene mipmaps (parpadeo a distancia y más memoria de vídeo).", d.Id,
                    new RegenerateMipsFix(relative)));
        }
    }

    private static void CheckOversized(List<ValidationIssue> issues, Drawable d, string relative, int systemSize, int graphicsSize)
    {
        var largest = Math.Max(systemSize, graphicsSize);
        if (largest > AnalysisLimits.FiveMOversizedBytes)
            issues.Add(new(IssueSeverity.Error, $"{d.Name}: {ShortName(relative)} ocupa {largest / 1048576.0:N1} MiB en memoria; FiveM avisa a partir de 16 MiB y puede no cargarlo.", d.Id));
    }

    private static string ShortName(string path)
    {
        var name = Path.GetFileName(path);
        return name[(name.IndexOf('^') + 1)..];
    }

    private static int RoundDownPow2(int value) => 1 << (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)value));
}
