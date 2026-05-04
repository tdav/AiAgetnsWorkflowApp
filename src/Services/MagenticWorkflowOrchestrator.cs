using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using System.ClientModel.Primitives;
using System.Text.Json;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Orchestrator for Magentic workflow execution
/// </summary>
public class MagenticWorkflowOrchestrator : IWorkflowOrchestrator
{
    private const int MagenticTimeoutMinutesDefault = 30;

    private readonly ILogger<MagenticWorkflowOrchestrator> logger;
    private readonly ILoggerFactory loggerFactory;
    private readonly IWorkflowJsonLoader jsonLoader;
    private readonly IWorkflowVisualizer visualizer;
    private readonly IConfiguration configuration;
    private readonly IMcpClientPool mcpPool;
    private readonly IHostedToolFactory hostedFactory;
    private readonly IAgentPluginRegistry pluginRegistry;

    public MagenticWorkflowOrchestrator(
        ILogger<MagenticWorkflowOrchestrator> logger,
        ILoggerFactory loggerFactory,
        IWorkflowJsonLoader jsonLoader,
        IWorkflowVisualizer visualizer,
        IConfiguration configuration,
        IMcpClientPool mcpPool,
        IHostedToolFactory hostedFactory,
        IAgentPluginRegistry pluginRegistry)
    {
        this.logger = logger;
        this.loggerFactory = loggerFactory;
        this.jsonLoader = jsonLoader;
        this.visualizer = visualizer;
        this.configuration = configuration;
        this.mcpPool = mcpPool;
        this.hostedFactory = hostedFactory;
        this.pluginRegistry = pluginRegistry;
    }

    public async Task ExecuteWorkflowFromJsonAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Load configuration from JSON
        var config = await jsonLoader.LoadConfigurationAsync(jsonFilePath).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Visualize workflow before execution
        visualizer.VisualizeWorkflow(config);

        // Validate plugin references and register MCP servers
        ValidatePluginReferences(config);
        await mcpPool.RegisterServersAsync(config.McpServers, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Get API keys from configuration
        var openAiApiKey = configuration["OpenAI:ApiKey"];
        var azureOpenAiEndpoint = configuration["AzureOpenAI:Endpoint"];
        var ollamaEndpoint = configuration["Ollama:Endpoint"];

        if (string.IsNullOrWhiteSpace(openAiApiKey)
            && string.IsNullOrWhiteSpace(azureOpenAiEndpoint)
            && string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  Warning: No OpenAI, Azure OpenAI or Ollama configuration found!");
            Console.WriteLine("   This is a DEMO mode - simulating workflow execution.");
            Console.ResetColor();
            await SimulateWorkflowExecutionAsync(config).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🦙 Using local Ollama endpoint: {ollamaEndpoint}");
            Console.ResetColor();
        }

        // Execute actual workflow based on type
        await ExecuteActualWorkflowAsync(config, openAiApiKey, azureOpenAiEndpoint, ollamaEndpoint).ConfigureAwait(false);
    }

