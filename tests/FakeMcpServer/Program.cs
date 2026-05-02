using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Все логи направляем в stderr, чтобы не ломать stdio-протокол MCP.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class EchoTools
{
    [McpServerTool(Name = "Echo"), Description("Echoes the provided message back.")]
    public static string Echo([Description("Message to echo.")] string message) => message;

    [McpServerTool(Name = "Add"), Description("Adds two integers.")]
    public static int Add(
        [Description("First addend.")] int a,
        [Description("Second addend.")] int b) => a + b;
}
