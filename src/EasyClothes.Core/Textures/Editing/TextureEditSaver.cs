using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Textures.Editing;

public enum TextureSaveMode
{
    NewVariation,
    Overwrite,
}

public sealed record TextureSaveResult(string TexturePath, int Index, string? BackupPath);

public static class TextureEditSaver
{
    // The edited image replaces the texture inside a copy of the original dictionary, so the
    // dictionary's own fields and texture name stay as the game expects them.
    public static TextureSaveResult Save(string projectDir, Drawable drawable, int textureIndex, TextureEdit edit, TextureSaveMode mode)
    {
        var sourceRelative = drawable.Textures[textureIndex].TexturePath;
        var original = ResourceFile.Read(Path.Combine(projectDir, sourceRelative));
        var info = TextureDictionary.Read(original).FirstOrDefault()
            ?? throw new InvalidDataException($"{Path.GetFileName(sourceRelative)} no contiene texturas.");

        var image = TextureCompositor.Compose(edit);
        var (width, height) = TextureOptimizer.TargetSize(image.Width, image.Height, Math.Max(image.Width, image.Height));
        image = TextureCompositor.Resample(image, width, height);
        var format = info.PixelFormat == TextureFormat.Dxt1 && !TextureEncoder.HasTransparency(image)
            ? TextureFormat.Dxt1
            : TextureFormat.Dxt5;
        var rebuilt = TextureDictionaryWriter.Rebuild(original, [TextureEncoder.Encode(image, format)]);

        if (mode == TextureSaveMode.Overwrite)
        {
            var backup = ProjectBackups.Copy(projectDir, sourceRelative);
            rebuilt.Write(Path.Combine(projectDir, sourceRelative));
            return new TextureSaveResult(sourceRelative, textureIndex, backup);
        }

        if (drawable.Textures.Count >= Drawable.MaxTextures)
            throw new InvalidOperationException($"{drawable.Name} ya tiene {Drawable.MaxTextures} texturas, el máximo del juego.");
        var relative = UniquePath(projectDir, sourceRelative);
        rebuilt.Write(Path.Combine(projectDir, relative));
        drawable.Textures.Add(new TextureVariation { TexturePath = relative });
        return new TextureSaveResult(relative, drawable.Textures.Count - 1, null);
    }

    private static string UniquePath(string projectDir, string sourceRelative)
    {
        var dir = Path.GetDirectoryName(sourceRelative) ?? "";
        var stem = Path.GetFileNameWithoutExtension(sourceRelative);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_edit{i}.ytd").Replace('\\', '/');
            if (!File.Exists(Path.Combine(projectDir, candidate))) return candidate;
        }
    }
}
