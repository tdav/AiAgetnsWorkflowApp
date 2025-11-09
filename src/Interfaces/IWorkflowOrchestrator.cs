namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Interface for workflow orchestration
/// </summary>
public interface IWorkflowOrchestrator
{
    Task ExecuteWorkflowFromJsonAsync(string jsonFilePath);
}
