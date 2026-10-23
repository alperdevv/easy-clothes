namespace EasyClothes.Core.Modeling;

public sealed record CapTemplate(string Name, CapDesign Design);

// The user's own cap designs.
public sealed class CapTemplates(string folder)
{
    public static string DefaultFolder => TemplateFiles.Root;

    public string Folder { get; } = folder;

    public IReadOnlyList<CapTemplate> List() =>
        TemplateFiles.List<CapDesign>(Folder, d => d.Clamped()).Select(t => new CapTemplate(t.Name, t.Design)).ToList();

    public CapTemplate? Find(string name) =>
        List().FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public CapTemplate Save(string name, CapDesign design) => new(TemplateFiles.Save(Folder, name, design.Clamped()), design.Clamped());

    public bool Delete(string name) => TemplateFiles.Delete(Folder, name);
}
