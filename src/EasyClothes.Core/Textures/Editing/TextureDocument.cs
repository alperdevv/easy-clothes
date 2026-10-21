namespace EasyClothes.Core.Textures.Editing;

// Layers are stored bottom first. Original is the texture the document was opened from; its alpha is the
// garment's cut-out, which KeepOriginalAlpha preserves whatever the layers do.
public sealed class TextureDocument
{
    public TextureDocument(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public RgbaImage? Original { get; init; }
    public bool KeepOriginalAlpha { get; set; }
    public List<Layer> Layers { get; private set; } = [];

    public static TextureDocument FromTexture(RgbaImage original)
    {
        var document = new TextureDocument(original.Width, original.Height) { Original = original, KeepOriginalAlpha = HasTransparency(original) };
        document.Layers.Add(new PixelLayer { Name = "Textura original", Image = original });
        document.Layers.Add(document.NewPixelLayer("Capa 1"));
        return document;
    }

    public PixelLayer NewPixelLayer(string name) => new() { Name = name, Image = Blank(Width, Height) };

    public Layer? Find(Guid id) => Layers.FirstOrDefault(l => l.Id == id);

    // A copy whose layers can change without touching this one; safe to compose on another thread.
    public TextureDocument Snapshot() => new(Width, Height)
    {
        Original = Original,
        KeepOriginalAlpha = KeepOriginalAlpha,
        Layers = Layers.Select(l => l.Clone()).ToList(),
    };

    public void Restore(TextureDocument state)
    {
        Width = state.Width;
        Height = state.Height;
        KeepOriginalAlpha = state.KeepOriginalAlpha;
        Layers = state.Layers.Select(l => l.Clone()).ToList();
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;
        foreach (var layer in Layers.OfType<PixelLayer>())
            layer.Image = TextureCompositor.Resample(layer.Image, width, height);
        Width = width;
        Height = height;
    }

    // Bakes an image, fill or adjustment into plain pixels, keeping its place, opacity and blend mode.
    public PixelLayer Rasterize(Layer layer) =>
        Replace(layer, TextureCompositor.Compose(new TextureDocument(Width, Height) { Layers = [Normalised(layer)] }));

    public PixelLayer MergeDown(Layer layer)
    {
        var index = Layers.IndexOf(layer);
        if (index < 1) throw new InvalidOperationException("No hay ninguna capa debajo con la que combinar.");
        var below = Layers[index - 1];
        var merged = new TextureDocument(Width, Height) { Layers = [Normalised(below), layer.Clone()] };
        Layers.RemoveAt(index);
        return Replace(below, TextureCompositor.Compose(merged));
    }

    private static Layer Normalised(Layer layer)
    {
        var copy = layer.Clone();
        (copy.Visible, copy.Opacity, copy.Blend) = (true, 1, BlendMode.Normal);
        return copy;
    }

    private PixelLayer Replace(Layer target, RgbaImage image)
    {
        var result = new PixelLayer
        {
            Id = target.Id,
            Name = target.Name,
            Visible = target.Visible,
            Locked = target.Locked,
            Opacity = target.Opacity,
            Blend = target.Blend,
            Image = image,
        };
        Layers[Layers.IndexOf(target)] = result;
        return result;
    }

    public static RgbaImage Blank(int width, int height) => new(width, height, new byte[width * height * 4]);

    private static bool HasTransparency(RgbaImage image)
    {
        for (var o = 3; o < image.Pixels.Length; o += 4)
            if (image.Pixels[o] < 255) return true;
        return false;
    }
}
