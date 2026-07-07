using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Services.Executors;

/// <summary>
/// Thin adapter exposing the DeepResearch pipeline as a workflow executor.
/// </summary>
public sealed class DeepResearchWorkflowExecutor : IWorkflowExecutor
{
    private readonly IDeepResearchOrchestrator deepResearch;

    public DeepResearchWorkflowExecutor(IDeepResearchOrchestrator deepResearch)
    {
        this.deepResearch = deepResearch;
    }

    public string Name => "deepresearch";

    public bool CanExecute(string workflowType) =>
        string.Equals(workflowType, Name, StringComparison.OrdinalIgnoreCase);

    public Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default)
        => deepResearch.ExecuteAsync(config, cancellationToken);
}
