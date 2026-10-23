using EasyClothes.Core.Modeling;

namespace EasyClothes.Core.Model;

public sealed class ClothingProject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public required string Name { get; set; }

    public required string CollectionName { get; set; }

    public string? Author { get; set; }
    public string? Description { get; set; }
    public List<Drawable> Drawables { get; set; } = [];
    public List<Tattoo> Tattoos { get; set; } = [];

    // A valid collection name (lowercase letters, digits and underscores) derived from a display name.
    public static string CollectionNameFrom(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return result.Length > 0 ? result : "pack";
    }

    public IEnumerable<Drawable> DrawablesFor(PedGender gender) =>
        Drawables.Where(d => d.Gender == gender);

    // The order drawables get their in-game index, and the order the tree shows them.
    public static List<Drawable> InGameOrder(IEnumerable<Drawable> drawables) =>
        drawables.OrderBy(d => d.Position).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id).ToList();

    public IEnumerable<Drawable> SameSlot(Drawable drawable) =>
        Drawables.Where(d => d.Gender == drawable.Gender && d.Component == drawable.Component && d.Prop == drawable.Prop);
}

public sealed class Drawable
{
    public const int MaxTextures = 26;

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public PedGender Gender { get; set; }

    public ComponentSlot? Component { get; set; }

    public PropSlot? Prop { get; set; }

    public required string ModelPath { get; set; }

    // Place within its slot; equal positions fall back to name order, as older projects have all zeros.
    public int Position { get; set; }

    // Keeps an in-game number for a garment added later; it exports with no files.
    public bool Reserved { get; set; }

    public string? FirstPersonModelPath { get; set; }

    // "_r" models use the ped's skin tone and pair with "_whi" textures instead of "_u" and "_uni".
    public bool Skin { get; set; }

    // Cloth simulation (.yld) streamed next to the model, e.g. for skirts and capes.
    public string? ClothPhysicsPath { get; set; }

    public List<TextureVariation> Textures { get; set; } = [];
    public DrawableFlags Flags { get; set; } = new();

    // Set on caps made by EasyClothes, so their shape can be changed later.
    public CapDesign? Cap { get; set; }

    // Set on props made from the user's own .obj, so they can be placed again later.
    public ImportedModel? Imported { get; set; }

    public bool IsProp => Prop is not null;
}

public sealed class TextureVariation
{
    public required string TexturePath { get; set; }

    public static char LetterFor(int index) =>
        index is >= 0 and < Drawable.MaxTextures
            ? (char)('a' + index)
            : throw new ArgumentOutOfRangeException(nameof(index), index, $"Texture index must be 0..{Drawable.MaxTextures - 1}.");
}

public enum HatHair
{
    Keep,
    Cut,
    Remove,
}

public sealed class DrawableFlags
{
    // Hats: how much hair stays visible underneath. Cut is what the game's own hats use.
    public HatHair HatHair { get; set; } = HatHair.Cut;

    // Shoes: lifts the ped like the game's heels; 1 is their usual height (a sole about 9 cm lower).
    public float? HeelHeight { get; set; }

    public bool KnockOffOnHit { get; set; }
}

public sealed class Tattoo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public TattooZone Zone { get; set; }

    public List<PedGender> Genders { get; set; } = [PedGender.Male, PedGender.Female];

    public required string TexturePath { get; set; }

    // Placement on the body part's UV space, as written to the overlay file.
    public float UvX { get; set; } = 0.5f;
    public float UvY { get; set; } = 0.5f;
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float Rotation { get; set; }
}

public enum TattooZone
{
    Torso,
    Head,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}
