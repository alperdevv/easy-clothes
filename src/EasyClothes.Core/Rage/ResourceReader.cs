using System.Buffers.Binary;
using System.Text;

namespace EasyClothes.Core.Rage;

/// <summary>
/// Random-access reader over a decompressed resource. Resource structures link to each
/// other through virtual pointers: 0x5xxxxxxx points into the system segment and
/// 0x6xxxxxxx into the graphics segment (low 28 bits are the offset).
/// </summary>
public sealed class ResourceReader(ResourceFile resource)
{
    private const ulong SystemBase = 0x50000000;
    private const ulong GraphicsBase = 0x60000000;

    public ResourceFile Resource { get; } = resource;

    public ReadOnlySpan<byte> At(ulong pointer, int length)
    {
        var (segment, offset) = Resolve(pointer);
        if (offset + (long)length > segment.Length)
            throw new InvalidDataException($"Pointer 0x{pointer:X} + {length} runs past its segment ({segment.Length} bytes).");
        return segment.AsSpan((int)offset, length);
    }

    public ushort UInt16(ulong pointer) => BinaryPrimitives.ReadUInt16LittleEndian(At(pointer, 2));
    public uint UInt32(ulong pointer) => BinaryPrimitives.ReadUInt32LittleEndian(At(pointer, 4));
    public ulong UInt64(ulong pointer) => BinaryPrimitives.ReadUInt64LittleEndian(At(pointer, 8));

    public string? CString(ulong pointer, int maxLength = 256)
    {
        if (pointer == 0) return null;
        var (segment, offset) = Resolve(pointer);
        var available = segment.AsSpan((int)offset, (int)Math.Min(maxLength, segment.Length - offset));
        var end = available.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? available : available[..end]);
    }

    /// <summary>Reads a 64-bit "pointer + count + capacity" array header and returns its element pointers.</summary>
    public IReadOnlyList<ulong> PointerArray(ulong arrayHeader)
    {
        var data = UInt64(arrayHeader);
        var count = UInt16(arrayHeader + 8);
        var result = new ulong[count];
        for (var i = 0; i < count; i++)
            result[i] = UInt64(data + (ulong)(i * 8));
        return result;
    }

    /// <summary>Reads a 64-bit "pointer + count + capacity" array of uint32 values.</summary>
    public IReadOnlyList<uint> UInt32Array(ulong arrayHeader)
    {
        var data = UInt64(arrayHeader);
        var count = UInt16(arrayHeader + 8);
        var result = new uint[count];
        for (var i = 0; i < count; i++)
            result[i] = UInt32(data + (ulong)(i * 4));
        return result;
    }

    private (byte[] Segment, long Offset) Resolve(ulong pointer)
    {
        var offset = (long)(pointer & 0x0FFFFFFF);
        var segment = (pointer & 0xF0000000) switch
        {
            SystemBase => Resource.SystemData,
            GraphicsBase => Resource.GraphicsData,
            _ => throw new InvalidDataException($"Invalid resource pointer 0x{pointer:X}."),
        };
        if (offset >= segment.Length)
            throw new InvalidDataException($"Pointer 0x{pointer:X} is outside its segment ({segment.Length} bytes).");
        return (segment, offset);
    }
}
