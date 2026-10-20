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

    private const int DictionarySize = 0x40;
    private const int TextureSize = 0x90;
    private const ulong SystemBase = 0x50000000;

    // Builds a dictionary from scratch with the layout the game's own .ytd files use;
    // entries are sorted by name hash, which the game binary-searches.
    public static ResourceFile Create(IReadOnlyList<(string Name, EncodedTexture Texture)> textures)
    {
        var sorted = textures.Select(t => (t.Name, t.Texture, Hash: Jenkins.Hash(t.Name))).OrderBy(t => t.Hash).ToList();
        if (sorted.Select(t => t.Hash).Distinct().Count() != sorted.Count)
            throw new ArgumentException("Texture names must be unique.", nameof(textures));

        var system = new MemoryStream();
        var w = new BinaryWriter(system);
        var texturesAt = DictionarySize;
        system.SetLength(texturesAt + sorted.Count * TextureSize);
        var pagesAt = Reserve(system, 0x20);
        var namesAt = sorted.Select(t => Reserve(system, t.Name.Length + 1)).ToList();
        var itemsAt = Reserve(system, sorted.Count * 8);
        var hashesAt = Reserve(system, sorted.Count * 4);

        using var graphics = new MemoryStream();
        for (var i = 0; i < sorted.Count; i++)
        {
            var (name, texture, hash) = sorted[i];
            graphics.SetLength(Align(graphics.Length));
            graphics.Position = graphics.Length;
            var dataPointer = GraphicsBase + (ulong)graphics.Position;
            foreach (var mip in texture.Mips) graphics.Write(mip);

            var at = texturesAt + i * TextureSize;
            system.Position = at;
            w.Write(0xB6F0EE97UL);
            system.Position = at + (int)TextureDictionary.TextureName;
            w.Write(SystemBase + (ulong)namesAt[i]);
            w.Write(0x00800001u);
            system.Position = at + 0x40;
            w.Write(0x202AB014u);
            system.Position = at + (int)TextureDictionary.TextureWidth;
            w.Write((ushort)texture.Width);
            w.Write((ushort)texture.Height);
            w.Write((ushort)1);
            w.Write((ushort)Stride(texture.Format, texture.Width));
            w.Write(FormatCode(texture.Format));
            w.Write((byte)0);
            w.Write((byte)texture.Mips.Count);
            system.Position = at + (int)TextureDictionary.TextureData;
            w.Write(dataPointer);

            system.Position = namesAt[i];
            w.Write(System.Text.Encoding.ASCII.GetBytes(name));
            system.Position = itemsAt + i * 8;
            w.Write(SystemBase + (ulong)at);
            system.Position = hashesAt + i * 4;
            w.Write(hash);
        }

        system.Position = 0;
        w.Write(0x140573510UL);
        w.Write(SystemBase + (ulong)pagesAt);
        system.Position = 0x18;
        w.Write(1u);
        system.Position = (int)TextureDictionary.DictionaryHashes;
        w.Write(SystemBase + (ulong)hashesAt);
        w.Write((ushort)sorted.Count);
        w.Write((ushort)sorted.Count);
        system.Position = (int)TextureDictionary.DictionaryItems;
        w.Write(SystemBase + (ulong)itemsAt);
        w.Write((ushort)sorted.Count);
        w.Write((ushort)sorted.Count);
        system.Position = pagesAt + 8;
        w.Write((byte)1);
        w.Write((byte)1);
        w.Flush();

        return ResourceFile.Create(TextureDictionary.ResourceVersion, system.ToArray(), graphics.ToArray());
    }

    private static int Reserve(MemoryStream stream, int size)
    {
        var at = (int)Align(stream.Length);
        stream.SetLength(at + size);
        return at;
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
