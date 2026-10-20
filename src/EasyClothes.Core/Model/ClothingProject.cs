namespace EasyClothes.Core.Model;

public sealed class ClothingProject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public required string Name { get; set; }

    /// <summary>Collection name used for the generated DLC/resource (lowercase, no spaces).</summary>
    public required string CollectionName { get; set; }

    public string? Author { get; set; }
    public string? Description { get; set; }
    public List<Drawable> Drawables { get; set; } = [];
    public List<Tattoo> Tattoos { get; set; } = [];

    public IEnumerable<Drawable> DrawablesFor(PedGender gender) =>
        Drawables.Where(d => d.Gender == gender);
}

/// <summary>A wearable mesh (component or prop) plus its texture variations.</summary>
public sealed class Drawable
{
    /// <summary>The game allows at most 26 texture variations (a..z) per drawable.</summary>
    public const int MaxTextures = 26;

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public PedGender Gender { get; set; }

    /// <summary>Set for components; null when the drawable is a prop.</summary>
    public ComponentSlot? Component { get; set; }

    /// <summary>Set for props; null when the drawable is a component.</summary>
    public PropSlot? Prop { get; set; }

    /// <summary>Path to the source .ydd, relative to the project folder.</summary>
    public required string ModelPath { get; set; }

    /// <summary>Optional first-person model (.ydd), relative to the project folder.</summary>
    public string? FirstPersonModelPath { get; set; }

    public List<TextureVariation> Textures { get; set; } = [];
    public DrawableFlags Flags { get; set; } = new();

    public bool IsProp => Prop is not null;
}

public sealed class TextureVariation
{
    /// <summary>Path to the source .ytd, relative to the project folder.</summary>
    public required string TexturePath { get; set; }

    /// <summary>Letter suffix used in file names: 0 → 'a', 25 → 'z'.</summary>
    public static char LetterFor(int index) =>
        index is >= 0 and < Drawable.MaxTextures
            ? (char)('a' + index)
            : throw new ArgumentOutOfRangeException(nameof(index), index, $"Texture index must be 0..{Drawable.MaxTextures - 1}.");
}

public sealed class DrawableFlags
{
    /// <summary>Hides the hair while worn (hats, masks).</summary>
    public bool HideHair { get; set; }

    /// <summary>Prop is removed when the ped takes a hit.</summary>
    public bool KnockOffOnHit { get; set; }
}

/// <summary>A tattoo or overlay decal applied to the ped's skin.</summary>
public sealed class Tattoo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public TattooZone Zone { get; set; }

    /// <summary>Genders this tattoo is available for.</summary>
    public List<PedGender> Genders { get; set; } = [PedGender.Male, PedGender.Female];

    /// <summary>Path to the source .ytd, relative to the project folder.</summary>
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
