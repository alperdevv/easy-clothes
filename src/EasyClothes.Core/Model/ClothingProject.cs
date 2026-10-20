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

    public IEnumerable<Drawable> DrawablesFor(PedGender gender) =>
        Drawables.Where(d => d.Gender == gender);

    // The order drawables get their in-game index, and the order the tree shows them.
    public static List<Drawable> InGameOrder(IEnumerable<Drawable> drawables) =>
        drawables.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id).ToList();
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

    public string? FirstPersonModelPath { get; set; }

    public List<TextureVariation> Textures { get; set; } = [];
    public DrawableFlags Flags { get; set; } = new();

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

public sealed class DrawableFlags
{
    public bool HideHair { get; set; }

    public bool KnockOffOnHit { get; set; }
}

public sealed class Tattoo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public TattooZone Zone { get; set; }

    public List<PedGender> Genders { get; set; } = [PedGender.Male, PedGender.Female];

    public required string TexturePath { get; set; }
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
