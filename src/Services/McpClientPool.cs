using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Пул MCP-клиентов с ленивой инициализацией. Имеет внутреннюю seam:
/// фабрика <see cref="IMcpClient"/> подменяется в тестах на stub без реальных процессов.
/// </summary>
public sealed class McpClientPool : IMcpClientPool
{
    private readonly ILogger<McpClientPool> logger;
    private readonly Func<McpServerConfiguration, CancellationToken, Task<IMcpClient>> clientFactory;
    private readonly Dictionary<string, McpServerConfiguration> configs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMcpClient> clients = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool disposed;

    public McpClientPool(
        ILogger<McpClientPool> logger,
        Func<McpServerConfiguration, CancellationToken, Task<IMcpClient>>? clientFactory = null)
    {
        this.logger = logger;
        this.clientFactory = clientFactory ?? DefaultClientFactory;
    }

    public Task RegisterServersAsync(IReadOnlyList<McpServerConfiguration> servers, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        configs.Clear();
        foreach (var s in servers)
        {
            configs[s.Name] = s;
        }
        logger.LogInformation("Registering {Count} MCP servers", servers.Count);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(IReadOnlyList<string> serverNames, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (serverNames.Count == 0)
        {
            return Array.Empty<AITool>();
        }

        var tools = new List<AITool>();
        foreach (var name in serverNames)
        {
            var client = await GetOrCreateClientAsync(name, ct).ConfigureAwait(false);
            var clientTools = await client.ListToolsAsync(ct).ConfigureAwait(false);
            tools.AddRange(clientTools);
        }
        return tools;
    }

    private async Task<IMcpClient> GetOrCreateClientAsync(string name, CancellationToken ct)
    {
        if (clients.TryGetValue(name, out var existing))
        {
            return existing;
        }
        if (!configs.TryGetValue(name, out var cfg))
        {
            throw new KeyNotFoundException($"MCP server '{name}' is not registered");
        }

        await initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (clients.TryGetValue(name, out existing))
            {
                return existing;
            }
            logger.LogInformation("Starting MCP server {Name} via {Transport}", cfg.Name, cfg.Transport);

            try
            {
                var client = await clientFactory(cfg, ct).ConfigureAwait(false);
                clients[name] = client;
                logger.LogInformation("MCP server {Name} ready", cfg.Name);
                return client;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MCP server {Name} startup failed", cfg.Name);
                throw new McpServerStartupException(cfg.Name, ex.Message, ex);
            }
        }
        finally
        {
            initLock.Release();
        }
    }

    private static async Task<IMcpClient> DefaultClientFactory(McpServerConfiguration cfg, CancellationToken ct)
    {
        IClientTransport transport = cfg.Transport.ToLowerInvariant() switch
        {
            "stdio" => new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = cfg.Command ?? throw new InvalidOperationException($"MCP server '{cfg.Name}': stdio transport requires 'command'"),
                Arguments = cfg.Args,
                EnvironmentVariables = cfg.Env.Count > 0 ? cfg.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value) : null
            }, NullLoggerFactory.Instance),
            "http" => new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(cfg.Url ?? throw new InvalidOperationException($"MCP server '{cfg.Name}': http transport requires 'url'")),
                AdditionalHeaders = cfg.Headers.Count > 0 ? cfg.Headers : null
            }, NullLoggerFactory.Instance),
            _ => throw new InvalidOperationException($"Unknown transport '{cfg.Transport}'")
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.StartupTimeoutSeconds));
        var inner = await McpClient.CreateAsync(
            transport,
            clientOptions: null,
            loggerFactory: null,
            cancellationToken: timeoutCts.Token).ConfigureAwait(false);
        return new RealMcpClient(inner);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (var c in clients.Values)
        {
            try
            {
                await c.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Disposing MCP client failed");
            }
        }
        clients.Clear();
        initLock.Dispose();
    }

    /// <summary>Production-обёртка над реальным <see cref="ModelContextProtocol.Client.McpClient"/>.</summary>
    private sealed class RealMcpClient(McpClient inner) : IMcpClient
    {
        public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            var tools = await inner.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            // McpClientTool : AIFunction : AITool — приводимы к AITool напрямую.
            return tools.Cast<AITool>().ToArray();
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
