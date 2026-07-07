using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// DEMO-mode executor: simulates workflow execution when no LLM credentials are
/// configured. Selected directly by the orchestrator facade, not via type matching.
/// </summary>
public sealed class SimulatedWorkflowExecutor
{
    private readonly IAgentActivityLogger activity;

    public SimulatedWorkflowExecutor(IAgentActivityLogger activity)
    {
        this.activity = activity;
    }

    public async Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🎭 DEMO MODE: Simulating {config.WorkflowType} workflow execution...\n");

        switch (config.WorkflowType.ToLower())
        {
            case "sequential":
                await SimulateSequentialWorkflowAsync(config).ConfigureAwait(false);
                break;
            case "concurrent":
                await SimulateConcurrentWorkflowAsync(config).ConfigureAwait(false);
                break;
            case "conditional":
                await SimulateConditionalWorkflowAsync(config).ConfigureAwait(false);
                break;
            case "magentic":
                await SimulateMagenticWorkflowAsync(config).ConfigureAwait(false);
                break;
        }
    }

    private async Task SimulateSequentialWorkflowAsync(WorkflowConfiguration config)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        LogEvent("WORKFLOW", $"Starting Sequential execution with {config.Agents.Count} agents", ConsoleColor.Cyan);
        if (config.Orchestration?.StartAgent != null)
        {
            LogEvent("WORKFLOW", $"Start agent: {config.Orchestration.StartAgent}", ConsoleColor.Cyan);
        }

        await Task.Delay(300).ConfigureAwait(false);

        var processedAgents = new HashSet<string>();
        var currentAgent = config.Orchestration?.StartAgent ?? config.Agents.First().Name;

        while (currentAgent != null && !processedAgents.Contains(currentAgent))
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == currentAgent);
            if (agent == null) break;
            processedAgents.Add(currentAgent);

            await Task.Delay(400).ConfigureAwait(false);
            activity.OnChunk(agent.Name, $"Processing using {agent.ModelId}...");
            await Task.Delay(600).ConfigureAwait(false);
            activity.OnTurnCompleted(agent.Name, $"✓ Completed task: {agent.Description}");

            var edge = config.Orchestration?.Edges.FirstOrDefault(e => e.From == currentAgent);
            currentAgent = edge?.To;
            if (currentAgent != null)
            {
                await Task.Delay(200).ConfigureAwait(false);
                LogEvent("WORKFLOW", $"→ Passing result to {currentAgent}", ConsoleColor.Cyan);
            }
        }

        await Task.Delay(300).ConfigureAwait(false);
        activity.OnWorkflowOutput("Sequential pipeline completed successfully!");
    }

    private async Task SimulateConcurrentWorkflowAsync(WorkflowConfiguration config)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Concurrent);

        LogEvent("WORKFLOW", $"Starting Concurrent execution with {config.Agents.Count} agents", ConsoleColor.Cyan);

        var participants = config.Orchestration?.Concurrent?.ParticipantAgents
            ?? config.Agents.Select(a => a.Name).ToList();
        LogEvent("WORKFLOW", $"Participants: {string.Join(", ", participants)}", ConsoleColor.Cyan);
        await Task.Delay(300).ConfigureAwait(false);

        LogEvent("WORKFLOW", "⚡ Fan-out: Distributing task to all agents simultaneously", ConsoleColor.Magenta);
        await Task.Delay(400).ConfigureAwait(false);

        var tasks = new List<Task>();
        foreach (var agentName in participants)
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == agentName);
            if (agent != null) tasks.Add(SimulateAgentWorkAsync(agent));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        await Task.Delay(300).ConfigureAwait(false);
        var strategy = config.Orchestration?.Concurrent?.AggregationStrategy ?? "Collect";
        LogEvent("WORKFLOW", $"⚡ Fan-in: Aggregating results using '{strategy}' strategy", ConsoleColor.Magenta);
        await Task.Delay(400).ConfigureAwait(false);

        activity.OnWorkflowOutput($"Concurrent execution completed! All {participants.Count} agents finished.");
    }

    private async Task SimulateConditionalWorkflowAsync(WorkflowConfiguration config)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        LogEvent("WORKFLOW", "Starting Conditional execution with dynamic routing", ConsoleColor.Cyan);
        if (config.Orchestration?.StartAgent != null)
        {
            LogEvent("WORKFLOW", $"Start agent: {config.Orchestration.StartAgent}", ConsoleColor.Cyan);
        }

        await Task.Delay(300).ConfigureAwait(false);

        var processedAgents = new HashSet<string>();
        var currentAgent = config.Orchestration?.StartAgent ?? config.Agents.First().Name;

        while (currentAgent != null && !processedAgents.Contains(currentAgent))
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == currentAgent);
            if (agent == null) break;
            processedAgents.Add(currentAgent);

            await Task.Delay(400).ConfigureAwait(false);
            activity.OnChunk(agent.Name, $"Processing using {agent.ModelId}...");
            await Task.Delay(600).ConfigureAwait(false);
            activity.OnTurnCompleted(agent.Name, $"✓ Completed: {agent.Description}");

            var conditionalEdge = config.Orchestration?.ConditionalEdges
                .FirstOrDefault(ce => ce.From == currentAgent);
            if (conditionalEdge != null)
            {
                await Task.Delay(300).ConfigureAwait(false);
                activity.OnManagerDecision("DECISION", $"Evaluating condition: {conditionalEdge.SelectionFunction}");
                var selectedTargets = conditionalEdge.ToOptions.Take(1).ToList();
                await Task.Delay(200).ConfigureAwait(false);
                activity.OnManagerDecision("DECISION", $"✓ Selected target(s): {string.Join(", ", selectedTargets)}");
                currentAgent = selectedTargets.FirstOrDefault();
            }
            else
            {
                var edge = config.Orchestration?.Edges.FirstOrDefault(e => e.From == currentAgent);
                currentAgent = edge?.To;
                if (currentAgent != null)
                {
                    await Task.Delay(200).ConfigureAwait(false);
                    LogEvent("WORKFLOW", $"→ Moving to {currentAgent}", ConsoleColor.Cyan);
                }
            }
        }

        await Task.Delay(300).ConfigureAwait(false);
        activity.OnWorkflowOutput("Conditional workflow completed with dynamic routing!");
    }

    private async Task SimulateMagenticWorkflowAsync(WorkflowConfiguration config)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        await Task.Delay(500).ConfigureAwait(false);
        LogEvent("ORCHESTRATOR", "Initializing Magentic Manager...", ConsoleColor.Cyan);
        await Task.Delay(300).ConfigureAwait(false);
        LogEvent("ORCHESTRATOR",
            $"Creating execution plan for task: {config.Task.Substring(0, Math.Min(80, config.Task.Length))}...",
            ConsoleColor.Cyan);

        for (int round = 1; round <= 3; round++)
        {
            Console.WriteLine($"\n--- Round {round} ---");
            foreach (var agent in config.Agents)
            {
                await Task.Delay(400).ConfigureAwait(false);
                activity.OnChunk(agent.Name, $"Executing task using {agent.ModelId}...");
                await Task.Delay(600).ConfigureAwait(false);
                activity.OnTurnCompleted(agent.Name, $"[{agent.Description}] Completed subtask.");
            }
            await Task.Delay(300).ConfigureAwait(false);
            LogEvent("ORCHESTRATOR", $"Reviewing progress from round {round}...", ConsoleColor.Cyan);
        }

        await Task.Delay(500).ConfigureAwait(false);
        activity.OnWorkflowOutput($"Magentic orchestration completed! All {config.Agents.Count} agents collaborated successfully.");
    }

    private async Task SimulateAgentWorkAsync(AgentConfiguration agent)
    {
        await Task.Delay(500).ConfigureAwait(false);
        activity.OnChunk(agent.Name, $"[Concurrent] Processing using {agent.ModelId}...");
        await Task.Delay(Random.Shared.Next(800, 1500)).ConfigureAwait(false);
        activity.OnTurnCompleted(agent.Name, $"[Concurrent] ✓ Completed: {agent.Description}");
    }

    private static void LogEvent(string source, string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write($"[{source}] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }
}
