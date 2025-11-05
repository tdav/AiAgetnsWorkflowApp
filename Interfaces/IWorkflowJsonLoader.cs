using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Interface for loading workflow configuration from JSON
/// </summary>
public interface IWorkflowJsonLoader
{
    Task<WorkflowConfiguration> LoadConfigurationAsync(string jsonFilePath);
}
