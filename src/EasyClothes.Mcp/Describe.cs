using EasyClothes.Core.Model;
using EasyClothes.Core.Validation;

namespace EasyClothes.Mcp;

public sealed record GarmentSummary(
    string Id, string Name, string Gender, string Slot, string SlotCode, int Number, int Textures,
    bool Reserved, bool FirstPerson, bool ClothPhysics, float? HeelHeight, string? HatHair);

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
        d.FirstPersonModelPath is not null, d.ClothPhysicsPath is not null,
        d.Component == ComponentSlot.Shoes ? d.Flags.HeelHeight : null,
        d.Prop == PropSlot.Hat ? d.Flags.HatHair.ToString() : null);

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
