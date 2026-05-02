using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Orchestrator for Magentic workflow execution
/// </summary>
public class MagenticWorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly ILogger<MagenticWorkflowOrchestrator> _logger;
    private readonly IWorkflowJsonLoader _jsonLoader;
    private readonly IWorkflowVisualizer _visualizer;
    private readonly IConfiguration _configuration;
    private readonly IMcpClientPool _mcpPool;
    private readonly IHostedToolFactory _hostedFactory;
    private readonly IAgentPluginRegistry _pluginRegistry;

    public MagenticWorkflowOrchestrator(
        ILogger<MagenticWorkflowOrchestrator> logger,
        IWorkflowJsonLoader jsonLoader,
        IWorkflowVisualizer visualizer,
        IConfiguration configuration,
        IMcpClientPool mcpPool,
        IHostedToolFactory hostedFactory,
        IAgentPluginRegistry pluginRegistry)
    {
        _logger = logger;
        _jsonLoader = jsonLoader;
        _visualizer = visualizer;
        _configuration = configuration;
        _mcpPool = mcpPool;
        _hostedFactory = hostedFactory;
        _pluginRegistry = pluginRegistry;
    }

    public async Task ExecuteWorkflowFromJsonAsync(string jsonFilePath)
    {
        // Load configuration from JSON
        var config = await _jsonLoader.LoadConfigurationAsync(jsonFilePath);

        // Visualize workflow before execution
        _visualizer.VisualizeWorkflow(config);

        // Validate plugin references and register MCP servers
        ValidatePluginReferences(config);
        await _mcpPool.RegisterServersAsync(config.McpServers).ConfigureAwait(false);

        // Get API keys from configuration
        var openAiApiKey = _configuration["OpenAI:ApiKey"];
        var azureOpenAiEndpoint = _configuration["AzureOpenAI:Endpoint"];

        if (string.IsNullOrWhiteSpace(openAiApiKey) && string.IsNullOrWhiteSpace(azureOpenAiEndpoint))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  Warning: No OpenAI or Azure OpenAI configuration found!");
            Console.WriteLine("   This is a DEMO mode - simulating workflow execution.");
            Console.ResetColor();
            await SimulateWorkflowExecutionAsync(config);
            return;
        }

        // Execute actual workflow based on type
        await ExecuteActualWorkflowAsync(config, openAiApiKey, azureOpenAiEndpoint);
    }

    private async Task ExecuteActualWorkflowAsync(
        WorkflowConfiguration config, 
        string? openAiApiKey, 
        string? azureEndpoint)
    {
        Console.WriteLine("\n" + new string('─', 80));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"▶️  STARTING {config.WorkflowType.ToUpper()} WORKFLOW EXECUTION");
        Console.ResetColor();
        Console.WriteLine(new string('─', 80) + "\n");

        /*
         * ACTUAL IMPLEMENTATION WITH MICROSOFT AGENT FRAMEWORK
         * 
         * When you install the required NuGet packages, uncomment and use this code:
         */

        switch (config.WorkflowType.ToLower())
        {
            case "sequential":
                await ExecuteSequentialWorkflowAsync(config, openAiApiKey, azureEndpoint);
                break;
            case "concurrent":
                await ExecuteConcurrentWorkflowAsync(config, openAiApiKey, azureEndpoint);
                break;
            case "conditional":
                await ExecuteConditionalWorkflowAsync(config, openAiApiKey, azureEndpoint);
                break;
            case "magentic":
                await ExecuteMagenticWorkflowAsync(config, openAiApiKey, azureEndpoint);
                break;
            default:
                throw new NotSupportedException($"Workflow type '{config.WorkflowType}' is not supported");
        }
    }

    private async Task ExecuteSequentialWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint)
    {
        /*
         * SEQUENTIAL WORKFLOW IMPLEMENTATION
         * 
         * var agents = CreateAgentsFromConfiguration(config, openAiApiKey);
         * 
         * var builder = new WorkflowBuilder()
         *     .SetStartExecutor(agents[config.Orchestration.StartAgent]);
         * 
         * foreach (var edge in config.Orchestration.Edges)
         * {
         *     builder.AddEdge(agents[edge.From], agents[edge.To]);
         * }
         * 
         * var workflow = builder.Build();
         * 
         * await foreach (var evt in workflow.RunStream(config.Task))
         * {
         *     HandleWorkflowEvent(evt);
         * }
         */

        Console.WriteLine("📝 Sequential workflow execution would occur here.");
        Console.WriteLine("   Install Microsoft.Agents.AI.Workflows package.\n");
        await SimulateWorkflowExecutionAsync(config);
    }

    private async Task ExecuteConcurrentWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint)
    {
        /*
         * CONCURRENT WORKFLOW IMPLEMENTATION
         * 
         * var agents = CreateAgentsFromConfiguration(config, openAiApiKey);
         * var participantAgents = config.Orchestration.Concurrent.ParticipantAgents
         *     .Select(name => agents[name])
         *     .ToArray();
         * 
         * var workflow = new ConcurrentBuilder()
         *     .Participants(participantAgents)
         *     .Build();
         * 
         * await foreach (var evt in workflow.RunStream(config.Task))
         * {
         *     HandleWorkflowEvent(evt);
         * }
         */

        Console.WriteLine("📝 Concurrent workflow execution would occur here.");
        Console.WriteLine("   Install Microsoft.Agents.AI.Workflows package.\n");
        await SimulateWorkflowExecutionAsync(config);
    }

    private async Task ExecuteConditionalWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint)
    {
        /*
         * CONDITIONAL WORKFLOW IMPLEMENTATION
         * 
         * var agents = CreateAgentsFromConfiguration(config, openAiApiKey);
         * 
         * var builder = new WorkflowBuilder()
         *     .SetStartExecutor(agents[config.Orchestration.StartAgent]);
         * 
         * // Add regular edges
         * foreach (var edge in config.Orchestration.Edges)
         * {
         *     builder.AddEdge(agents[edge.From], agents[edge.To]);
         * }
         * 
         * // Add conditional edges
         * foreach (var condEdge in config.Orchestration.ConditionalEdges)
         * {
         *     var targetAgents = condEdge.ToOptions.Select(name => agents[name]).ToArray();
         *     Func<WorkflowContext, Task<string[]>> selectionFunc = CreateSelectionFunction(condEdge);
         *     
         *     builder.AddMultiSelectionEdgeGroup(
         *         agents[condEdge.From],
         *         targetAgents,
         *         selectionFunc
         *     );
         * }
         * 
         * var workflow = builder.Build();
         * 
         * await foreach (var evt in workflow.RunStream(config.Task))
         * {
         *     HandleWorkflowEvent(evt);
         * }
         */

        Console.WriteLine("📝 Conditional workflow execution would occur here.");
        Console.WriteLine("   Install Microsoft.Agents.AI.Workflows package.\n");
        await SimulateWorkflowExecutionAsync(config);
    }

    private async Task ExecuteMagenticWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint)
    {
        /*
         * MAGENTIC WORKFLOW IMPLEMENTATION
         * 
         * // 1. Create specialized agents from configuration
         * var agents = new Dictionary<string, ChatAgent>();
         * 
         * foreach (var agentConfig in config.Agents)
         * {
         *     var chatClient = new OpenAIChatClient(
         *         aiModelId: agentConfig.ModelId,
         *         apiKey: openAiApiKey
         *     );
         *     
         *     var tools = new List<ITool>();
         *     foreach (var toolName in agentConfig.Tools)
         *     {
         *         if (toolName == "CodeInterpreter")
         *         {
         *             tools.Add(new HostedCodeInterpreterTool());
         *         }
         *     }
         *     
         *     var agent = new ChatAgent(
         *         name: agentConfig.Name,
         *         description: agentConfig.Description,
         *         instructions: agentConfig.Instructions,
         *         chatClient: chatClient,
         *         tools: tools.ToArray()
         *     );
         *     
         *     agents.Add(agentConfig.Name, agent);
         * }
         * 
         * // 2. Setup event callbacks
         * async Task OnEvent(MagenticCallbackEvent evt)
         * {
         *     HandleWorkflowEvent(evt);
         * }
         * 
         * // 3. Build workflow using MagenticBuilder
         * var managerClient = new OpenAIChatClient(
         *     aiModelId: config.Manager.ModelId,
         *     apiKey: openAiApiKey
         * );
         * 
         * var builder = new MagenticBuilder()
         *     .Participants(agents)
         *     .OnEvent(OnEvent, MagenticCallbackMode.STREAMING)
         *     .WithStandardManager(
         *         chatClient: managerClient,
         *         maxRoundCount: config.Manager.MaxRoundCount,
         *         maxStallCount: config.Manager.MaxStallCount,
         *         maxResetCount: config.Manager.MaxResetCount
         *     );
         * 
         * if (config.Manager.EnablePlanReview)
         * {
         *     builder.WithPlanReview();
         * }
         * 
         * var workflow = builder.Build();
         * 
         * // 4. Execute workflow
         * await foreach (var evt in workflow.RunStream(config.Task))
         * {
         *     if (evt is WorkflowCompletedEvent completedEvt)
         *     {
         *         Console.WriteLine("\n✅ Workflow completed successfully!");
         *     }
         * }
         */

        Console.WriteLine("📝 Magentic workflow execution would occur here.");
        Console.WriteLine("   Install Microsoft.Agents.AI.Workflows package.\n");
        await SimulateWorkflowExecutionAsync(config);
    }

    private async Task SimulateWorkflowExecutionAsync(WorkflowConfiguration config)
    {
        Console.WriteLine($"🎭 DEMO MODE: Simulating {config.WorkflowType} workflow execution...\n");

        switch (config.WorkflowType.ToLower())
        {
            case "sequential":
                await SimulateSequentialWorkflowAsync(config);
                break;
            case "concurrent":
                await SimulateConcurrentWorkflowAsync(config);
                break;
            case "conditional":
                await SimulateConditionalWorkflowAsync(config);
                break;
            case "magentic":
                await SimulateMagenticWorkflowAsync(config);
                break;
        }
    }

    private async Task SimulateSequentialWorkflowAsync(WorkflowConfiguration config)
    {
        LogEvent("WORKFLOW", $"Starting Sequential execution with {config.Agents.Count} agents", ConsoleColor.Cyan);
        
        if (config.Orchestration?.StartAgent != null)
        {
            LogEvent("WORKFLOW", $"Start agent: {config.Orchestration.StartAgent}", ConsoleColor.Cyan);
        }

        await Task.Delay(300);

        // Process agents sequentially following edges
        var processedAgents = new HashSet<string>();
        var currentAgent = config.Orchestration?.StartAgent ?? config.Agents.First().Name;

        while (currentAgent != null && !processedAgents.Contains(currentAgent))
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == currentAgent);
            if (agent == null) break;

            processedAgents.Add(currentAgent);

            await Task.Delay(400);
            LogEvent($"AGENT:{agent.Name}", $"Processing using {agent.ModelId}...", ConsoleColor.Yellow);
            await Task.Delay(600);
            LogEvent($"AGENT:{agent.Name}", $"✓ Completed task: {agent.Description}", ConsoleColor.Green);

            // Find next agent
            var edge = config.Orchestration?.Edges.FirstOrDefault(e => e.From == currentAgent);
            currentAgent = edge?.To;

            if (currentAgent != null)
            {
                await Task.Delay(200);
                LogEvent("WORKFLOW", $"→ Passing result to {currentAgent}", ConsoleColor.Cyan);
            }
        }

        await Task.Delay(300);
        ShowFinalResult("Sequential pipeline completed successfully!");
    }

    private async Task SimulateConcurrentWorkflowAsync(WorkflowConfiguration config)
    {
        LogEvent("WORKFLOW", $"Starting Concurrent execution with {config.Agents.Count} agents", ConsoleColor.Cyan);
        
        var participants = config.Orchestration?.Concurrent?.ParticipantAgents ?? 
                          config.Agents.Select(a => a.Name).ToList();
        
        LogEvent("WORKFLOW", $"Participants: {string.Join(", ", participants)}", ConsoleColor.Cyan);
        await Task.Delay(300);

        // Simulate fan-out
        LogEvent("WORKFLOW", "⚡ Fan-out: Distributing task to all agents simultaneously", ConsoleColor.Magenta);
        await Task.Delay(400);

        // Simulate parallel processing
        var tasks = new List<Task>();
        foreach (var agentName in participants)
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == agentName);
            if (agent != null)
            {
                tasks.Add(SimulateAgentWorkAsync(agent));
            }
        }

        await Task.WhenAll(tasks);

        // Simulate fan-in
        await Task.Delay(300);
        var strategy = config.Orchestration?.Concurrent?.AggregationStrategy ?? "Collect";
        LogEvent("WORKFLOW", $"⚡ Fan-in: Aggregating results using '{strategy}' strategy", ConsoleColor.Magenta);
        await Task.Delay(400);

        ShowFinalResult($"Concurrent execution completed! All {participants.Count} agents finished.");
    }

    private async Task SimulateConditionalWorkflowAsync(WorkflowConfiguration config)
    {
        LogEvent("WORKFLOW", "Starting Conditional execution with dynamic routing", ConsoleColor.Cyan);
        
        if (config.Orchestration?.StartAgent != null)
        {
            LogEvent("WORKFLOW", $"Start agent: {config.Orchestration.StartAgent}", ConsoleColor.Cyan);
        }

        await Task.Delay(300);

        var processedAgents = new HashSet<string>();
        var currentAgent = config.Orchestration?.StartAgent ?? config.Agents.First().Name;

        while (currentAgent != null && !processedAgents.Contains(currentAgent))
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == currentAgent);
            if (agent == null) break;

            processedAgents.Add(currentAgent);

            await Task.Delay(400);
            LogEvent($"AGENT:{agent.Name}", $"Processing using {agent.ModelId}...", ConsoleColor.Yellow);
            await Task.Delay(600);
            LogEvent($"AGENT:{agent.Name}", $"✓ Completed: {agent.Description}", ConsoleColor.Green);

            // Check for conditional edges
            var conditionalEdge = config.Orchestration?.ConditionalEdges
                .FirstOrDefault(ce => ce.From == currentAgent);

            if (conditionalEdge != null)
            {
                await Task.Delay(300);
                LogEvent("DECISION", $"Evaluating condition: {conditionalEdge.SelectionFunction}", ConsoleColor.Magenta);
                
                // Simulate condition evaluation
                var selectedTargets = conditionalEdge.ToOptions.Take(1).ToList(); // Simulate selecting one option
                await Task.Delay(200);
                
                LogEvent("DECISION", $"✓ Selected target(s): {string.Join(", ", selectedTargets)}", ConsoleColor.Green);
                currentAgent = selectedTargets.FirstOrDefault();
            }
            else
            {
                // Regular edge
                var edge = config.Orchestration?.Edges.FirstOrDefault(e => e.From == currentAgent);
                currentAgent = edge?.To;

                if (currentAgent != null)
                {
                    await Task.Delay(200);
                    LogEvent("WORKFLOW", $"→ Moving to {currentAgent}", ConsoleColor.Cyan);
                }
            }
        }

        await Task.Delay(300);
        ShowFinalResult("Conditional workflow completed with dynamic routing!");
    }

    private async Task SimulateMagenticWorkflowAsync(WorkflowConfiguration config)
    {
        // Simulate orchestrator initialization
        await Task.Delay(500);
        LogEvent("ORCHESTRATOR", "Initializing Magentic Manager...", ConsoleColor.Cyan);
        await Task.Delay(300);
        LogEvent("ORCHESTRATOR", $"Creating execution plan for task: {config.Task.Substring(0, Math.Min(80, config.Task.Length))}...", ConsoleColor.Cyan);

        // Simulate agent coordination
        for (int round = 1; round <= 3; round++)
        {
            Console.WriteLine($"\n--- Round {round} ---");
            
            foreach (var agent in config.Agents)
            {
                await Task.Delay(400);
                LogEvent($"AGENT:{agent.Name}", $"Executing task using {agent.ModelId}...", ConsoleColor.Yellow);
                await Task.Delay(600);
                LogEvent($"AGENT:{agent.Name}", $"[{agent.Description}] Completed subtask.", ConsoleColor.Yellow);
            }

            await Task.Delay(300);
            LogEvent("ORCHESTRATOR", $"Reviewing progress from round {round}...", ConsoleColor.Cyan);
        }

        // Simulate final result
        await Task.Delay(500);
        ShowFinalResult($"Magentic orchestration completed! All {config.Agents.Count} agents collaborated successfully.");
    }

    private async Task SimulateAgentWorkAsync(AgentConfiguration agent)
    {
        await Task.Delay(500);
        LogEvent($"AGENT:{agent.Name}", $"[Concurrent] Processing using {agent.ModelId}...", ConsoleColor.Yellow);
        await Task.Delay(Random.Shared.Next(800, 1500)); // Simulate variable processing time
        LogEvent($"AGENT:{agent.Name}", $"[Concurrent] ✓ Completed: {agent.Description}", ConsoleColor.Green);
    }

    private void ShowFinalResult(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("FINAL RESULT:");
        Console.WriteLine(new string('=', 60));
        Console.ResetColor();
        Console.WriteLine($"✅ {message}");
        Console.WriteLine(new string('=', 60));
    }

    private void LogEvent(string source, string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write($"[{source}] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    /// <summary>
    /// Унифицированная обработка событий workflow из Microsoft.Agents.AI.Workflows 1.3.0.
    /// Будет вызываться из Execute*WorkflowAsync методов в задачах 14-17.
    /// </summary>
    private void HandleWorkflowEvent(WorkflowEvent evt)
    {
        switch (evt)
        {
            case AgentResponseUpdateEvent a:
                LogEvent(
                    $"AGENT:{a.Update?.AuthorName ?? a.ExecutorId ?? "?"}",
                    a.Update?.Text ?? string.Empty,
                    ConsoleColor.Yellow);
                break;
            case AgentResponseEvent r:
                LogEvent(
                    $"AGENT:{r.ExecutorId ?? "?"}",
                    r.Response?.Text ?? "(empty response)",
                    ConsoleColor.Green);
                break;
            case ExecutorFailedEvent ef:
                LogEvent(
                    $"EXECUTOR:{ef.ExecutorId ?? "?"}",
                    (ef.Data as Exception)?.Message ?? "executor failed",
                    ConsoleColor.Red);
                break;
            case WorkflowErrorEvent e:
                LogEvent("ERROR", e.Exception?.Message ?? "unknown", ConsoleColor.Red);
                break;
            case WorkflowOutputEvent o:
                ShowFinalResult(o.Data?.ToString() ?? "(no result)");
                break;
            default:
                LogEvent("WORKFLOW", evt.GetType().Name, ConsoleColor.Cyan);
                break;
        }
    }

    private async Task<Dictionary<string, AIAgent>> CreateAgentsFromConfigurationAsync(
        WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint, CancellationToken ct)
    {
        var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);

        foreach (var agentConfig in config.Agents)
        {
            var hostedTools = _hostedFactory.Create(agentConfig.Tools);
            var mcpTools = await _mcpPool.GetToolsAsync(agentConfig.McpServers, ct).ConfigureAwait(false);
            var pluginTools = ResolvePluginTools(agentConfig);

            var allTools = hostedTools.Concat(mcpTools).Concat(pluginTools).ToArray();

            _logger.LogInformation(
                "Agent {Agent} resolved tools: hosted={H}, mcp={M}, plugins={P}",
                agentConfig.Name, hostedTools.Count, mcpTools.Count, pluginTools.Count);

            var chatClient = BuildChatClient(agentConfig.ModelId, openAiApiKey, azureEndpoint);
            agents[agentConfig.Name] = chatClient.AsAIAgent(
                instructions: agentConfig.Instructions,
                name: agentConfig.Name,
                description: agentConfig.Description,
                tools: allTools);
        }
        return agents;
    }

    private IReadOnlyList<AITool> ResolvePluginTools(AgentConfiguration agentConfig)
    {
        if (agentConfig.Plugins.Count == 0) return Array.Empty<AITool>();
        var tools = new List<AITool>();
        foreach (var name in agentConfig.Plugins)
        {
            if (!_pluginRegistry.TryGet(name, out var plugin))
                // Defensive: ValidatePluginReferences runs at start of ExecuteWorkflowFromJsonAsync; this guard handles direct invocation paths.
                throw new WorkflowValidationException(
                    $"Agent '{agentConfig.Name}' references unknown plugin '{name}'");
            tools.AddRange(plugin!.AsAITools());
        }
        return tools;
    }

    private static IChatClient BuildChatClient(string modelId, string? openAiApiKey, string? azureEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(azureEndpoint))
        {
            // Azure.AI.OpenAI пакет ещё не подключён к проекту — вернёмся к этому в Task 13/14.
            throw new NotSupportedException(
                "Azure OpenAI endpoint is configured, but Azure.AI.OpenAI package is not yet referenced. " +
                "Will be enabled in upcoming tasks.");
        }
        var openAi = new OpenAIClient(openAiApiKey!);
        return openAi.GetChatClient(modelId).AsIChatClient();
    }

    private void ValidatePluginReferences(WorkflowConfiguration config)
    {
        foreach (var agent in config.Agents)
            foreach (var name in agent.Plugins)
                if (!_pluginRegistry.TryGet(name, out _))
                    throw new WorkflowValidationException(
                        $"Agent '{agent.Name}' references unknown plugin '{name}'");
    }
}
