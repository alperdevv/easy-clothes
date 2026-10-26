using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

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

        foreach (var d in project.Drawables.Where(d => !d.Reserved))
        {
            var modelFile = Path.Combine(projectDir, d.ModelPath);
            if (!File.Exists(modelFile))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta el modelo {d.ModelPath}.", d.Id));
            else if (cache is not null)
                AnalyzeModel(issues, d, d.ModelPath, modelFile, cache, limits);
            if (d.FirstPersonModelPath is { } firstPerson && !File.Exists(Path.Combine(projectDir, firstPerson)))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta el modelo de primera persona {firstPerson}.", d.Id));
            foreach (var alternate in d.AlternateModels.Where(a => !File.Exists(Path.Combine(projectDir, a.ModelPath))))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta el modelo alternativo {alternate.ModelPath}.", d.Id));
            if (Enumerable.Range(1, d.AlternateCount()).FirstOrDefault(k => d.AlternateModelPath(k) is null) is var gap and > 0)
                issues.Add(new(IssueSeverity.Warning, $"{d.Name}: tiene el modelo alternativo _{d.AlternateCount()} pero no el _{gap}; el .ymt declarará {d.AlternateCount()} alternativos.", d.Id));
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

        if (cache is not null) FindDuplicates(issues, project, projectDir, cache);
        CheckAlternateSwitches(issues, project);

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

    // Switches use the game's absolute numbers, so the pack's first number in each slot is guessed as the
    // one that makes the most switches land on a garment with the requested "_k" model.
    private static void CheckAlternateSwitches(List<ValidationIssue> issues, ClothingProject project)
    {
        foreach (var group in project.AlternateVariations.GroupBy(s => (s.Gender, s.Component)))
        {
            if (!Enum.IsDefined((ComponentSlot)group.Key.Component)) continue;
            var slot = (ComponentSlot)group.Key.Component;
            var garments = ClothingProject.InGameOrder(project.DrawablesFor(group.Key.Gender).Where(d => d.Component == slot));
            var votes = new Dictionary<int, int>();
            foreach (var sw in group)
                for (var i = 0; i < garments.Count; i++)
                    if (garments[i].AlternateModelPath(sw.Alt) is not null && sw.Index >= i)
                        votes[sw.Index - i] = votes.GetValueOrDefault(sw.Index - i) + 1;

            var where = $"{PedSlotNames.FileCode(slot)} ({(group.Key.Gender == PedGender.Male ? "hombre" : "mujer")})";
            if (votes.Count == 0)
            {
                foreach (var alt in group.GroupBy(s => s.Alt))
                    issues.Add(new(IssueSeverity.Warning, $"pedalternatevariations.meta pide {alt.Count()} veces el modelo alternativo _{alt.Key} en {where} y ninguna prenda de ese hueco lo tiene; el juego no cambiará de modelo."));
                continue;
            }
            var first = votes.MaxBy(v => (v.Value, -v.Key)).Key;
            foreach (var target in group.Select(s => (Local: s.Index - first, s.Index, s.Alt)).Distinct())
            {
                if (target.Local < 0 || target.Local >= garments.Count || garments[target.Local] is { Reserved: true } || garments[target.Local].AlternateModelPath(target.Alt) is not null) continue;
                var d = garments[target.Local];
                issues.Add(new(IssueSeverity.Warning, $"{d.Name}: pedalternatevariations.meta pide su modelo alternativo _{target.Alt} (número {target.Index} del juego en {where}) y no lo tiene; el juego no cambiará de modelo.", d.Id));
            }
        }
    }

    // Same content means a double import or a copy that was never changed.
    private static void FindDuplicates(List<ValidationIssue> issues, ClothingProject project, string projectDir, AnalysisCache cache)
    {
        string? HashOf(string relative)
        {
            var file = Path.Combine(projectDir, relative);
            try
            {
                return File.Exists(file) ? cache.Hash(file) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        var garments = project.Drawables.Where(d => !d.Reserved).ToList();
        foreach (var group in garments.GroupBy(d => HashOf(d.ModelPath)).Where(g => g.Key is not null && g.Count() > 1))
        {
            var names = group.Select(d => d.Name).ToList();
            issues.Add(new(IssueSeverity.Warning, $"{string.Join(", ", names)}: usan el mismo modelo; ¿se importó dos veces?", group.Last().Id));
        }
        foreach (var d in garments)
        {
            foreach (var group in d.Textures.Select((t, i) => (Hash: HashOf(t.TexturePath), Letter: TextureVariation.LetterFor(i))).Where(t => t.Hash is not null).GroupBy(t => t.Hash).Where(g => g.Count() > 1))
                issues.Add(new(IssueSeverity.Warning, $"{d.Name}: las texturas {string.Join(", ", group.Select(t => t.Letter))} son idénticas.", d.Id));
        }
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

        issues.AddRange(TextureIssues(report, relative, limits, d.Name, d.Id));
    }

    // Shared with FolderOptimizer, which applies the same fixes to resources that have no project.
    public static IEnumerable<ValidationIssue> TextureIssues(TextureReport report, string relative, AnalysisLimits limits, string owner, Guid? drawableId = null)
    {
        var name = ShortName(relative);
        var largest = Math.Max(report.SystemSize, report.GraphicsSize);
        if (largest > AnalysisLimits.FiveMOversizedBytes)
        {
            FixAction? fix = null;
            if (report.Textures.Count > 0)
            {
                var side = SideThatFits(report.Textures, limits.MaxTextureSide);
                fix = side < report.Textures.Max(t => Math.Max(t.Width, t.Height)) ? new ResizeTextureFix(relative, side) : new CompressTextureFix(relative, side);
            }
            yield return new(IssueSeverity.Error, OversizedMessage(owner, name, largest), drawableId, fix);
        }
        foreach (var t in report.Textures)
        {
            var side = Math.Max(t.Width, t.Height);
            if (side > limits.MaxTextureSide)
                yield return new(IssueSeverity.Warning, $"{owner}: {name} mide {t.Width}x{t.Height} (recomendado ≤ {limits.MaxTextureSide}).", drawableId,
                    new ResizeTextureFix(relative, limits.MaxTextureSide));
            if (!int.IsPow2(t.Width) || !int.IsPow2(t.Height))
                yield return new(IssueSeverity.Warning, $"{owner}: {name} mide {t.Width}x{t.Height}, que no es potencia de dos.", drawableId,
                    new ResizeTextureFix(relative, Math.Clamp(RoundDownPow2(side), 4, limits.MaxTextureSide)));
            if (t.MipLevels <= 1 && side > 64)
                yield return new(IssueSeverity.Warning, $"{owner}: {name} no tiene mipmaps (parpadeo a distancia y más memoria de vídeo).", drawableId,
                    new RegenerateMipsFix(relative));
            if (t.PixelFormat is TextureFormat.A8R8G8B8 or TextureFormat.X8R8G8B8 or TextureFormat.A8B8G8R8 && side > 256)
                yield return new(IssueSeverity.Warning, $"{owner}: {name} está sin comprimir ({t.Format}); en DXT ocupa de 4 a 8 veces menos memoria.", drawableId,
                    new CompressTextureFix(relative, Math.Clamp(RoundDownPow2(side), 4, limits.MaxTextureSide)));
        }
    }

    // Largest power-of-two side whose DXT5 textures with mips stay under the FiveM limit, with room for the header.
    internal static int SideThatFits(IReadOnlyList<TextureInfo> textures, int maxSide)
    {
        var side = Math.Clamp(RoundDownPow2(textures.Max(t => Math.Max(t.Width, t.Height))), 4, maxSide);
        while (side > 4 && textures.Sum(t => DxtBytes(TextureOptimizer.TargetSize(t.Width, t.Height, side))) > AnalysisLimits.FiveMOversizedBytes - 64 * 1024)
            side /= 2;
        return side;
    }

    private static long DxtBytes((int Width, int Height) size) => (long)size.Width * size.Height * 4 / 3;

    private static string OversizedMessage(string owner, string name, int bytes) =>
        $"{owner}: {name} ocupa {bytes / 1048576.0:N1} MiB en memoria; FiveM avisa a partir de 16 MiB y puede no cargarlo.";

    private static void CheckOversized(List<ValidationIssue> issues, Drawable d, string relative, int systemSize, int graphicsSize)
    {
        var largest = Math.Max(systemSize, graphicsSize);
        if (largest > AnalysisLimits.FiveMOversizedBytes)
            issues.Add(new(IssueSeverity.Error, OversizedMessage(d.Name, ShortName(relative), largest), d.Id));
    }

    private static string ShortName(string path)
    {
        var name = Path.GetFileName(path);
        return name[(name.IndexOf('^') + 1)..];
    }

    private static int RoundDownPow2(int value) => 1 << (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)value));
}
