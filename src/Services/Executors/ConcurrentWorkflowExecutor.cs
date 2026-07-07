using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Executes "concurrent" workflows: fan-out of the same task to all participants,
/// default fan-in aggregation from Microsoft.Agents.AI.Workflows.
/// </summary>
public sealed class ConcurrentWorkflowExecutor : IWorkflowExecutor
{
    private readonly AgentTeamBuilder teamBuilder;
    private readonly IAgentActivityLogger activity;
    private readonly ILogger<ConcurrentWorkflowExecutor> logger;

    public ConcurrentWorkflowExecutor(
        AgentTeamBuilder teamBuilder,
        IAgentActivityLogger activity,
        ILogger<ConcurrentWorkflowExecutor> logger)
    {
        this.teamBuilder = teamBuilder;
        this.activity = activity;
        this.logger = logger;
    }

    public string Name => "concurrent";

    public bool CanExecute(string workflowType) =>
        string.Equals(workflowType, Name, StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Concurrent);
        var participantNames = config.Orchestration?.Concurrent?.ParticipantAgents;
        if (participantNames is null || participantNames.Count == 0)
        {
            throw new WorkflowValidationException(
                "Concurrent workflow requires Orchestration.Concurrent.ParticipantAgents (non-empty list)");
        }

        var agents = await teamBuilder.CreateAgentsAsync(config, cancellationToken).ConfigureAwait(false);

        // Pre-validate participant references: every name must map to a known agent.
        foreach (var name in participantNames)
        {
            if (!agents.ContainsKey(name))
            {
                throw new WorkflowValidationException(
                    $"Concurrent participant references unknown agent '{name}'");
            }
        }

        var participants = participantNames.Select(n => agents[n]).ToArray();

        var strategy = config.Orchestration?.Concurrent?.AggregationStrategy ?? "Collect";
        logger.LogInformation(
            "Concurrent workflow: {Count} participants, aggregation strategy '{Strategy}' (default aggregator)",
            participants.Length, strategy);

        // SDK 1.3.0: AgentWorkflowBuilder.BuildConcurrent fans out the same input to each
        // participant in parallel and aggregates per-agent last messages by default.
        var workflow = AgentWorkflowBuilder.BuildConcurrent(participants);
        await teamBuilder.RunWorkflowAsync(workflow, config.Task).ConfigureAwait(false);
    }
}
