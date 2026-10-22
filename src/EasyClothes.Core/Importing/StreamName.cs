using System.Text.RegularExpressions;
using EasyClothes.Core.Model;

namespace EasyClothes.Core.Importing;

public sealed record SlotRef(ComponentSlot? Component, PropSlot? Prop)
{
    public string FileCode => Component is { } c ? PedSlotNames.FileCode(c) : PedSlotNames.FileCode(Prop!.Value);
}

// Alternate 1 is the first person model ("jbib_000_u_1.ydd"); Skin marks "_r" models that use the ped's skin tone.
public sealed record ModelName(PedGender? Gender, SlotRef Slot, int Index, int Alternate = 0, bool Skin = false);

public sealed record TextureName(PedGender? Gender, SlotRef Slot, int Index, int Variation);

// Parses streamed clothing file names, e.g. "mp_m_freemode_01_mp_m_eup^jbib_040_u.ydd",
// "mp_f_freemode_01_p_mp_f_eup^p_head_diff_012_c.ytd" or the bare "jbib_diff_040_a_uni.ytd".
public static partial class StreamName
{
    private static readonly Dictionary<string, SlotRef> SlotsByCode =
        Enum.GetValues<ComponentSlot>().Select(c => new SlotRef(c, null))
            .Concat(Enum.GetValues<PropSlot>().Select(p => new SlotRef(null, p)))
            .ToDictionary(s => s.FileCode);

    public static ModelName? ParseModel(string fileName)
    {
        var (gender, body) = Split(fileName);
        var match = ModelPattern().Match(body);
        if (!match.Success || !SlotsByCode.TryGetValue(match.Groups["code"].Value, out var slot)) return null;
        var alternate = match.Groups["alt"].Success ? int.Parse(match.Groups["alt"].Value) : 0;
        return new ModelName(gender, slot, int.Parse(match.Groups["index"].Value), alternate, match.Groups["kind"].Value == "r");
    }

    public static TextureName? ParseTexture(string fileName)
    {
        var (gender, body) = Split(fileName);
        var match = TexturePattern().Match(body);
        if (!match.Success || !SlotsByCode.TryGetValue(match.Groups["code"].Value, out var slot)) return null;
        return new TextureName(gender, slot, int.Parse(match.Groups["index"].Value), match.Groups["letter"].Value[0] - 'a');
    }

    // Packs often keep bare file names inside a folder named after the ped, e.g. "stream/mp_f_freemode_01_mp_f_eup/".
    public static PedGender? GenderFromFolders(string filePath)
    {
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (GenderOf(Path.GetFileName(dir).ToLowerInvariant()) is { } gender) return gender;
        }
        return null;
    }

    // The collection a streamed file belongs to: "mp_m_freemode_01_mp_m_eup^jbib_000_u.ydd" → "mp_m_eup",
    // also for props ("..._01_p_mine^p_head_000.ydd" → "mine") and bare names inside a "<ped>_<collection>" folder.
    public static string? CollectionOf(string filePath)
    {
        var name = Path.GetFileName(filePath).ToLowerInvariant();
        var caret = name.IndexOf('^');
        if (caret >= 0) return CollectionFromPrefix(name[..caret]);
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (CollectionFromPrefix(Path.GetFileName(dir).ToLowerInvariant()) is { } collection) return collection;
        }
        return null;
    }

    private static string? CollectionFromPrefix(string prefix)
    {
        foreach (var gender in Enum.GetValues<PedGender>())
        {
            var ped = PedSlotNames.PedModel(gender) + "_";
            if (!prefix.StartsWith(ped) || prefix.Length == ped.Length) continue;
            var rest = prefix[ped.Length..];
            return rest.StartsWith("p_") && rest.Length > 2 ? rest[2..] : rest;
        }
        return null;
    }

    private static PedGender? GenderOf(string prefix) =>
        prefix.StartsWith(PedSlotNames.PedModel(PedGender.Male)) ? PedGender.Male
        : prefix.StartsWith(PedSlotNames.PedModel(PedGender.Female)) ? PedGender.Female
        : null;

    private static (PedGender? Gender, string Body) Split(string fileName)
    {
        var name = Path.GetFileName(fileName).ToLowerInvariant();
        var caret = name.IndexOf('^');
        if (caret < 0) return (null, name);

        return (GenderOf(name[..caret]), name[(caret + 1)..]);
    }

    [GeneratedRegex(@"^(?<code>p_[a-z]+|[a-z]{4})_(?<index>\d{3})(_(?<kind>[a-z]))?(_(?<alt>\d))?\.ydd$")]
    private static partial Regex ModelPattern();

    [GeneratedRegex(@"^(?<code>p_[a-z]+|[a-z]{4})_diff_(?<index>\d{3})_(?<letter>[a-z])(_[a-z]{3})?\.ytd$")]
    private static partial Regex TexturePattern();
}
