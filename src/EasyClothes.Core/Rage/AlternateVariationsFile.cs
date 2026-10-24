using System.Xml;
using System.Xml.Linq;
using EasyClothes.Core.Model;

namespace EasyClothes.Core.Rage;

// pedalternatevariations.meta (CAlternateVariations): per ped, which component switches to its
// alternate model while certain other components are worn.
public static class AlternateVariationsFile
{
    public const string FileName = "pedalternatevariations.meta";
    public const string DataFileType = "ALTERNATE_VARIATIONS_FILE";

    // Null when the file is not an alternate variations meta; switches for other peds are skipped with a warning.
    public static List<AlternateVariation>? TryRead(string path, List<string> warnings)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (XmlException)
        {
            return null;
        }
        if (document.Root?.Name.LocalName != "CAlternateVariations") return null;

        var result = new List<AlternateVariation>();
        foreach (var ped in document.Root.Element("peds")?.Elements("Item") ?? [])
        {
            var name = ped.Element("name")?.Value.Trim() ?? "";
            var genders = Enum.GetValues<PedGender>().Where(g => PedSlotNames.PedModel(g).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (genders is not [var gender])
            {
                warnings.Add($"{Path.GetFileName(path)}: se ignoran los interruptores de {name}, solo se admiten los personajes freemode.");
                continue;
            }
            var malformed = 0;
            foreach (var item in ped.Element("switches")?.Elements("Item") ?? [])
            {
                var sources = (item.Element("sourceAssets")?.Elements("Item") ?? [])
                    .Select(s => (Component: Value(s, "component"), Index: Value(s, "index"))).ToList();
                if (Value(item, "component") is not { } component || Value(item, "index") is not { } index || Value(item, "alt") is not { } alt
                    || sources.Any(s => s.Component is null || s.Index is null))
                {
                    malformed++;
                    continue;
                }
                result.Add(new AlternateVariation
                {
                    Gender = gender,
                    Component = component,
                    Index = index,
                    Alt = alt,
                    Sources = sources.Select(s => new AlternateSource { Component = s.Component!.Value, Index = s.Index!.Value }).ToList(),
                });
            }
            if (malformed > 0)
                warnings.Add($"{Path.GetFileName(path)}: {malformed} interruptores de {name} están incompletos y se han ignorado.");
        }
        return result;
    }

    public static XDocument Write(IEnumerable<AlternateVariation> switches) => new(
        new XDeclaration("1.0", "UTF-8", null),
        new XElement("CAlternateVariations",
            new XElement("peds", switches.GroupBy(s => s.Gender).OrderBy(g => g.Key).Select(ped => new XElement("Item",
                new XElement("name", PedSlotNames.PedModel(ped.Key)),
                new XElement("switches", ped.Select(s => new XElement("Item",
                    Attr("component", s.Component),
                    Attr("index", s.Index),
                    Attr("alt", s.Alt),
                    new XElement("sourceAssets", s.Sources.Select(a => new XElement("Item",
                        Attr("component", a.Component),
                        Attr("index", a.Index))))))))))));

    private static XElement Attr(string name, int value) => new(name, new XAttribute("value", value));

    private static int? Value(XElement parent, string name) =>
        int.TryParse(parent.Element(name)?.Attribute("value")?.Value, out var value) ? value : null;
}
