using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Workflow orchestrator facade: loads and validates the JSON configuration,
/// visualizes it, registers MCP servers, applies the context budget and
/// dispatches execution to the matching <see cref="IWorkflowExecutor"/> strategy
/// (or to the DEMO simulator when no LLM credentials are configured).
/// </summary>
public class MagenticWorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly ILogger<MagenticWorkflowOrchestrator> logger;
    private readonly IWorkflowJsonLoader jsonLoader;
    private readonly IWorkflowVisualizer visualizer;
    private readonly IConfiguration configuration;
    private readonly IMcpClientPool mcpPool;
    private readonly IChatClientProvider clientProvider;
    private readonly AgentTeamBuilder teamBuilder;
    private readonly SimulatedWorkflowExecutor simulated;
    private readonly IEnumerable<IWorkflowExecutor> executors;

    public MagenticWorkflowOrchestrator(
        ILogger<MagenticWorkflowOrchestrator> logger,
        IWorkflowJsonLoader jsonLoader,
        IWorkflowVisualizer visualizer,
        IConfiguration configuration,
        IMcpClientPool mcpPool,
        IChatClientProvider clientProvider,
        AgentTeamBuilder teamBuilder,
        SimulatedWorkflowExecutor simulated,
        IEnumerable<IWorkflowExecutor> executors)
    {
        this.logger = logger;
        this.jsonLoader = jsonLoader;
        this.visualizer = visualizer;
        this.configuration = configuration;
        this.mcpPool = mcpPool;
        this.clientProvider = clientProvider;
        this.teamBuilder = teamBuilder;
        this.simulated = simulated;
        this.executors = executors;
    }

    public async Task ExecuteWorkflowFromJsonAsync(string jsonFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = await jsonLoader.LoadConfigurationAsync(jsonFilePath).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        visualizer.VisualizeWorkflow(config);

        teamBuilder.ValidatePluginReferences(config);
        await mcpPool.RegisterServersAsync(config.McpServers, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        clientProvider.SetWorkflowBudget(config.ContextBudget);

        if (!clientProvider.HasCredentials)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  Warning: No OpenAI, Azure OpenAI or Ollama configuration found!");
            Console.WriteLine("   This is a DEMO mode - simulating workflow execution.");
            Console.ResetColor();
            await simulated.ExecuteAsync(config, cancellationToken).ConfigureAwait(false);
            return;
        }

        var ollamaEndpoint = configuration["Ollama:Endpoint"];
        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🦙 Using local Ollama endpoint: {ollamaEndpoint}");
            Console.ResetColor();
        }

        Console.WriteLine("\n" + new string('─', 80));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"▶️  STARTING {config.WorkflowType.ToUpper()} WORKFLOW EXECUTION");
        Console.ResetColor();
        Console.WriteLine(new string('─', 80) + "\n");

        var executor = executors.FirstOrDefault(e => e.CanExecute(config.WorkflowType))
            ?? throw new NotSupportedException($"Workflow type '{config.WorkflowType}' is not supported");

        logger.LogInformation(
            "Dispatching {WorkflowType} workflow to executor {Executor}",
            config.WorkflowType, executor.Name);

        await executor.ExecuteAsync(config, cancellationToken).ConfigureAwait(false);
    }
}
