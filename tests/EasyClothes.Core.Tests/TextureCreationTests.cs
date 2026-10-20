using System.Buffers.Binary;
using System.IO.Compression;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public class TextureCreationTests
{
    [Fact]
    public void Png_written_as_rgba_reads_back_identically()
    {
        var image = new RgbaImage(3, 2, Enumerable.Range(0, 24).Select(i => (byte)(i * 10)).ToArray());
        using var stream = new MemoryStream();
        PngWriter.Write(image, stream);
        stream.Position = 0;

        var read = PngReader.Read(stream);

        Assert.Equal(image.Pixels, read.Pixels);
    }

    [Fact]
    public void Rgb_png_with_sub_and_up_filters_decodes()
    {
        byte[] row0 = [1, 10, 10, 10, 5, 5, 5];
        byte[] row1 = [2, 1, 2, 3, 1, 2, 3];
        using var stream = new MemoryStream();
        WriteRgbPng(stream, 2, 2, [.. row0, .. row1]);
        stream.Position = 0;

        var read = PngReader.Read(stream);

        Assert.Equal([10, 10, 10, 255, 15, 15, 15, 255, 11, 12, 13, 255, 16, 17, 18, 255], read.Pixels);
    }

    [Fact]
    public void Created_dictionary_lists_and_decodes_every_texture()
    {
        var red = Solid(64, 32, 255, 0, 0, 255);
        var clear = Solid(16, 16, 0, 0, 255, 0);
        var file = TextureDictionaryWriter.Create([
            ("rojo", TextureEncoder.Encode(red, TextureFormat.Dxt1)),
            ("Transparente", TextureEncoder.Encode(clear, TextureFormat.Dxt5)),
        ]);
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        var resource = ResourceFile.Read(stream);

        var textures = TextureDictionary.Read(resource);

        Assert.Equal(["Transparente", "rojo"], textures.Select(t => t.Name).Order(StringComparer.Ordinal));
        var rojo = textures.Single(t => t.Name == "rojo");
        Assert.Equal((64, 32, "DXT1", TextureEncoder.MipCount(64, 32)), (rojo.Width, rojo.Height, rojo.Format, rojo.MipLevels));
        Assert.Equal(255, TextureDictionary.Decode(resource, rojo).Pixels[0]);
        Assert.Equal(0, TextureDictionary.Decode(resource, textures.Single(t => t.Name == "Transparente")).Pixels[3]);
        var hashes = textures.Select(t => Jenkins.Hash(t.Name)).ToList();
        Assert.Equal(hashes.Order(), hashes);
    }

    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b, byte a) =>
        new(w, h, Enumerable.Range(0, w * h).SelectMany(_ => new[] { r, g, b, a }).ToArray());

    private static void WriteRgbPng(Stream output, int width, int height, byte[] filteredRows)
    {
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(output, "IHDR", ihdr);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) z.Write(filteredRows);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(System.Text.Encoding.ASCII.GetBytes(type));
        output.Write(data);
        output.Write(new byte[4]);
    }
}
