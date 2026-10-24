using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Rage;

public sealed record TextureInfo(string Name, int Width, int Height, string Format, int MipLevels)
{
    internal ulong DataPointer { get; init; }
    internal ulong StructPointer { get; init; }

    public TextureFormat PixelFormat => Format switch
    {
        "DXT1" => TextureFormat.Dxt1,
        "DXT5" => TextureFormat.Dxt5,
        "A8R8G8B8" => TextureFormat.A8R8G8B8,
        "X8R8G8B8" => TextureFormat.X8R8G8B8,
        "A8B8G8R8" => TextureFormat.A8B8G8R8,
        "DXT3" => TextureFormat.Dxt3,
        "ATI1" => TextureFormat.Bc4,
        "ATI2" => TextureFormat.Bc5,
        "BC7" => TextureFormat.Bc7,
        _ => TextureFormat.Unknown,
    };
}

public static class TextureDictionary
{
    public const uint ResourceVersion = 13;

    internal const ulong DictionaryRoot = 0x50000000;
    internal const ulong DictionaryPagesInfo = 0x08;
    internal const ulong DictionaryHashes = 0x20;
    internal const ulong DictionaryItems = 0x30;

    internal const ulong TextureName = 0x28;
    internal const ulong TextureWidth = 0x50;
    internal const ulong TextureHeight = 0x52;
    internal const ulong TextureStride = 0x56;
    internal const ulong TextureFormat = 0x58;
    internal const ulong TextureLevels = 0x5D;
    internal const ulong TextureData = 0x70;

    public static IReadOnlyList<TextureInfo> Read(ResourceFile resource)
    {
        if (resource.Header.Version != ResourceVersion)
            throw new InvalidDataException($"Expected a texture dictionary (version {ResourceVersion}), got version {resource.Header.Version}.");

        var reader = new ResourceReader(resource);
        return reader.PointerArray(DictionaryRoot + DictionaryItems)
            .Select(texture => new TextureInfo(
                reader.CString(reader.UInt64(texture + TextureName)) ?? "",
                reader.UInt16(texture + TextureWidth),
                reader.UInt16(texture + TextureHeight),
                FormatName(reader.UInt32(texture + TextureFormat)),
                reader.At(texture + TextureLevels, 1)[0])
            {
                DataPointer = reader.UInt64(texture + TextureData),
                StructPointer = texture,
            })
            .ToList();
    }

    public static RgbaImage Decode(ResourceFile resource, TextureInfo texture)
    {
        var size = BlockDecoder.CompressedSize(texture.PixelFormat, texture.Width, texture.Height);
        var data = new ResourceReader(resource).At(texture.DataPointer, size);
        return BlockDecoder.Decode(texture.PixelFormat, texture.Width, texture.Height, data);
    }

    internal static string FormatName(uint format) => format switch
    {
        21 => "A8R8G8B8",
        22 => "X8R8G8B8",
        28 => "A8",
        32 => "A8B8G8R8",
        50 => "L8",
        // FourCCs are padded with spaces, e.g. "BC7 ".
        _ when IsFourCc(format) => string.Create(4, format, (span, f) =>
        {
            for (var i = 0; i < 4; i++) span[i] = (char)((f >> (8 * i)) & 0xFF);
        }).TrimEnd(),
        _ => $"0x{format:X}",
    };

    private static bool IsFourCc(uint format)
    {
        for (var i = 0; i < 4; i++)
        {
            var c = (format >> (8 * i)) & 0xFF;
            if (c is < 0x20 or > 0x7E) return false;
        }
        return true;
    }
}
