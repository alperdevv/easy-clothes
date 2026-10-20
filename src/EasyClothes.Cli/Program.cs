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
          easyclothes inspect <file.ydd|file.ytd|...>   Show resource header and segment sizes
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
        return 0;
    }
    catch (InvalidDataException ex)
    {
        Console.Error.WriteLine($"Cannot read {path}: {ex.Message}");
        return 1;
    }
}
