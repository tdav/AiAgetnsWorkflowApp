using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Executes "sequential" and "conditional" workflows over Microsoft.Agents.AI.Workflows.
/// Both build a static edge graph; conditional additionally carries conditionalEdges,
/// whose selection-function support is deferred (a warning is emitted and the static
/// part of the graph runs as-is).
/// </summary>
public sealed class SequentialWorkflowExecutor : IWorkflowExecutor
{
    private readonly AgentTeamBuilder teamBuilder;
    private readonly IAgentActivityLogger activity;
    private readonly ILogger<SequentialWorkflowExecutor> logger;

    public SequentialWorkflowExecutor(
        AgentTeamBuilder teamBuilder,
        IAgentActivityLogger activity,
        ILogger<SequentialWorkflowExecutor> logger)
    {
        this.teamBuilder = teamBuilder;
        this.activity = activity;
        this.logger = logger;
    }

    public string Name => "sequential";

    public bool CanExecute(string workflowType) =>
        string.Equals(workflowType, "sequential", StringComparison.OrdinalIgnoreCase)
        || string.Equals(workflowType, "conditional", StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);
        var agents = await teamBuilder.CreateAgentsAsync(config, cancellationToken).ConfigureAwait(false);

        var startName = config.Orchestration?.StartAgent
            ?? throw new WorkflowValidationException(
                $"{config.WorkflowType} workflow requires Orchestration.StartAgent");
        var edges = config.Orchestration?.Edges ?? new List<EdgeConfiguration>();

        // Pre-validate edge references: every From/To must map to a known agent.
        foreach (var edge in edges)
        {
            if (!agents.ContainsKey(edge.From))
            {
                throw new WorkflowValidationException(
                    $"Edge references unknown agent '{edge.From}'");
            }
            if (!agents.ContainsKey(edge.To))
            {
                throw new WorkflowValidationException(
                    $"Edge references unknown agent '{edge.To}'");
            }
        }

        // Selection-function support is deferred (см. Future work спеки).
        // Если в JSON присутствуют conditionalEdges — выводим warning и идём только
        // по статическим edges.
        if (config.Orchestration?.ConditionalEdges?.Count > 0)
        {
            logger.LogWarning(
                "Conditional edges present but selection-function support is deferred — статическая часть workflow выполняется как есть.");
        }

        // SDK 1.3.0: WorkflowBuilder ctor takes start ExecutorBinding;
        // AIAgent → ExecutorBinding via implicit conversion.
        var builder = new WorkflowBuilder(agents[startName]);
        foreach (var edge in edges)
        {
            builder.AddEdge(agents[edge.From], agents[edge.To]);
        }

        // Robust terminal detection: collect all leaves (nodes with no outgoing edge).
        // The static graph expects a single chain — exactly one leaf.
        var fromSet = edges.Select(e => e.From).ToHashSet(StringComparer.Ordinal);
        var leaves = config.Agents
            .Select(a => a.Name)
            .Where(n => !fromSet.Contains(n))
            .ToList();

        string terminalName;
        if (leaves.Count == 1)
        {
            terminalName = leaves[0];
        }
        else if (leaves.Count == 0)
        {
            terminalName = startName;   // single-node graph (no edges)
        }
        else
        {
            throw new WorkflowValidationException(
                $"{config.WorkflowType} workflow requires single terminal agent; found {leaves.Count}: {string.Join(", ", leaves)}");
        }

        builder.WithOutputFrom(agents[terminalName]);

        var workflow = builder.Build();
        await teamBuilder.RunWorkflowAsync(workflow, config.Task).ConfigureAwait(false);
    }
}
