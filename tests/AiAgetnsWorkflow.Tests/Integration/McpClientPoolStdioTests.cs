using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Integration;

[Trait("Category", "Integration")]
public class McpClientPoolStdioTests
{
    private static McpServerConfiguration FakeServer() => new()
    {
        Name = "fake",
        Transport = "stdio",
        Command = "dotnet",
        Args = new() { Path.Combine(AppContext.BaseDirectory, "FakeMcpServer.dll") },
        StartupTimeoutSeconds = 30
    };

    [Fact]
    public async Task GetTools_StartsServerAndExposesEchoAndAdd()
    {
        await using var pool = new McpClientPool(NullLogger<McpClientPool>.Instance);
        await pool.RegisterServersAsync(new[] { FakeServer() });
        var tools = await pool.GetToolsAsync(new[] { "fake" });

        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain(new[] { "Echo", "Add" });
    }

    [Fact]
    public async Task EchoTool_InvokeReturnsInput()
    {
        await using var pool = new McpClientPool(NullLogger<McpClientPool>.Instance);
        await pool.RegisterServersAsync(new[] { FakeServer() });
        var tools = await pool.GetToolsAsync(new[] { "fake" });

        var echo = (AIFunction)tools.First(t => t.Name == "Echo");
        var result = await echo.InvokeAsync(new() { ["message"] = "hi" });
        result?.ToString().Should().Contain("hi");
    }
}
