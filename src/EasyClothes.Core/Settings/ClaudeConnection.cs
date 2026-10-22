using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyClothes.Core.Settings;

// Registers the EasyClothes MCP server with Claude Desktop and builds the matching Claude Code command.
public static class ClaudeConnection
{
    public const string ServerName = "easyclothes";

    public static string? DesktopConfigPath =>
        OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json")
        : OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude_desktop_config.json")
        : null;

    public static IReadOnlyList<string> ServerArgs(string? bodyFolder) => bodyFolder is null ? [] : ["--body", bodyFolder];

    // Adds or replaces only our entry; every other setting stays and the previous file is kept as .bak.
    public static void AddToDesktop(string configPath, string serverPath, IReadOnlyList<string> args)
    {
        var root = new JsonObject();
        if (File.Exists(configPath))
        {
            var text = File.ReadAllText(configPath);
            if (!string.IsNullOrWhiteSpace(text))
            {
                root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                    ?? throw new InvalidDataException("La configuración de Claude Desktop no es un objeto JSON.");
            }
            File.Copy(configPath, configPath + ".bak", overwrite: true);
        }
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[ServerName] = new JsonObject
        {
            ["command"] = serverPath,
            ["args"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var tmp = configPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, configPath, overwrite: true);
    }

    public static string ClaudeCodeCommand(string serverPath, IReadOnlyList<string> args) =>
        $"claude mcp add --scope user {ServerName} -- {string.Join(" ", new[] { serverPath }.Concat(args).Select(Quote))}";

    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}
