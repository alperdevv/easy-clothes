using EasyClothes.Core.Model;

namespace EasyClothes.Core.Validation;

public enum IssueSeverity
{
    Error,
    Warning,
}

public sealed record ValidationIssue(IssueSeverity Severity, string Message, Guid? DrawableId = null);

public static class ProjectValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ClothingProject project, string projectDir)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(project.CollectionName) || project.CollectionName.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_'))
            issues.Add(new(IssueSeverity.Error, "El nombre de colección solo puede tener minúsculas, números y guiones bajos."));

        foreach (var d in project.Drawables)
        {
            if (!File.Exists(Path.Combine(projectDir, d.ModelPath)))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta el modelo {d.ModelPath}.", d.Id));
            if (d.Textures.Count == 0)
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: no tiene texturas.", d.Id));
            if (d.Textures.Count > Drawable.MaxTextures)
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: {d.Textures.Count} texturas, el máximo es {Drawable.MaxTextures}.", d.Id));
            foreach (var t in d.Textures.Where(t => !File.Exists(Path.Combine(projectDir, t.TexturePath))))
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: falta la textura {t.TexturePath}.", d.Id));
            if (d.Component is null == d.Prop is null)
                issues.Add(new(IssueSeverity.Error, $"{d.Name}: debe ser una componente o un prop.", d.Id));
        }

        foreach (var group in project.Drawables.GroupBy(d => (d.Gender, d.Name)).Where(g => g.Count() > 1))
            issues.Add(new(IssueSeverity.Warning, $"Hay {group.Count()} prendas llamadas {group.Key.Name} ({group.Key.Gender})."));

        return issues;
    }
}
