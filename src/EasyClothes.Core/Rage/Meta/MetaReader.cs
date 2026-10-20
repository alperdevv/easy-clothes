using System.Buffers.Binary;

namespace EasyClothes.Core.Rage.Meta;

internal sealed class MetaReader
{
    private readonly byte[] _data;
    private readonly (uint Type, int Offset, int Length)[] _blocks;

    public MetaReader(ResourceFile resource)
    {
        if (resource.Header.Version != MetaBuilder.ResourceVersion)
            throw new InvalidDataException($"Not a meta resource (version {resource.Header.Version}).");
        _data = resource.SystemData;
        var blocksAt = Offset(UInt64(0x30));
        _blocks = new (uint, int, int)[UInt16(0x4C)];
        for (var i = 0; i < _blocks.Length; i++)
        {
            var at = blocksAt + i * 16;
            _blocks[i] = (UInt32(at), Offset(UInt64(at + 8)), (int)UInt32(at + 4));
        }
        RootIndex = (int)UInt32(0x1C) - 1;
    }

    public int RootIndex { get; }
    public IReadOnlyList<(uint Type, int Offset, int Length)> Blocks => _blocks;

    public ReadOnlySpan<byte> Block(int index) => _data.AsSpan(_blocks[index].Offset, _blocks[index].Length);

    public ReadOnlySpan<byte> Root => Block(RootIndex);

    // Resolves an array field to its items; empty arrays give an empty span.
    public ReadOnlySpan<byte> Array(ReadOnlySpan<byte> field, int itemSize, out int count)
    {
        var pointer = BinaryPrimitives.ReadUInt32LittleEndian(field);
        count = BinaryPrimitives.ReadUInt16LittleEndian(field[8..]);
        if (count == 0 || pointer == 0) return [];
        var block = (int)(pointer & 0xFFF) - 1;
        var offset = (int)(pointer >> 12);
        if (block < 0 || block >= _blocks.Length || offset + count * itemSize > _blocks[block].Length)
            throw new InvalidDataException($"Meta array pointer 0x{pointer:X8} is out of range.");
        return Block(block).Slice(offset, count * itemSize);
    }

    private static int Offset(ulong pointer) => (int)(pointer & 0x0FFFFFFF);
    private ushort UInt16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at));
    private uint UInt32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at));
    private ulong UInt64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(at));
}
