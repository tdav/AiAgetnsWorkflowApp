namespace MagenticWorkflowApp.Models;

public enum AgentActivityKind
{
    TurnStarted,
    Chunk,
    TurnCompleted,
    ToolCall,
    ManagerDecision,
    ExecutorFailed,
    WorkflowError,
    WorkflowOutput,
}
