namespace EasyClothes.Core.Model;

public enum PedGender
{
    Male,
    Female,
}

/// <summary>Ped component slots. Numeric values match the game's component ids.</summary>
public enum ComponentSlot
{
    Head = 0,
    Mask = 1,
    Hair = 2,
    Torso = 3,
    Legs = 4,
    Bag = 5,
    Shoes = 6,
    Accessory = 7,
    Undershirt = 8,
    Armor = 9,
    Decal = 10,
    Top = 11,
}

/// <summary>Ped prop anchors. Numeric values match the game's anchor ids.</summary>
public enum PropSlot
{
    Hat = 0,
    Glasses = 1,
    Ears = 2,
    Watch = 6,
    Bracelet = 7,
}

public static class PedSlotNames
{
    /// <summary>Base ped model each gender's freemode clothing targets.</summary>
    public static string PedModel(PedGender gender) => gender switch
    {
        PedGender.Male => "mp_m_freemode_01",
        PedGender.Female => "mp_f_freemode_01",
        _ => throw new ArgumentOutOfRangeException(nameof(gender), gender, null),
    };

    /// <summary>Short name used in streamed file names (e.g. <c>jbib_000_u.ydd</c>).</summary>
    public static string FileCode(ComponentSlot slot) => slot switch
    {
        ComponentSlot.Head => "head",
        ComponentSlot.Mask => "berd",
        ComponentSlot.Hair => "hair",
        ComponentSlot.Torso => "uppr",
        ComponentSlot.Legs => "lowr",
        ComponentSlot.Bag => "hand",
        ComponentSlot.Shoes => "feet",
        ComponentSlot.Accessory => "teef",
        ComponentSlot.Undershirt => "accs",
        ComponentSlot.Armor => "task",
        ComponentSlot.Decal => "decl",
        ComponentSlot.Top => "jbib",
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    public static string FileCode(PropSlot slot) => slot switch
    {
        PropSlot.Hat => "p_head",
        PropSlot.Glasses => "p_eyes",
        PropSlot.Ears => "p_ears",
        PropSlot.Watch => "p_lwrist",
        PropSlot.Bracelet => "p_rwrist",
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };
}
