using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Strategy for executing one workflow type. Implementations are registered in DI
/// and resolved by <see cref="IWorkflowOrchestrator"/> by matching
/// <see cref="CanExecute"/> — adding a new workflow type requires no switch edits.
/// </summary>
public interface IWorkflowExecutor
{
    /// <summary>Primary workflow-type name (for logs).</summary>
    string Name { get; }

    /// <summary>Case-insensitive check whether this executor handles the given workflow type.</summary>
    bool CanExecute(string workflowType);

    Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default);
}
