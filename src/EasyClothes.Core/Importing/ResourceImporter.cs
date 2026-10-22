using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Importing;

public sealed record ResourceImportResult(IReadOnlyList<Drawable> Added, IReadOnlyList<string> Warnings, string? Collection);

// Brings in a finished resource: each .ymt says which garments exist and with which options,
// so every garment keeps its in-game number, and numbers without files become reserved.
public static class ResourceImporter
{
    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    private sealed record Variation(string Path, PedGender Gender, string Collection);

    private sealed record Asset(string Path, PedGender? Gender, string? Collection, SlotRef Slot, int Index);

    public static bool IsResource(string dir) => Directory.Exists(dir) && FindVariations(dir).Any();

    // Folders with a ped .ymt keep their numbers and options; everything else is imported garment by garment.
    public static ResourceImportResult ImportAny(ClothingProject project, string projectDir, IReadOnlyList<string> paths, PedGender fallbackGender)
    {
        var added = new List<Drawable>();
        var warnings = new List<string>();
        string? collection = null;
        var resources = paths.Where(IsResource).ToList();
        foreach (var resource in resources)
        {
            var result = Import(project, projectDir, resource);
            added.AddRange(result.Added);
            warnings.AddRange(result.Warnings);
            collection ??= result.Collection;
        }
        if (paths.Except(resources).ToList() is { Count: > 0 } loose)
        {
            var result = DrawableImporter.Import(project, projectDir, loose, fallbackGender);
            added.AddRange(result.Added);
            warnings.AddRange(result.Warnings);
        }
        return new ResourceImportResult(added, warnings, collection);
    }

    public static ResourceImportResult Import(ClothingProject project, string projectDir, string resourceDir)
    {
        var warnings = new List<string>();
        var added = new List<Drawable>();
        var ownAssets = Path.GetFullPath(projectDir) + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(resourceDir, "*", Recursive).Where(f => !Path.GetFullPath(f).StartsWith(ownAssets, StringComparison.OrdinalIgnoreCase)).ToList();

        var models = new List<(Asset Asset, ModelName Name)>();
        var textures = new List<(Asset Asset, int Variation)>();
        foreach (var file in files)
        {
            if (file.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase) && StreamName.ParseModel(file) is { Alternate: 0 } model)
                models.Add((new Asset(file, model.Gender ?? StreamName.GenderFromFolders(file), StreamName.CollectionOf(file), model.Slot, model.Index), model));
            else if (file.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) && StreamName.ParseTexture(file) is { } texture)
                textures.Add((new Asset(file, texture.Gender ?? StreamName.GenderFromFolders(file), StreamName.CollectionOf(file), texture.Slot, texture.Index), texture.Variation));
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variations = FindVariations(resourceDir).ToList();
        foreach (var variation in variations)
        {
            PedVariation content;
            try
            {
                content = PedVariation.Read(ResourceFile.Read(variation.Path));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                warnings.Add($"{Path.GetFileName(variation.Path)}: no se puede leer ({ex.Message}).");
                continue;
            }

            var slots = content.Components.Select(c => (Slot: new SlotRef(c.Key, null), Items: c.Value))
                .Concat(content.Props.Select(p => (Slot: new SlotRef(null, p.Key), Items: p.Value)));
            foreach (var (slot, items) in slots)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    bool Matches(Asset a) => a.Slot == slot && a.Index == i && (a.Gender ?? variation.Gender) == variation.Gender
                                             && (a.Collection is null || a.Collection == variation.Collection);
                    if (models.FirstOrDefault(m => Matches(m.Asset)) is not { Asset: not null } model)
                    {
                        added.Add(DrawableOrder.Reserve(project, variation.Gender, slot.Component, slot.Prop));
                        continue;
                    }
                    var maps = textures.Where(t => Matches(t.Asset)).OrderBy(t => t.Variation).Select(t => t.Asset.Path).ToList();
                    if (maps.Count != items[i].Textures)
                        warnings.Add($"{Path.GetFileName(model.Asset.Path)}: el .ymt declara {items[i].Textures} texturas y hay {maps.Count}.");
                    var drawable = DrawableImporter.Create(project, projectDir, model.Asset.Path, model.Name, variation.Gender, maps, warnings);
                    drawable.Skin |= items[i].Skin;
                    drawable.Flags.HeelHeight = items[i].HeelHeight > 0 ? items[i].HeelHeight : null;
                    if (slot.Prop == PropSlot.Hat) drawable.Flags.HatHair = HatHairOf(items[i].HairScale);
                    used.Add(model.Asset.Path);
                    DrawableOrder.PlaceAtEnd(project, drawable);
                    project.Drawables.Add(drawable);
                    added.Add(drawable);
                }
            }
        }

        var reserved = added.Count(d => d.Reserved);
        if (reserved > 0)
            warnings.Add($"{reserved} números del .ymt no tienen modelo; se han dejado reservados para no mover los demás.");
        var leftover = models.Count(m => !used.Contains(m.Asset.Path));
        if (leftover > 0)
            warnings.Add($"{leftover} modelos no aparecen en ningún .ymt y no se han importado; impórtalos como prendas sueltas si los necesitas.");

        // "mp_m_eup" and "mp_f_eup" share the collection "eup" in a project.
        var names = variations.Select(v => StripGender(v.Collection)).Distinct().ToList();
        return new ResourceImportResult(added, warnings, names.Count == 1 ? names[0] : null);
    }

    private static HatHair HatHairOf(float scale) => scale switch
    {
        > -0.25f => HatHair.Keep,
        > -0.75f => HatHair.Cut,
        _ => HatHair.Remove,
    };

    private static string StripGender(string collection) =>
        collection.StartsWith("mp_m_") || collection.StartsWith("mp_f_") ? collection[5..] : collection;

    // "<ped>_<collection>.ymt" in RSC7 form; creature metadata and other .ymt files are skipped.
    private static IEnumerable<Variation> FindVariations(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.ymt", Recursive))
        {
            var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            foreach (var gender in Enum.GetValues<PedGender>())
            {
                var prefix = PedSlotNames.PedModel(gender) + "_";
                if (stem.StartsWith(prefix) && stem.Length > prefix.Length && IsRsc7(file))
                    yield return new Variation(file, gender, stem[prefix.Length..]);
            }
        }
    }

    private static bool IsRsc7(string file)
    {
        Span<byte> magic = stackalloc byte[4];
        using var stream = File.OpenRead(file);
        return stream.Read(magic) == 4 && BitConverter.ToUInt32(magic) == ResourceHeader.Rsc7Magic;
    }
}
