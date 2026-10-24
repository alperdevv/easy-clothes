using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EasyClothes.Core.Settings;

public sealed record UpdateInfo(Version Version, string InstallerUrl, string InstallerName, string Notes);

// New versions are GitHub releases of the repository with the setup .exe attached (installer/release.sh).
public static class Updater
{
    public const string Repository = "alperdevv/easy-clothes";
    public static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static Version CurrentVersion => Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // Null when there is no newer release, or the latest one has no installer attached.
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, Version current, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EasyClothes", current.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        // A repository without releases answers 404.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var latest = ParseRelease(await response.Content.ReadAsStringAsync(ct));
        return latest is not null && latest.Version > Normalize(current) ? latest : null;
    }

    public static UpdateInfo? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new UpdateInfo(Normalize(version), asset.GetProperty("browser_download_url").GetString()!, name, notes.Trim());
        }
        return null;
    }

    // Downloads under a temporary name and renames at the end, so a cut download is never run.
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo update, string folder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, update.InstallerName);
        var partial = target + ".part";
        using (var response = await http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(partial);
            await response.Content.CopyToAsync(file, ct);
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }

    // 0.9.1 and 0.9.1.0 are the same version.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
