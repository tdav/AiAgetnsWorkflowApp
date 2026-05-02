using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Локальный seam над <c>ModelContextProtocol.Client.McpClient</c>: позволяет подменять
/// реальное MCP-соединение в unit-тестах. В production — обёртка над <see cref="ModelContextProtocol.Client.McpClient"/>.
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken = default);
}

public interface IMcpClientPool : IAsyncDisposable
{
    Task RegisterServersAsync(IReadOnlyList<McpServerConfiguration> servers, CancellationToken ct = default);
    Task<IReadOnlyList<AITool>> GetToolsAsync(IReadOnlyList<string> serverNames, CancellationToken ct = default);
}
