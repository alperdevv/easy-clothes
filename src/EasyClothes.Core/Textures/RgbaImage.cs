namespace EasyClothes.Core.Textures;

/// <summary>Uncompressed 8-bit RGBA image, rows top to bottom.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);
