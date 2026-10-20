namespace EasyClothes.Core.Rage;

public sealed record TextureInfo(string Name, int Width, int Height, string Format, int MipLevels);

/// <summary>Reads the texture list of a .ytd (pgDictionary&lt;grcTexture&gt;). Pixel data is not decoded yet.</summary>
public static class TextureDictionary
{
    public const uint ResourceVersion = 13;

    // Offsets inside pgDictionary (shared by .ytd and .ydd).
    internal const ulong DictionaryRoot = 0x50000000;
    internal const ulong DictionaryHashes = 0x20;
    internal const ulong DictionaryItems = 0x30;

    // Offsets inside grcTexture (legacy/gen8 layout).
    private const ulong TextureName = 0x28;
    private const ulong TextureWidth = 0x50;
    private const ulong TextureHeight = 0x52;
    private const ulong TextureFormat = 0x58;
    private const ulong TextureLevels = 0x5D;

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
                reader.At(texture + TextureLevels, 1)[0]))
            .ToList();
    }

    /// <summary>Formats are stored as D3D FourCC codes ("DXT1") or D3DFORMAT numbers.</summary>
    internal static string FormatName(uint format) => format switch
    {
        21 => "A8R8G8B8",
        22 => "X8R8G8B8",
        28 => "A8",
        50 => "L8",
        _ when IsFourCc(format) => string.Create(4, format, (span, f) =>
        {
            for (var i = 0; i < 4; i++) span[i] = (char)((f >> (8 * i)) & 0xFF);
        }),
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
