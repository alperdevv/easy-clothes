namespace EasyClothes.Core.Modeling;

public sealed record ShirtTemplate(string Name, ShirtDesign Design);

// The user's own shirt designs, in their own folder so caps and shirts never mix.
public sealed class ShirtTemplates(string folder)
{
    public static string DefaultFolder => Path.Combine(TemplateFiles.Root, "camisetas");

    public string Folder { get; } = folder;

    public IReadOnlyList<ShirtTemplate> List() =>
        TemplateFiles.List<ShirtDesign>(Folder, d => d.Clamped()).Select(t => new ShirtTemplate(t.Name, t.Design)).ToList();

    public ShirtTemplate? Find(string name) =>
        List().FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public ShirtTemplate Save(string name, ShirtDesign design) => new(TemplateFiles.Save(Folder, name, design.Clamped()), design.Clamped());

    public bool Delete(string name) => TemplateFiles.Delete(Folder, name);
}
