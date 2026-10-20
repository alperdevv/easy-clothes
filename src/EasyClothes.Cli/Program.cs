using System.Globalization;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

return args switch
{
    ["inspect", var path] => Inspect(path),
    ["export-png", var path, var outDir] => ExportPng(path, outDir),
    ["export-obj", var path, var outDir] => ExportObj(path, outDir),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          easyclothes inspect <file.ydd|file.ytd>      Show resource header and list textures or drawables
          easyclothes export-png <file.ytd> <out-dir>  Decode every texture to PNG
          easyclothes export-obj <file.ydd> <out-dir>  Export each drawable's high LOD as Wavefront OBJ
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

static int ExportPng(string path, string outDir)
{
    var resource = ResourceFile.Read(path);
    Directory.CreateDirectory(outDir);
    foreach (var texture in TextureDictionary.Read(resource))
    {
        var target = Path.Combine(outDir, texture.Name + ".png");
        using var file = File.Create(target);
        PngWriter.Write(TextureDictionary.Decode(resource, texture), file);
        Console.WriteLine(target);
    }
    return 0;
}

static int ExportObj(string path, string outDir)
{
    Directory.CreateDirectory(outDir);
    foreach (var drawable in DrawableDictionary.Read(ResourceFile.Read(path), includeMeshes: true))
    {
        var target = Path.Combine(outDir, drawable.Name + ".obj");
        using var obj = new StreamWriter(target);
        var inv = CultureInfo.InvariantCulture;
        var baseVertex = 1;
        var geometries = drawable.Lods[0].Models.SelectMany(m => m).Select(g => g.Mesh!).ToList();
        for (var gi = 0; gi < geometries.Count; gi++)
        {
            var mesh = geometries[gi];
            obj.WriteLine($"o geometry_{gi}");
            for (var v = 0; v < mesh.VertexCount; v++)
            {
                obj.WriteLine(string.Format(inv, "v {0} {1} {2}", mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]));
                obj.WriteLine(string.Format(inv, "vn {0} {1} {2}", mesh.Normals[v * 3], mesh.Normals[v * 3 + 1], mesh.Normals[v * 3 + 2]));
                // OBJ's V axis points up; GTA's points down.
                obj.WriteLine(string.Format(inv, "vt {0} {1}", mesh.TexCoords[v * 2], 1 - mesh.TexCoords[v * 2 + 1]));
            }
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                int a = mesh.Indices[t * 3] + baseVertex, b = mesh.Indices[t * 3 + 1] + baseVertex, c = mesh.Indices[t * 3 + 2] + baseVertex;
                obj.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }
            baseVertex += mesh.VertexCount;
        }
        Console.WriteLine(target);
    }
    return 0;
}
