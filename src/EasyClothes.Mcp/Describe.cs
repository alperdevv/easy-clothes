using EasyClothes.Core.Model;
using EasyClothes.Core.Validation;

namespace EasyClothes.Mcp;

public sealed record GarmentSummary(
    string Id, string Name, string Gender, string Slot, string SlotCode, int Number, int Textures,
    bool Reserved, bool AlternateModel, bool FirstPerson, int Alternates, bool ClothPhysics, float? HeelHeight, string? HatHair, int? PropMask,
    string? SourcePackage, string? SourceFile);

public sealed record TattooSummary(string Id, string Name, string Zone, IReadOnlyList<string> Genders);

public sealed record IssueInfo(string Severity, string Message, string? Garment, string? GarmentId, string? Fix);

// Turns project objects into the plain records the tools return.
public static class Describe
{
    public static string Gender(PedGender gender) => gender == PedGender.Male ? "hombre" : "mujer";

    public static GarmentSummary Garment(ClothingProject project, Drawable d) => new(
        d.Id.ToString(), d.Name, Gender(d.Gender),
        d.Component?.ToString() ?? $"Prop{d.Prop}",
        d.Component is { } c ? PedSlotNames.FileCode(c) : d.Prop is { } p ? PedSlotNames.FileCode(p) : "?",
        DrawableOrder.NumberOf(project, d), d.Textures.Count, d.Reserved,
        d.FirstPersonModelPath is not null, d.FirstPersonModelPath is not null && d.Flags.AlternateInFirstPerson, d.AlternateCount(), d.ClothPhysicsPath is not null,
        d.Component == ComponentSlot.Shoes ? d.Flags.HeelHeight : null,
        d.Prop == PropSlot.Hat ? d.Flags.HatHair.ToString() : null,
        d.Component is null ? null : PropMask(d),
        d.Source?.Package, d.Source?.File);

    // As the .ymt gets it: 0x10 follows Skin.
    public static int PropMask(Drawable d) => (d.Flags.PropMask ?? 1) & ~0x10 | (d.Skin ? 0x10 : 0);

    public static IEnumerable<GarmentSummary> Garments(ClothingProject project) =>
        project.Drawables
            .OrderBy(d => d.Gender).ThenBy(d => d.Prop is not null).ThenBy(d => (int?)d.Component ?? (int)d.Prop!.Value)
            .ThenBy(d => DrawableOrder.NumberOf(project, d))
            .Select(d => Garment(project, d));

    public static TattooSummary Tattoo(Tattoo t) =>
        new(t.Id.ToString(), t.Name, t.Zone.ToString(), t.Genders.Select(Gender).ToList());

    public static IssueInfo Issue(ClothingProject project, ValidationIssue issue)
    {
        var garment = issue.DrawableId is { } id ? project.Drawables.FirstOrDefault(d => d.Id == id) : null;
        return new(issue.Severity.ToString().ToLowerInvariant(), issue.Message, garment?.Name, garment?.Id.ToString(), issue.Fix?.Label);
    }
}
