using EasyClothes.Core.Model;

namespace EasyClothes.Core.Viewing;

public sealed record BodyPart(ComponentSlot Slot, string ModelPath, string? TexturePath);

// The freemode body, exported from the game by the user (game files are never shipped).
// Expects the folder that holds mp_m_freemode_01/ and mp_f_freemode_01/ with their .ydd and .ytd files.
public static class BaseBody
{
    // Variations from the base game archive that make a neutral mannequin.
    private static readonly Dictionary<PedGender, (ComponentSlot Slot, int Index)[]> Outfits = new()
    {
        [PedGender.Male] = [(ComponentSlot.Head, 0), (ComponentSlot.Hair, 0), (ComponentSlot.Torso, 15), (ComponentSlot.Legs, 0), (ComponentSlot.Shoes, 1)],
        [PedGender.Female] = [(ComponentSlot.Head, 0), (ComponentSlot.Hair, 0), (ComponentSlot.Torso, 15), (ComponentSlot.Top, 15), (ComponentSlot.Legs, 0), (ComponentSlot.Shoes, 3)],
    };

    // Accepts the parent folder or one of the ped folders inside it.
    public static string? ResolveRoot(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        foreach (var candidate in new[] { full, Path.GetDirectoryName(full) })
        {
            if (candidate is not null && Enum.GetValues<PedGender>().Any(g => Directory.Exists(Path.Combine(candidate, PedSlotNames.PedModel(g)))))
                return candidate;
        }
        return null;
    }

    // The worn slot is left out so the garment replaces the mannequin's piece instead of overlapping it.
    public static IReadOnlyList<BodyPart> Parts(string root, PedGender gender, ComponentSlot? worn)
    {
        var dir = Path.Combine(root, PedSlotNames.PedModel(gender));
        if (!Directory.Exists(dir)) return [];

        var parts = new List<BodyPart>();
        foreach (var (slot, index) in Outfits[gender])
        {
            if (slot == worn) continue;
            var code = PedSlotNames.FileCode(slot);
            var model = new[] { "r", "u" }.Select(s => Path.Combine(dir, $"{code}_{index:D3}_{s}.ydd")).FirstOrDefault(File.Exists);
            if (model is null) continue;
            var texture = Directory.EnumerateFiles(dir, $"{code}_diff_{index:D3}_a_*.ytd").Order(StringComparer.Ordinal).FirstOrDefault();
            parts.Add(new BodyPart(slot, model, texture));
        }
        return parts;
    }
}
