using System.Buffers.Binary;
using System.Text;

namespace EasyClothes.Core.Rage.Meta;

// Writes an RSC7 "meta" resource (.ymt): data blocks grouped by struct type, plus the
// struct/enum reflection tables. Arrays point at "block index + offset << 12".
internal sealed class MetaBuilder
{
    public const uint ByteType = 0x11;
    public const uint ResourceVersion = 2;
    private const ulong SystemBase = 0x50000000;
    private const int HeaderSize = 0x50;

    private readonly List<(uint Type, MemoryStream Data)> _blocks = [];

    public readonly record struct BlockRef(int Block, int Offset)
    {
        public uint Pointer => (uint)(Block + 1) | (uint)Offset << 12;
    }

    public BlockRef Append(uint type, ReadOnlySpan<byte> data)
    {
        var index = _blocks.FindIndex(b => b.Type == type);
        if (index < 0)
        {
            _blocks.Add((type, new MemoryStream()));
            index = _blocks.Count - 1;
        }
        var stream = _blocks[index].Data;
        stream.SetLength(Align(stream.Length));
        stream.Position = stream.Length;
        var offset = (int)stream.Position;
        if (offset >= 1 << 20) throw new InvalidDataException($"Meta block 0x{type:X8} exceeds 1 MiB.");
        stream.Write(data);
        return new BlockRef(index, offset);
    }

    public void Patch(BlockRef at, ReadOnlySpan<byte> data) =>
        data.CopyTo(_blocks[at.Block].Data.GetBuffer().AsSpan(at.Offset));

    public static void WriteArray(Span<byte> target, BlockRef? items, int count)
    {
        target[..16].Clear();
        if (items is not { } at || count == 0) return;
        BinaryPrimitives.WriteUInt32LittleEndian(target, at.Pointer);
        BinaryPrimitives.WriteUInt16LittleEndian(target[8..], (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(target[10..], (ushort)count);
    }

    public ResourceFile Build(string name, IReadOnlyList<MetaStructInfo> structs, IReadOnlyList<MetaEnumInfo> enums)
    {
        var output = new MemoryStream();
        var w = new BinaryWriter(output);
        output.SetLength(HeaderSize);

        var nameAt = Place(output, Encoding.ASCII.GetBytes(name + "\0"));

        var blockDataAt = new long[_blocks.Count];
        for (var i = 0; i < _blocks.Count; i++)
            blockDataAt[i] = Place(output, _blocks[i].Data.ToArray());
        var blocksAt = Reserve(output, _blocks.Count * 16);
        for (var i = 0; i < _blocks.Count; i++)
        {
            output.Position = blocksAt + i * 16;
            w.Write(_blocks[i].Type);
            w.Write((uint)_blocks[i].Data.Length);
            w.Write(SystemBase + (ulong)blockDataAt[i]);
        }

        var enumsAt = Reserve(output, enums.Count * 0x18);
        for (var i = 0; i < enums.Count; i++)
        {
            var values = enums[i].Values;
            var valuesAt = Reserve(output, values.Length * 8);
            output.Position = valuesAt;
            foreach (var (valueName, value) in values)
            {
                w.Write(valueName);
                w.Write(value);
            }
            output.Position = enumsAt + i * 0x18;
            w.Write(enums[i].Name);
            w.Write(enums[i].Key);
            w.Write(SystemBase + (ulong)valuesAt);
            w.Write(values.Length);
        }

        var structsAt = Reserve(output, structs.Count * 0x20);
        for (var i = 0; i < structs.Count; i++)
        {
            var info = structs[i];
            var entriesAt = Reserve(output, info.Entries.Length * 16);
            output.Position = entriesAt;
            foreach (var e in info.Entries)
            {
                w.Write(e.Name);
                w.Write(e.Offset);
                w.Write(e.Type);
                w.Write((byte)0);
                w.Write(e.ReferenceIndex);
                w.Write(e.ReferenceKey);
            }
            output.Position = structsAt + i * 0x20;
            w.Write(info.Name);
            w.Write(info.Key);
            w.Write(info.Unknown);
            w.Write(0u);
            w.Write(SystemBase + (ulong)entriesAt);
            w.Write(info.Size);
            w.Write((ushort)0);
            w.Write((ushort)info.Entries.Length);
        }

        // Page map: the game fills in the page addresses, it only needs the counts.
        var pagesAt = Reserve(output, 0x18);
        output.Position = pagesAt + 8;
        w.Write((byte)1);

        output.Position = 0;
        w.Write(0UL);
        w.Write(SystemBase + (ulong)pagesAt);
        w.Write(0x50524430u);
        w.Write(0x79u);
        w.Write(0u);
        w.Write(1u); // root block index
        w.Write(SystemBase + (ulong)structsAt);
        w.Write(SystemBase + (ulong)enumsAt);
        w.Write(SystemBase + (ulong)blocksAt);
        w.Write(SystemBase + (ulong)nameAt);
        w.Write(0UL);
        w.Write((ushort)structs.Count);
        w.Write((ushort)enums.Count);
        w.Write((ushort)_blocks.Count);
        w.Flush();

        return ResourceFile.Create(ResourceVersion, output.ToArray(), []);
    }

    private static long Reserve(MemoryStream output, int size)
    {
        var at = Align(output.Length);
        output.SetLength(at + size);
        return at;
    }

    private static long Place(MemoryStream output, byte[] data)
    {
        var at = Reserve(output, data.Length);
        output.Position = at;
        output.Write(data);
        return at;
    }

    private static long Align(long value) => (value + 15) & ~15L;
}
