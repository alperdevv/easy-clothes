using System.Buffers.Binary;
using System.IO.Compression;

namespace EasyClothes.Core.Rage;

// RSC7 layout: magic, version, system flags, graphics flags (4 × uint32 LE),
// then a raw-deflate stream with both segments back to back.
public sealed record ResourceHeader(uint Version, uint SystemFlags, uint GraphicsFlags)
{
    public const uint Rsc7Magic = 0x37435352; // "RSC7"
    public const int Size = 16;

    public int SystemSize => SizeFromFlags(SystemFlags);
    public int GraphicsSize => SizeFromFlags(GraphicsFlags);

    public static ResourceHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"File is {data.Length} bytes; an RSC7 header needs {Size}.");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != Rsc7Magic)
            throw new InvalidDataException($"Not an RSC7 resource (magic 0x{magic:X8}). Encrypted or packed files are not supported.");

        return new ResourceHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]));
    }

    // Low nibble = base page size (0x200 << n); the other bit fields count pages of decreasing size.
    internal static int SizeFromFlags(uint flags)
    {
        var baseSize = 0x200L << (int)(flags & 0xF);
        long pages =
            (((flags >> 27) & 0x1) << 0) +
            (((flags >> 26) & 0x1) << 1) +
            (((flags >> 25) & 0x1) << 2) +
            (((flags >> 24) & 0x1) << 3) +
            (((flags >> 17) & 0x7F) << 4) +
            (((flags >> 11) & 0x3F) << 5) +
            (((flags >> 7) & 0xF) << 6) +
            (((flags >> 5) & 0x3) << 7) +
            (((flags >> 4) & 0x1) << 8);
        return checked((int)(baseSize * pages));
    }
}

public sealed class ResourceFile
{
    public required ResourceHeader Header { get; init; }
    public required byte[] SystemData { get; init; }
    public required byte[] GraphicsData { get; init; }

    public static ResourceFile Read(Stream stream)
    {
        Span<byte> headerBytes = stackalloc byte[ResourceHeader.Size];
        stream.ReadExactly(headerBytes);
        var header = ResourceHeader.Read(headerBytes);

        var expected = header.SystemSize + header.GraphicsSize;
        var buffer = new byte[expected];
        using (var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true))
        {
            var read = deflate.ReadAtLeast(buffer, expected, throwOnEndOfStream: false);
            if (read < expected)
                throw new InvalidDataException($"Resource data truncated: expected {expected} bytes, got {read}.");
        }

        return new ResourceFile
        {
            Header = header,
            SystemData = buffer[..header.SystemSize],
            GraphicsData = buffer[header.SystemSize..],
        };
    }

    public static ResourceFile Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }
}
