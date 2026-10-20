using System.Buffers.Binary;
using System.IO.Compression;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Tests;

public class ResourceFileTests
{
    private const uint OnePage512 = 1u << 27;
    private const uint SixteenPages1K = (1u << 17) | 1;

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(OnePage512, 0x200)]
    [InlineData(SixteenPages1K, 0x400 * 16)]
    public void Decodes_page_flags_into_sizes(uint flags, int expected) =>
        Assert.Equal(expected, ResourceHeader.SizeFromFlags(flags));

    [Fact]
    public void Reads_segments_from_a_compressed_resource()
    {
        var system = Enumerable.Range(0, 0x200).Select(i => (byte)i).ToArray();
        var graphics = Enumerable.Repeat((byte)0xAB, 0x200 * 2).ToArray();
        using var file = BuildResource(version: 165, OnePage512, (1u << 26), system, graphics);

        var resource = ResourceFile.Read(file);

        Assert.Equal(165u, resource.Header.Version);
        Assert.Equal(system, resource.SystemData);
        Assert.Equal(graphics, resource.GraphicsData);
    }

    [Fact]
    public void Rejects_files_without_rsc7_magic()
    {
        using var file = new MemoryStream(new byte[32]);
        var ex = Assert.Throws<InvalidDataException>(() => ResourceFile.Read(file));
        Assert.Contains("Not an RSC7", ex.Message);
    }

    [Fact]
    public void Rejects_empty_files_as_invalid_data()
    {
        using var file = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => ResourceFile.Read(file));
    }

    [Fact]
    public void Rejects_truncated_data()
    {
        using var file = BuildResource(13, OnePage512, OnePage512, new byte[0x200], new byte[10]);
        var ex = Assert.Throws<InvalidDataException>(() => ResourceFile.Read(file));
        Assert.Contains("truncated", ex.Message);
    }

    private static MemoryStream BuildResource(uint version, uint systemFlags, uint graphicsFlags, byte[] system, byte[] graphics)
    {
        var ms = new MemoryStream();
        Span<byte> header = stackalloc byte[ResourceHeader.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(header, ResourceHeader.Rsc7Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], systemFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], graphicsFlags);
        ms.Write(header);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(system);
            deflate.Write(graphics);
        }
        ms.Position = 0;
        return ms;
    }
}
