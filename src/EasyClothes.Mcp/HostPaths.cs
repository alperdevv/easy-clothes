using System.Text.RegularExpressions;

namespace EasyClothes.Mcp;

// Claude may pass Windows paths to a server running in WSL and the other way round.
public static partial class HostPaths
{
    public static string Normalize(string path)
    {
        path = path.Trim().Trim('"');
        if (OperatingSystem.IsWindows())
        {
            var wsl = WslPath().Match(path);
            if (wsl.Success) path = $"{char.ToUpperInvariant(wsl.Groups["drive"].Value[0])}:\\{wsl.Groups["rest"].Value.Replace('/', '\\')}";
        }
        else
        {
            var windows = WindowsPath().Match(path);
            if (windows.Success) path = $"/mnt/{char.ToLowerInvariant(windows.Groups["drive"].Value[0])}/{windows.Groups["rest"].Value.Replace('\\', '/')}";
        }
        var full = Path.GetFullPath(path);
        return Path.GetPathRoot(full) == full ? full : Path.TrimEndingDirectorySeparator(full);
    }

    [GeneratedRegex(@"^/mnt/(?<drive>[a-zA-Z])(/(?<rest>.*))?$")]
    private static partial Regex WslPath();

    [GeneratedRegex(@"^(?<drive>[a-zA-Z]):[\\/]?(?<rest>.*)$")]
    private static partial Regex WindowsPath();
}
