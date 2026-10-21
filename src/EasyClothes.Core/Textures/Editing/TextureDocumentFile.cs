using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyClothes.Core.Textures.Editing;

// The editable source of a texture: a zip next to the .ytd with the layers as PNG plus their settings.
// It records a hash of the .ytd it produced, so a texture replaced by other means is not shadowed by stale layers.
public static class TextureDocumentFile
{
    public const string Extension = ".ectex";
    private const int Version = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string PathFor(string texturePath) => Path.ChangeExtension(texturePath, Extension);

    public static string HashOf(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void Save(TextureDocument document, string path, string textureHash)
    {
        var temp = path + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            var layers = new List<LayerDto>();
            for (var i = 0; i < document.Layers.Count; i++)
            {
                var layer = document.Layers[i];
                string? image = null;
                if (layer is PixelLayer or ImageLayer)
                {
                    image = $"layers/{i}.png";
                    WritePng(zip, image, layer is PixelLayer p ? p.Image : ((ImageLayer)layer).Image);
                }
                layers.Add(LayerDto.From(layer, image));
            }
            if (document.Original is { } original) WritePng(zip, "original.png", original);

            var dto = new DocumentDto(Version, document.Width, document.Height, document.KeepOriginalAlpha, textureHash, layers);
            using var json = zip.CreateEntry("document.json").Open();
            JsonSerializer.Serialize(json, dto, Json);
        }
        File.Move(temp, path, overwrite: true);
    }

    // Null when there is no layered source or it belongs to a different version of the texture.
    public static TextureDocument? TryLoad(string path, string textureHash)
    {
        if (!File.Exists(path)) return null;
        using var zip = ZipFile.OpenRead(path);
        DocumentDto dto;
        using (var json = Entry(zip, "document.json").Open())
            dto = JsonSerializer.Deserialize<DocumentDto>(json, Json) ?? throw new InvalidDataException("document.json vacío.");
        if (dto.Version > Version) throw new InvalidDataException($"{Path.GetFileName(path)} es de una versión más nueva de EasyClothes.");
        if (!string.Equals(dto.TextureHash, textureHash, StringComparison.OrdinalIgnoreCase)) return null;

        var original = zip.GetEntry("original.png") is { } entry ? ReadPng(entry) : null;
        var document = new TextureDocument(dto.Width, dto.Height) { Original = original, KeepOriginalAlpha = dto.KeepOriginalAlpha };
        foreach (var layer in dto.Layers)
            document.Layers.Add(layer.ToLayer(name => ReadPng(Entry(zip, name))));
        return document;
    }

    private static ZipArchiveEntry Entry(ZipArchive zip, string name) =>
        zip.GetEntry(name) ?? throw new InvalidDataException($"Falta {name} en el archivo de capas.");

    private static void WritePng(ZipArchive zip, string name, RgbaImage image)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
        PngWriter.Write(image, stream);
    }

    private static RgbaImage ReadPng(ZipArchiveEntry entry)
    {
        using var buffer = new MemoryStream();
        using (var stream = entry.Open()) stream.CopyTo(buffer);
        buffer.Position = 0;
        return PngReader.Read(buffer);
    }

    private sealed record DocumentDto(int Version, int Width, int Height, bool KeepOriginalAlpha, string TextureHash, List<LayerDto> Layers);

    private sealed record LayerDto(
        string Kind, Guid Id, string Name, bool Visible, bool Locked, float Opacity, BlendMode Blend,
        string? Image = null, float? X = null, float? Y = null, float? Scale = null, float? Rotation = null, uint? Rgb = null,
        float? Hue = null, float? Saturation = null, float? Brightness = null, float? Contrast = null, uint? TintRgb = null, float? TintStrength = null)
    {
        public static LayerDto From(Layer layer, string? image)
        {
            var common = new LayerDto("", layer.Id, layer.Name, layer.Visible, layer.Locked, layer.Opacity, layer.Blend, image);
            return layer switch
            {
                PixelLayer => common with { Kind = "pixels" },
                ImageLayer i => common with { Kind = "image", X = i.X, Y = i.Y, Scale = i.Scale, Rotation = i.Rotation },
                FillLayer f => common with { Kind = "fill", Rgb = f.Rgb },
                AdjustmentLayer a => common with
                {
                    Kind = "adjustment", Hue = a.Hue, Saturation = a.Saturation, Brightness = a.Brightness, Contrast = a.Contrast,
                    TintRgb = a.TintRgb, TintStrength = a.TintStrength,
                },
                _ => throw new NotSupportedException(layer.GetType().Name),
            };
        }

        public Layer ToLayer(Func<string, RgbaImage> image)
        {
            Layer layer = Kind switch
            {
                "pixels" => new PixelLayer { Id = Id, Name = Name, Image = image(Image!) },
                "image" => new ImageLayer { Id = Id, Name = Name, Image = image(Image!), X = X ?? 0.5f, Y = Y ?? 0.5f, Scale = Scale ?? 0.25f, Rotation = Rotation ?? 0 },
                "fill" => new FillLayer { Id = Id, Name = Name, Rgb = Rgb ?? 0xFFFFFF },
                "adjustment" => new AdjustmentLayer
                {
                    Id = Id, Name = Name, Hue = Hue ?? 0, Saturation = Saturation ?? 0, Brightness = Brightness ?? 0, Contrast = Contrast ?? 0,
                    TintRgb = TintRgb ?? 0xFFFFFF, TintStrength = TintStrength ?? 0,
                },
                _ => throw new InvalidDataException($"Tipo de capa desconocido: {Kind}."),
            };
            (layer.Visible, layer.Locked, layer.Opacity, layer.Blend) = (Visible, Locked, Opacity, Blend);
            return layer;
        }
    }
}
