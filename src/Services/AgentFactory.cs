using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Builds <see cref="AIAgent"/> instances for all Microsoft.Agents.AI workflow paths.
/// The chat client comes from <see cref="IChatClientProvider"/> (already wrapped with
/// context-budget trimming), gets UseFunctionInvocation on the IChatClient and
/// UseOpenTelemetry on the agent itself.
/// </summary>
public sealed class AgentFactory : IAgentFactory
{
    public const string TelemetrySourceName = "MagenticWorkflowApp.Agents";

    private readonly IChatClientProvider clientProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly IServiceProvider services;

    public AgentFactory(
        IChatClientProvider clientProvider,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        this.clientProvider = clientProvider;
        this.loggerFactory = loggerFactory;
        this.services = services;
    }

    public AIAgent BuildAgent(
        AgentConfiguration config,
        IReadOnlyList<AITool>? tools = null,
        string? nameOverride = null,
        int? historyWindowOverride = null)
    {
        IChatClient chatClient = clientProvider
            .GetChatClient(config.ModelId, config.EnableThinking, historyWindowOverride)
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory)
            .Build();

        var options = new ChatClientAgentOptions
        {
            Name = nameOverride ?? config.Name,
            Description = string.IsNullOrWhiteSpace(config.Description) ? null : config.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = config.Instructions,
                Tools = tools is { Count: > 0 } ? tools.ToList() : null,
                MaxOutputTokens = config.MaxOutputTokens,
                Temperature = config.Temperature,
            },
        };

        var baseAgent = chatClient.AsAIAgent(options, loggerFactory, services);

        return baseAgent
            .AsBuilder()
            .UseOpenTelemetry(TelemetrySourceName, t => t.EnableSensitiveData = true)
            .Build();
    }
}
