using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Executes "magentic" workflows on Semantic Kernel: a StandardMagenticManager
/// dynamically coordinates ChatCompletionAgents on an in-process runtime.
/// Tool/MCP/plugin bridging into SK is deferred (a warning is emitted per agent).
/// </summary>
public sealed class MagenticWorkflowExecutor : IWorkflowExecutor
{
    private const int MagenticTimeoutMinutesDefault = 30;

    private readonly IChatClientProvider clientProvider;
    private readonly IAgentActivityLogger activity;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MagenticWorkflowExecutor> logger;

    public MagenticWorkflowExecutor(
        IChatClientProvider clientProvider,
        IAgentActivityLogger activity,
        ILoggerFactory loggerFactory,
        ILogger<MagenticWorkflowExecutor> logger)
    {
        this.clientProvider = clientProvider;
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
}
