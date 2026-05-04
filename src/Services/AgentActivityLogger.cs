using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

public sealed class AgentActivityLogger : IAgentActivityLogger
{
    private readonly ILogger<AgentActivityLogger> logger;
    private readonly IConsoleWriter console;

    public AgentActivityLogger(ILogger<AgentActivityLogger> logger, IConsoleWriter console)
    {
        this.logger = logger;
        this.console = console;
    }

    public void SetWorkflowMode(WorkflowDisplayMode mode) => throw new NotImplementedException();
    public void OnTurnStarted(string agent, string? executorId = null) => throw new NotImplementedException();
    public void OnChunk(string agent, string text) => throw new NotImplementedException();
    public void OnTurnCompleted(string agent, string? fullText = null) => throw new NotImplementedException();
    public void OnToolCall(string agent, string toolName, string? args = null) => throw new NotImplementedException();
    public void OnManagerDecision(string managerName, string decision) => throw new NotImplementedException();
    public void OnExecutorFailed(string executorId, Exception exception) => throw new NotImplementedException();
    public void OnWorkflowError(Exception exception) => throw new NotImplementedException();
    public void OnWorkflowOutput(string output) => throw new NotImplementedException();
    public void FlushAllPendingTurns(string reason) => throw new NotImplementedException();
}
