using System.Net;
using EasyClothes.Core.Settings;

namespace EasyClothes.Core.Tests;

public sealed class UpdaterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-update-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Release = """
        {
          "tag_name": "v0.9.2",
          "body": "Avisos al importar.\n",
          "assets": [
            { "name": "notas.txt", "browser_download_url": "https://example.com/notas.txt" },
            { "name": "EasyClothes-0.9.2-setup.exe", "browser_download_url": "https://example.com/EasyClothes-0.9.2-setup.exe" }
          ]
        }
        """;

    [Theory]
    [InlineData("0.9.1", true)]
    [InlineData("0.9.1.0", true)]
    [InlineData("0.9.2", false)]
    [InlineData("1.0.0", false)]
    public async Task Offers_only_a_newer_release_with_its_installer(string current, bool offered)
    {
        var http = new HttpClient(new Stub(HttpStatusCode.OK, Release));

        var update = await Updater.CheckAsync(http, Version.Parse(current));

        Assert.Equal(offered, update is not null);
        if (update is null) return;
        Assert.Equal(new Version(0, 9, 2), update.Version);
        Assert.Equal("EasyClothes-0.9.2-setup.exe", update.InstallerName);
        Assert.Equal("Avisos al importar.", update.Notes);
    }

    [Fact]
    public async Task A_repository_without_releases_has_nothing_to_offer()
    {
        var http = new HttpClient(new Stub(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        Assert.Null(await Updater.CheckAsync(http, new Version(0, 9, 1)));
    }

    [Fact]
    public void A_release_without_an_installer_is_ignored()
    {
        Assert.Null(Updater.ParseRelease("""{ "tag_name": "v2.0.0", "assets": [] }"""));
        Assert.Null(Updater.ParseRelease("""{ "tag_name": "beta", "assets": [] }"""));
    }

    [Fact]
    public async Task Download_leaves_the_installer_and_no_partial_file()
    {
        var http = new HttpClient(new Stub(HttpStatusCode.OK, "MZ installer bytes"));
        var update = Updater.ParseRelease(Release)!;

        var path = await Updater.DownloadAsync(http, update, _dir);

        Assert.Equal(Path.Combine(_dir, "EasyClothes-0.9.2-setup.exe"), path);
        Assert.Equal("MZ installer bytes", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
