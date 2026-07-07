using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Shared collaborator for graph-based executors (sequential/conditional/concurrent):
/// resolves hosted/MCP/plugin tools, builds agents via <see cref="IAgentFactory"/>,
/// runs a built workflow and funnels its events into <see cref="IAgentActivityLogger"/>.
/// </summary>
public sealed class AgentTeamBuilder
{
    private readonly IAgentFactory agentFactory;
    private readonly IMcpClientPool mcpPool;
    private readonly IHostedToolFactory hostedFactory;
    private readonly IAgentPluginRegistry pluginRegistry;
    private readonly IAgentActivityLogger activity;
    private readonly ILogger<AgentTeamBuilder> logger;

    public AgentTeamBuilder(
        IAgentFactory agentFactory,
        IMcpClientPool mcpPool,
        IHostedToolFactory hostedFactory,
        IAgentPluginRegistry pluginRegistry,
        IAgentActivityLogger activity,
        ILogger<AgentTeamBuilder> logger)
    {
        this.agentFactory = agentFactory;
        this.mcpPool = mcpPool;
        this.hostedFactory = hostedFactory;
        this.pluginRegistry = pluginRegistry;
        this.activity = activity;
        this.logger = logger;
    }

    public void ValidatePluginReferences(WorkflowConfiguration config)
    {
        foreach (var agent in config.Agents)
        {
            foreach (var name in agent.Plugins)
            {
                if (!pluginRegistry.TryGet(name, out _))
                {
                    throw new WorkflowValidationException(
                        $"Agent '{agent.Name}' references unknown plugin '{name}'");
                }
            }
        }
    }

    public async Task<Dictionary<string, AIAgent>> CreateAgentsAsync(
        WorkflowConfiguration config, CancellationToken cancellationToken)
    {
        var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);

        foreach (var agentConfig in config.Agents)
        {
            var hostedTools = hostedFactory.Create(agentConfig.Tools);
            var mcpTools = await mcpPool.GetToolsAsync(agentConfig.McpServers, cancellationToken).ConfigureAwait(false);
            var pluginTools = ResolvePluginTools(agentConfig);

            var allTools = hostedTools.Concat(mcpTools).Concat(pluginTools).ToArray();

            logger.LogInformation(
                "Agent {Agent} resolved tools: hosted={H}, mcp={M}, plugins={P}",
                agentConfig.Name, hostedTools.Count, mcpTools.Count, pluginTools.Count);

            agents[agentConfig.Name] = agentFactory.BuildAgent(agentConfig, allTools);
        }
        return agents;
    }

    /// <summary>
    /// Runs the workflow, streams its events into the activity logger and throws
    /// if any executor/workflow error events were observed.
    /// </summary>
    public async Task RunWorkflowAsync(Workflow workflow, string task)
    {
        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, task)
            .ConfigureAwait(false);

        var errors = new List<Exception>();
        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            var error = HandleWorkflowEvent(evt);
            if (error is not null)
            {
                errors.Add(error);
            }
        }
        ThrowIfErrors(errors);
    }

    private IReadOnlyList<AITool> ResolvePluginTools(AgentConfiguration agentConfig)
    {
        if (agentConfig.Plugins.Count == 0)
        {
            return Array.Empty<AITool>();
        }
        var tools = new List<AITool>();
        foreach (var name in agentConfig.Plugins)
        {
            if (!pluginRegistry.TryGet(name, out var plugin))
            {
                // Defensive: ValidatePluginReferences runs up-front; this guard handles direct invocation paths.
                throw new WorkflowValidationException(
                    $"Agent '{agentConfig.Name}' references unknown plugin '{name}'");
            }
            tools.AddRange(plugin!.AsAITools());
        }
        return tools;
    }

    private Exception? HandleWorkflowEvent(WorkflowEvent evt)
    {
        switch (evt)
        {
            case AgentResponseUpdateEvent a:
                activity.OnChunk(
                    a.Update?.AuthorName ?? a.ExecutorId ?? "?",
                    a.Update?.Text ?? string.Empty);
                return null;

            case AgentResponseEvent r:
                var agentName = r.ExecutorId ?? "?";
                activity.OnTurnCompleted(agentName, r.Response?.Text);
                if (r.Response?.Messages is { } msgs)
                {
                    foreach (var m in msgs)
                    {
                        foreach (var c in m.Contents.OfType<FunctionCallContent>())
                        {
                            activity.OnToolCall(
                                agentName,
                                c.Name,
                                c.Arguments is null ? null : System.Text.Json.JsonSerializer.Serialize(c.Arguments));
                        }
                    }
                }
                return null;

            case ExecutorFailedEvent ef:
                var execEx = ef.Data as Exception
                    ?? new InvalidOperationException($"Executor '{ef.ExecutorId}' failed");
                activity.OnExecutorFailed(ef.ExecutorId ?? "?", execEx);
                return execEx;

            case WorkflowErrorEvent e:
                var workflowEx = e.Exception ?? new InvalidOperationException("Unknown workflow error");
                activity.OnWorkflowError(workflowEx);
                return workflowEx;

            case WorkflowOutputEvent o:
                activity.OnWorkflowOutput(o.Data?.ToString() ?? "(no result)");
                return null;

            default:
                logger.LogDebug("Unhandled workflow event: {Type}", evt.GetType().Name);
                return null;
        }
    }

    private static void ThrowIfErrors(List<Exception> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }
        if (errors.Count == 1)
        {
            throw new InvalidOperationException("Workflow execution failed", errors[0]);
        }
        throw new AggregateException("Workflow execution failed with multiple errors", errors);
    }
}
