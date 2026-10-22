using System.Text.Json.Nodes;
using EasyClothes.Core.Settings;

namespace EasyClothes.Core.Tests;

public sealed class ClaudeConnectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-claude-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Adding_to_claude_desktop_keeps_other_servers_and_settings()
    {
        var config = Path.Combine(_dir, "claude_desktop_config.json");
        File.WriteAllText(config, """
            {
              "mcpServers": {
                "photoshop": { "command": "npx", "args": ["-y", "@alisaitteke/photoshop-mcp"], "env": { "LOG_LEVEL": "1" } },
              },
              "globalShortcut": "Ctrl+Space"
            }
            """);

        ClaudeConnection.AddToDesktop(config, @"C:\EasyClothes\EasyClothes.Mcp.exe", ClaudeConnection.ServerArgs(@"D:\GTA Vice\modelos"));
        ClaudeConnection.AddToDesktop(config, @"C:\EasyClothes\EasyClothes.Mcp.exe", ClaudeConnection.ServerArgs(null));

        var root = JsonNode.Parse(File.ReadAllText(config))!;
        Assert.Equal("npx", root["mcpServers"]!["photoshop"]!["command"]!.GetValue<string>());
        Assert.Equal(@"C:\EasyClothes\EasyClothes.Mcp.exe", root["mcpServers"]!["easyclothes"]!["command"]!.GetValue<string>());
        Assert.Empty(root["mcpServers"]!["easyclothes"]!["args"]!.AsArray());
        Assert.Equal("Ctrl+Space", root["globalShortcut"]!.GetValue<string>());
        Assert.True(File.Exists(config + ".bak"));
    }

    [Fact]
    public void A_missing_config_is_created_and_the_code_command_quotes_spaces()
    {
        var config = Path.Combine(_dir, "nuevo", "claude_desktop_config.json");

        ClaudeConnection.AddToDesktop(config, "/opt/easyclothes/EasyClothes.Mcp", []);

        Assert.NotNull(JsonNode.Parse(File.ReadAllText(config))!["mcpServers"]!["easyclothes"]);
        Assert.Equal("claude mcp add --scope user easyclothes -- C:\\EC\\EasyClothes.Mcp.exe --body \"D:\\GTA Vice\\modelos\"",
            ClaudeConnection.ClaudeCodeCommand(@"C:\EC\EasyClothes.Mcp.exe", ClaudeConnection.ServerArgs(@"D:\GTA Vice\modelos")));
    }
}
