using EasyClothes.Core.Rage;

return args switch
{
    ["inspect", var path] => Inspect(path),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          easyclothes inspect <file.ydd|file.ytd>   Show resource header and list textures or drawables
        """);
    return 2;
}

static int Inspect(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 1;
    }

    try
    {
        var resource = ResourceFile.Read(path);
        var h = resource.Header;
        Console.WriteLine($"File:          {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes on disk)");
        Console.WriteLine($"Format:        RSC7, version {h.Version}");
        Console.WriteLine($"System data:   {h.SystemSize:N0} bytes (flags 0x{h.SystemFlags:X8})");
        Console.WriteLine($"Graphics data: {h.GraphicsSize:N0} bytes (flags 0x{h.GraphicsFlags:X8})");

        switch (h.Version)
        {
            case TextureDictionary.ResourceVersion:
                PrintTextures(TextureDictionary.Read(resource));
                break;
            case DrawableDictionary.ResourceVersion:
                PrintDrawables(DrawableDictionary.Read(resource));
                break;
            default:
                Console.WriteLine("Contents:      (no reader for this resource version yet)");
                break;
        }
        return 0;
    }
    catch (InvalidDataException ex)
    {
        Console.Error.WriteLine($"Cannot read {path}: {ex.Message}");
        return 1;
    }
}

static void PrintTextures(IReadOnlyList<TextureInfo> textures)
{
    Console.WriteLine($"Textures:      {textures.Count}");
    foreach (var t in textures)
        Console.WriteLine($"  {t.Name,-32} {t.Width,5}x{t.Height,-5} {t.Format,-9} {t.MipLevels} mips");
}

static void PrintDrawables(IReadOnlyList<DrawableInfo> drawables)
{
    Console.WriteLine($"Drawables:     {drawables.Count}");
    foreach (var d in drawables)
    {
        Console.WriteLine($"  {d.Name}");
        foreach (var lod in d.Lods)
        {
            var geometries = lod.Models.Sum(m => m.Count);
            Console.WriteLine($"    {lod.Level,-8} {lod.Models.Count} model(s), {geometries} geometr{(geometries == 1 ? "y" : "ies")}, {lod.Vertices:N0} vertices, {lod.Triangles:N0} triangles");
        }
    }
}
