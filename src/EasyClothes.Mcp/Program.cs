using System.Text.Encodings.Web;
using System.Text.Json;
using EasyClothes.Mcp;
using ModelContextProtocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
// stdout carries the MCP messages, so every log line goes to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(ServerOptions.Parse(args));
builder.Services.AddSingleton<Workspace>();
builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "easyclothes", Version = "0.1.0" })
    .WithStdioServerTransport()
    // Unescaped accents keep the Spanish messages readable and shorter.
    .WithToolsFromAssembly(serializerOptions: new JsonSerializerOptions(McpJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
await builder.Build().RunAsync();
