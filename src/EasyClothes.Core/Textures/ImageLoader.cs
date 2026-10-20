using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Textures;

public static class ImageLoader
{
    public static readonly string[] Extensions = [".png", ".ytd"];

    // A .ytd contributes its first texture.
    public static RgbaImage Load(string path)
    {
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) return PngReader.Read(path);
        var resource = ResourceFile.Read(path);
        var texture = TextureDictionary.Read(resource).FirstOrDefault()
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} no contiene texturas.");
        return TextureDictionary.Decode(resource, texture);
    }
}
