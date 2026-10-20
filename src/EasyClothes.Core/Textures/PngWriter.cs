using System.Buffers.Binary;
using System.IO.Compression;

namespace EasyClothes.Core.Textures;

/// <summary>Minimal PNG encoder (8-bit RGBA, no filtering) for previews and exports.</summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Write(RgbaImage image, Stream output)
    {
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], image.Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // colour type: RGBA
        WriteChunk(output, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var stride = image.Width * 4;
            for (var y = 0; y < image.Height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(image.Pixels, y * stride, stride);
            }
        }
        WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        for (var i = 0; i < 4; i++) header[4 + i] = (byte)type[i];
        output.Write(header);
        output.Write(data);

        var crc = Crc32(Crc32(0xFFFFFFFF, header[4..]), data) ^ 0xFFFFFFFF;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
