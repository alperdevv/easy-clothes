using System.Buffers.Binary;
using System.Text;

namespace EasyClothes.Core.Rage;

// Lays out a resource from scratch: structures go in the system segment and return its virtual
// pointers, bulk texture data goes in the graphics segment.
internal sealed class ResourceBuilder
{
    private const ulong SystemBase = 0x50000000;
    private const ulong GraphicsBase = 0x60000000;
    private const int Alignment = 16;

    private byte[] _system = new byte[0x1000];
    private int _systemLength;
    private readonly MemoryStream _graphics = new();

    public ulong Reserve(int size)
    {
        var at = Align(_systemLength);
        if (at + size > _system.Length) System.Array.Resize(ref _system, Math.Max(_system.Length * 2, at + size));
        _systemLength = at + size;
        return SystemBase + (ulong)at;
    }

    public ulong Add(ReadOnlySpan<byte> data)
    {
        var pointer = Reserve(data.Length);
        data.CopyTo(Span(pointer, data.Length));
        return pointer;
    }

    public ulong AddString(string text) => Add(Encoding.ASCII.GetBytes(text + "\0"));

    public ulong AddGraphics(IEnumerable<byte[]> blocks)
    {
        _graphics.SetLength(Align((int)_graphics.Length));
        _graphics.Position = _graphics.Length;
        var pointer = GraphicsBase + (ulong)_graphics.Position;
        foreach (var block in blocks) _graphics.Write(block);
        return pointer;
    }

    public void UInt8(ulong at, byte value) => Span(at, 1)[0] = value;
    public void UInt16(ulong at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Span(at, 2), value);
    public void UInt32(ulong at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Span(at, 4), value);
    public void UInt64(ulong at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Span(at, 8), value);
    public void Float(ulong at, float value) => BinaryPrimitives.WriteSingleLittleEndian(Span(at, 4), value);

    // Count and capacity follow the pointer in the game's arrays.
    public void Array(ulong at, ulong pointer, int count)
    {
        UInt64(at, pointer);
        UInt16(at + 8, (ushort)count);
        UInt16(at + 10, (ushort)count);
    }

    public ResourceFile Build(uint version) =>
        ResourceFile.Create(version, _system[.._systemLength], _graphics.ToArray());

    private Span<byte> Span(ulong pointer, int length)
    {
        if ((pointer & 0xF0000000) != SystemBase) throw new ArgumentOutOfRangeException(nameof(pointer), "Only system memory is writable.");
        return _system.AsSpan((int)(pointer - SystemBase), length);
    }

    private static int Align(int value) => (value + Alignment - 1) / Alignment * Alignment;
}
