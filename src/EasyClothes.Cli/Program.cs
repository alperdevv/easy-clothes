using System.Globalization;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Meshes;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Validation;

return args switch
{
    ["new", var dir, var name] => await NewProject(dir, name),
    ["import", var project, .. var models] when models.Length > 0 => await Import(project, models),
    ["validate", var project] => await Validate(project),
    ["build", var project, var outDir] => await Build(project, outDir),
    ["pack", var project, var package] => Pack(project, package),
    ["unpack", var package, var dir] => Unpack(package, dir),
    ["inspect", var path] => Inspect(path),
    ["stats", var dir] => Stats(dir),
    ["optimize-texture", var path, var maxSide] => OptimizeTexture(path, int.Parse(maxSide)),
    ["reduce-mesh", var path, var triangles] => ReduceMesh(path, int.Parse(triangles)),
    ["export-png", var path, var outDir] => ExportPng(path, outDir),
    ["export-obj", var path, var outDir] => ExportObj(path, outDir),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          easyclothes new <dir> <name>                 Create an empty project
          easyclothes import <project> <file.ydd>...   Import models and the textures next to them
          easyclothes validate <project>               List project problems (exit 1 on errors)
          easyclothes build <project> <out-dir>        Generate the FiveM resource (fxmanifest, stream, .ymt, .meta)
          easyclothes pack <project> <file.ecpack>     Zip the project with its assets
          easyclothes unpack <file.ecpack> <dir>       Extract a packaged project into an empty folder
          easyclothes inspect <file.ydd|file.ytd>      Show resource header and list textures or drawables
          easyclothes optimize-texture <file.ytd> <max-side>  Resize, regenerate mips and recompress in place
          easyclothes reduce-mesh <file.ydd> <triangles>      Simplify the high LOD in place
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

static async Task<int> NewProject(string dir, string name)
{
    Directory.CreateDirectory(dir);
    var collection = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
    var path = Path.Combine(dir, collection + ProjectFile.Extension);
    await ProjectFile.SaveAsync(new ClothingProject { Name = name, CollectionName = collection }, path);
    Console.WriteLine(path);
    return 0;
}

static async Task<int> Import(string projectPath, string[] models)
{
    var project = await ProjectFile.LoadAsync(projectPath);
    var result = DrawableImporter.Import(project, Path.GetDirectoryName(Path.GetFullPath(projectPath))!, models, PedGender.Male);
    await ProjectFile.SaveAsync(project, projectPath);
    foreach (var d in result.Added) Console.WriteLine($"+ {d.Gender} {d.Name} ({d.Textures.Count} textures)");
    foreach (var w in result.Warnings) Console.Error.WriteLine($"! {w}");
    return 0;
}

static async Task<int> Validate(string projectPath)
{
    var issues = ProjectValidator.Validate(await ProjectFile.LoadAsync(projectPath), Path.GetDirectoryName(Path.GetFullPath(projectPath))!, new AnalysisCache());
    foreach (var i in issues)
        Console.WriteLine($"{i.Severity.ToString().ToLowerInvariant()}: {i.Message}{(i.Fix is { } fix ? $" [fix: {fix.Label}]" : "")}");
    Console.WriteLine(issues.Count == 0 ? "No problems found." : $"{issues.Count} problem(s).");
    return issues.Any(i => i.Severity == IssueSeverity.Error) ? 1 : 0;
}

static async Task<int> Build(string projectPath, string outDir)
{
    try
    {
        var result = ResourceExporter.Export(await ProjectFile.LoadAsync(projectPath), Path.GetDirectoryName(Path.GetFullPath(projectPath))!, outDir);
        foreach (var w in result.Warnings) Console.Error.WriteLine($"! {w}");
        Console.WriteLine($"{result.Drawables} drawable(s), {result.Files} file(s) written to {result.OutputDir}");
        return 0;
    }
    catch (ExportException ex)
    {
        foreach (var e in ex.Errors) Console.Error.WriteLine($"error: {e}");
        return 1;
    }
}

static int Pack(string projectPath, string package)
{
    Console.WriteLine($"{ProjectPackage.Export(projectPath, package)} file(s) packed into {package}");
    return 0;
}

static int Unpack(string package, string dir)
{
    Console.WriteLine($"Project extracted: {ProjectPackage.Import(package, dir)}");
    return 0;
}

static int Stats(string dir)
{
    var models = Directory.EnumerateFiles(dir, "*.ydd", SearchOption.AllDirectories)
        .Select(p => (Path: p, Report: AssetAnalyzer.AnalyzeModel(p))).ToList();
    var textures = Directory.EnumerateFiles(dir, "*.ytd", SearchOption.AllDirectories)
        .Select(p => (Path: p, Report: AssetAnalyzer.AnalyzeTexture(p))).ToList();

    static string Percentiles(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return string.Join("  ", new[] { 50, 90, 99, 100 }.Select(p => $"p{p}={sorted[Math.Min(sorted.Length - 1, sorted.Length * p / 100)]:N0}"));
    }

    Console.WriteLine($"Models: {models.Count}");
    Console.WriteLine($"  high LOD triangles  {Percentiles(models.Select(m => (double)(m.Report.High?.Triangles ?? 0)))}");
    Console.WriteLine($"  graphics KiB        {Percentiles(models.Select(m => m.Report.GraphicsSize / 1024.0))}");
    Console.WriteLine($"  with UV out of 0..1 {models.Count(m => m.Report.UvOutOfRange > 0)}  invalid UV {models.Count(m => m.Report.UvInvalid > 0)}  degenerate {models.Count(m => m.Report.DegenerateTriangles > 0)}");
    Console.WriteLine($"  without Medium LOD  {models.Count(m => m.Report.Lods.All(l => l.Level != LodLevel.Medium))}");
    var all = textures.SelectMany(t => t.Report.Textures).ToList();
    Console.WriteLine($"Textures: {all.Count} in {textures.Count} files");
    Console.WriteLine($"  max side            {Percentiles(all.Select(t => (double)Math.Max(t.Width, t.Height)))}");
    Console.WriteLine($"  graphics KiB        {Percentiles(textures.Select(t => t.Report.GraphicsSize / 1024.0))}");
    Console.WriteLine($"  non power of two    {all.Count(t => !int.IsPow2(t.Width) || !int.IsPow2(t.Height))}  single mip {all.Count(t => t.MipLevels == 1 && Math.Max(t.Width, t.Height) > 64)}");
    foreach (var (path, report) in models.Where(m => m.Report.UvOutOfRange > 0).Take(5))
        Console.WriteLine($"  uv sample: {Path.GetFileName(path)} {report.UvOutOfRange}/{report.Vertices}");
    return 0;
}

static int OptimizeTexture(string path, int maxSide)
{
    var result = TextureOptimizer.Optimize(path, maxSide);
    foreach (var c in result.Changes)
        Console.WriteLine($"{c.Name}: {c.OldWidth}x{c.OldHeight} {c.OldFormat} -> {c.NewWidth}x{c.NewHeight} {c.NewFormat}, {c.Mips} mips");
    Console.WriteLine($"File {result.OldFileSize:N0} -> {result.NewFileSize:N0} bytes, graphics memory {result.OldGraphicsSize / 1024:N0} -> {result.NewGraphicsSize / 1024:N0} KiB");
    return 0;
}

static int ReduceMesh(string path, int triangles)
{
    var result = MeshReducer.Reduce(path, triangles);
    Console.WriteLine($"Triangles {result.OldTriangles:N0} -> {result.NewTriangles:N0}, vertices {result.OldVertices:N0} -> {result.NewVertices:N0}");
    return 0;
}
