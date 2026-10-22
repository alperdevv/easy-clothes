using System.Xml.Linq;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage.Meta;

namespace EasyClothes.Core.Rage;

// Links the expression values of the .ymt to bones: shoes lift the ped (heels) and hats squash
// the hair. Indices are the drawable's position in its slot within the collection.
public static class CreatureMetadata
{
    private const int ScaleTrack = 33;
    private const int HeelBone = 28462;
    private const int HairBone = 13201;
    private const int HeelExpression = 4;
    private const int HairExpression = 0;

    public static string Name(PedGender gender, string collection) =>
        $"mp_creaturemetadata_{(gender == PedGender.Male ? "m" : "f")}_{collection}";

    public static byte[] Write(IEnumerable<int> heelShoes, IEnumerable<int> hairHats)
    {
        var hats = hairHats.ToList();
        var props = new XElement("pedPropExpressions");
        // The game's own files start with this placeholder; FiveM has been seen crashing without it.
        if (hats.Count > 0) props.Add(Item("pedProp", (int)PropSlot.Hat, -1, -1, HairBone));
        foreach (var hat in hats) props.Add(Item("pedProp", (int)PropSlot.Hat, hat, HairExpression, HairBone));

        return Rbf.Write(new XElement("CCreatureMetaData",
            new XElement("pedCompExpressions", heelShoes.Select(shoe => Item("pedComp", (int)ComponentSlot.Shoes, shoe, HeelExpression, HeelBone))),
            props));
    }

    private static XElement Item(string prefix, int slot, int index, int expression, int bone) => new("Item",
        new XElement($"{prefix}ID", new XAttribute("value", slot)),
        new XElement($"{prefix}VarIndex", new XAttribute("value", index)),
        new XElement($"{prefix}ExpressionIndex", new XAttribute("value", expression)),
        Array("tracks", "char_array", ScaleTrack),
        Array("ids", "short_array", bone),
        Array("types", "char_array", 2),
        Array("components", "char_array", 1));

    private static XElement Array(string name, string content, int value) =>
        new(name, new XAttribute("content", content), value);
}
