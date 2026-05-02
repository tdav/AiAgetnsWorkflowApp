using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Mcp;

public class McpClientPoolTests
{
    private static McpServerConfiguration Cfg(string name) => new()
    {
        Name = name,
        Transport = "stdio",
        Command = "noop"
    };

    [Fact]
    public async Task Register_DoesNotStartClients()
    {
        var calls = 0;
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => { calls++; return Task.FromResult(StubClient(Array.Empty<string>())); });

        await pool.RegisterServersAsync(new[] { Cfg("A") });
        calls.Should().Be(0);
    }

    [Fact]
    public async Task GetTools_FirstAccess_StartsClientOnce()
    {
        var calls = 0;
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => { calls++; return Task.FromResult(StubClient(new[] { "echo" })); });

        await pool.RegisterServersAsync(new[] { Cfg("A") });
        await pool.GetToolsAsync(new[] { "A" });
        await pool.GetToolsAsync(new[] { "A" });
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetTools_UnknownName_Throws()
    {
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => Task.FromResult(StubClient(Array.Empty<string>())));
        await pool.RegisterServersAsync(new[] { Cfg("A") });

        var act = () => pool.GetToolsAsync(new[] { "B" });
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*B*");
    }

    [Fact]
    public async Task Dispose_DisposesAllStartedClients()
    {
        var disposed = false;
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => Task.FromResult(StubClient(Array.Empty<string>(), () => disposed = true)));

        await pool.RegisterServersAsync(new[] { Cfg("A") });
        await pool.GetToolsAsync(new[] { "A" });
        await pool.DisposeAsync();
        disposed.Should().BeTrue();
    }

    private static IMcpClient StubClient(IReadOnlyList<string> toolNames, Action? onDispose = null)
        => new StubMcpClient(toolNames, onDispose);
}

internal sealed class StubMcpClient(IReadOnlyList<string> toolNames, Action? onDispose) : IMcpClient
{
    public ValueTask DisposeAsync()
    {
        onDispose?.Invoke();
        return ValueTask.CompletedTask;
    }

    public Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AITool> tools = toolNames
            .Select(n => (AITool)new StubTool(n))
            .ToArray();
        return Task.FromResult(tools);
    }

    private sealed class StubTool(string name) : AITool
    {
        public override string Name { get; } = name;
    }
}