    private async Task ExecuteActualWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint,
        string? ollamaEndpoint = null)
    {
        Console.WriteLine("\n" + new string('─', 80));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"▶️  STARTING {config.WorkflowType.ToUpper()} WORKFLOW EXECUTION");
        Console.ResetColor();
        Console.WriteLine(new string('─', 80) + "\n");

        switch (config.WorkflowType.ToLower())
        {
            case "sequential":
                await ExecuteSequentialWorkflowAsync(config, openAiApiKey, azureEndpoint, ollamaEndpoint);
                break;
            case "concurrent":
                await ExecuteConcurrentWorkflowAsync(config, openAiApiKey, azureEndpoint, ollamaEndpoint);
                break;
            case "conditional":
                await ExecuteConditionalWorkflowAsync(config, openAiApiKey, azureEndpoint, ollamaEndpoint);
                break;
            case "magentic":
                await ExecuteMagenticWorkflowAsync(config, openAiApiKey, azureEndpoint, ollamaEndpoint);
                break;
            default:
                throw new NotSupportedException($"Workflow type '{config.WorkflowType}' is not supported");
        }
    }

    private async Task ExecuteSequentialWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint,
        string? ollamaEndpoint = null)
    {
        var agents = await CreateAgentsFromConfigurationAsync(
            config, openAiApiKey, azureEndpoint, ollamaEndpoint, default).ConfigureAwait(false);

        var startName = config.Orchestration?.StartAgent
            ?? throw new WorkflowValidationException(
                "Sequential workflow requires Orchestration.StartAgent");
        var edges = config.Orchestration?.Edges ?? new List<EdgeConfiguration>();

        // Pre-validate edge references: every From/To must map to a known agent.
        foreach (var edge in edges)
        {
            if (!agents.ContainsKey(edge.From))
                throw new WorkflowValidationException(
                    $"Edge references unknown agent '{edge.From}'");
            if (!agents.ContainsKey(edge.To))
                throw new WorkflowValidationException(
                    $"Edge references unknown agent '{edge.To}'");
        }

        // SDK 1.3.0: WorkflowBuilder ctor takes start ExecutorBinding;
        // AIAgent → ExecutorBinding via implicit conversion.
        var builder = new WorkflowBuilder(agents[startName]);
        foreach (var edge in edges)
        {
            builder.AddEdge(agents[edge.From], agents[edge.To]);
        }

        // Robust terminal detection: collect all leaves (nodes with no outgoing edge).
        // Sequential workflow expects a single chain — exactly one leaf.
        var fromSet = edges.Select(e => e.From).ToHashSet(StringComparer.Ordinal);
        var leaves = config.Agents
            .Select(a => a.Name)
            .Where(n => !fromSet.Contains(n))
            .ToList();

        string terminalName;
        if (leaves.Count == 1)
            terminalName = leaves[0];
        else if (leaves.Count == 0)
            terminalName = startName;   // single-node graph (no edges)
        else
            throw new WorkflowValidationException(
                $"Sequential workflow requires single terminal agent; found {leaves.Count}: {string.Join(", ", leaves)}");

        builder.WithOutputFrom(agents[terminalName]);

        var workflow = builder.Build();

        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, config.Task)
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

    private async Task ExecuteConcurrentWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint,
        string? ollamaEndpoint = null)
    {
        var participantNames = config.Orchestration?.Concurrent?.ParticipantAgents;
        if (participantNames is null || participantNames.Count == 0)
            throw new WorkflowValidationException(
                "Concurrent workflow requires Orchestration.Concurrent.ParticipantAgents (non-empty list)");

        var agents = await CreateAgentsFromConfigurationAsync(
            config, openAiApiKey, azureEndpoint, ollamaEndpoint, default).ConfigureAwait(false);

        // Pre-validate participant references: every name must map to a known agent.
        foreach (var name in participantNames)
        {
            if (!agents.ContainsKey(name))
                throw new WorkflowValidationException(
                    $"Concurrent participant references unknown agent '{name}'");
        }

        var participants = participantNames.Select(n => agents[n]).ToArray();

        var strategy = config.Orchestration?.Concurrent?.AggregationStrategy ?? "Collect";
        logger.LogInformation(
            "Concurrent workflow: {Count} participants, aggregation strategy '{Strategy}' (default aggregator)",
            participants.Length, strategy);

        // SDK 1.3.0: AgentWorkflowBuilder.BuildConcurrent fans out the same input to each
        // participant in parallel and aggregates per-agent last messages by default.
        var workflow = AgentWorkflowBuilder.BuildConcurrent(participants);

        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, config.Task)
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

    private async Task ExecuteConditionalWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint,
        string? ollamaEndpoint = null)
    {
        var agents = await CreateAgentsFromConfigurationAsync(
            config, openAiApiKey, azureEndpoint, ollamaEndpoint, default).ConfigureAwait(false);

        var startName = config.Orchestration?.StartAgent
            ?? throw new WorkflowValidationException(
                "Conditional workflow requires Orchestration.StartAgent");
        var edges = config.Orchestration?.Edges ?? new List<EdgeConfiguration>();

        // Pre-validate edge references: every From/To must map to a known agent.
        foreach (var edge in edges)
        {
            if (!agents.ContainsKey(edge.From))
                throw new WorkflowValidationException(
                    $"Edge references unknown agent '{edge.From}'");
            if (!agents.ContainsKey(edge.To))
                throw new WorkflowValidationException(
                    $"Edge references unknown agent '{edge.To}'");
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
        // Conditional (без selection-функций) сводится к статическому DAG —
        // ожидаем ровно один листовой узел.
        var fromSet = edges.Select(e => e.From).ToHashSet(StringComparer.Ordinal);
        var leaves = config.Agents
            .Select(a => a.Name)
            .Where(n => !fromSet.Contains(n))
            .ToList();

        string terminalName;
        if (leaves.Count == 1)
            terminalName = leaves[0];
        else if (leaves.Count == 0)
            terminalName = startName;   // single-node graph (no edges)
        else
            throw new WorkflowValidationException(
                $"Conditional workflow requires single terminal agent; found {leaves.Count}: {string.Join(", ", leaves)}");

        builder.WithOutputFrom(agents[terminalName]);

        var workflow = builder.Build();

        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, config.Task)
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

    private async Task ExecuteMagenticWorkflowAsync(
        WorkflowConfiguration config,
        string? openAiApiKey,
        string? azureEndpoint,
        string? ollamaEndpoint = null)
    {
        if (string.IsNullOrWhiteSpace(openAiApiKey) && string.IsNullOrWhiteSpace(ollamaEndpoint))
            throw new WorkflowValidationException(
                "Magentic workflow requires OpenAI API key or Ollama endpoint.");

        var skAgents = new List<ChatCompletionAgent>();
        foreach (var agentConfig in config.Agents)
        {
            var kernel = BuildKernel(agentConfig.ModelId, openAiApiKey, ollamaEndpoint, agentConfig.EnableThinking);

            var toolCount = agentConfig.Tools.Count + agentConfig.McpServers.Count + agentConfig.Plugins.Count;
            if (toolCount > 0)
            {
                logger.LogWarning(
                    "Agent '{Agent}' has {Count} tool(s) configured, but tool bridging to SemanticKernel is deferred for Magentic workflows",
                    agentConfig.Name, toolCount);
            }

            var instructions = agentConfig.EnableThinking
                ? "<|think|>\n" + agentConfig.Instructions
                : agentConfig.Instructions;

            skAgents.Add(new ChatCompletionAgent
            {
                Name = agentConfig.Name,
                Description = agentConfig.Description,
                Instructions = instructions,
                Kernel = kernel,
            });
        }

        var managerKernel = BuildKernel(config.Manager.ModelId, openAiApiKey, ollamaEndpoint, config.Manager.EnableThinking);
        var managerService = managerKernel.GetRequiredService<IChatCompletionService>();

        var managerSettings = new OpenAIPromptExecutionSettings { ResponseFormat = "json_object" };
        var manager = new StandardMagenticManager(managerService, managerSettings)
        {
            MaximumInvocationCount = config.Manager.MaxRoundCount,
            MaximumStallCount = config.Manager.MaxStallCount,
            MaximumResetCount = config.Manager.MaxResetCount,
        };

        var orchestration = new MagenticOrchestration(manager, skAgents.ToArray())
        {
            ResponseCallback = response =>
            {
                LogEvent(
                    $"AGENT:{response.AuthorName ?? "?"}",
                    response.Content ?? "(empty)",
                    ConsoleColor.Yellow);
                return ValueTask.CompletedTask;
            },
            LoggerFactory = loggerFactory,
        };

        await using var runtime = new InProcessRuntime();
        await runtime.StartAsync(default).ConfigureAwait(false);

        try
        {
            using var result = await orchestration
                .InvokeAsync(config.Task, runtime, default)
                .ConfigureAwait(false);

            var timeoutMinutes = MagenticTimeoutMinutesDefault;
            if (config.Settings.TryGetValue("timeoutSeconds", out var timeoutSecondsStr)
                && int.TryParse(timeoutSecondsStr, out var timeoutSeconds)
                && timeoutSeconds > 0)
            {
                timeoutMinutes = (int)Math.Ceiling(timeoutSeconds / 60.0);
            }

            logger.LogInformation("Ожидание результата Magentic workflow, таймаут: {TimeoutMinutes} мин.", timeoutMinutes);

            var output = await result
                .GetValueAsync(TimeSpan.FromMinutes(timeoutMinutes), default)
                .ConfigureAwait(false);

            ShowFinalResult(output ?? "(no output)");
        }
        finally
        {
            await runtime.StopAsync(default).ConfigureAwait(false);
        }
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
    private Exception? HandleWorkflowEvent(WorkflowEvent evt)
    {
        switch (evt)
        {
            case AgentResponseUpdateEvent a:
                LogEvent(
                    $"AGENT:{a.Update?.AuthorName ?? a.ExecutorId ?? "?"}",
                    a.Update?.Text ?? string.Empty,
                    ConsoleColor.Yellow);

                LogEvent($"AGENT:{a.Update?.AuthorName ?? a.ExecutorId ?? "?"}", $"Data: {a.Data}", ConsoleColor.Yellow);
                return null;

            case AgentResponseEvent r:
                LogEvent(
                    $"AGENT:{r.ExecutorId ?? "?"}",
                    r.Response?.Text ?? "(empty response)",
                    ConsoleColor.Green);
                return null;

            case ExecutorFailedEvent ef:
                var execEx = ef.Data as Exception
                    ?? new InvalidOperationException($"Executor '{ef.ExecutorId}' failed");
                LogEvent($"EXECUTOR:{ef.ExecutorId ?? "?"}", execEx.Message, ConsoleColor.Red);
                return execEx;

            case WorkflowErrorEvent e:
                var workflowEx = e.Exception ?? new InvalidOperationException("Unknown workflow error");
                LogEvent("ERROR", workflowEx.Message, ConsoleColor.Red);
                return workflowEx;

            case WorkflowOutputEvent o:
                ShowFinalResult(o.Data?.ToString() ?? "(no result)");
                return null;

            default:
                var dataText = evt.Data is not null
                    ? $"{evt.GetType().Name} | Data: {evt.Data}"
                    : evt.GetType().Name;
                LogEvent("WORKFLOW", dataText, ConsoleColor.Cyan);
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

    private async Task<Dictionary<string, AIAgent>> CreateAgentsFromConfigurationAsync(
        WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint, string? ollamaEndpoint, CancellationToken ct)
    {
        var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);

        foreach (var agentConfig in config.Agents)
        {
            var hostedTools = hostedFactory.Create(agentConfig.Tools);
            var mcpTools = await mcpPool.GetToolsAsync(agentConfig.McpServers, ct).ConfigureAwait(false);
            var pluginTools = ResolvePluginTools(agentConfig);

            var allTools = hostedTools.Concat(mcpTools).Concat(pluginTools).ToArray();

            logger.LogInformation(
                "Agent {Agent} resolved tools: hosted={H}, mcp={M}, plugins={P}",
                agentConfig.Name, hostedTools.Count, mcpTools.Count, pluginTools.Count);

            var chatClient = BuildChatClient(agentConfig.ModelId, openAiApiKey, azureEndpoint, ollamaEndpoint, agentConfig.EnableThinking);
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
            if (!pluginRegistry.TryGet(name, out var plugin))
                // Defensive: ValidatePluginReferences runs at start of ExecuteWorkflowFromJsonAsync; this guard handles direct invocation paths.
                throw new WorkflowValidationException(
                    $"Agent '{agentConfig.Name}' references unknown plugin '{name}'");
            tools.AddRange(plugin!.AsAITools());
        }
        return tools;
    }


    private static IChatClient BuildChatClient(
        string modelId, string? openAiApiKey, string? azureEndpoint, string? ollamaEndpoint = null, bool enableThinking = false)
    {
        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(ollamaEndpoint + "/v1"), NetworkTimeout = TimeSpan.FromMinutes(5) };
            options.AddPolicy(new OllamaThinkingPolicy(enableThinking), PipelinePosition.PerCall);
            var ollamaClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential("ollama"), options);
            return ollamaClient.GetChatClient(modelId).AsIChatClient();
        }
        if (!string.IsNullOrWhiteSpace(azureEndpoint))
        {
            throw new NotSupportedException(
                "Azure OpenAI endpoint is configured, but Azure.AI.OpenAI package is not yet referenced.");
        }
        var openAi = new OpenAIClient(openAiApiKey!);
        return openAi.GetChatClient(modelId).AsIChatClient();
    }

    private static Kernel BuildKernel(string modelId, string? openAiApiKey, string? ollamaEndpoint, bool enableThinking = false)
    {
        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(ollamaEndpoint + "/v1"), NetworkTimeout = TimeSpan.FromMinutes(5) };
            options.AddPolicy(new OllamaThinkingPolicy(enableThinking), PipelinePosition.PerCall);
            var ollamaClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential("ollama"), options);
            return Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId, ollamaClient)
                .Build();
        }
        return Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId, openAiApiKey!)
            .Build();
    }

    private void ValidatePluginReferences(WorkflowConfiguration config)
    {
        foreach (var agent in config.Agents)
            foreach (var name in agent.Plugins)
                if (!pluginRegistry.TryGet(name, out _))
                    throw new WorkflowValidationException(
                        $"Agent '{agent.Name}' references unknown plugin '{name}'");
    }

    private sealed class OllamaThinkingPolicy : PipelinePolicy
    {
        private readonly bool _think;

        internal OllamaThinkingPolicy(bool think) => _think = think;

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            InjectThink(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            InjectThink(message);
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        }

        private void InjectThink(PipelineMessage message)
        {
            if (message.Request?.Content is null) return;
            using var ms = new MemoryStream();
            message.Request.Content.WriteTo(ms, CancellationToken.None);
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            using var output = new MemoryStream();
            using var writer = new Utf8JsonWriter(output);
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteBoolean("think", _think);
            writer.WriteEndObject();
            writer.Flush();
            message.Request.Content = System.ClientModel.BinaryContent.Create(new BinaryData(output.ToArray()));
        }
    }
}
