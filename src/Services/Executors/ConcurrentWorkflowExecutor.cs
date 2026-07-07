using System.Text;
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Executes "concurrent" workflows: fan-out of the same task to all participants,
/// fan-in per the configured aggregation strategy — "Collect" (SDK default: all
/// last messages), "Merge" (single message joining every participant's answer)
/// or "Vote" (the most common answer wins).
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
        var aggregator = ResolveAggregator(strategy);
        logger.LogInformation(
            "Concurrent workflow: {Count} participants, aggregation strategy '{Strategy}'",
            participants.Length, strategy);

        // AgentWorkflowBuilder.BuildConcurrent fans out the same input to each participant
        // in parallel; a null aggregator keeps the SDK default (collect last messages).
        var workflow = aggregator is null
            ? AgentWorkflowBuilder.BuildConcurrent(participants)
            : AgentWorkflowBuilder.BuildConcurrent(participants, aggregator);
        await teamBuilder.RunWorkflowAsync(workflow, config.Task).ConfigureAwait(false);
    }

    internal static Func<IList<List<ChatMessage>>, List<ChatMessage>>? ResolveAggregator(string strategy)
        => strategy.ToLowerInvariant() switch
        {
            "collect" => null,
            "merge" => MergeAggregator,
            "vote" => VoteAggregator,
            _ => throw new WorkflowValidationException(
                $"Unknown aggregation strategy '{strategy}' (supported: Collect, Merge, Vote)"),
        };

    /// <summary>Joins every participant's last answer into a single labeled message.</summary>
    internal static List<ChatMessage> MergeAggregator(IList<List<ChatMessage>> results)
    {
        var sb = new StringBuilder();
        foreach (var last in LastAnswers(results))
        {
            sb.Append(last.AuthorName ?? "agent").Append(": ").AppendLine(last.Text).AppendLine();
        }
        return new List<ChatMessage> { new(ChatRole.Assistant, sb.ToString().TrimEnd()) };
    }

    /// <summary>Returns the most common answer (normalized text comparison); ties keep participant order.</summary>
    internal static List<ChatMessage> VoteAggregator(IList<List<ChatMessage>> results)
    {
        var answers = LastAnswers(results);
        if (answers.Count == 0)
        {
            return new List<ChatMessage>();
        }
        var winner = answers
            .GroupBy(m => m.Text.Trim().ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .First()
            .First();
        return new List<ChatMessage> { winner };
    }

    private static List<ChatMessage> LastAnswers(IList<List<ChatMessage>> results)
        => results
            .Select(messages => messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text)))
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();
}
