namespace EasyClothes.Core.Model;

// Manual ordering and reserved numbers inside a slot. The in-game number is the index in InGameOrder.
public static class DrawableOrder
{
    public static int NumberOf(ClothingProject project, Drawable drawable) =>
        ClothingProject.InGameOrder(project.SameSlot(drawable)).IndexOf(drawable);

    public static void PlaceAtEnd(ClothingProject project, Drawable drawable) =>
        drawable.Position = project.SameSlot(drawable).Where(d => d != drawable).Select(d => d.Position + 1).DefaultIfEmpty(0).Max();

    public static bool Move(ClothingProject project, Drawable drawable, int offset)
    {
        var ordered = ClothingProject.InGameOrder(project.SameSlot(drawable));
        var from = ordered.IndexOf(drawable);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= ordered.Count) return false;
        ordered.RemoveAt(from);
        ordered.Insert(to, drawable);
        Renumber(ordered);
        return true;
    }

    public static Drawable Reserve(ClothingProject project, PedGender gender, ComponentSlot? component, PropSlot? prop)
    {
        var reserved = new Drawable { Name = "", Gender = gender, Component = component, Prop = prop, ModelPath = "", Reserved = true };
        var names = project.Drawables.Where(d => d.Gender == gender).Select(d => d.Name).ToHashSet();
        var n = 1;
        while (names.Contains($"reservado {n}")) n++;
        reserved.Name = $"reservado {n}";
        PlaceAtEnd(project, reserved);
        project.Drawables.Add(reserved);
        return reserved;
    }

    // The garment takes over the reserved number, slot and gender.
    public static void Fill(ClothingProject project, Drawable reserved, Drawable garment)
    {
        if (!reserved.Reserved) throw new InvalidOperationException($"{reserved.Name} no es un hueco reservado.");
        Renumber(ClothingProject.InGameOrder(project.SameSlot(reserved)));
        garment.Gender = reserved.Gender;
        garment.Component = reserved.Component;
        garment.Prop = reserved.Prop;
        garment.Position = reserved.Position;
        project.Drawables.Remove(reserved);
    }

    private static void Renumber(List<Drawable> ordered)
    {
        for (var i = 0; i < ordered.Count; i++) ordered[i].Position = i;
    }
}
