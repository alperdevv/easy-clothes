using EasyClothes.Core.Settings;

namespace EasyClothes.Mcp;

// --root limits the folders Claude can touch (repeatable); --body points at the exported freemode ped.
public sealed record ServerOptions(IReadOnlyList<string> Roots, string? BodyFolder)
{
    public static ServerOptions Parse(string[] args)
    {
        var roots = new List<string>();
        string? body = Environment.GetEnvironmentVariable("EASYCLOTHES_BODY");
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--root") roots.Add(HostPaths.Normalize(args[++i]));
            else if (args[i] == "--body") body = args[++i];
        }
        body ??= AppSettings.Load(AppSettings.DefaultPath).BaseBodyFolder;
        return new ServerOptions(roots, body is null ? null : HostPaths.Normalize(body));
    }
}
