using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Executes "magentic" workflows on Semantic Kernel: a StandardMagenticManager
/// dynamically coordinates ChatCompletionAgents on an in-process runtime.
/// MCP/plugin tools are bridged into each agent's Kernel via AIFunction →
/// KernelFunction; hosted tools (e.g. CodeInterpreter) cannot be bridged and
/// produce a warning.
/// </summary>
public sealed class MagenticWorkflowExecutor : IWorkflowExecutor
{
    private const int MagenticTimeoutMinutesDefault = 30;
    private const string BridgedToolsPluginName = "AgentTools";

    private readonly IChatClientProvider clientProvider;
    private readonly AgentTeamBuilder teamBuilder;
    private readonly IAgentActivityLogger activity;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MagenticWorkflowExecutor> logger;

    public MagenticWorkflowExecutor(
        IChatClientProvider clientProvider,
        AgentTeamBuilder teamBuilder,
        IAgentActivityLogger activity,
        ILoggerFactory loggerFactory,
        ILogger<MagenticWorkflowExecutor> logger)
    {
        this.clientProvider = clientProvider;
        this.teamBuilder = teamBuilder;
        this.activity = activity;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    public string Name => "magentic";

    public bool CanExecute(string workflowType) =>
        string.Equals(workflowType, Name, StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default)
    {
        activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        var skAgents = new List<ChatCompletionAgent>();
        foreach (var agentConfig in config.Agents)
        {
            var kernel = clientProvider.BuildKernel(
                agentConfig.ModelId, agentConfig.EnableThinking, agentName: agentConfig.Name);

            var hasFunctions = await BridgeToolsIntoKernelAsync(kernel, agentConfig, cancellationToken).ConfigureAwait(false);

            var instructions = agentConfig.EnableThinking
                ? "<|think|>\n" + agentConfig.Instructions
                : agentConfig.Instructions;

            skAgents.Add(new ChatCompletionAgent
            {
                Name = agentConfig.Name,
                Description = agentConfig.Description,
                Instructions = instructions,
                Kernel = kernel,
                Arguments = hasFunctions
                    ? new KernelArguments(new PromptExecutionSettings
                    {
                        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                    })
                    : null,
            });
        }

        var managerKernel = clientProvider.BuildKernel(
            config.Manager.ModelId, config.Manager.EnableThinking,
            agentName: LoggingChatCompletionService.ManagerAgentName);
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
            ResponseCallback = _ => ValueTask.CompletedTask,
            LoggerFactory = loggerFactory,
        };

        await using var runtime = new InProcessRuntime();
        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var result = await orchestration
                .InvokeAsync(config.Task, runtime, cancellationToken)
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
                .GetValueAsync(TimeSpan.FromMinutes(timeoutMinutes), cancellationToken)
                .ConfigureAwait(false);

            activity.OnWorkflowOutput(output ?? "(no output)");
        }
        finally
        {
            await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Bridges the agent's MCP/plugin tools (AIFunction) into the SK kernel as a
    /// KernelFunction plugin. Returns true when at least one function was bridged.
    /// </summary>
    private async Task<bool> BridgeToolsIntoKernelAsync(
        Kernel kernel, AgentConfiguration agentConfig, CancellationToken cancellationToken)
    {
        var tools = await teamBuilder.ResolveToolsAsync(agentConfig, cancellationToken).ConfigureAwait(false);
        if (tools.Count == 0)
        {
            return false;
        }

        var functions = tools.OfType<AIFunction>().Select(f => f.AsKernelFunction()).ToList();
        var unbridgeable = tools.Count - functions.Count;
        if (unbridgeable > 0)
        {
            logger.LogWarning(
                "Agent '{Agent}': {Count} hosted tool(s) cannot be bridged to SemanticKernel and are skipped",
                agentConfig.Name, unbridgeable);
        }
        if (functions.Count == 0)
        {
            return false;
        }

        kernel.Plugins.AddFromFunctions(BridgedToolsPluginName, functions);
        logger.LogInformation(
            "Agent '{Agent}': bridged {Count} tool(s) into SemanticKernel",
            agentConfig.Name, functions.Count);
        return true;
    }
}
