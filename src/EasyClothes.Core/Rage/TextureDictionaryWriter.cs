using System.Buffers.Binary;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Rage;

public static class TextureDictionaryWriter
{
    private const int DataAlignment = 16;
    private const ulong GraphicsBase = 0x60000000;

    // Keeps the system segment of the original byte for byte (unknown fields included)
    // and only patches each texture's size, format, mips and data pointer.
    public static ResourceFile Rebuild(ResourceFile original, IReadOnlyList<EncodedTexture> textures)
    {
        var infos = TextureDictionary.Read(original);
        if (infos.Count != textures.Count)
            throw new ArgumentException($"Dictionary has {infos.Count} textures, got {textures.Count}.", nameof(textures));

        var system = (byte[])original.SystemData.Clone();
        using var graphics = new MemoryStream();
        for (var i = 0; i < infos.Count; i++)
        {
            var texture = textures[i];
            graphics.SetLength(Align(graphics.Length));
            graphics.Position = graphics.Length;
            var dataPointer = GraphicsBase + (ulong)graphics.Position;
            foreach (var mip in texture.Mips) graphics.Write(mip);

            var s = system.AsSpan((int)(infos[i].StructPointer & 0x0FFFFFFF));
            BinaryPrimitives.WriteUInt16LittleEndian(s[(int)TextureDictionary.TextureWidth..], (ushort)texture.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(s[(int)TextureDictionary.TextureHeight..], (ushort)texture.Height);
            BinaryPrimitives.WriteUInt16LittleEndian(s[(int)TextureDictionary.TextureStride..], (ushort)Stride(texture.Format, texture.Width));
            BinaryPrimitives.WriteUInt32LittleEndian(s[(int)TextureDictionary.TextureFormat..], FormatCode(texture.Format));
            s[(int)TextureDictionary.TextureLevels] = (byte)texture.Mips.Count;
            BinaryPrimitives.WriteUInt64LittleEndian(s[(int)TextureDictionary.TextureData..], dataPointer);
        }

        // The rebuilt graphics segment is always one page.
        var pagesInfo = BinaryPrimitives.ReadUInt64LittleEndian(system.AsSpan((int)TextureDictionary.DictionaryPagesInfo));
        if (pagesInfo != 0) system[(int)(pagesInfo & 0x0FFFFFFF) + 9] = 1;

        return ResourceFile.Create(original.Header.Version, system, graphics.ToArray());
    }

    public static int Stride(TextureFormat format, int width) => format switch
    {
        TextureFormat.Dxt1 => width / 2,
        TextureFormat.Dxt5 => width,
        TextureFormat.A8R8G8B8 => width * 4,
        _ => throw new NotSupportedException($"Unknown stride for {format}."),
    };

    private static uint FormatCode(TextureFormat format) => format switch
    {
        TextureFormat.Dxt1 => 0x31545844,
        TextureFormat.Dxt5 => 0x35545844,
        TextureFormat.A8R8G8B8 => 21,
        _ => throw new NotSupportedException($"Cannot write {format}."),
    };

    private static long Align(long value) => (value + DataAlignment - 1) / DataAlignment * DataAlignment;
}
