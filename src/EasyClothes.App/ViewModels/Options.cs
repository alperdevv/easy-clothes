using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;

namespace EasyClothes.App.ViewModels;

public sealed record GenderOption(string Label, PedGender Value)
{
    public static readonly IReadOnlyList<GenderOption> All =
    [
        new("Hombre", PedGender.Male),
        new("Mujer", PedGender.Female),
    ];

    public static GenderOption For(PedGender gender) => All.First(o => o.Value == gender);
}

public sealed record SlotOption(string Label, SlotRef Slot)
{
    public static readonly IReadOnlyList<SlotOption> All =
    [
        Component("Cabeza", ComponentSlot.Head),
        Component("Máscaras", ComponentSlot.Mask),
        Component("Pelo", ComponentSlot.Hair),
        Component("Brazos y torso", ComponentSlot.Torso),
        Component("Piernas", ComponentSlot.Legs),
        Component("Bolsas y mochilas", ComponentSlot.Bag),
        Component("Calzado", ComponentSlot.Shoes),
        Component("Accesorios", ComponentSlot.Accessory),
        Component("Camisetas interiores", ComponentSlot.Undershirt),
        Component("Chalecos", ComponentSlot.Armor),
        Component("Calcomanías", ComponentSlot.Decal),
        Component("Partes de arriba", ComponentSlot.Top),
        Prop("Sombreros y cascos", PropSlot.Hat),
        Prop("Gafas", PropSlot.Glasses),
        Prop("Orejas", PropSlot.Ears),
        Prop("Relojes", PropSlot.Watch),
        Prop("Pulseras", PropSlot.Bracelet),
    ];

    public bool IsProp => Slot.Prop is not null;
    public string Display => $"{Label} ({Slot.FileCode})";

    public static SlotOption For(Drawable d) => All.First(o => o.Slot == new SlotRef(d.Component, d.Prop));

    private static SlotOption Component(string label, ComponentSlot slot) => new(label, new SlotRef(slot, null));
    private static SlotOption Prop(string label, PropSlot slot) => new(label, new SlotRef(null, slot));
}
