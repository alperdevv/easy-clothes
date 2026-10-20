using System.Buffers.Binary;
using System.Text;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

// Builds minimal but structurally valid .ytd files, laid out like the game's own.
internal static class TestResources
{
    public static void WriteTextureDictionary(string path, string name, int width, int height, TextureFormat format, byte[] mip0, int levels = 1)
    {
        var system = new byte[0x200];
        var s = system.AsSpan();
        BinaryPrimitives.WriteUInt64LittleEndian(s[0x08..], 0x500000D0);
        BinaryPrimitives.WriteUInt64LittleEndian(s[0x20..], 0x50000120);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x28..], 0x00010001);
        BinaryPrimitives.WriteUInt64LittleEndian(s[0x30..], 0x50000110);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x38..], 0x00010001);

        var t = s[0x40..];
        BinaryPrimitives.WriteUInt64LittleEndian(t[0x28..], 0x500000F0);
        BinaryPrimitives.WriteUInt16LittleEndian(t[0x50..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(t[0x52..], (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(t[0x54..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(t[0x58..], format switch { TextureFormat.Dxt1 => 0x31545844u, TextureFormat.Dxt5 => 0x35545844u, _ => 21u });
        t[0x5D] = (byte)levels;
        BinaryPrimitives.WriteUInt64LittleEndian(t[0x70..], 0x60000000);

        system[0xD8] = 1;
        system[0xD9] = 1;
        Encoding.ASCII.GetBytes(name).CopyTo(s[0xF0..]);
        BinaryPrimitives.WriteUInt64LittleEndian(s[0x110..], 0x50000040);

        using var file = File.Create(path);
        ResourceFile.Create(TextureDictionary.ResourceVersion, system, mip0).Write(file);
    }

    public static byte[] SolidArgb(int width, int height, byte r, byte g, byte b, byte a)
    {
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = a;
        }
        return data;
    }
}
