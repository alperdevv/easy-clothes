using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Textures;

public sealed record TextureChange(string Name, int OldWidth, int OldHeight, string OldFormat, int NewWidth, int NewHeight, TextureFormat NewFormat, int Mips);

public sealed record TextureOptimization(long OldFileSize, long NewFileSize, int OldGraphicsSize, int NewGraphicsSize, IReadOnlyList<TextureChange> Changes);

public static class TextureOptimizer
{
    // Downscales to fit maxSide (power-of-two sides), regenerates mips and recompresses:
    // DXT1/DXT5 keep their format, uncompressed textures become DXT1 or DXT5 depending on alpha.
    public static TextureOptimization Optimize(string path, int maxSide)
    {
        var oldFileSize = new FileInfo(path).Length;
        var resource = ResourceFile.Read(path);
        var encoded = new List<EncodedTexture>();
        var changes = new List<TextureChange>();

        foreach (var info in TextureDictionary.Read(resource))
        {
            var image = TextureDictionary.Decode(resource, info);
            var (width, height) = TargetSize(info.Width, info.Height, maxSide);
            image = ImageResampler.Resize(image, width, height);
            var format = info.PixelFormat switch
            {
                TextureFormat.Dxt1 or TextureFormat.Dxt5 => info.PixelFormat,
                _ => TextureEncoder.HasTransparency(image) ? TextureFormat.Dxt5 : TextureFormat.Dxt1,
            };
            var texture = TextureEncoder.Encode(image, format);
            encoded.Add(texture);
            changes.Add(new TextureChange(info.Name, info.Width, info.Height, info.Format, width, height, format, texture.Mips.Count));
        }

        var rebuilt = TextureDictionaryWriter.Rebuild(resource, encoded);
        rebuilt.Write(path);
        return new TextureOptimization(oldFileSize, new FileInfo(path).Length, resource.Header.GraphicsSize, rebuilt.Header.GraphicsSize, changes);
    }

    public static (int Width, int Height) TargetSize(int width, int height, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(width, height));
        return (FloorPow2((int)(width * scale)), FloorPow2((int)(height * scale)));
    }

    private static int FloorPow2(int value) =>
        Math.Max(4, 1 << (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(1, value))));
}
