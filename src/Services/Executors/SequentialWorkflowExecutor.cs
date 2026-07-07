using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Executes "sequential" and "conditional" workflows over Microsoft.Agents.AI.Workflows.
/// Both build a static edge graph; conditional edges route dynamically via an
/// <see cref="ISelectionFunction"/> resolved by name (default: keyword matching
/// over the routing agent's output).
/// </summary>
public sealed class SequentialWorkflowExecutor : IWorkflowExecutor
{
    private readonly AgentTeamBuilder teamBuilder;
    private readonly ISelectionFunctionRegistry selectionFunctions;
    private readonly IAgentActivityLogger activity;
    private readonly ILogger<SequentialWorkflowExecutor> logger;

    public SequentialWorkflowExecutor(
        AgentTeamBuilder teamBuilder,
        ISelectionFunctionRegistry selectionFunctions,
        IAgentActivityLogger activity,
        ILogger<SequentialWorkflowExecutor> logger)
    {
        this.teamBuilder = teamBuilder;
        this.selectionFunctions = selectionFunctions;
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

        var conditionalEdges = config.Orchestration?.ConditionalEdges ?? new List<ConditionalEdgeConfiguration>();
        foreach (var ce in conditionalEdges)
        {
            if (!agents.ContainsKey(ce.From))
            {
                throw new WorkflowValidationException(
                    $"Conditional edge references unknown agent '{ce.From}'");
            }
            if (ce.ToOptions.Count == 0)
            {
                throw new WorkflowValidationException(
                    $"Conditional edge from '{ce.From}' requires non-empty toOptions");
            }
            foreach (var option in ce.ToOptions)
            {
                if (!agents.ContainsKey(option))
                {
                    throw new WorkflowValidationException(
                        $"Conditional edge from '{ce.From}' references unknown agent '{option}'");
                }
            }
        }

        // SDK 1.3.0: WorkflowBuilder ctor takes start ExecutorBinding;
        // AIAgent → ExecutorBinding via implicit conversion.
        var builder = new WorkflowBuilder(agents[startName]);
        foreach (var edge in edges)
        {
            builder.AddEdge(agents[edge.From], agents[edge.To]);
        }

        // Conditional routing: one predicated edge per target. Conditions are evaluated
        // per flowing message, so the decision made on the routing agent's textual
        // output is cached per edge and reused for text-less batches (TurnToken etc.).
        foreach (var ce in conditionalEdges)
        {
            var selector = selectionFunctions.Resolve(ce.SelectionFunction);
            var route = new ConditionalRoute(selector, ce, activity);
            foreach (var target in ce.ToOptions)
            {
                var targetName = target;
                builder.AddEdge<List<ChatMessage>>(
                    agents[ce.From], agents[targetName],
                    messages => route.IsSelected(targetName, messages));
            }
        }

        // Robust terminal detection: collect all leaves (nodes with no outgoing edge).
        // The graph (static + conditional) must converge to exactly one leaf.
        var fromSet = edges.Select(e => e.From)
            .Concat(conditionalEdges.Select(ce => ce.From))
            .ToHashSet(StringComparer.Ordinal);
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

    /// <summary>
    /// Per-edge routing state shared by all target predicates of one conditional edge.
    /// Text-bearing messages update the decision; text-less messages follow the last
    /// decision (or the first option when nothing has been decided yet).
    /// ponytail: streaming per-message routing — context forwarded before the routing
    /// agent answers follows the then-current decision; buffer via a custom executor
    /// if strict turn-based routing is ever required.
    /// </summary>
    private sealed class ConditionalRoute
    {
        private readonly ISelectionFunction selector;
        private readonly ConditionalEdgeConfiguration edge;
        private readonly IAgentActivityLogger activity;
        private readonly object gate = new();
        private string? currentTarget;

        public ConditionalRoute(
            ISelectionFunction selector, ConditionalEdgeConfiguration edge, IAgentActivityLogger activity)
        {
            this.selector = selector;
            this.edge = edge;
            this.activity = activity;
        }

        public bool IsSelected(string target, List<ChatMessage>? messages)
        {
            lock (gate)
            {
                var text = messages?.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Text))?.Text;
                if (text is not null)
                {
                    var chosen = selector.SelectTarget(edge, text);
                    if (chosen is not null && !string.Equals(chosen, currentTarget, StringComparison.Ordinal))
                    {
                        currentTarget = chosen;
                        activity.OnManagerDecision("ROUTER", $"{edge.From} → {chosen} (function: {selector.Name})");
                    }
                }
                var effective = currentTarget ?? edge.ToOptions[0];
                return string.Equals(effective, target, StringComparison.Ordinal);
            }
        }
    }
}
