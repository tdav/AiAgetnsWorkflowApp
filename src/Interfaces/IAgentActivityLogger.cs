using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

public interface IAgentActivityLogger
{
    void SetWorkflowMode(WorkflowDisplayMode mode);

    void OnTurnStarted(string agent, string? executorId = null);
    void OnChunk(string agent, string text);
    void OnTurnCompleted(string agent, string? fullText = null);

    void OnToolCall(string agent, string toolName, string? args = null);
    void OnManagerDecision(string managerName, string decision);

    void OnExecutorFailed(string executorId, Exception exception);
    void OnWorkflowError(Exception exception);
    void OnWorkflowOutput(string output);

    void FlushAllPendingTurns(string reason);
}
